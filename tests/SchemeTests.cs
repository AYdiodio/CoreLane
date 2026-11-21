using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static void SchemeTests(string working)
        {
            Topology topology = Topology.Parse(Fixture(false));
            ProcessRow first = new ProcessRow { Pid = 301, Created = 10, Name = "demo.exe", ExecutablePath = @"C:\SchemeFixture\Demo\demo.exe", Mask = 0xffff0000 };
            ProcessRow second = new ProcessRow { Pid = 302, Created = 20, Name = "demo.exe", ExecutablePath = first.ExecutablePath, Mask = 0xffff0000 };
            ProcessRow different = new ProcessRow { Pid = 303, Created = 30, Name = "demo.exe", ExecutablePath = @"C:\SchemeFixture\Other\demo.exe", Mask = 0xffff0000 };
            Func<string, ProcessVisual> none = delegate { return null; };
            List<ApplicationGroup> apps = ApplicationGroup.Build(new[] { first, second, different }, none);
            ApplicationGroup app = apps.Single(a => a.Members.Count == 2);
            string path = Path.Combine(working, "schemes.json"); SchemeStore store = new SchemeStore(path);
            Case("schemes: empty by default, classifications persist and read copies cannot mutate store", delegate
            {
                Check(store.Read().Games.Count == 0 && store.Read().Schemes.Count == 0, "no preset game list");
                store.Classify(app, true); SchemeStore loaded = new SchemeStore(path);
                Check(loaded.Read().Games.Single().IsGame, "yes persisted"); loaded.Read().Games.Clear(); Check(loaded.Read().Games.Count == 1, "read returns detached copy");
                loaded.Classify(app, false); Check(!new SchemeStore(path).Read().Games.Single().IsGame, "no replaces game decision");
            });
            store = new SchemeStore(path); store.Classify(app, true);
            Case("automation: game mode resolves group one from topology and excludes unclassified paths", delegate
            {
                AutomationSession session = new AutomationSession("game", new ProcessRow[0]);
                List<AutomaticTarget> work = session.Plan(apps, store.Read(), topology);
                Check(work.Count == 2 && work.All(t => t.Mask == 0xffff), "only explicitly classified game instances targeted");
                Topology sparse = new Topology(); sparse.Groups.Add(new CpuGroup { Number = 0, Mask = 0x35 });
                Check(new AutomationSession("game", new ProcessRow[0]).Plan(apps, store.Read(), sparse).All(t => t.Mask == 0x35), "no hardcoded 0-15 mask");
            });
            Case("automation: each instance attempted once, denial does not retry and new identities are handled", delegate
            {
                AutomationSession session = new AutomationSession("game", new ProcessRow[0]);
                foreach (AutomaticTarget target in session.Plan(apps, store.Read(), topology)) session.MarkAttempted(target);
                Check(session.Plan(apps, store.Read(), topology).Count == 0, "no polling write loop even after failed attempt");
                first.Created++; Check(session.Plan(apps, store.Read(), topology).Single().Process == first, "new process identity retried once"); first.Created--;
            });
            Case("automation: prompt only new window apps, baseline/background/remembered-no suppressed", delegate
            {
                AutomationSession baseline = new AutomationSession("game", new[] { first, second });
                SchemeData empty = new SchemeData(); List<ApplicationGroup> only = new List<ApplicationGroup> { app };
                Check(baseline.NextQuestion(only, empty, delegate { return true; }) == null, "already running app not prompted on activation");
                AutomationSession session = new AutomationSession("game", new ProcessRow[0]);
                Check(session.NextQuestion(only, empty, delegate { return false; }) == null, "background member not a window app");
                Check(session.NextQuestion(only, empty, delegate { return true; }) == app, "new unknown window app asked");
                SchemeData notGame = new SchemeData(); notGame.Games.Add(new GameClassification { Application = SavedApplication.From(app), IsGame = false });
                Check(session.NextQuestion(only, notGame, delegate { return true; }) == null && session.Plan(only, notGame, topology).All(t => t.Mask == 0xffff0000) && session.Plan(only, notGame, topology).Count == 2, "remembered no does not prompt and uses actual group two");
                session.Defer(app); Check(session.NextQuestion(only, empty, delegate { return true; }) == null, "later deferred during this activation");
            });
            SavedScheme custom = new SavedScheme { Id = "fixture", Name = "用户方案", TopologyKey = SchemeStore.Fingerprint(topology) };
            custom.Rules.Add(new SchemeRule { Application = SavedApplication.From(app), MaskHex = "FFFF0000" });
            custom.Rules.Add(new SchemeRule { Application = SavedApplication.From(apps.Single(a => a.Members.Count == 1)), MaskHex = "00000001" });
            store.Update(delegate(SchemeData next) { next.Schemes.Add(custom); });
            Case("schemes: user rules persist with distinct targets and active custom mode ignores game classification", delegate
            {
                SchemeData loaded = new SchemeStore(path).Read();
                List<AutomaticTarget> work = new AutomationSession("fixture", new ProcessRow[0]).Plan(apps, loaded, topology);
                Check(work.Count == 3 && work.Single(t => t.Process == different).Mask == 1 && work.Where(t => t.Process != different).All(t => t.Mask == 0xffff0000), "custom masks replace game target");
                Check(new AutomationSession("fixture", new ProcessRow[0]).NextQuestion(apps, new SchemeData(), delegate { return true; }) == null, "only game mode asks classifications");
            });
            Case("schemes: invalid writes and duplicate paths leave persisted data intact", delegate
            {
                string before = File.ReadAllText(path);
                Throws(delegate { store.Update(delegate(SchemeData next) { next.Schemes[0].Rules[0].MaskHex = "0"; }); });
                Throws(delegate { store.Update(delegate(SchemeData next) { next.Schemes[0].Name = "游戏"; }); });
                Throws(delegate { store.Update(delegate(SchemeData next) { next.Schemes[0].Rules.Add(next.Schemes[0].Rules[0]); }); });
                Check(File.ReadAllText(path) == before && store.Read().Schemes[0].Rules.Count == 2, "failed validation did not partially update");
                string blocker = Path.Combine(working, "scheme-blocker"); File.WriteAllText(blocker, "fixture");
                SchemeStore blocked = new SchemeStore(Path.Combine(blocker, "schemes.json"));
                Throws(delegate { blocked.Classify(app, true); }); Check(blocked.Read().Games.Count == 0, "save failure does not change in-memory classification");
            });
            Case("schemes: changed topology missing scheme and multi-group prohibit automatic planning", delegate
            {
                SchemeData changed = store.Read(); changed.Schemes[0].TopologyKey = "different machine";
                Throws(delegate { new AutomationSession("fixture", new ProcessRow[0]).Plan(apps, changed, topology); });
                Throws(delegate { new AutomationSession("missing", new ProcessRow[0]).Plan(apps, store.Read(), topology); });
                Throws(delegate { new AutomationSession("game", new ProcessRow[0]).Plan(apps, store.Read(), Topology.Parse(Fixture(true))); });
            });
            Case("schemes: corrupt records and untrusted unknown paths cannot become automatic rules", delegate
            {
                string corrupt = Path.Combine(working, "schemes-corrupt.json"); File.WriteAllText(corrupt, "{broken"); Throws(delegate { new SchemeStore(corrupt); });
                Check(!SavedApplication.ValidPath("demo.exe") && !SavedApplication.ValidPath(@"\\server\share\demo.exe"), "relative and UNC paths excluded");
                ProcessRow unknown = new ProcessRow { Pid = 304, Created = 40, Name = "unknown.exe" };
                ApplicationGroup unknownApp = ApplicationGroup.Build(new[] { unknown }, none).Single();
                Throws(delegate { SavedApplication.From(unknownApp); });
                Check(new AutomationSession("game", new ProcessRow[0]).NextQuestion(new[] { unknownApp }, new SchemeData(), delegate { return true; }) == null, "unconfirmed paths not prompted");
            });
            Case("automation: shared Windows components are never game prompts or game-mode targets", delegate
            {
                ProcessRow host = new ProcessRow { Pid = 401, Created = 50, Name = "svchost.exe", ExecutablePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "svchost.exe") };
                List<ApplicationGroup> hosts = ApplicationGroup.Build(new[] { host }, none);
                AutomationSession session = new AutomationSession("game", new ProcessRow[0]);
                Check(session.NextQuestion(hosts, new SchemeData(), delegate { return true; }) == null, "system host not a classification question");
                SchemeData classified = new SchemeData(); classified.Games.Add(new GameClassification { Application = SavedApplication.From(hosts.Single()), IsGame = true });
                Check(session.Plan(hosts, classified, topology).Count == 0, "stale system-host game classification cannot trigger writes");
            });
        }
        private static void LiveAutomationTest(Topology topology, string working)
        {
            Case("live automation: classified group applies once, new child included, each original restored", delegate
            {
                List<Process> children = new List<Process>();
                try
                {
                    Func<Process> start = delegate { Process child = Process.Start(new ProcessStartInfo { FileName = typeof(SelfTests).Assembly.Location, Arguments = "--test-worker", UseShellExecute = false, CreateNoWindow = true }); children.Add(child); return child; };
                    start(); start(); Dictionary<int, ulong> originals = children.ToDictionary(c => c.Id, c => Read(c.Id));
                    Func<List<ProcessRow>> rows = delegate
                    {
                        List<ProcessRow> values = new List<ProcessRow>();
                        foreach (Process child in children) using (ProcessHandle read = Native.Open(child.Id, false))
                            values.Add(new ProcessRow { Pid = child.Id, Created = Native.Created(read), Name = "automation-fixture.exe", ExecutablePath = Native.ImagePath(read) });
                        return values;
                    };
                    Func<string, ProcessVisual> none = delegate { return null; };
                    List<ApplicationGroup> apps = ApplicationGroup.Build(rows(), none);
                    SchemeStore store = new SchemeStore(Path.Combine(working, "live-auto-schemes.json")); store.Classify(apps.Single(), true);
                    AutomationSession session = new AutomationSession("game", new ProcessRow[0]);
                    AffinityService service = new AffinityService(topology, new SnapshotStore(Path.Combine(working, "live-auto-original.json")));
                    foreach (AutomaticTarget target in session.Plan(apps, store.Read(), topology)) { session.MarkAttempted(target); CheckResult(service.Apply(target.Process, target.Mask)); }
                    ulong expected = CpuSelectionGroup.FromTopology(topology)[0].Mask;
                    Check(children.All(c => Read(c.Id) == expected) && session.Plan(apps, store.Read(), topology).Count == 0, "classified members assigned exactly once");
                    Process later = start(); originals.Add(later.Id, Read(later.Id));
                    List<AutomaticTarget> newWork = session.Plan(ApplicationGroup.Build(rows(), none), new SchemeStore(Path.Combine(working, "live-auto-schemes.json")).Read(), topology);
                    Check(newWork.Count == 1 && newWork[0].Process.Pid == later.Id, "only new child needs assignment");
                    session.MarkAttempted(newWork[0]); CheckResult(service.Apply(newWork[0].Process, newWork[0].Mask));
                    foreach (OriginalAffinity saved in service.Store.Items) CheckResult(service.Restore(saved));
                    Check(children.All(c => Read(c.Id) == originals[c.Id]) && service.Store.Items.Length == 0, "exact originals restored");
                }
                finally { foreach (Process child in children) { if (!child.HasExited) { child.Kill(); child.WaitForExit(); } child.Dispose(); } }
            });
        }
    }
}
