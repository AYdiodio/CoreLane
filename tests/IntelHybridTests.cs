using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static Topology HybridFixture()
        {
            // Deliberately non-contiguous and out of P/E order, with/without SMT and CPU 63.
            ulong p = (1UL << 3) | (1UL << 9) | (1UL << 63);
            ulong e = (1UL << 1) | (1UL << 11) | (1UL << 45);
            List<byte> data = new List<byte>(); data.AddRange(GroupRecord(new[] { p | e }));
            data.AddRange(CoreRecord(0, (1UL << 3) | (1UL << 63), 7));
            data.AddRange(CoreRecord(0, 1UL << 1, 2)); data.AddRange(CoreRecord(0, 1UL << 9, 7));
            data.AddRange(CoreRecord(0, 1UL << 11, 2)); data.AddRange(CoreRecord(0, 1UL << 45, 2));
            data.AddRange(CacheRecord(p | e, 30 * 1048576));
            Topology topology = Topology.Parse(data.ToArray()); topology.CpuVendor = "GenuineIntel"; topology.CpuName = "Synthetic Intel hybrid";
            return topology;
        }
        private static void IntelHybridTests()
        {
            Case("Intel hybrid: native EfficiencyClass parsed; higher relative class identifies P including SMT", delegate
            {
                Topology topology = HybridFixture(); IntelHybridInfo info = IntelHybridInfo.Detect(topology);
                Check(topology.Cores[0].EfficiencyClass == 7 && topology.Cores[1].EfficiencyClass == 2, "native byte offset parsed");
                Check(info.Recognized && info.PerformanceCount == 2 && info.EfficientCount == 3, "physical counts differ from logical CPU counts");
                Check(info.MaskFor(0, true) == ((1UL << 3) | (1UL << 9) | (1UL << 63)), "both SMT siblings and non-SMT P retained, not first CPU numbers");
                Check((info.MaskFor(0, true) & info.MaskFor(0, false)) == 0 && (info.MaskFor(0, true) | info.MaskFor(0, false)) == topology.Available, "P and E exactly partition native CPUs");
            });
            Case("Intel hybrid: AMD and missing vendor never inherit P/E labels from cache SMT or name", delegate
            {
                Topology topology = HybridFixture(); topology.CpuVendor = "AuthenticAMD";
                Check(!IntelHybridInfo.Detect(topology).Recognized && CpuSelectionGroup.GameTarget(topology).Mask == topology.Available, "AMD stays on ordinary group even with differing classes");
                topology.CpuVendor = null; Check(!IntelHybridInfo.Detect(topology).Recognized, "Intel text in CPU name is insufficient");
            });
            Case("Intel hybrid: uniform zero/nonzero classes and unsupported three-tier data do not guess", delegate
            {
                Topology topology = HybridFixture(); foreach (CpuCore core in topology.Cores) core.EfficiencyClass = 0;
                Check(!IntelHybridInfo.Detect(topology).Recognized, "no heterogeneity reported");
                foreach (CpuCore core in topology.Cores) core.EfficiencyClass = 7;
                Check(!IntelHybridInfo.Detect(topology).Recognized, "nonzero alone does not imply P/E");
                topology = HybridFixture(); topology.Cores[4].EfficiencyClass = 4;
                Check(!IntelHybridInfo.Detect(topology).Recognized && CpuSelectionGroup.GameTarget(topology).Mask == topology.Available, "unknown tiers fall back to explicit ordinary group");
            });
            Case("Intel hybrid: incomplete overlapping or foreign-group core maps disable preset", delegate
            {
                Topology topology = HybridFixture(); topology.Cores.RemoveAt(4);
                Check(!IntelHybridInfo.Detect(topology).Recognized, "incomplete coverage");
                topology = HybridFixture(); topology.Cores[1].Mask |= topology.Cores[0].Mask;
                Check(!IntelHybridInfo.Detect(topology).Recognized, "overlapping physical masks");
                topology = HybridFixture(); topology.Cores[1].Group = 1;
                Check(!IntelHybridInfo.Detect(topology).Recognized, "unknown Windows group not folded into zero");
            });
            Case("Intel hybrid: multiple Windows groups retain identities and remain read-only", delegate
            {
                Topology topology = HybridFixture(); topology.Groups.Add(new CpuGroup { Number = 1, Mask = 3 });
                topology.Cores.Add(new CpuCore { Number = 5, Group = 1, Mask = 1, EfficiencyClass = 7 });
                topology.Cores.Add(new CpuCore { Number = 6, Group = 1, Mask = 2, EfficiencyClass = 2 });
                IntelHybridInfo info = IntelHybridInfo.Detect(topology);
                Check(info.Recognized && info.MaskFor(1, true) == 1 && (info.MaskFor(0, true) & 1) == 0, "group-relative indices stay separate");
                Check(!topology.CanAssign, "hybrid detection never enables multi-group writes");
                using (CpuMaskDialog dialog = new CpuMaskDialog(topology, 0, false))
                    Check(!ChildControls(dialog).OfType<Button>().Single(b => b.Text == "仅大核（P）").Enabled, "preset disabled in read-only topology");
            });
            Case("Intel hybrid: game plans target only P while custom rules remain independent", delegate
            {
                Topology topology = HybridFixture();
                ProcessRow row = new ProcessRow { Pid = 901, Created = 12, Name = "fixture.exe", ExecutablePath = @"C:\HybridFixture\fixture.exe", Mask = topology.Available };
                List<ApplicationGroup> apps = ApplicationGroup.Build(new[] { row }, delegate { return null; });
                SchemeData data = new SchemeData(); data.Games.Add(new GameClassification { Application = SavedApplication.From(apps[0]), IsGame = true });
                AutomationSession session = new AutomationSession("game", new ProcessRow[0]);
                AutomaticTarget target = session.Plan(apps, data, topology).Single();
                Check(target.Mask == IntelHybridInfo.Detect(topology).MaskFor(0, true) && target.Mask == CpuSelectionGroup.FromTopology(topology).First().Mask, "game and first displayed group both target actual P cores");
                Check(CpuSelectionGroup.GameTarget(topology).Name.Contains("P"), "question/hint target has P label");
                session.MarkAttempted(target); Check(session.Plan(apps, data, topology).Count == 0, "P target still attempts each instance once");
                SavedScheme custom = new SavedScheme { Id = "custom", Name = "自定义", TopologyKey = SchemeStore.Fingerprint(topology) };
                custom.Rules.Add(new SchemeRule { Application = SavedApplication.From(apps[0]), MaskHex = (1UL << 1).ToString("X16") }); data.Schemes.Add(custom);
                Check(new AutomationSession("custom", new ProcessRow[0]).Plan(apps, data, topology).Single().Mask == (1UL << 1), "explicit custom E selection not overridden by game classification");
                data.Games[0].IsGame = false; Check(CpuSelectionGroup.NonGameTarget(topology).Mask == IntelHybridInfo.Detect(topology).MaskFor(0, false) &&
                    new AutomationSession("game", new ProcessRow[0]).Plan(apps, data, topology).Single().Mask == IntelHybridInfo.Detect(topology).MaskFor(0, false),
                    "non-game classification follows the displayed E group");
            });
            Case("Intel hybrid: picker preset replaces previous E selection exactly and shows P/E labels", delegate
            {
                Topology topology = HybridFixture(); IntelHybridInfo info = IntelHybridInfo.Detect(topology);
                using (CpuMaskDialog dialog = new CpuMaskDialog(topology, info.MaskFor(0, false), false))
                {
                    Button button = ChildControls(dialog).OfType<Button>().Single(b => b.Text == "仅大核（P）");
                    // PerformClick needs a visible parent; invoke OnClick to test the actual preset event without a desktop window.
                    typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(button, new object[] { EventArgs.Empty });
                    Check(dialog.SelectedMask == info.MaskFor(0, true), "no old E bits leak into P-only selection");
                    Check(ChildControls(dialog).OfType<CheckBox>().Count(b => b.Text.EndsWith("P", StringComparison.Ordinal)) == 3, "each P logical processor labeled");
                    Check(ChildControls(dialog).OfType<CheckBox>().Count(b => b.Text.EndsWith("E", StringComparison.Ordinal)) == 3, "each E logical processor labeled");
                }
                using (CpuMaskDialog details = new CpuMaskDialog(topology, 0, true))
                    Check(!ChildControls(details).OfType<Button>().Single(b => b.Text == "仅大核（P）").Enabled, "device info cannot alter mask");
            });
        }
        private static void LiveHybridPolicyTest(string working)
        {
            Case("live hybrid policy fixture: P-only game target applies to own child and restores exact original", delegate
            {
                Topology fixture = Topology.Read();
                if (fixture.Cores.Select(c => c.Number).Distinct().Count() < 2)
                { lines.Add("INFO live hybrid policy fixture skipped: requires two physical cores"); return; }
                // Only annotate an in-memory test topology; this is not Intel hardware validation.
                fixture.CpuVendor = "GenuineIntel"; int lastCore = fixture.Cores.Max(c => c.Number);
                foreach (CpuCore core in fixture.Cores) core.EfficiencyClass = (byte)(core.Number == lastCore ? 7 : 2);
                ulong targetMask = IntelHybridInfo.Detect(fixture).MaskFor(0, true);
                using (Process worker = Process.Start(new ProcessStartInfo { FileName = typeof(SelfTests).Assembly.Location, Arguments = "--test-worker", UseShellExecute = false, CreateNoWindow = true }))
                {
                    try
                    {
                        ulong original = Read(worker.Id); ProcessRow row;
                        using (ProcessHandle handle = Native.Open(worker.Id, false))
                            row = new ProcessRow { Pid = worker.Id, Created = Native.Created(handle), Name = "hybrid-test.exe", ExecutablePath = Native.ImagePath(handle), Mask = original };
                        List<ApplicationGroup> apps = ApplicationGroup.Build(new[] { row }, delegate { return null; });
                        SchemeData data = new SchemeData(); data.Games.Add(new GameClassification { Application = SavedApplication.From(apps[0]), IsGame = true });
                        AutomaticTarget target = new AutomationSession("game", new ProcessRow[0]).Plan(apps, data, fixture).Single();
                        AffinityService service = new AffinityService(fixture, new SnapshotStore(Path.Combine(working, "hybrid-child.json")));
                        Check(target.Mask == targetMask, "game planning resolves annotated P core");
                        CheckResult(service.Apply(row, target.Mask)); Check(Read(worker.Id) == targetMask, "only test child's mask changed");
                        Check(service.Store.Items.Single().Mask == original, "exact original recorded before write");
                        CheckResult(service.Restore(service.Store.Items.Single()));
                        Check(Read(worker.Id) == original && service.Store.Items.Length == 0, "exact original restored and recovery record removed");
                    }
                    finally { if (!worker.HasExited) { worker.Kill(); worker.WaitForExit(); } }
                }
            });
        }
    }
}
