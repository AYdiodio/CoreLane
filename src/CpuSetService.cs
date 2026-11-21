using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Principal;

namespace CoreAllocator
{
    internal static class CpuSetNative
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct GroupAffinity
        {
            internal UIntPtr Mask;
            internal ushort Group, Reserved1, Reserved2, Reserved3;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessDefaultCpuSetMasks(ProcessHandle process, [Out] GroupAffinity[] masks, ushort capacity, out ushort count);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessDefaultCpuSetMasks(ProcessHandle process, GroupAffinity[] masks, ushort count);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetProcessDefaultCpuSets(ProcessHandle process, [Out] uint[] ids, uint capacity, out uint count);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetProcessDefaultCpuSets(ProcessHandle process, uint[] ids, uint count);

        internal static ulong ReadMask(ProcessHandle process)
        {
            var masks = new GroupAffinity[1];
            ushort count;
            if (!GetProcessDefaultCpuSetMasks(process, masks, 1, out count)) throw Error("读取默认 CPU 集");
            if (count == 0) return 0;
            if (count != 1 || masks[0].Group != 0) throw new InvalidOperationException("当前只支持 Windows G0 的 CPU 集。");
            return masks[0].Mask.ToUInt64();
        }

        internal static uint[] ReadIds(ProcessHandle process)
        {
            uint count;
            bool ok = GetProcessDefaultCpuSets(process, null, 0, out count);
            int error = Marshal.GetLastWin32Error();
            if (ok && count == 0) return new uint[0];
            if (!ok && error != 122) throw new NativeOperationException(error, "读取原 CPU 集");
            // Changes made by another app between the two reads must not produce a partial snapshot.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (count == 0 || count > 4096) throw new InvalidOperationException("CPU 集数量超出可保存范围。");
                var ids = new uint[count];
                if (GetProcessDefaultCpuSets(process, ids, (uint)ids.Length, out count))
                    return ids.Take((int)count).OrderBy(id => id).ToArray();
                error = Marshal.GetLastWin32Error();
                if (error != 122) throw new NativeOperationException(error, "读取原 CPU 集");
            }
            throw new InvalidOperationException("CPU 集正在变化，请刷新后重试。");
        }

        internal static void EnsureWritable(ProcessHandle process)
        {
            uint? granted = Native.GrantedAccess(process);
            if (granted.HasValue && (granted.Value & 0x2000U) == 0)
                throw new InvalidOperationException("Windows 未授予 CPU 集设置权限（PROCESS_SET_LIMITED_INFORMATION）。");
        }

        internal static void SetMask(ProcessHandle process, ulong mask)
        {
            if (!SetProcessDefaultCpuSetMasks(process, new[] { new GroupAffinity { Mask = new UIntPtr(mask) } }, 1))
                throw Error("写入默认 CPU 集");
        }

        internal static void RestoreIds(ProcessHandle process, uint[] ids)
        {
            if (!SetProcessDefaultCpuSets(process, ids.Length == 0 ? null : ids, (uint)ids.Length))
                throw Error("恢复默认 CPU 集");
        }

