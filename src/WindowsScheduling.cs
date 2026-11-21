using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace CoreAllocator
{
    internal sealed class SchedulingRestoreEntry
    {
        internal readonly int Pid;
        internal readonly long Created;
        internal readonly string Name, OwnerSid, Key;
        internal readonly Action Remove;

        internal SchedulingRestoreEntry(int pid, long created, string name, string ownerSid, Action remove)
        { Pid = pid; Created = created; Name = name; OwnerSid = ownerSid; Key = pid + ":" + created; Remove = remove; }
    }

    internal static class WindowsScheduling
    {
        internal static OperationResult Reset(Topology topology, SchedulingRestoreEntry[] entries)
        {
            SchedulingRestoreEntry target = entries[0];
            var result = new OperationResult { Pid = target.Pid, Name = target.Name };
            try
            {
                using (ProcessHandle read = OpenForRestore(target.Pid))
                {
                    if (read == null || Native.IsExited(read) || Native.Created(read) != target.Created) return Exited(result);
                    if (!topology.CanAssign) throw new InvalidOperationException("当前硬件有多个 Windows 处理器组，工具无法确认全部可用 CPU。");
                    if (entries.Any(entry => entry.OwnerSid != target.OwnerSid)) throw new InvalidOperationException("恢复记录的进程所有者不一致。");
                    Validate(read, target);
                    var failures = new List<string>();
                    ResetPart("默认 CPU 集", failures, delegate { ResetCpuSets(read, target); });
                    ResetPart("硬亲和性", failures, delegate { ResetAffinity(read, target); });
                    ResetPart("进程优先级", failures, delegate { ResetPriority(read, target); });
                    if (Native.IsExited(read)) return Exited(result);
                    if (failures.Count > 0) throw new InvalidOperationException(string.Join("；", failures));
                }
                result.Success = true;
                result.Message = "已交还 Windows 调度 · 全部可用 CPU · 无默认 CPU 集 · 正常优先级";
            }
            catch (Exception error)
            {
                result.Message = Native.Explain(error) + "。恢复记录已保留。";
            }
            return result;
        }

        private static ProcessHandle OpenForRestore(int pid)
        {
            try { return Native.Open(pid, false); }
            catch (Win32Exception error) { if (error.NativeErrorCode == 87) return null; throw; }
        }

        private static OperationResult Exited(OperationResult result)
        {
            result.Skipped = true;
            result.Message = "原进程已退出，已跳过调度写入。";
            return result;
        }

        private static void Validate(ProcessHandle handle, SchedulingRestoreEntry target)
        {
            Native.ValidateTarget(handle, target.Pid, target.Created);
            if (Native.Owner(handle) != target.OwnerSid) throw new InvalidOperationException("恢复记录的进程所有者不匹配。");
        }

        private static void ResetPart(string label, List<string> failures, Action reset)
        {
            try { reset(); }
            catch (Exception error) { failures.Add(label + " · " + Native.Explain(error)); }
        }

        private static void ResetCpuSets(ProcessHandle read, SchedulingRestoreEntry target)
        {
            if (CpuSetNative.ReadIds(read).Length == 0) return;
            using (ProcessHandle write = Native.OpenForCpuSets(target.Pid))
            {
                Validate(write, target);
                CpuSetNative.EnsureWritable(write);
                CpuSetNative.RestoreIds(write, new uint[0]);
                if (CpuSetNative.ReadIds(write).Length != 0) throw new InvalidOperationException("回读时仍有默认 CPU 集。");
            }
        }

        private static void ResetAffinity(ProcessHandle read, SchedulingRestoreEntry target)
        {
            ulong system;
            if (Native.Affinity(read, out system) == system) return;
            using (ProcessHandle write = Native.Open(target.Pid, true))
            {
                Validate(write, target);
                if (Native.Affinity(write, out system) != system) Native.SetAffinity(write, system);
                if (Native.Affinity(write, out system) != system) throw new InvalidOperationException("回读时仍有限制。");
            }
        }

        private static void ResetPriority(ProcessHandle read, SchedulingRestoreEntry target)
        {
            if (Native.Priority(read) == PriorityLevels.Normal) return;
            using (ProcessHandle write = Native.Open(target.Pid, true))
            {
                Validate(write, target);
                Native.SetPriority(write, PriorityLevels.Normal);
                if (Native.Priority(write) != PriorityLevels.Normal) throw new InvalidOperationException("回读时优先级不是正常。");
            }
        }
    }
}
