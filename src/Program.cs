using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("核心分配工具")]
[assembly: AssemblyDescription("手动分配选中进程的 CPU 亲和性，保存并恢复原设置")]
[assembly: AssemblyVersion("0.1.1.0")]

namespace CoreAllocator
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
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
            bool acquired = false;
            using (Mutex mutex = new Mutex(false, @"Local\CoreAllocator-" + WindowsIdentity.GetCurrent().User.Value))
            {
                try
                {
                    try { acquired = mutex.WaitOne(0); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) { MessageBox.Show("当前会话已有一个核心分配工具在运行，请使用已有窗口。", "核心分配工具"); return 1; }
                    string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
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
                    MessageBox.Show(Native.Explain(error) + "\r\n\r\n请把程序放在有写入权限的文件夹，并保留 data 目录。", "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
                finally { if (acquired) mutex.ReleaseMutex(); }
            }
        }
    }
}
