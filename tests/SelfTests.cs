using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;

namespace CoreAllocator
{
    internal static class SelfTests
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
                Topology actual = Topology.Read();
                Case("live topology read", delegate
                {
                    Check(actual.Groups.Count > 0 && actual.Cores.Count > 0, "live topology");
                    lines.Add("INFO " + actual.CpuName + "; logical=" + actual.Groups.Sum(g => Masks.Bits(g.Mask).Count()) + "; physical=" + actual.Cores.Select(c => c.Number).Distinct().Count());
                    foreach (CpuCluster cluster in actual.Clusters) lines.Add("INFO " + cluster);
                });
                if (actual.CanAssign) LiveChildTests(actual, working);
                else lines.Add("SKIP child affinity tests: machine uses multiple processor groups.");
            }
            catch (Exception error) { lines.Add("FAIL " + error); exit = 1; }
            lines.Add("RESULT " + (exit == 0 ? "PASS" : "FAIL") + " - " + passed + " test cases passed");
            File.WriteAllLines(report, lines.ToArray(), new UTF8Encoding(true));
            return exit;
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
        private static byte[] CoreRecord(ushort group, ulong mask)
        {
            byte[] data = new byte[40]; data[0] = 1;
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
