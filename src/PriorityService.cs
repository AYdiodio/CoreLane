using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Principal;

namespace CoreAllocator
{
    internal static class PriorityLevels
    {
        internal const uint BelowNormal = 0x4000, Normal = 0x20, AboveNormal = 0x8000;
        internal static bool Selectable(uint value) { return value == BelowNormal || value == Normal || value == AboveNormal; }
        internal static bool Known(uint value) { return Selectable(value) || value == 0x40 || value == 0x80 || value == 0x100; }
        internal static string Indicator(IEnumerable<ProcessRow> rows)
        {
            uint[] values = rows.Select(row => row.Priority).Distinct().ToArray();
            if (values.Length == 0 || values.Any(value => !Known(value))) return "?";
            if (values.Length > 1) return "↕";
            uint level = values[0];
            if (level == Normal) return "";
            return level == BelowNormal || level == 0x40 ? "↓" : "↑";
        }
        internal static string Describe(uint value)
        {
            switch (value)
            {
                case BelowNormal: return "低于正常";
                case Normal: return "正常";
                case AboveNormal: return "高于正常";
                case 0x40: return "空闲";
                case 0x80: return "高";
                case 0x100: return "实时";
                default: return "无法读取";
            }
        }
    }

    [DataContract]
    internal sealed class OriginalPriority
    {
        [DataMember] public int Pid;
        [DataMember] public long Created;
        [DataMember] public string Name;
        [DataMember] public string OwnerSid;
        [DataMember] public uint Original;
        // Zero denotes a regular assignment. A guard can restore only its own applied value.
        [DataMember(EmitDefaultValue = false)] public uint TemporaryValue;
        internal string Key { get { return Pid + ":" + Created; } }
    }

    internal sealed class PriorityService
    {
        private readonly string path;
        private List<OriginalPriority> records = new List<OriginalPriority>();
        internal OriginalPriority[] Records { get { return records.ToArray(); } }

        internal PriorityService(string directory)
        {
            path = Path.Combine(directory, "original-priority.json");
            if (!File.Exists(path)) return;
            using (FileStream stream = File.OpenRead(path))
                records = (List<OriginalPriority>)new DataContractJsonSerializer(typeof(List<OriginalPriority>)).ReadObject(stream);
            if (records == null || records.Any(record => record == null || record.Pid <= 4 || record.Created <= 0 ||
                string.IsNullOrEmpty(record.OwnerSid) || !PriorityLevels.Known(record.Original) ||
                record.TemporaryValue != 0 && (record.TemporaryValue != PriorityLevels.BelowNormal ||
                (record.Original != PriorityLevels.Normal && record.Original != PriorityLevels.AboveNormal))) ||
                records.GroupBy(record => record.Key).Any(group => group.Count(r => r.TemporaryValue == 0) > 1 || group.Count(r => r.TemporaryValue != 0) > 1))
                throw new InvalidDataException("优先级恢复记录无效，请保留 original-priority.json。");
        }

        private void Save(List<OriginalPriority> next)
        {
            SnapshotStore.SaveAtomically(path, next);
            records = next;
        }

        internal void Remove(OriginalPriority record) { Save(records.Where(item => item != record).ToList()); }