        private static NativeOperationException Error(string operation)
        { return new NativeOperationException(Marshal.GetLastWin32Error(), operation); }
    }

    [DataContract]
    internal sealed class OriginalCpuSet
    {
        [DataMember] public int Pid;
        [DataMember] public long Created;
        [DataMember] public string Name;
        [DataMember] public string OwnerSid;
        [DataMember] public uint[] Ids;
        [DataMember] public ulong Mask;
        internal string Key { get { return Pid + ":" + Created; } }
    }

    internal sealed class CpuSetService
    {
        private readonly Topology topology;
        private readonly string path;
        private List<OriginalCpuSet> records = new List<OriginalCpuSet>();
        internal OriginalCpuSet[] Records { get { return records.ToArray(); } }

        internal CpuSetService(Topology topology, string directory)
        {
            this.topology = topology;
            path = Path.Combine(directory, "original-cpu-sets.json");
            if (!File.Exists(path)) return;
            using (FileStream stream = File.OpenRead(path))
                records = (List<OriginalCpuSet>)new DataContractJsonSerializer(typeof(List<OriginalCpuSet>)).ReadObject(stream);
            if (records == null || records.Any(row => row == null || row.Pid <= 4 || row.Created <= 0 ||
                string.IsNullOrEmpty(row.OwnerSid) || row.Ids == null || row.Ids.Length > 4096 ||
                row.Ids.Length != row.Ids.Distinct().Count() || (row.Ids.Length == 0) != (row.Mask == 0)) ||
                records.Select(row => row.Key).Distinct().Count() != records.Count)
                throw new InvalidDataException("CPU 集恢复记录无效，请保留 original-cpu-sets.json。");
        }

        internal OperationResult Apply(ProcessRow row, ulong requested)
        { return Apply(row, requested, allowClear: false); }

        internal OperationResult Apply(ProcessRow row, ulong requested, bool allowClear)
        {
            var result = new OperationResult { Pid = row.Pid, Name = row.Name };
            try
            {
                EnsureSupported();
                if (requested != 0 || !allowClear) Masks.Validate(requested, topology.Available);
                using (ProcessHandle process = Native.OpenForCpuSets(row.Pid))
                {
                    Native.ValidateTarget(process, row.Pid, row.Created);
                    string imagePath = Native.ImagePath(process);
                    if (!PerformanceProfile.EligiblePath(imagePath) || !string.Equals(imagePath, row.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("CPU 集只适用于路径已确认的当前用户应用。");
                    ulong system;
                    ulong affinity = Native.Affinity(process, out system);
                    if ((requested & ~(affinity & system)) != 0)
                        throw new InvalidOperationException("当前硬亲和性只允许 CPU " + Masks.Format(affinity) + "。CPU 集只能选这个范围内的核心。");
                    ulong prior = CpuSetNative.ReadMask(process);
                    if (prior != requested)
                    {
                        CpuSetNative.EnsureWritable(process);
                        if (!records.Any(saved => saved.Key == row.Key))
                        {
                            uint[] originalIds = CpuSetNative.ReadIds(process);
                            if (CpuSetNative.ReadMask(process) != prior || (originalIds.Length == 0) != (prior == 0))
                                throw new InvalidOperationException("CPU 集正在变化，请刷新后重试。");
                            var next = new List<OriginalCpuSet>(records);
                            next.Add(new OriginalCpuSet { Pid = row.Pid, Created = row.Created, Name = row.Name,
                                OwnerSid = WindowsIdentity.GetCurrent().User.Value, Ids = originalIds, Mask = prior });
                            Commit(next);
                        }
                        if (requested == 0) CpuSetNative.RestoreIds(process, new uint[0]);
                        else CpuSetNative.SetMask(process, requested);
                    }
                    if (CpuSetNative.ReadMask(process) != requested || Native.IsExited(process))
                        throw new InvalidOperationException("CPU 集回读与请求不一致或进程已退出。");
                    if ((requested & ~Native.Affinity(process, out system)) != 0)
                        throw new InvalidOperationException("CPU 集已写入，但硬亲和性在操作期间改变了。请查看当前范围。");
                    result.Success = true;
                    result.Message = requested == 0 ? "已确认未设置默认 CPU 集。" : "默认 CPU 集已确认：CPU " + Masks.Format(requested) + "。";
                }
            }
            catch (Exception error) { SetFailure(result, error); }
            return result;
        }

        internal OperationResult Restore(OriginalCpuSet saved)
        {
            var result = new OperationResult { Pid = saved.Pid, Name = saved.Name };
            try
            {
                ProcessHandle handle;
                try { handle = Native.Open(saved.Pid, false); }
                catch (Win32Exception error) { if (error.NativeErrorCode != 87) throw; return RemoveExited(saved, result); }
                using (handle)
                {
                    if (Native.IsExited(handle) || Native.Created(handle) != saved.Created) return RemoveExited(saved, result);
                    CheckOwner(handle, saved);
                    if (CpuSetNative.ReadIds(handle).SequenceEqual(saved.Ids.OrderBy(id => id)))
                    {
                        Remove(saved);
                        result.Success = true;
                        result.Message = "已与原 CPU 集一致，无需写入。";
                        return result;
                    }
                }
                using (ProcessHandle writable = Native.OpenForCpuSets(saved.Pid))
                {
                    CheckOwner(writable, saved);
                    CpuSetNative.EnsureWritable(writable);
                    CpuSetNative.RestoreIds(writable, saved.Ids);
                    if (!CpuSetNative.ReadIds(writable).SequenceEqual(saved.Ids.OrderBy(id => id)) || Native.IsExited(writable))
                        throw new InvalidOperationException("恢复后的 CPU 集回读不一致或进程已退出。");
                }
                Remove(saved);
                result.Success = true;
                result.Message = saved.Ids.Length == 0 ? "已清除工具设置的默认 CPU 集。" : "已恢复原默认 CPU 集。";
            }
            catch (Exception error) { SetFailure(result, error); result.Message += " 恢复记录已保留。"; }
            return result;
        }

        private void EnsureSupported()
        {
            if (!topology.CanAssign) throw new InvalidOperationException("当前只支持单 Windows 处理器组的 CPU 集。");
        }
        private static void CheckOwner(ProcessHandle handle, OriginalCpuSet saved)
        {
            Native.ValidateTarget(handle, saved.Pid, saved.Created);
            if (Native.Owner(handle) != saved.OwnerSid) throw new InvalidOperationException("CPU 集恢复记录的进程所有者不匹配。");
        }
        private void Commit(List<OriginalCpuSet> next) { SnapshotStore.SaveAtomically(path, next); records = next; }
        internal void Remove(OriginalCpuSet saved) { Commit(records.Where(row => row.Key != saved.Key).ToList()); }
        private OperationResult RemoveExited(OriginalCpuSet saved, OperationResult result)
        {
            Remove(saved);
            result.Skipped = true;
            result.Message = "原进程已退出，CPU 集恢复记录已清理。";
            return result;
        }
        private static void SetFailure(OperationResult result, Exception error)
        {
            var win32 = error as Win32Exception;
            if (win32 != null) result.Win32Error = win32.NativeErrorCode;
            result.Message = error is EntryPointNotFoundException ? "当前 Windows 版本不支持此 CPU 集接口。" : Native.Explain(error);
        }
    }
}
