using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static void TrayRuleTests(string working)
        {
            Case("admin startup: task is bound to the interactive user and highest privileges", delegate
            {
                string sid = AppStartup.UserSid;
                string executable = Path.Combine(working, "Folder & space", "CoreLane.exe");
                var xml = new XmlDocument();
                xml.LoadXml(AppStartup.TaskXml(sid, executable));
                var ns = new XmlNamespaceManager(xml.NameTable);
                ns.AddNamespace("t", "http://schemas.microsoft.com/windows/2004/02/mit/task");
                Func<string, string> value = path => xml.SelectSingleNode("/t:Task/" + path, ns).InnerText;
                Check(value("t:Triggers/t:LogonTrigger/t:UserId") == sid &&
                    value("t:Principals/t:Principal/t:UserId") == sid && value("t:Principals/t:Principal/t:LogonType") == "InteractiveToken",
                    "normal and automatic Windows logons use this account's interactive token");
                Check(value("t:Principals/t:Principal/t:RunLevel") == "HighestAvailable" &&
                    value("t:Actions/t:Exec/t:Command") == executable && value("t:Actions/t:Exec/t:Arguments") == AppStartup.LogonArgument,
                    "highest run level and literal executable path are encoded");
            });
            Case("uninstall: both cleanup modes remove the running program and only full uninstall removes settings", delegate
            {
                foreach (bool keep in new[] { true, false })
                {
                    string folder = Path.Combine(working, keep ? "uninstall preserve's copy" : "uninstall full");
                    string data = Path.Combine(folder, "data");
                    string runtime = Path.Combine(folder, "protected-runtime", "user");
                    Directory.CreateDirectory(data);
                    Directory.CreateDirectory(runtime);
                    string source = Path.Combine(folder, "CCD test.exe");
                    File.Copy(typeof(SelfTests).Assembly.Location, source);
                    string settings = Path.Combine(data, "schemes.json");
                    string runtimeFile = Path.Combine(runtime, "CoreAllocator.v0.1.0.exe");
                    string brandedRuntime = Path.Combine(runtime, "CoreLane.v0.13.13.exe");
                    string untouched = Path.Combine(folder, "My notes.txt");
                    File.Copy(typeof(SelfTests).Assembly.Location + ".config", source + ".config");
                    foreach (string file in new[] { runtimeFile, runtimeFile + ".origin", brandedRuntime, brandedRuntime + ".config", brandedRuntime + ".origin",
                        settings, Path.Combine(data, "retained-assignments.json"), Path.Combine(data, "instance.lock"), untouched })
                        File.WriteAllText(file, "fixture");
                    using (Process worker = Process.Start(new ProcessStartInfo { FileName = source, Arguments = "--test-worker", UseShellExecute = false, CreateNoWindow = true }))
                    {
                        try
                        {
                            var uninstall = new AppUninstall(source, runtime, keep);
                            using (Process cleanup = uninstall.StartCleanup(worker.Id, worker.StartTime.ToFileTimeUtc(), false))
                            {
                                Check(!cleanup.WaitForExit(200) && File.Exists(source), "cleanup waits for the original process to exit");
                                worker.Kill(); worker.WaitForExit();
                                Check(cleanup.WaitForExit(12000) && cleanup.ExitCode == 0, "cleanup completes in a separate process");
                            }
                            Check(!File.Exists(source) && !File.Exists(source + ".config") && !Directory.Exists(runtime) && File.Exists(untouched),
                                "current executable and protected files are gone, unrelated files are preserved");
                            Check(File.Exists(settings) == keep && Directory.Exists(data) == keep, "settings are removed only on full uninstall");
                        }
                        finally { if (!worker.HasExited) { worker.Kill(); worker.WaitForExit(); } }
                    }
                }
            });
            Case("Steam fallback confirms active Apex tracking and rejects stale or unrelated process entries", delegate
            {
                DateTime created = new DateTime(2026, 11, 21, 21, 25, 6);
                long steam = created.AddMinutes(-10).ToFileTime();
                var apex = new ProcessRow { Pid = 13368, Name = "r5apex_dx12.exe", Created = created.ToFileTime(),
                    ExecutablePath = @"D:\Library\steamapps\common\Apex Legends\r5apex_dx12.exe" };
                string launch = "[2026-11-21 21:24:57] AppID 1172470 adding PID 37592 as a tracked process \"\"D:\\Library\\steamapps\\common\\Apex Legends\\EAAntiCheat.GameServiceLauncher.exe\" -steam\"\r\n";
                string track = "[2026-11-21 21:25:07] AppID 1172470 adding PID 13368 as a tracked process\r\n";
                Check(SteamLaunchAffinity.ConfirmsLaunch(new StringReader(launch + track), apex, steam), "app id, executable, PID and start time identify Steam launch");
                Check(!SteamLaunchAffinity.ConfirmsLaunch(new StringReader(track), apex, steam), "tracking entry alone does not establish launch");
                Check(!SteamLaunchAffinity.ConfirmsLaunch(new StringReader(launch + track.Replace("13368", "133680")), apex, steam), "PID must match exactly");
                Check(!SteamLaunchAffinity.ConfirmsLaunch(new StringReader(launch + track.Replace("25:07", "27:07")), apex, steam), "stale tracking is rejected");
                Check(!SteamLaunchAffinity.ConfirmsLaunch(new StringReader(launch + track + "[2026-11-21 21:25:25] AppID 1172470 no longer tracking PID 13368, exit code 0"), apex, steam), "removed tracking is rejected");
            });
            Case("rule status distinguishes verified affinity, drift, read errors and denied writes", delegate
            {
                var row = new ProcessRow { Pid = 982001, Created = 10, Mask = 1 };
                var target = new AutomaticTarget { Process = row, Mask = 2 };
                var session = new AutomationSession("fixture", new ProcessRow[0]);
                Check(session.ExecutionStatus(target) == "待执行", "unattempted and different mask");
                session.MarkAttempted(target, new OperationResult { Success = true });
                Check(session.ExecutionStatus(target) == "已偏离目标", "successful result with changed readback");
                row.Mask = 2;
                Check(session.ExecutionStatus(target) == "已生效", "successful result with matching readback");
                row.ReadError = "denied";
                Check(session.ExecutionStatus(target) == "无法读取当前范围", "unreadable result is unknown");
                session.MarkAttempted(target, new OperationResult { Win32Error = 5 });
                Check(session.ExecutionStatus(target) == "访问被拒绝", "typed permission failure");
                var restarted = new AutomaticTarget { Process = new ProcessRow { Pid = row.Pid, Created = 11, Mask = 2 }, Mask = 2 };
                Check(session.ExecutionStatus(restarted) == "当前已符合目标", "reused PID does not inherit a failed attempt");
            });

            Topology topology = Topology.Read();
            if (!topology.CanAssign || Masks.Bits(topology.Available).Count() < 2) return;
            Case("Windows scheduling reset releases all three restrictions on tracked processes and keeps failed records", delegate
            {
                string folder = Path.Combine(working, "windows-defaults");
                Directory.CreateDirectory(folder);
                string executable = Path.Combine(folder, "reset-worker.exe");
                File.Copy(typeof(SelfTests).Assembly.Location, executable);
                var service = new AffinityService(topology, new SnapshotStore(Path.Combine(folder, "original.json")));
                Func<Process> start = () => Process.Start(new ProcessStartInfo { FileName = executable, Arguments = "--test-worker", UseShellExecute = false, CreateNoWindow = true });
                ulong[] bits = Masks.Bits(topology.Available).Take(2).Select(bit => 1UL << bit).ToArray();
                using (Process tracked = start())
                using (Process untracked = start())
                using (var form = new MainForm(service))
                {
                    try
                    {
                        foreach (Process worker in new[] { tracked, untracked })
                        {
                            using (ProcessHandle handle = Native.Open(worker.Id, true))
                            {
                                Native.SetAffinity(handle, bits[0]);
                                Native.SetPriority(handle, PriorityLevels.AboveNormal);
                            }
                            using (ProcessHandle handle = Native.OpenForCpuSets(worker.Id)) CpuSetNative.SetMask(handle, bits[0]);
                        }
                        int unavailable;
                        ProcessRow changed = service.Processes(out unavailable).Single(row => row.Pid == tracked.Id);
                        CheckResult(service.Apply(changed, bits[1]));
                        Check(service.Store.Items.Single().Mask == bits[0], "the saved original is already restricted");
                        Check((bool)StartupInvoke(form, "Restore", true), "main restore succeeds with only a hard affinity receipt");
                        Check(service.Store.Items.Length == 0 && Read(tracked.Id) == topology.Available && ReadPriority(tracked.Id) == PriorityLevels.Normal,
                            "tracked process receives the Windows mask and normal priority");
                        using (ProcessHandle read = Native.Open(tracked.Id, false)) Check(CpuSetNative.ReadIds(read).Length == 0, "any pre-existing process default CPU set is cleared");
                        Check(Read(untracked.Id) == bits[0] && ReadPriority(untracked.Id) == PriorityLevels.AboveNormal, "untracked process is left alone");
                        using (ProcessHandle read = Native.Open(untracked.Id, false)) Check(CpuSetNative.ReadMask(read) == bits[0], "untracked CPU set remains unchanged");

                        service.Store.Add(new OriginalAffinity { Pid = tracked.Id, Created = changed.Created, Name = changed.Name, OwnerSid = "invalid", MaskHex = bits[0].ToString("X16") });
                        Check(!(bool)StartupInvoke(form, "Restore", true) && service.Store.Items.Length == 1, "owner failure keeps the recovery entry");
                    }
                    finally
                    {
                        foreach (Process worker in new[] { tracked, untracked })
                            if (!worker.HasExited) { worker.Kill(); worker.WaitForExit(); }
                    }
                }
            });
            Case("keep exit: restart applies saved manual settings to current and later instances; restore exit clears them", delegate
            {
                string folder = Path.Combine(working, "retained-exit");
                Directory.CreateDirectory(folder);
                string executable = Path.Combine(folder, "retained-worker.exe");
                string originals = Path.Combine(folder, "original.json");
                File.Copy(typeof(SelfTests).Assembly.Location, executable);
                Func<Process> launch = () => Process.Start(new ProcessStartInfo { FileName = executable, Arguments = "--test-worker", UseShellExecute = false, CreateNoWindow = true });
                ulong target = 1UL << Masks.Bits(topology.Available).First();
                var service = new AffinityService(topology, new SnapshotStore(originals));
                using (Process beforeExit = launch())
                {
                    try
                    {
                        using (var form = new MainForm(service) { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000) })
                        {
                            form.Show(); Application.DoEvents();
                            int ignored;
                            ProcessRow row = service.Processes(out ignored).Single(p => p.Pid == beforeExit.Id);
                            StartupInvoke(form, "ApplyTargets", new[] { row }, target, "测试硬亲和性");
                            StartupInvoke(form, "ApplyCpuTargets", new[] { row }, target, "测试 CPU 集", true);
                            Check(Read(beforeExit.Id) == target, "manual operation sets the isolated worker");
                            ExitViaTray(form, "保留并退出");
                            Check(form.IsDisposed && Read(beforeExit.Id) == target, "keep closes only the tool");
                        }
                    }
                    finally { if (!beforeExit.HasExited) { beforeExit.Kill(); beforeExit.WaitForExit(); } }
                }
                service = new AffinityService(topology, new SnapshotStore(originals));
                using (Process existing = launch())
                using (var restarted = new MainForm(service) { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000) })
                {
                    try
                    {
                        ulong preassigned = Masks.Bits(topology.Available).Select(bit => 1UL << bit).First(mask => mask != target);
                        using (ProcessHandle write = Native.Open(existing.Id, true)) Native.SetAffinity(write, preassigned);
                        restarted.Show(); Application.DoEvents();
                        Check(Read(existing.Id) == target, "reopening immediately applies to an existing new instance");
                        using (ProcessHandle read = Native.Open(existing.Id, false))
                            Check(CpuSetNative.ReadMask(read) == target, "CPU Sets survive alongside hard affinity");
                        using (Process later = launch())
                        {
                            try
                            {
                                StartupInvoke(restarted, "AutomationTick");
                                Check(Read(later.Id) == target, "new instance receives kept settings while tool is open");
                                ExitViaTray(restarted, "恢复并退出");
                                Check(restarted.IsDisposed && Read(existing.Id) == topology.Available && Read(later.Id) == topology.Available,
                                    "restore exit returns both instances to Windows defaults, including a previously restricted original");
                                using (ProcessHandle read = Native.Open(later.Id, false))
                                    Check(CpuSetNative.ReadMask(read) == 0, "restore removes the retained CPU Set");
                            }
                            finally { if (!later.HasExited) { later.Kill(); later.WaitForExit(); } }
                        }
                        var rules = new RetainedAssignments(folder, topology);
                        rules.Load(); Check(!rules.HasRules && !rules.Active, "restart after restore cannot reapply old settings");
                    }
                    finally { if (!existing.HasExited) { existing.Kill(); existing.WaitForExit(); } }
                }
            });
            Case("live tray: close keeps rules running for a new process, pause preserves it and exit restores", delegate
            {
                string folder = Path.Combine(working, "tray-lifecycle");
                Directory.CreateDirectory(folder);
                string executable = Path.Combine(folder, "tray-worker.exe");
                File.Copy(typeof(SelfTests).Assembly.Location, executable);
                var service = new AffinityService(topology, new SnapshotStore(Path.Combine(folder, "original.json")));
                var store = new SchemeStore(Path.Combine(folder, "schemes.json"));
                using (var form = new MainForm(service) { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000) })
                using (var worker = Process.Start(new ProcessStartInfo { FileName = executable, Arguments = "--test-worker", UseShellExecute = false, CreateNoWindow = true }))
                {
                    try
                    {
                        ulong original = Read(worker.Id);
                        ulong target = Masks.Bits(topology.Available).Select(bit => 1UL << bit).First(mask => mask != original);
                        int unavailable;
                        ProcessRow row = service.Processes(out unavailable).Single(p => p.Pid == worker.Id);
                        string id = ManualSchemeRules.Save(store, topology, ManualSchemeRules.Capture(new[] { row }, target, delegate { return null; }), null, "托盘测试");
                        store.SelectScheme(id);
                        form.Show();
                        Application.DoEvents();
                        Check(form.Visible && Read(worker.Id) == original, "startup remains manual");
                        StartupInvoke(form, "BuildTrayMenu");
                        var tray = (ContextMenuStrip)StartupField(form, "trayMenu");
                        var schemes = (ToolStripMenuItem)tray.Items.Cast<ToolStripItem>().Single(item => item.Text == "切换并启用方案");
                        schemes.DropDownItems.Cast<ToolStripItem>().Single(item => item.Text == "托盘测试").PerformClick();
                        Check(Read(worker.Id) == target, "tray activates own custom scheme");
                        form.Close();
                        Check(!form.Visible && !form.IsDisposed && StartupField(form, "automation") != null, "user close hides window and retains session");

                        using (var next = Process.Start(new ProcessStartInfo { FileName = executable, Arguments = "--test-worker", UseShellExecute = false, CreateNoWindow = true }))
                        {
                            try
                            {
                                StartupInvoke(form, "AutomationTick");
                                Check(Read(next.Id) == target, "hidden session applies rule to next instance");
                                StartupInvoke(form, "BuildTrayMenu");
                                var menu = (ContextMenuStrip)StartupField(form, "trayMenu");
                                menu.Items.Cast<ToolStripItem>().Single(item => item.Text == "暂停自动方案").PerformClick();
                                Check(!form.Visible && StartupField(form, "automation") == null && Read(next.Id) == target, "tray pause preserves active affinity");

                                using (var reply = new Timer { Interval = 30 })
                                {
                                    reply.Tick += delegate
                                    {
                                        ExitSettingsDialog dialog = Application.OpenForms.OfType<ExitSettingsDialog>().FirstOrDefault();
                                        if (dialog == null) return;
                                        reply.Stop(); dialog.DialogResult = DialogResult.Yes;
                                    };
                                    reply.Start();
                                    menu.Items.Cast<ToolStripItem>().Single(item => item.Text == "退出 CoreLane").PerformClick();
                                    Check(form.IsDisposed && StartupField(form, "trayIcon") == null, "tray exit disposes form and icon");
                                    Check(Read(worker.Id) == topology.Available && Read(next.Id) == topology.Available && service.Store.Items.Length == 0, "exit confirmation opens all available CPUs for both processes");
                                }
                            }
                            finally
                            {
                                foreach (OriginalAffinity record in service.Store.Items.Where(r => r.Pid == next.Id).ToArray()) service.Restore(record);
                                if (!next.HasExited) { next.Kill(); next.WaitForExit(); }
                            }
                        }
                    }
                    finally
                    {
                        foreach (OriginalAffinity record in service.Store.Items.ToArray()) service.Restore(record);
                        if (!worker.HasExited) { worker.Kill(); worker.WaitForExit(); }
                    }
                }
            });
        }

        private static void ExitViaTray(MainForm form, string label)
        {
            StartupInvoke(form, "BuildTrayMenu");
            var tray = (ContextMenuStrip)StartupField(form, "trayMenu");
            using (var reply = new Timer { Interval = 30 })
            {
                reply.Tick += delegate
                {
                    ExitSettingsDialog dialog = Application.OpenForms.OfType<ExitSettingsDialog>().FirstOrDefault();
                    if (dialog == null) return;
                    reply.Stop();
                    ChildControls(dialog).OfType<SoftButton>().Single(button => button.Text == label).PerformClick();
                };
                reply.Start();
                tray.Items.Cast<ToolStripItem>().Single(item => item.Text == "退出 CoreLane").PerformClick();
            }
        }
    }
}