        internal OperationResult Apply(ProcessRow row, uint value, bool temporary = false)
        {
            var result = new OperationResult { Pid = row.Pid, Name = row.Name };
            try
            {
                if (!PriorityLevels.Selectable(value) || temporary && value != PriorityLevels.BelowNormal)
                    throw new InvalidOperationException("不支持的优先级。");
                if (records.Any(record => record.Key == row.Key && record.TemporaryValue != 0))
                    throw new InvalidOperationException("请先恢复该进程的临时优先级。");
                using (ProcessHandle handle = Native.Open(row.Pid, true))
                {
                    Native.ValidateTarget(handle, row.Pid, row.Created);
                    string executable = Native.ImagePath(handle);
                    if (!PerformanceProfile.EligiblePath(executable) || !string.Equals(executable, row.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("优先级仅适用于已确认路径的当前用户应用。");
                    uint original = Native.Priority(handle);
                    if (temporary && original != PriorityLevels.Normal && original != PriorityLevels.AboveNormal)
                    {
                        result.Skipped = true;
                        result.Message = "当前优先级不在后台保护的降级范围内。";
                        return result;
                    }
                    if (original != value)
                    {
                        if (!PriorityLevels.Known(original)) throw new InvalidOperationException("无法保存当前优先级。");
                        if (temporary || !records.Any(record => record.Key == row.Key))
                        {
                            var next = new List<OriginalPriority>(records);
                            next.Add(new OriginalPriority { Pid = row.Pid, Created = row.Created, Name = row.Name,
                                OwnerSid = WindowsIdentity.GetCurrent().User.Value, Original = original, TemporaryValue = temporary ? value : 0 });
                            Save(next);
                        }
                        Native.SetPriority(handle, value);
                        if (Native.Priority(handle) != value || Native.IsExited(handle)) throw new InvalidOperationException("优先级回读不一致或进程已退出，恢复记录已保留。");
                    }
                    result.Success = true;
                    result.Message = (temporary ? "后台保护临时调整为" : "当前优先级为") + PriorityLevels.Describe(value) + "。";
                }
            }
            catch (Exception error) { SetFailure(result, error); }
            return result;
        }

        internal OperationResult Restore(OriginalPriority record)
        {
            var result = new OperationResult { Pid = record.Pid, Name = record.Name };
            try
            {
                if (record.TemporaryValue == 0 && records.Any(other => other.Key == record.Key && other.TemporaryValue != 0))
                    throw new InvalidOperationException("需先解除该进程的后台保护。");
                using (ProcessHandle read = OpenForRestore(record))
                {
                    if (read == null || Native.IsExited(read) || Native.Created(read) != record.Created)
                    {
                        Remove(record);
                        result.Skipped = true;
                        result.Message = "原进程已退出或 PID 已复用，已清理优先级记录。";
                        return result;
                    }
                    ValidateOwner(read, record);
                    if (CanRestore(read, record))
                    {
                        using (ProcessHandle write = Native.Open(record.Pid, true))
                        {
                            ValidateOwner(write, record);
                            if (CanRestore(write, record))
                            {
                                Native.SetPriority(write, record.Original);
                                if (Native.Priority(write) != record.Original || Native.IsExited(write)) throw new InvalidOperationException("原优先级恢复后回读不一致，记录已保留。");
                            }
                        }
                    }
                    uint actual = Native.Priority(read);
                    if (record.TemporaryValue == 0 && actual != record.Original)
                        throw new InvalidOperationException("原优先级恢复后回读不一致，记录已保留。");
                    Remove(record);
                    result.Success = actual == record.Original;
                    result.Skipped = !result.Success;
                    result.Message = result.Success ? "已恢复优先级「" + PriorityLevels.Describe(record.Original) + "」。" : "当前优先级已变化，保留当前设置。";
                }
            }
            catch (Exception error) { SetFailure(result, error); }
            return result;
        }

        private static ProcessHandle OpenForRestore(OriginalPriority record)
        {
            try { return Native.Open(record.Pid, false); }
            catch (Win32Exception error) { if (error.NativeErrorCode == 87) return null; throw; }
        }

        private static void ValidateOwner(ProcessHandle handle, OriginalPriority record)
        {
            Native.ValidateTarget(handle, record.Pid, record.Created);
            if (Native.Owner(handle) != record.OwnerSid) throw new InvalidOperationException("优先级恢复记录的进程所有者不匹配。");
        }

        private static bool CanRestore(ProcessHandle handle, OriginalPriority record)
        {
            uint current = Native.Priority(handle);
            return current != record.Original && (record.TemporaryValue == 0 || current == record.TemporaryValue);
        }

        private static void SetFailure(OperationResult result, Exception error)
        {
            var win32 = error as Win32Exception;
            if (win32 != null) result.Win32Error = win32.NativeErrorCode;
            result.Message = Native.Explain(error);
        }
    }
}
