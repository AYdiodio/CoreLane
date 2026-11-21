using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static readonly List<string> lines = new List<string>();
        private static int passed;

        internal static int Run(string reportPath)
        {
            string report = Path.GetFullPath(reportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            string working = Path.Combine(Path.GetDirectoryName(report), "run-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(working);
            int exit = 0;
            try
            {
                Case("mask: empty / unavailable bits rejected; CPU 63 retained", delegate
                {
                    Throws(delegate { Masks.Validate(0, ulong.MaxValue); });
                    Throws(delegate { Masks.Validate(4, 3); });
                    Masks.Validate(1UL << 63, ulong.MaxValue);
                    Check(Masks.Format(ulong.MaxValue) == "0–63", "64-bit mask formatting");
                    Check(Masks.Format(0x8000000000000001UL) == "0, 63", "sparse mask");
                });
                Case("topology: synthetic 16 cores / 32 threads / 2 distinct L3 clusters", delegate
                {
                    Topology topology = Topology.Parse(Fixture(false));
                    Check(topology.CanAssign && topology.Available == uint.MaxValue, "available CPU mask");
                    Check(topology.Cores.Count == 16 && topology.Clusters.Count == 2, "core/cache count");
                    Check(topology.Clusters[0].Mask == 0xffff && topology.Clusters[1].Mask == 0xffff0000, "L3 group masks");
                    Check(topology.Clusters[0].CacheBytes == 96U * 1048576, "L3 size offset");
                });
                Case("topology: multi-group read supported, assignment disabled", delegate
                {
                    Topology topology = Topology.Parse(Fixture(true));
                    Check(topology.Groups.Count == 2 && !topology.CanAssign, "multi-group guard");
                });
                Case("UI groups: two L3 groups preserve exact masks and group labels", delegate
                {
                    Topology topology = Topology.Parse(Fixture(false));
                    List<CpuSelectionGroup> groups = CpuSelectionGroup.FromTopology(topology);
                    Check(groups.Count == 2 && groups[0].Mask == 0xffff && groups[1].Mask == 0xffff0000, "native L3 masks preserved");
                    Check(CpuSelectionGroup.Describe(0xffff, groups) == "CPU 组 1", "first group label");
                    Check(CpuSelectionGroup.Describe(0xffff0000, groups) == "CPU 组 2", "second group label");
                    Check(CpuSelectionGroup.Describe(1, groups).StartsWith("自定义", StringComparison.Ordinal), "partial mask remains custom");
                });
                Case("UI groups: sparse non-32-CPU machines do not get invented CPU numbers", delegate
                {
                    Topology topology = new Topology();
                    topology.Groups.Add(new CpuGroup { Number = 0, Mask = 0x8000000000000035UL });
                    List<CpuSelectionGroup> groups = CpuSelectionGroup.FromTopology(topology);
                    Check(groups.Count == 1 && groups[0].Mask == topology.Available, "fallback preserves sparse and high bits");
                    Check(CpuSelectionGroup.Describe(topology.Available, groups) == "CPU 组 1", "fallback name");
                });
                Case("UI groups: multi-group masks retain Windows group identity", delegate
                {
                    Topology topology = Topology.Parse(Fixture(true));
                    List<CpuSelectionGroup> groups = CpuSelectionGroup.FromTopology(topology);
                    Check(groups.Count == 3 && groups[2].WindowsGroup == 1 && groups[2].Mask == 3, "group 1 is not folded into group 0");
                    Check(!topology.CanAssign, "presentation never enables unsupported assignment");
                });
                Case("topology: unknown records skipped; malformed lengths rejected", delegate
                {
                    byte[] valid = Fixture(false);
                    byte[] unknown = Record(999, new byte[0]);
                    Check(Topology.Parse(unknown.Concat(valid).ToArray()).Cores.Count == 16, "unknown relationship");
                    Throws(delegate { Topology.Parse(valid.Take(valid.Length - 1).ToArray()); });
                    Throws(delegate { Topology.Parse(new byte[8]); });
                });
                Case("topology: CPU 63 and non-contiguous CPUs parsed without renumbering", delegate
                {
                    List<byte> bytes = new List<byte>();
                    bytes.AddRange(GroupRecord(new[] { 0x8000000000000001UL }));
                    bytes.AddRange(CoreRecord(0, 1)); bytes.AddRange(CoreRecord(0, 1UL << 63));
                    Topology topology = Topology.Parse(bytes.ToArray());
                    Check(topology.Available == 0x8000000000000001UL, "group mask");
                    Check(topology.Cores[1].Mask == (1UL << 63), "high CPU index");
                });
                Case("snapshot: durable save / reload / first-original retained / removal", delegate
                {
                    string path = Path.Combine(working, "store.json");
                    SnapshotStore store = new SnapshotStore(path);
                    store.Add(new OriginalAffinity { Pid = 123, Created = 456, Name = "fixture", MaskHex = "8000000000000001", OwnerSid = "fixture" });
                    store.Add(new OriginalAffinity { Pid = 123, Created = 456, Name = "fixture", MaskHex = "1", OwnerSid = "fixture" });
                    SnapshotStore reloaded = new SnapshotStore(path);
                    Check(reloaded.Items.Length == 1 && reloaded.Items[0].Mask == 0x8000000000000001UL, "first snapshot");
                    reloaded.Remove(reloaded.Items[0]);
                    Check(new SnapshotStore(path).Items.Length == 0, "persisted removal");
                });
                Case("snapshot: corrupt recovery data blocks operations", delegate
                {
                    string path = Path.Combine(working, "corrupt.json");
                    File.WriteAllText(path, "invalid json");
                    Throws(delegate { new SnapshotStore(path); });
                });
                Case("permissions: SET_LIMITED_INFORMATION cannot substitute for SET_INFORMATION", delegate
                {
                    Check(!Native.HasAffinityWriteAccess(0x00103000), "filtered handle is not writable");
                    Check(!Native.HasAffinityWriteAccess(0x00002000), "limited information right is insufficient");
                    Check(Native.HasAffinityWriteAccess(0x00103200), "actual SET_INFORMATION is recognized");
                });
                ApplicationGroupingTests();
                ApplicationOrderingTests();
                IconGroupingTests();
                IconSimilarityTests();
                IconInteractionTests();
                DesktopDragTests();
                LogoStyleTests();
                IconAppearanceTests();
                CpuPickerTests();
                ResizeScrollTests(working);
                CoreEditTests(working);
                DragGestureTests(working);
                AutomaticDefaultsTests(working);
                IntelHybridTests();
                SchemeTests(working);
                ManualSchemeSaveTests(working);
                GameSchemeRuleTests(working);
                StartupAutomationTests(working);
                CpuAssignmentLabelTests(working);
                CapsuleControlsTests(working);
                ReleaseUpdateTests();
                UnifiedViewTests(working);
                TrayRuleTests(working);
                PerformanceTests(working);
                Topology actual = Topology.Read();
                Case("live topology read", delegate
                {
                    Check(actual.Groups.Count > 0 && actual.Cores.Count > 0, "live topology");
                    lines.Add("INFO " + actual.CpuName + "; logical=" + actual.Groups.Sum(g => Masks.Bits(g.Mask).Count()) + "; physical=" + actual.Cores.Select(c => c.Number).Distinct().Count());
                    foreach (CpuCluster cluster in actual.Clusters) lines.Add("INFO " + cluster);
                });
                if (actual.CanAssign) { LiveChildTests(actual, working); LiveApplicationTest(actual, working); LiveAutomationTest(actual, working); LiveHybridPolicyTest(working); LiveStartupAutomationTest(actual, working); }
                else lines.Add("SKIP child affinity tests: machine uses multiple processor groups.");
            }
            catch (Exception error) { lines.Add("FAIL " + error); exit = 1; }
            lines.Add("RESULT " + (exit == 0 ? "PASS" : "FAIL") + " - " + passed + " test cases passed");
            File.WriteAllLines(report, lines.ToArray(), new UTF8Encoding(true));
            return exit;
        }

        private static void ApplicationGroupingTests()
        {
            ProcessRow main = new ProcessRow { Pid = 101, Created = 10, Name = "demo.exe", ExecutablePath = @"C:\GroupingFixture\Demo\demo.exe", Mask = 1 };
            ProcessRow other = new ProcessRow { Pid = 102, Created = 20, Name = "demo.exe", ExecutablePath = @"c:\GroupingFixture\Demo\DEMO.EXE", Mask = 2 };
            ProcessRow helper = new ProcessRow { Pid = 103, Created = 30, Name = "helper.exe", ExecutablePath = @"C:\GroupingFixture\Demo\resources\helper.exe", Mask = 1 };
            Func<string, ProcessVisual> none = delegate { return null; };
            Func<string, ProcessVisual> metadata = delegate { return new ProcessVisual { ProductName = "Fixture App", CompanyName = "Fixture Publisher", DisplayName = "Helper description" }; };
            Case("apps: exact executable path merges instances and deduplicates operation targets", delegate
            {
                ApplicationGroup app = ApplicationGroup.Build(new[] { main, other }, none).Single();
                Check(app.Members.Count == 2, "same path ignores case");
                Check(ApplicationGroup.Targets(new[] { app, app }).Count == 2, "all members exactly once");
            });
            Case("apps: same executable name in different installations remains separate", delegate
            {
                ProcessRow unrelated = new ProcessRow { Pid = 104, Created = 40, Name = "demo.exe", ExecutablePath = @"C:\GroupingFixture\Other\demo.exe" };
                Check(ApplicationGroup.Build(new[] { main, unrelated }, metadata).Count == 2, "same product in sibling installs not combined");
            });
            Case("apps: matching product publisher and parent install directory include helper executables", delegate
            {
                List<ApplicationGroup> apps = ApplicationGroup.Build(new[] { main, other, helper }, metadata);
                Check(apps.Count == 1 && apps[0].Members.Count == 3, "known helper included");
                Func<string, ProcessVisual> differentPublisher = delegate(string path) { return new ProcessVisual { ProductName = "Fixture App", CompanyName = path.EndsWith("helper.exe", StringComparison.OrdinalIgnoreCase) ? "Another Publisher" : "Fixture Publisher" }; };
                Check(ApplicationGroup.Build(new[] { main, helper }, differentPublisher).Count == 2, "publisher mismatch isolated");
                  Func<string, ProcessVisual> helperProduct = delegate(string path) { return new ProcessVisual {
                      ProductName = path.EndsWith("helper.exe", StringComparison.OrdinalIgnoreCase) ? "Fixture App System Helper" : "Fixture App",
                      CompanyName = "Fixture Publisher" }; };
                  Check(ApplicationGroup.Build(new[] { main, helper }, helperProduct).Count == 1, "nested same-publisher product-family helper included");
                  ProcessRow sibling = new ProcessRow { Pid = 105, Created = 50, Name = "helper.exe", ExecutablePath = @"C:\GroupingFixture\Demo\helper.exe" };
                  Check(ApplicationGroup.Build(new[] { main, sibling }, helperProduct).Count == 2, "different products beside one another remain separate");
            });
            Case("apps: Windows hosts and unavailable paths are never merged using display names", delegate
            {
                string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                ProcessRow a = new ProcessRow { Pid = 201, Created = 1, Name = "a.exe", ExecutablePath = Path.Combine(windows, "System32", "a.exe") };
                ProcessRow b = new ProcessRow { Pid = 202, Created = 2, Name = "b.exe", ExecutablePath = Path.Combine(windows, "System32", "b.exe") };
                Check(ApplicationGroup.Build(new[] { a, b }, metadata).Count == 2, "shared system directory isolated");
                a.ExecutablePath = b.ExecutablePath = null;
                Check(ApplicationGroup.Build(new[] { a, b }, none).Count == 2, "unknown paths use exact process identities");
            });
            Case("apps: new or reused process identities invalidate a previous group selection", delegate
            {
                ApplicationGroup app = ApplicationGroup.Build(new[] { main, other }, none).Single();
                HashSet<string> checkedKeys = new HashSet<string>(new[] { main.Key, other.Key });
                Check(app.WasSelected(checkedKeys), "unchanged membership retains selection");
                app.Members.Add(helper);
                Check(!app.WasSelected(checkedKeys), "new helper requires explicit new selection");
                app.Members.Remove(helper);
                app.Members[1] = new ProcessRow { Pid = other.Pid, Created = other.Created + 1 };
                Check(!app.WasSelected(checkedKeys), "PID reuse never silently selected");
            });
            Case("apps: mixed affinity unreadable members and partial failures stay visible", delegate
            {
                ApplicationGroup app = ApplicationGroup.Build(new[] { main, other }, none).Single();
                Check(app.Allocation(new List<CpuSelectionGroup>()) == "多种分配", "different member masks not misrepresented");
                Dictionary<string, OperationResult> outcomes = new Dictionary<string, OperationResult>();
                outcomes[main.Key] = new OperationResult { Success = true };
                Check(app.ResultText(outcomes).Contains("未操作"), "missing outcomes not green success");
                outcomes[other.Key] = new OperationResult { Success = false };
                Check(app.ResultText(outcomes).StartsWith("部分失败", StringComparison.Ordinal), "one failure not hidden");
                other.ReadError = "fixture denied";
                Check(app.Allocation(new List<CpuSelectionGroup>()) == "部分无法读取", "unreadable member not hidden");
                other.ReadError = null;
            });
            Case("apps: matching a helper returns the complete application membership", delegate
            {
                ApplicationGroup app = ApplicationGroup.Build(new[] { main, other, helper }, metadata).Single();
                Check(app.Matches(new[] { "helper.exe" }, metadata) && app.Members.Count == 3, "filter does not truncate grouped targets");
                Check(!app.Matches(new[] { "unrelated.exe" }, metadata), "unmatched app stays hidden");
            });
        }

        private static void LiveApplicationTest(Topology topology, string working)
        {
            Case("live apps: grouped children apply together and restore each exact original", delegate
            {
                int[] bits = Masks.Bits(topology.Available).ToArray();
                if (bits.Length < 2) { lines.Add("INFO grouped two-mask check needs at least two CPUs"); return; }
                Process first = Process.Start(new ProcessStartInfo { FileName = typeof(SelfTests).Assembly.Location, Arguments = "--test-worker", UseShellExecute = false, CreateNoWindow = true });
                Process second = null;
                try
                {
                    second = Process.Start(new ProcessStartInfo { FileName = typeof(SelfTests).Assembly.Location, Arguments = "--test-worker", UseShellExecute = false, CreateNoWindow = true });
                    ulong initialFirst = Read(first.Id), initialSecond = 1UL << bits[bits.Length - 1], target = 1UL << bits[0];
                    using (ProcessHandle write = Native.Open(second.Id, true)) Native.SetAffinity(write, initialSecond);
                    List<ProcessRow> members = new List<ProcessRow>();
                    foreach (Process child in new[] { first, second })
                        using (ProcessHandle read = Native.Open(child.Id, false)) members.Add(new ProcessRow { Pid = child.Id, Created = Native.Created(read), Name = "grouped-worker.exe", ExecutablePath = Native.ImagePath(read) });
                    ApplicationGroup app = ApplicationGroup.Build(members, delegate { return null; }).Single();
                    AffinityService service = new AffinityService(topology, new SnapshotStore(Path.Combine(working, "grouped-live.json")));
                    ApplicationIconGroup bucket = ApplicationIconGroup.Build(new[] { app }).Single();
                    ProcessSelection selected = new ProcessSelection(); selected.Set(new[] { members[0] }, true);
                    AppDragPayload drag = new AppDragPayload(selected.Targets(bucket.Members));
                    foreach (ProcessRow row in drag.Targets) CheckResult(service.Apply(row, target));
                    Check(Read(first.Id) == target && Read(second.Id) == initialSecond, "only individually selected child changed, identical-icon sibling untouched");
                    foreach (OriginalAffinity saved in service.Store.Items) CheckResult(service.Restore(saved));
                    Check(Read(first.Id) == initialFirst && Read(second.Id) == initialSecond, "individual restore retains both originals");
                    foreach (ProcessRow row in ApplicationGroup.Targets(new[] { app })) CheckResult(service.Apply(row, target));
                    Check(Read(first.Id) == target && Read(second.Id) == target, "all selected app members changed");
                    foreach (OriginalAffinity saved in service.Store.Items) CheckResult(service.Restore(saved));
                    Check(Read(first.Id) == initialFirst && Read(second.Id) == initialSecond && service.Store.Items.Length == 0, "per-process originals remain distinct and recoverable");
                }
                finally
                {
                    foreach (Process child in new[] { first, second }) if (child != null) { if (!child.HasExited) { child.Kill(); child.WaitForExit(); } child.Dispose(); }
                }
            });
        }

        private static void LiveChildTests(Topology topology, string working)
        {
            Process worker = Process.Start(new ProcessStartInfo { FileName = typeof(SelfTests).Assembly.Location, Arguments = "--test-worker", UseShellExecute = false, CreateNoWindow = true });
            try
            {
                ProcessRow row;
                ulong original;
                using (ProcessHandle handle = Native.Open(worker.Id, false))
                {
                    ulong system;
                    original = Native.Affinity(handle, out system);
                    row = new ProcessRow { Pid = worker.Id, Created = Native.Created(handle), Name = "CoreAllocator test worker" };
                }
                string path = Path.Combine(working, "live.json");
                AffinityService service = new AffinityService(topology, new SnapshotStore(path));
                ulong first = 1UL << Masks.Bits(original).First();
                Case("live: permission diagnostics never change child or recovery records", delegate
                {
                    CheckResult(service.InspectPermissions(row));
                    Check(Read(worker.Id) == original && service.Store.Items.Length == 0, "read-only inspection");
                    using (ProcessHandle read = Native.Open(worker.Id, false))
                    {
                        uint? granted = Native.GrantedAccess(read);
                        if (granted.HasValue) Throws(delegate { Native.EnsureCanSetAffinity(read); });
                    }
                    Check(Read(worker.Id) == original, "read handle preflight does not mutate child");
                });
                Case("live: icon path is read using the existing query-only handle", delegate
                {
                    using (ProcessHandle read = Native.Open(worker.Id, false))
                    {
                        Check(string.Equals(Path.GetFullPath(Native.ImagePath(read)), Path.GetFullPath(typeof(SelfTests).Assembly.Location), StringComparison.OrdinalIgnoreCase), "test worker executable path");
                    }
                    Check(Read(worker.Id) == original && service.Store.Items.Length == 0, "icon lookup changes no affinity or records");
                });
                Case("live: already-original restore verifies and clears record without setting affinity", delegate
                {
                    SnapshotStore unchanged = new SnapshotStore(Path.Combine(working, "unchanged.json"));
                    OriginalAffinity saved = new OriginalAffinity { Pid = row.Pid, Created = row.Created, Name = row.Name,
                        MaskHex = original.ToString("X16"), OwnerSid = WindowsIdentity.GetCurrent().User.Value };
                    unchanged.Add(saved);
                    OperationResult restored = new AffinityService(topology, unchanged).Restore(saved);
                    CheckResult(restored);
                    Check(restored.Message.Contains("无需写入") && unchanged.Items.Length == 0 && Read(worker.Id) == original, "no-write restore");
                });
                Case("live: empty / unavailable CPU masks never touch child", delegate
                {
                    Check(!service.Apply(row, 0).Success, "empty mask refused");
                    if (topology.Available != ulong.MaxValue)
                    {
                        ulong unavailable = 1UL << Masks.Bits(~topology.Available).First();
                        Check(!service.Apply(row, unavailable).Success, "unavailable CPU refused");
                    }
                    Check(service.Store.Items.Length == 0, "no snapshot after invalid input");
                    Check(Read(worker.Id) == original, "child unchanged");
                });
                Case("live: stale PID identity never touches child", delegate
                {
                    ProcessRow stale = new ProcessRow { Pid = row.Pid, Created = row.Created + 1, Name = row.Name };
                    Check(!service.Apply(stale, first).Success && Read(worker.Id) == original, "stale list identity refused");
                });
                Case("live: tool itself is excluded", delegate
                {
                    using (ProcessHandle handle = Native.Open(Process.GetCurrentProcess().Id, false))
                    {
                        ProcessRow self = new ProcessRow { Pid = Process.GetCurrentProcess().Id, Created = Native.Created(handle), Name = "self" };
                        Check(!service.Apply(self, first).Success, "self protection");
                    }
                });
                Case("live: durable snapshot failure leaves child unchanged", delegate
                {
                    if (first == original) { lines.Add("INFO single-CPU child: snapshot write-failure test has no distinct target mask"); return; }
                    string blocker = Path.Combine(working, "not-a-directory");
                    File.WriteAllText(blocker, "fixture");
                    AffinityService blocked = new AffinityService(topology, new SnapshotStore(Path.Combine(blocker, "original.json")));
                    Check(!blocked.Apply(row, first).Success, "disk write failure reported");
                    Check(Read(worker.Id) == original, "no mutation before durable snapshot");
                });
                Case("live: apply, repeat, reload service, restore exact original", delegate
                {
                    CheckResult(service.Apply(row, first));
                    Check(Read(worker.Id) == first, "first affinity readback");
                    if (original != first)
                    {
                        Check(service.Store.Find(row.Pid, row.Created).Mask == original, "saved exact original");
                        ulong second = 1UL << Masks.Bits(original).Last();
                        CheckResult(service.Apply(row, second));
                        Check(service.Store.Find(row.Pid, row.Created).Mask == original, "repeat preserves original");
                        AffinityService reloaded = new AffinityService(topology, new SnapshotStore(path));
                        CheckResult(reloaded.Restore(reloaded.Store.Items[0]));
                        Check(Read(worker.Id) == original && reloaded.Store.Items.Length == 0, "restored and removed record");
                    }
                    else Check(service.Store.Items.Length == 0, "no-op did not create snapshot");
                });
                Case("live: PID reuse simulation skips restore, retains unrelated process mask", delegate
                {
                    SnapshotStore store = new SnapshotStore(Path.Combine(working, "reuse.json"));
                    OriginalAffinity stale = new OriginalAffinity { Pid = worker.Id, Created = row.Created - 1, Name = "expired worker", MaskHex = first.ToString("X16"), OwnerSid = WindowsIdentity.GetCurrent().User.Value };
                    store.Add(stale);
                    OperationResult result = new AffinityService(topology, store).Restore(stale);
                    Check(result.Skipped && store.Items.Length == 0 && Read(worker.Id) == original, "reuse guard");
                });
                Case("live: exited child record is cleaned without touching other processes", delegate
                {
                    SnapshotStore store = new SnapshotStore(Path.Combine(working, "exited.json"));
                    OriginalAffinity saved = new OriginalAffinity { Pid = worker.Id, Created = row.Created, Name = row.Name, MaskHex = original.ToString("X16"), OwnerSid = WindowsIdentity.GetCurrent().User.Value };
                    store.Add(saved);
                    worker.Kill(); worker.WaitForExit();
                    Check(new AffinityService(topology, store).Restore(saved).Skipped && store.Items.Length == 0, "exited cleanup");
                });
            }
            finally
            {
                // Only the worker created above can ever be terminated by these tests.
                if (!worker.HasExited) { worker.Kill(); worker.WaitForExit(); }
                worker.Dispose();
            }
        }
        private static ulong Read(int pid) { using (ProcessHandle handle = Native.Open(pid, false)) { ulong system; return Native.Affinity(handle, out system); } }
        private static void CheckResult(OperationResult result) { Check(result.Success, result.ToString()); }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        private static void Throws(Action action)
        {
            bool threw = false;
            try { action(); } catch { threw = true; }
            Check(threw, "expected rejection");
        }
        private static void Case(string name, Action action) { action(); passed++; lines.Add("PASS " + name); }

        private static byte[] Fixture(bool multipleGroups)
        {
            List<byte> bytes = new List<byte>();
            bytes.AddRange(GroupRecord(multipleGroups ? new[] { (ulong)uint.MaxValue, 3UL } : new[] { (ulong)uint.MaxValue }));
            for (int i = 0; i < 16; i++) bytes.AddRange(CoreRecord(0, 3UL << (i * 2)));
            bytes.AddRange(CacheRecord(0xffff, 96U * 1048576));
            bytes.AddRange(CacheRecord(0xffff0000, 32U * 1048576));
            if (multipleGroups) bytes.AddRange(CoreRecord(1, 3));
            return bytes.ToArray();
        }
        private static byte[] Record(int relation, byte[] payload)
        {
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream)) { writer.Write(relation); writer.Write(payload.Length + 8); writer.Write(payload); return stream.ToArray(); }
        }
        private static byte[] GroupRecord(ulong[] masks)
        {
            byte[] data = new byte[24 + masks.Length * 48];
            Put(data, 0, (ushort)masks.Length); Put(data, 2, (ushort)masks.Length);
            for (int i = 0; i < masks.Length; i++) { data[24 + i * 48] = 64; data[25 + i * 48] = (byte)Masks.Bits(masks[i]).Count(); Put(data, 64 + i * 48, masks[i]); }
            return Record(4, data);
        }
        private static byte[] CoreRecord(ushort group, ulong mask, byte efficiencyClass = 0)
        {
            byte[] data = new byte[40]; data[0] = 1; data[1] = efficiencyClass;
            Put(data, 22, (ushort)1); Put(data, 24, mask); Put(data, 32, group);
            return Record(0, data);
        }
        private static byte[] CacheRecord(ulong mask, uint size)
        {
            byte[] data = new byte[48]; data[0] = 3;
            Buffer.BlockCopy(BitConverter.GetBytes(size), 0, data, 4, 4);
            Put(data, 30, (ushort)1); Put(data, 32, mask);
            return Record(2, data);
        }
        private static void Put(byte[] data, int offset, ushort value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, data, offset, 2); }
        private static void Put(byte[] data, int offset, ulong value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, data, offset, 8); }
    }
}
