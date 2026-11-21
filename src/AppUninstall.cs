using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal sealed class AppUninstall
    {
        private static readonly string[] RecoveryFiles = { "original-affinity.json", "original-cpu-sets.json", "original-priority.json", "instance.lock" };
        private static readonly string[] SettingsFiles = { "schemes.json", "retained-assignments.json" };
        private static readonly Regex RuntimeFile = new Regex(@"^(?:CoreLane|CoreAllocator)\.v\d+\.\d+\.\d+\.exe(?:\.config|\.origin)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private readonly List<string> files = new List<string>();
        private readonly List<string> directories = new List<string>();

        internal AppUninstall(string executable, string runtimeDirectory, bool keepSettings)
        {
            executable = Path.GetFullPath(executable);
            string directory = Path.GetDirectoryName(executable);
            string data = Path.Combine(directory, "data");
            RequirePlainPath(executable);
            RequirePlainPath(runtimeDirectory);
            RequirePlainPath(data);
            files.Add(executable);
            files.Add(executable + ".config");
            foreach (string name in keepSettings ? RecoveryFiles : RecoveryFiles.Concat(SettingsFiles)) files.Add(Path.Combine(data, name));
            directories.Add(data);
            if (Directory.Exists(runtimeDirectory))
            {
                files.AddRange(Directory.EnumerateFiles(runtimeDirectory).Where(file => RuntimeFile.IsMatch(Path.GetFileName(file))));
                directories.Add(runtimeDirectory);
                directories.Add(Path.GetDirectoryName(runtimeDirectory));
            }
            directories.Add(directory);
        }

        internal void FindShortcuts()
        {
            string[] folders = { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.Programs) };
            string[] names = { Program.DisplayName + ".lnk", "核心分配工具.lnk", "CCD Scheduler.lnk" };
            foreach (string path in folders.Where(folder => !string.IsNullOrEmpty(folder)).SelectMany(folder => names.Select(name => Path.Combine(folder, name))).Where(File.Exists))
            {
                RequirePlainPath(path);
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                try
                {
                    dynamic link = shell.CreateShortcut(path);
                    try
                    {
                        string target = link.TargetPath;
                        if (files.Contains(target, StringComparer.OrdinalIgnoreCase)) files.Add(path);
                    }
                    finally { Marshal.FinalReleaseComObject(link); }
                }
                finally { Marshal.FinalReleaseComObject(shell); }
            }
        }

        private static void RequirePlainPath(string path)
        {
            for (string current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(current); }
                catch (FileNotFoundException) { continue; }
                catch (DirectoryNotFoundException) { continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("卸载路径包含文件系统链接：" + current);
            }
        }

        private static string Quote(string path) { return "'" + path.Replace("'", "''") + "'"; }

        internal Process StartCleanup(int processId, long createdUtc, bool showResult = true)
        {
            string script = @"
$ErrorActionPreference = 'Stop'
function Confirm-Plain([string]$path) {
    for ($entry = $path; $entry; $entry = [IO.Path]::GetDirectoryName($entry)) {
        try { $attributes = [IO.File]::GetAttributes($entry) }
        catch [IO.FileNotFoundException] { continue }
        catch [IO.DirectoryNotFoundException] { continue }
        if ($attributes -band [IO.FileAttributes]::ReparsePoint) { throw ('卸载路径包含文件系统链接：' + $entry) }
    }
}
try {
    $app = $null
    try {
        $app = [Diagnostics.Process]::GetProcessById(" + processId + @")
        if ($app.StartTime.ToFileTimeUtc() -eq [long]" + createdUtc + @" -and !$app.WaitForExit(20000)) { throw '程序尚未退出。' }
    } catch [ArgumentException] {} catch [InvalidOperationException] {}
    $failures = @()
    foreach ($path in @(" + string.Join(",", files.Distinct(StringComparer.OrdinalIgnoreCase).Select(Quote)) + @")) {
        try {
            Confirm-Plain $path
            for ($attempt = 0; $attempt -lt 8; $attempt++) {
                try { [IO.File]::Delete($path); break }
                catch [IO.DirectoryNotFoundException] { break }
                catch [IO.IOException] { if ($attempt -eq 7) { throw }; [Threading.Thread]::Sleep(200) }
            }
        } catch { $failures += $path }
    }
    foreach ($path in @(" + string.Join(",", directories.Distinct(StringComparer.OrdinalIgnoreCase).Select(Quote)) + @")) {
        try {
            Confirm-Plain $path
            if ([IO.Directory]::Exists($path) -and [IO.Directory]::GetFileSystemEntries($path).Length -eq 0) { [IO.Directory]::Delete($path, $false) }
        } catch { $failures += $path }
    }
    if ($failures.Count -gt 0) { throw ('以下文件或目录未能移除：' + [Environment]::NewLine + ($failures -join [Environment]::NewLine)) }
    $message = " + Quote("已卸载 " + Program.DisplayName + "。") + @"
    $result = 0
} catch { $message = $_.Exception.Message; $result = 1 }
" + (showResult ? "Add-Type -AssemblyName System.Windows.Forms; [void][Windows.Forms.MessageBox]::Show($message, " + Quote(Program.DisplayName) + ")\n" : "") + "exit $result\n";
            string powershell = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            return Process.Start(new ProcessStartInfo(powershell, "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + encoded)
            { WorkingDirectory = Environment.SystemDirectory, UseShellExecute = false, CreateNoWindow = true });
        }

        internal static bool NeedsAdministrator()
        { return Directory.Exists(AppStartup.ProtectedDirectory) || AppStartup.Mode() == StartupMode.Elevated; }
    }

    internal sealed class UninstallDialog : Form
    {
        internal UninstallDialog()
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            DialogUi.Setup(this, "卸载 " + Program.DisplayName, new Size(520, 156));
            var root = DialogUi.Rows(-1, 52);
            Controls.Add(root);
            root.Controls.Add(DialogUi.Label("卸载会将有恢复记录的进程交还 Windows 调度，并移除程序、快捷方式和自启动项。\r\n完全卸载还会删除保存的方案、游戏分类和分配。", 10, UiTheme.Ink), 0, 0);
            FlowLayoutPanel actions = DialogUi.Actions();
            SoftButton full = GameQuestionDialog.MakeButton("完全卸载", 105, delegate { DialogResult = DialogResult.Yes; }, false);
            SoftButton keep = GameQuestionDialog.MakeButton("保留设置卸载", 144, delegate { DialogResult = DialogResult.No; }, true);
            SoftButton cancel = GameQuestionDialog.MakeButton("取消", 80, delegate { DialogResult = DialogResult.Cancel; }, false);
            full.Destructive = true;
            actions.Controls.AddRange(new Control[] { full, keep, cancel });
            root.Controls.Add(actions, 0, 1);
            CancelButton = cancel;
            ActiveControl = cancel;
        }
    }
}
