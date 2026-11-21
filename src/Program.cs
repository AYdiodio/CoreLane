using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle(CoreAllocator.Program.DisplayName)]
[assembly: AssemblyProduct(CoreAllocator.Program.DisplayName)]
[assembly: AssemblyDescription("手动分配选中进程的 CPU 亲和性，保存并恢复原设置")]
[assembly: AssemblyVersion("0.13.16.0")]

namespace CoreAllocator
{
    internal static class Program
    {
        internal const string DisplayName = "CoreLane";
        internal static readonly Version CurrentVersion = typeof(Program).Assembly.GetName().Version;
        internal static string DisplayVersion { get { return "v" + CurrentVersion.ToString(3); } }

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--test-window-worker")
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                using (Form worker = new TestTargetWindow())
                using (System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 180000 })
                {
                    timer.Tick += delegate { worker.Close(); }; timer.Start(); Application.Run(worker);
                }
                return 0;
            }
            if (args.Length > 0 && args[0] == "--test-worker") { Thread.Sleep(180000); return 0; }
            if (args.Length == 2 && args[0] == "--self-test") return SelfTests.Run(args[1]);
            if (args.Length == 3 && args[0] == "--inspect")
            {
                try
                {
                    int pid = int.Parse(args[1]);
                    ProcessRow row;
                    using (ProcessHandle handle = Native.Open(pid, false))
                    using (Process process = Process.GetProcessById(pid))
                        row = new ProcessRow { Pid = pid, Created = Native.Created(handle), Name = process.ProcessName + ".exe" };
                    AffinityService service = new AffinityService(Topology.Read(), new SnapshotStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "original-affinity.json")));
                    OperationResult result = service.InspectPermissions(row);
                    File.WriteAllText(args[2], result.ToString());
                    return result.Success ? 0 : 1;
                }
                catch (Exception error) { File.WriteAllText(args[2], Native.Explain(error)); return 2; }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && (args[0] == AppStartup.EnableArgument || args[0] == AppStartup.DisableArgument))
            {
                try
                {
                    if (!Native.IsAdministrator || args[1] != AppStartup.UserSid) throw new UnauthorizedAccessException("请用当前 Windows 帐户的管理员权限确认配置。");
                    AppStartup.SetEnabled(args[0] == AppStartup.EnableArgument, Application.ExecutablePath);
                    return 0;
                }
                catch (Exception error)
                {
                    AppMessageBox.Show(Native.Explain(error), "管理员自启动配置失败");
                    return 2;
                }
            }
            bool acquired = false;
            using (Mutex mutex = new Mutex(false, @"Local\CoreAllocator-" + WindowsIdentity.GetCurrent().User.Value))
            {
                try
                {
                    try { acquired = mutex.WaitOne(0); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired)
                    {
                        if (args.Length == 1 && args[0] == AppStartup.LogonArgument) return 0;
                        AppMessageBox.Show("当前会话已有一个 " + DisplayName + " 实例在运行。请从任务栏或系统托盘打开。", DisplayName);
                        return 1;
                    }
                    string directory = Path.Combine(Path.GetDirectoryName(AppStartup.OriginExecutable(Application.ExecutablePath)), "data");
                    Directory.CreateDirectory(directory);
                    using (FileStream fileLock = new FileStream(Path.Combine(directory, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                    {
                        Topology topology = Topology.Read();
                        SnapshotStore store = new SnapshotStore(Path.Combine(directory, "original-affinity.json"));
                        Application.Run(new MainForm(new AffinityService(topology, store)));
                    }
                    return 0;
                }
                catch (Exception error)
                {
                    AppMessageBox.Show(Native.Explain(error) + "\r\n\r\n请把程序放在有写入权限的文件夹，并保留 data 目录。", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                finally { if (acquired) mutex.ReleaseMutex(); }
            }
        }
    }
}
