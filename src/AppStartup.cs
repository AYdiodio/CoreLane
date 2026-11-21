using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace CoreAllocator
{
    internal enum StartupMode { Off, Legacy, Elevated }

    internal static class AppStartup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string LegacyName = "CCD Scheduler";
        internal const string OriginExtension = ".origin";
        private const string ConfigResource = "CoreAllocator.App.config";
        internal const string LogonArgument = "--from-logon";
        internal const string EnableArgument = "--enable-logon";
        internal const string DisableArgument = "--disable-logon";
        internal static string UserSid { get { using (var identity = WindowsIdentity.GetCurrent()) return identity.User.Value; } }
        private static string TaskName { get { return "CCD Scheduler - " + UserSid; } }
        internal static string InstallRoot { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CCD Scheduler Runtime"); } }
        internal static string ProtectedDirectory { get { return Path.Combine(InstallRoot, UserSid); } }
        private static string ProtectedExecutable { get { return Path.Combine(ProtectedDirectory, Program.DisplayName + ".v" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3) + ".exe"); } }

        internal static string OriginExecutable(string executable)
        {
            executable = Path.GetFullPath(executable);
            string directory = Path.GetDirectoryName(executable);
            if (!string.Equals(directory, ProtectedDirectory, StringComparison.OrdinalIgnoreCase)) return executable;
            string origin = File.ReadAllText(executable + OriginExtension);
            if (!Path.IsPathRooted(origin) || !string.Equals(Path.GetFullPath(origin), origin, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(origin), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("程序安装路径无效。");
            return origin;
        }

        internal static StartupMode Mode()
        {
            bool enabled = WithTaskFolder(folder => WithRegisteredTask((object)folder, task => (bool)task.Enabled));
            if (enabled) return StartupMode.Elevated;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && !string.IsNullOrWhiteSpace(key.GetValue(LegacyName) as string) ? StartupMode.Legacy : StartupMode.Off;
        }

        internal static void SetEnabled(bool enabled, string executable)
        {
            if (!Native.IsAdministrator)
            {
                using (Process process = Process.Start(new ProcessStartInfo(executable, (enabled ? EnableArgument : DisableArgument) + " " + UserSid)
                { UseShellExecute = true, Verb = "runas", WorkingDirectory = Environment.SystemDirectory }))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0) throw new InvalidOperationException("未能配置管理员自启动，请查看授权窗口中的结果。");
                }
                return;
            }
            if (enabled)
            {
                InstallProtectedCopy(executable);
                WithTaskFolder(delegate(dynamic folder)
                {
                    const int createOrUpdate = 6, interactiveToken = 3;
                    object task = folder.RegisterTask(TaskName, TaskXml(UserSid, ProtectedExecutable), createOrUpdate, UserSid, null, interactiveToken, null);
                    if (task != null) Marshal.FinalReleaseComObject(task);
                    return true;
                });
            }
            else RemoveTask();
            RemoveLegacy();
        }

        internal static void RemoveForUninstall()
        {
            RemoveTask();
            RemoveLegacy();
        }

        private static void RemoveTask()
        {
            WithTaskFolder(folder => WithRegisteredTask((object)folder, task =>
            {
                folder.DeleteTask(TaskName, 0);
                return true;
            }));
        }

        private static void RemoveLegacy()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                if (key != null) key.DeleteValue(LegacyName, false);
        }

        private static bool WithTaskFolder(Func<dynamic, bool> action)
        {
            dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service", true));
            try
            {
                scheduler.Connect();
                dynamic folder = scheduler.GetFolder(@"\");
                try { return action((object)folder); }
                finally { Marshal.FinalReleaseComObject(folder); }
            }
            finally { Marshal.FinalReleaseComObject(scheduler); }
        }

        private static bool WithRegisteredTask(object folder, Func<dynamic, bool> action)
        {
            dynamic task;
            try { task = ((dynamic)folder).GetTask(TaskName); }
            catch (FileNotFoundException) { return false; }
            catch (COMException error)
            {
                if (error.ErrorCode == unchecked((int)0x80070002)) return false;
                throw;
            }
            try { return action((object)task); }
            finally { Marshal.FinalReleaseComObject(task); }
        }

        internal static string TaskXml(string sid, string executable)
        {
            string user = SecurityElement.Escape(sid), file = SecurityElement.Escape(executable);
            string directory = SecurityElement.Escape(Path.GetDirectoryName(executable));
            return "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
                "<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId></LogonTrigger></Triggers>" +
                "<Principals><Principal id=\"User\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType>" +
                "<RunLevel>HighestAvailable</RunLevel></Principal></Principals>" +
                "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit></Settings>" +
                "<Actions Context=\"User\"><Exec><Command>" + file + "</Command><Arguments>" + LogonArgument + "</Arguments>" +
                "<WorkingDirectory>" + directory + "</WorkingDirectory></Exec></Actions></Task>";
        }

        private static void InstallProtectedCopy(string executable)
        {
            string origin = OriginExecutable(executable);
            EnsureProtectedDirectory(InstallRoot);
            EnsureProtectedDirectory(ProtectedDirectory);
            if (!string.Equals(executable, ProtectedExecutable, StringComparison.OrdinalIgnoreCase))
            {
                // Use the running build's embedded configuration for the elevated copy.
                using (Stream config = Assembly.GetExecutingAssembly().GetManifestResourceStream(ConfigResource))
                {
                    if (config == null) throw new InvalidDataException("找不到内置程序配置。");
                    using (Stream destination = File.Create(ProtectedExecutable + ".config")) config.CopyTo(destination);
                }
                using (Stream source = File.OpenRead(executable))
                using (Stream destination = File.Create(ProtectedExecutable)) source.CopyTo(destination);
                File.WriteAllText(ProtectedExecutable + OriginExtension, origin);
            }
        }

        private static void EnsureProtectedDirectory(string path)
        {
            const string sddl = "O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;0x1200a9;;;BU)";
            var security = new DirectorySecurity();
            security.SetSecurityDescriptorSddlForm(sddl);
            if (!Directory.Exists(path)) Directory.CreateDirectory(path, security);
            var directory = new DirectoryInfo(path);
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("自启动安装目录不允许使用链接。");
            var actual = directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            var rules = actual.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
            var expected = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToArray();
            if (!actual.AreAccessRulesProtected || !actual.GetOwner(typeof(SecurityIdentifier)).Equals(security.GetOwner(typeof(SecurityIdentifier))) ||
                rules.Length != expected.Length || expected.Any(wanted => !rules.Any(rule => rule.IdentityReference.Equals(wanted.IdentityReference) &&
                    rule.AccessControlType == wanted.AccessControlType && rule.FileSystemRights == wanted.FileSystemRights &&
                    rule.InheritanceFlags == wanted.InheritanceFlags && rule.PropagationFlags == wanted.PropagationFlags)))
                throw new UnauthorizedAccessException("自启动安装目录的访问权限异常，无法登记管理员自启动。");
        }
    }
}
