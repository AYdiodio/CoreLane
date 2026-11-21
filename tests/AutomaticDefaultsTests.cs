using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static void AutomaticDefaultsTests(string working)
        {
            Case("game question: explicit no target and unavailable second-group message match scheduling policy", delegate
            {
                Topology t = Topology.Parse(Fixture(false)); ApplicationGroup app = new ApplicationGroup { Title = "分类测试" };
                app.Members.Add(new ProcessRow { Pid = 500, Created = 1, Name = "fixture.exe", ExecutablePath = @"C:\NonGameFixture\fixture.exe" });
                using (GameQuestionDialog dialog = new GameQuestionDialog(app, CpuSelectionGroup.GameTarget(t), true, CpuSelectionGroup.NonGameTarget(t)))
                {
                    Check(ChildControls(dialog).OfType<Label>().Any(l => l.Text.Contains("否：记住为非游戏，分配到 CPU 组 2（CPU 16–31）")), "no has its own concrete destination before confirmation");
                    Check(((Control)dialog.CancelButton).Text == "稍后", "classification is never selected automatically");
                }
                using (GameQuestionDialog absent = new GameQuestionDialog(app, CpuSelectionGroup.GameTarget(t)))
                    Check(ChildControls(absent).OfType<Label>().Any(l => l.Text.Contains("无可用组 2，不修改分配")), "missing group is explicit and does not claim success");
            });
            Case("non-game rules: sparse actual group two, remembered classification and new instances; unknown apps excluded", delegate
            {
                Topology t = new Topology(); t.Groups.Add(new CpuGroup { Number = 0, Mask = 0x8000000000000005UL });
                t.Clusters.Add(new CpuCluster { Group = 0, Mask = 5 }); t.Clusters.Add(new CpuCluster { Group = 0, Mask = 1UL << 63 });
                ProcessRow row = new ProcessRow { Pid = 501, Created = 1, Name = "nongame.exe", ExecutablePath = @"C:\NonGameFixture\nongame.exe" };
                List<ApplicationGroup> apps = ApplicationGroup.Build(new[] { row }, delegate { return null; });
                string path = Path.Combine(working, "nongame-saved.json"); SchemeStore store = new SchemeStore(path); store.Classify(apps.Single(), false);
                SchemeData saved = new SchemeStore(path).Read(); AutomationSession session = new AutomationSession("game", new ProcessRow[0]);
                AutomaticTarget target = session.Plan(apps, saved, t).Single(); Check(target.Mask == 1UL << 63, "group two is native sparse bit, never hardcoded half");
                session.MarkAttempted(target); Check(session.Plan(apps, saved, t).Count == 0, "attempt once even if write was denied");
                row.Created++; Check(session.Plan(apps, saved, t).Single().Mask == target.Mask, "later instance gets remembered non-game target");
                Check(session.NextQuestion(apps, saved, delegate { return true; }) == null, "non-game classification suppresses repeated question");
                Check(session.Plan(apps, new SchemeData(), t).Count == 0, "unclassified software is not swept into group two");
                Topology single = new Topology(); single.Groups.Add(new CpuGroup { Number = 0, Mask = 5 });
                Check(CpuSelectionGroup.NonGameTarget(single) == null && session.Plan(apps, saved, single).Count == 0, "missing second group skips without fallback");
                Throws(delegate { session.Plan(apps, saved, Topology.Parse(Fixture(true))); });
            });
            Case("non-game rules: Windows components stay excluded and custom masks remain independent", delegate
            {
                Topology t = Topology.Parse(Fixture(false)); ProcessRow row = new ProcessRow { Pid = 502, Created = 1, Name = "normal.exe", ExecutablePath = @"C:\NonGameFixture\normal.exe" };
                ApplicationGroup app = ApplicationGroup.Build(new[] { row }, delegate { return null; }).Single();
                ProcessRow host = new ProcessRow { Pid = 503, Created = 1, Name = "svchost.exe", ExecutablePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "svchost.exe") };
                ApplicationGroup system = ApplicationGroup.Build(new[] { host }, delegate { return null; }).Single();
                SchemeData data = new SchemeData(); data.Games.Add(new GameClassification { Application = SavedApplication.From(app), IsGame = false }); data.Games.Add(new GameClassification { Application = SavedApplication.From(system), IsGame = false });
                Check(new AutomationSession("game", new ProcessRow[0]).Plan(new[] { app, system }, data, t).Single().Process == row, "stale non-game system record cannot schedule a system host");
                SavedScheme custom = new SavedScheme { Id = "custom-nongame", Name = "自定义测试", TopologyKey = SchemeStore.Fingerprint(t) };
                custom.Rules.Add(new SchemeRule { Application = SavedApplication.From(app), MaskHex = "4" }); data.Schemes.Add(custom);
                Check(new AutomationSession(custom.Id, new ProcessRow[0]).Plan(new[] { app }, data, t).Single().Mask == 4, "non-game does not override custom single core");
            });
            Topology actual = Topology.Read(); if (!actual.CanAssign || CpuSelectionGroup.NonGameTarget(actual) == null) return;
            Case("live game rules: no answer assigns immediately and resumes only when game scheme is enabled", delegate
            {
                string directory = Path.Combine(working, "nongame-live"); Directory.CreateDirectory(directory);
                string workerPath = Path.Combine(directory, "nongame-unique.exe"); File.Copy(typeof(SelfTests).Assembly.Location, workerPath);
                string config = typeof(SelfTests).Assembly.Location + ".config"; if (File.Exists(config)) File.Copy(config, workerPath + ".config");
                using (Process child = Process.Start(new ProcessStartInfo { FileName = workerPath, Arguments = "--test-worker", UseShellExecute = false, CreateNoWindow = true }))
                {
                    SchemeStore store = new SchemeStore(Path.Combine(directory, "schemes.json")); AffinityService service = new AffinityService(actual, new SnapshotStore(Path.Combine(directory, "original.json")));
                    ulong groupTwo = CpuSelectionGroup.NonGameTarget(actual).Mask;
                    ulong customMask = 1UL << Masks.Bits(actual.Available).First();
                    try
                    {
                        int unused; ApplicationGroup app = ApplicationGroup.Build(service.Processes(out unused).Where(r => r.Pid == child.Id), delegate { return null; }).Single();
                        store.Update(delegate(SchemeData data) { SavedScheme custom = new SavedScheme { Id = "auto-tab", Name = "仅测试进程", TopologyKey = SchemeStore.Fingerprint(actual) }; custom.Rules.Add(new SchemeRule { Application = SavedApplication.From(app), MaskHex = customMask.ToString("X16") }); data.Schemes.Add(custom); });
                        store.SelectScheme("auto-tab");
                        using (MainForm form = new MainForm(service))
                        {
                            typeof(MainForm).GetField("schemeStore", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(form, store);
                            StartupInvoke(form, "ActivateScheme", "auto-tab");
                            Check(StartupField(form, "automation") != null && Read(child.Id) == customMask, "explicit activation applies custom scheme immediately");
                            StartupInvoke(form, "ActivateScheme", "game");
                            StartupInvoke(form, "RememberGameClassification", app, false);
                            Check(Read(child.Id) == groupTwo && !new SchemeStore(Path.Combine(directory, "schemes.json")).Read().Games.Single().IsGame, "no is persisted first and assigned to group two without waiting for timer");
                            StartupInvoke(form, "StopAutomation", "test pause"); service.Apply(app.Members[0], customMask);
                            StartupInvoke(form, "ActivateScheme", "game");
                            Check(Read(child.Id) == groupTwo && StartupField(form, "automation") != null, "explicit game activation reapplies remembered non-game");
                            StartupInvoke(form, "ActivateScheme", "auto-tab"); Check(Read(child.Id) == customMask, "custom scheme wins over remembered non-game");
                            StartupInvoke(form, "Restore", true); Check(Read(child.Id) == actual.Available && service.Store.Items.Length == 0, "restore pauses automatic handling and opens all available CPUs");
                        }
                        Check(service.Store.Items.All(r => r.Pid == child.Id), "all changes belong to dedicated child");
                    }
                    finally
                    {
                        foreach (OriginalAffinity record in service.Store.Items.ToArray()) service.Restore(record);
                        if (!child.HasExited) { child.Kill(); child.WaitForExit(); }
                    }
                }
            });
        }
    }
}
