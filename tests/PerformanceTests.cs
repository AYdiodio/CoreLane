using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static void PerformanceTests(string working)
        {
            string folder = Path.Combine(working, "performance");
            Directory.CreateDirectory(folder);
            string executable = Path.Combine(folder, "priority-worker.exe");
            string trigger = Path.Combine(folder, "trigger-worker.exe");
            File.Copy(typeof(SelfTests).Assembly.Location, executable);

            Case("performance profiles are opt-in, per scheme and versioned", delegate
            {
                string file = Path.Combine(folder, "profiles.json");
                var store = new SchemeStore(file);
                Check(store.Read().Version == 1 && store.Read().PerformanceFor("game") == null, "previous data has no implicit rules");
                store.Update(data => data.Schemes.Add(new SavedScheme { Id = "other", Name = "Custom", TopologyKey = "fixture" }));
                var profile = GuardProfile("game", executable, trigger);
                store.Update(data => data.Performance = new List<PerformanceProfile> { profile });
                Throws(delegate { store.Update(data => data.PerformanceFor("game").Rules[0].Background = true); });
                var persisted = new SchemeStore(file).Read();
                Check(persisted.Version == 3 && persisted.PerformanceFor("other") == null && persisted.PerformanceFor("game").GuardEnabled,
                    "profiles reload without affecting other schemes");
                Check(!persisted.PerformanceFor("game").Rules[0].Background, "failed validation leaves old file intact");
                using (var dialog = new PerformanceDialog(store, "game", "游戏", new ProcessRow[0]))
                {
                    DataGridView grid = ChildControls(dialog).OfType<DataGridView>().Single();
                    Check(grid.Rows.Count == 2 && grid.Rows.Cast<DataGridViewRow>().Any(row => (string)row.Tag == executable),
                        "saved program remains editable when offline");
                    TextBox search = ChildControls(dialog).OfType<SearchTextBox>().Single();
                    search.Text = "trigger";
                    Check(grid.Rows.Cast<DataGridViewRow>().Count(row => row.Visible) == 1, "search matches executable path");
                }
            });

            Case("live priority: first original survives repeat changes, reload and layered restore", delegate
            {
                WithPriorityWorker(executable, row =>
                {
                    var service = new PriorityService(folder);
                    CheckResult(service.Apply(row, PriorityLevels.AboveNormal));
                    CheckResult(service.Apply(row, PriorityLevels.BelowNormal));
                    CheckResult(service.Apply(row, PriorityLevels.AboveNormal));
                    Check(service.Records.Single().Original == PriorityLevels.Normal, "first original kept");
                    CheckResult(service.Apply(row, PriorityLevels.BelowNormal, temporary: true));
                    service = new PriorityService(folder);
                    var regular = service.Records.Single(record => record.TemporaryValue == 0);
                    var temporary = service.Records.Single(record => record.TemporaryValue != 0);
                    Check(!service.Restore(regular).Success && ReadPriority(row.Pid) == PriorityLevels.BelowNormal, "restore layers in order");
                    CheckResult(service.Restore(temporary));
                    Check(ReadPriority(row.Pid) == PriorityLevels.AboveNormal, "guard restoration keeps explicit priority");
                    CheckResult(service.Restore(regular));
                    Check(ReadPriority(row.Pid) == PriorityLevels.Normal && new PriorityService(folder).Records.Length == 0, "exact original restored");
                });
            });

            Case("background guard: sustained sampled load, opt-in, low load, trigger exit and external priority", delegate
            {
                WithPriorityWorker(executable, row =>
                {
                    var service = new PriorityService(folder);
                    var guard = new BackgroundProtection();
                    var profile = GuardProfile("game", executable, trigger);
                    var triggerRow = new ProcessRow { Pid = 198720, Created = 20, ExecutablePath = trigger };
                    long start = Stopwatch.GetTimestamp();
                    long ticks = 0;
                    int step = 0;
                    Action<bool, bool> sample = (running, high) =>
                    {
                        row.SampleTime = start + step++ * 2L * Stopwatch.Frequency;
                        row.CpuTicks = ticks += high ? 2 * TimeSpan.TicksPerSecond : 0;
                        row.Priority = ReadPriority(row.Pid);
                        guard.Update(profile, running ? new[] { row, triggerRow } : new[] { row }, service, row.SampleTime, true,
                            (result, key) => Check(result.Success || result.Skipped, result.ToString()));
                    };
                    for (int i = 0; i < 5; i++) sample(false, true);
                    Check(service.Records.Length == 0, "high load without trigger ignored");
                    sample(true, false);
                    sample(true, true);
                    sample(true, false);
                    Check(service.Records.Length == 0, "one high sample ignored");
                    for (int i = 0; i < 3; i++) sample(true, true);
                    Check(service.Records.Length == 0, "six seconds below required eight");
                    sample(true, true);
                    Check(ReadPriority(row.Pid) == PriorityLevels.BelowNormal, "sustained CPU load triggers verified write");
                    for (int i = 0; i < 3; i++) sample(true, false);
                    Check(ReadPriority(row.Pid) == PriorityLevels.BelowNormal, "short dip keeps protection");
                    sample(true, false);
                    Check(ReadPriority(row.Pid) == PriorityLevels.Normal && service.Records.Length == 0, "sustained low CPU load restores");
                    guard.Reset();
                    for (int i = 0; i < 5; i++) sample(true, true);
                    Check(ReadPriority(row.Pid) == PriorityLevels.BelowNormal, "protection can trigger again");
                    sample(false, true);
                    Check(ReadPriority(row.Pid) == PriorityLevels.Normal && service.Records.Length == 0, "trigger exit releases on next sample");
                    for (int i = 0; i < 5; i++) sample(true, true);
                    Check(ReadPriority(row.Pid) == PriorityLevels.Normal, "cooldown prevents immediate reapplication");
                    for (int i = 0; i < 3; i++) sample(true, true);
                    Check(ReadPriority(row.Pid) == PriorityLevels.BelowNormal, "trigger restart can apply after cooldown");
                    using (ProcessHandle handle = Native.Open(row.Pid, true)) Native.SetPriority(handle, PriorityLevels.AboveNormal);
                    sample(true, true);
                    Check(ReadPriority(row.Pid) == PriorityLevels.AboveNormal && service.Records.Length == 0, "external priority change preserved");
                });
            });

            Case("live scheme priority: explicit activation applies saved rule and restore restores only its isolated target", delegate
            {
                Topology topology = Topology.Read();
                if (!topology.CanAssign) return;
                string formData = Path.Combine(folder, "ui");
                Directory.CreateDirectory(formData);
                var store = new SchemeStore(Path.Combine(formData, "schemes.json"));
                store.Update(data => data.Performance = new List<PerformanceProfile> {
                    new PerformanceProfile { SchemeId = "game", Rules = new List<PerformanceRule> {
                        new PerformanceRule { Path = executable, Priority = PriorityLevels.AboveNormal } } } });
                var service = new AffinityService(topology, new SnapshotStore(Path.Combine(formData, "original-affinity.json")));
                WithPriorityWorker(executable, row =>
                {
                    using (var form = new MainForm(service) { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000) })
                    {
                        form.Show();
                        Application.DoEvents();
                        Check(ReadPriority(row.Pid) == PriorityLevels.Normal, "startup leaves saved priority inactive");
                        StartupInvoke(form, "ActivateScheme", "game");
                        Check(ReadPriority(row.Pid) == PriorityLevels.AboveNormal && service.Store.Items.Length == 0, "priority rule runs without changing affinity");
                        var priority = (PriorityService)StartupField(form, "priorityService");
                        Check(priority.Records.Single().Pid == row.Pid, "only isolated worker was written");
                        StartupInvoke(form, "Restore", true);
                        Check(ReadPriority(row.Pid) == PriorityLevels.Normal && priority.Records.Length == 0 && StartupField(form, "automation") == null,
                            "restore command pauses scheme and restores exact original");
                    }
                });
            });
        }

        private static PerformanceProfile GuardProfile(string id, string executable, string trigger)
        {
            return new PerformanceProfile { SchemeId = id, GuardEnabled = true, Rules = new List<PerformanceRule> {
                new PerformanceRule { Path = trigger, Trigger = true }, new PerformanceRule { Path = executable, Background = true } } };
        }

        private static void WithPriorityWorker(string executable, Action<ProcessRow> test)
        {
            using (Process worker = Process.Start(new ProcessStartInfo { FileName = executable, Arguments = "--test-worker", UseShellExecute = false, CreateNoWindow = true }))
            {
                try
                {
                    ProcessRow row;
                    using (ProcessHandle handle = Native.Open(worker.Id, true))
                    {
                        Native.SetPriority(handle, PriorityLevels.Normal);
                        row = new ProcessRow { Pid = worker.Id, Created = Native.Created(handle), Name = Path.GetFileName(executable),
                            ExecutablePath = Native.ImagePath(handle), Priority = Native.Priority(handle) };
                    }
                    test(row);
                }
                finally { if (!worker.HasExited) { worker.Kill(); worker.WaitForExit(); } }
            }
        }

        private static uint ReadPriority(int pid)
        {
            using (ProcessHandle handle = Native.Open(pid, false)) return Native.Priority(handle);
        }
    }
}
