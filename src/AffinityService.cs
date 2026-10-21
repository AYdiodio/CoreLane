using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Principal;

namespace CoreAllocator
{
    [DataContract]
    internal sealed class OriginalAffinity
    {
        [DataMember] public int Pid;
        [DataMember] public long Created;
        [DataMember] public string Name;
        [DataMember] public string MaskHex;
        [DataMember] public string OwnerSid;
        internal string Key { get { return Pid + ":" + Created; } }
        internal ulong Mask { get { return ulong.Parse(MaskHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture); } }
    }

    internal sealed class SnapshotStore
    {
        private readonly string path;
        private List<OriginalAffinity> items;
        internal OriginalAffinity[] Items { get { return items.ToArray(); } }

        internal SnapshotStore(string path)
        {
            this.path = path;
            items = new List<OriginalAffinity>();
            if (!File.Exists(path)) return;
            try
            {
                using (FileStream stream = File.OpenRead(path))
                    items = (List<OriginalAffinity>)new DataContractJsonSerializer(typeof(List<OriginalAffinity>)).ReadObject(stream);
                if (items == null || items.Any(x => x == null || x.Pid <= 4 || x.Created <= 0 || x.Mask == 0 || string.IsNullOrEmpty(x.OwnerSid)) ||
                    items.Select(x => x.Key).Distinct().Count() != items.Count) throw new InvalidDataException("恢复记录内容无效。");
            }
            catch (Exception error) { throw new InvalidDataException("无法读取恢复记录，已停止操作以保护原设置。请保留此文件：" + path + "\r\n" + error.Message, error); }
        }

        internal OriginalAffinity Find(int pid, long created) { return items.FirstOrDefault(x => x.Pid == pid && x.Created == created); }
        internal void Add(OriginalAffinity item)
        {
            if (Find(item.Pid, item.Created) != null) return;
            List<OriginalAffinity> next = new List<OriginalAffinity>(items);
            next.Add(item);
            Commit(next);
        }
        internal void Remove(OriginalAffinity item)
        {
            List<OriginalAffinity> next = items.Where(x => x.Key != item.Key).ToList();
            Commit(next);
        }
        private void Commit(List<OriginalAffinity> next)
        {
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(List<OriginalAffinity>)).WriteObject(stream, next);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                items = next;
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    internal sealed class ProcessRow
    {
        internal int Pid;
        internal long Created;
        internal string Name;
        internal ulong Mask;
        internal string ReadError;
        internal string Key { get { return Pid + ":" + Created; } }
    }

    internal sealed class OperationResult
    {
        internal bool Success;
        internal bool Skipped;
        internal int Pid;
        internal string Name;
        internal string Message;
        public override string ToString()
        {
            return (Skipped ? "跳过" : Success ? "成功" : "失败") + " · " + Name + " [" + Pid + "] · " + Message;
        }
    }

    internal sealed class AffinityService
    {
        internal readonly Topology Topology;
        internal readonly SnapshotStore Store;
        internal AffinityService(Topology topology, SnapshotStore store) { Topology = topology; Store = store; }

        internal List<ProcessRow> Processes(out int unavailable)
        {
            List<ProcessRow> rows = new List<ProcessRow>();
            unavailable = 0;
            int session = Process.GetCurrentProcess().SessionId, self = Process.GetCurrentProcess().Id;
            string sid = WindowsIdentity.GetCurrent().User.Value;
            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.Id <= 4 || process.Id == self || process.SessionId != session) continue;
                        using (ProcessHandle handle = Native.Open(process.Id, false))
                        {
                            if (Native.IsExited(handle) || Native.Owner(handle) != sid) continue;
                            ProcessRow row = new ProcessRow { Pid = process.Id, Created = Native.Created(handle), Name = process.ProcessName + ".exe" };
                            try { ulong system; row.Mask = Native.Affinity(handle, out system); }
                            catch (Exception error) { row.ReadError = Native.Explain(error); }
                            rows.Add(row);
                        }
                    }
                    catch (System.ComponentModel.Win32Exception) { unavailable++; }
                    catch (InvalidOperationException) { unavailable++; }
                    catch (ArgumentException) { unavailable++; }
                }
            }
            return rows.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Pid).ToList();
        }

        internal OperationResult Apply(ProcessRow row, ulong requested)
        {
            OperationResult result = new OperationResult { Pid = row.Pid, Name = row.Name };
            try
            {
                EnsureSupported();
                Masks.Validate(requested, Topology.Available);
                using (ProcessHandle handle = Native.Open(row.Pid, true))
                {
                    Native.ValidateTarget(handle, row.Pid, row.Created);
                    ulong system;
                    ulong original = Native.Affinity(handle, out system);
                    Masks.Validate(requested, system);
                    if (original == requested)
                    {
                        result.Success = true;
                        result.Message = "已是 CPU " + Masks.Format(requested) + "，无需修改。";
                        return result;
                    }
                    Native.EnsureCanSetAffinity(handle);
                    // Durably save BEFORE touching the process. Repeated assignments preserve the first mask.
                    Store.Add(new OriginalAffinity { Pid = row.Pid, Created = row.Created, Name = row.Name,
                        MaskHex = original.ToString("X16"), OwnerSid = WindowsIdentity.GetCurrent().User.Value });
                    Native.SetAffinity(handle, requested);
                    ulong actual = Native.Affinity(handle, out system);
                    if (Native.IsExited(handle)) throw new InvalidOperationException("进程在分配后退出；已保留恢复记录。");
                    if (actual != requested) throw new InvalidOperationException("回读与请求不一致，目标可能自行更改了亲和性；已保留恢复记录。");
                    result.Success = true;
                    result.Message = "已分配 CPU " + Masks.Format(actual) + "；原设置已保存。";
                }
            }
            catch (Exception error) { result.Message = Native.Explain(error); }
            return result;
        }

        internal OperationResult Restore(OriginalAffinity original)
        {
            OperationResult result = new OperationResult { Pid = original.Pid, Name = original.Name };
            try
            {
                EnsureSupported();
                ProcessHandle handle;
                try { handle = Native.Open(original.Pid, false); }
                catch (System.ComponentModel.Win32Exception error)
                {
                    if (error.NativeErrorCode != 87) throw;
                    Store.Remove(original);
                    result.Skipped = true;
                    result.Message = "原进程已退出，清理旧记录。";
                    return result;
                }
                using (handle)
                {
                    if (Native.IsExited(handle) || Native.Created(handle) != original.Created)
                    {
                        Store.Remove(original);
                        result.Skipped = true;
                        result.Message = "原进程已退出或 PID 被复用，未修改当前进程。";
                        return result;
                    }
                    Native.ValidateTarget(handle, original.Pid, original.Created);
                    if (Native.Owner(handle) != original.OwnerSid) throw new InvalidOperationException("恢复记录的进程所有者不匹配。");
                    ulong system;
                    if (Native.Affinity(handle, out system) == original.Mask)
                    {
                        Store.Remove(original);
                        result.Success = true;
                        result.Message = "当前 CPU " + Masks.Format(original.Mask) + " 已与原设置一致，无需写入；恢复记录已清理。";
                        return result;
                    }
                }
                // Revalidate after reopening with write access; never trust the PID alone.
                using (ProcessHandle writable = Native.Open(original.Pid, true))
                {
                    Native.ValidateTarget(writable, original.Pid, original.Created);
                    if (Native.Owner(writable) != original.OwnerSid) throw new InvalidOperationException("恢复记录的进程所有者不匹配。");
                    ulong system;
                    Native.Affinity(writable, out system);
                    Masks.Validate(original.Mask, system);
                    Native.SetAffinity(writable, original.Mask);
                    if (Native.Affinity(writable, out system) != original.Mask || Native.IsExited(writable))
                        throw new InvalidOperationException("恢复后的回读不一致或进程已退出，记录已保留。");
                    Store.Remove(original);
                    result.Success = true;
                    result.Message = "已恢复原 CPU " + Masks.Format(original.Mask) + "。";
                }
            }
            catch (Exception error) { result.Message = Native.Explain(error) + " 恢复记录已保留。"; }
            return result;
        }

        internal OperationResult InspectPermissions(ProcessRow row)
        {
            OperationResult result = new OperationResult { Pid = row.Pid, Name = row.Name };
            try
            {
                using (ProcessHandle read = Native.Open(row.Pid, false))
                {
                    Native.ValidateTarget(read, row.Pid, row.Created);
                    ulong system;
                    ulong current = Native.Affinity(read, out system);
                    result.Message = "工具" + (Native.IsAdministrator ? "已是管理员" : "为普通权限") + "；当前 CPU " + Masks.Format(current) + "。";
                }
                using (ProcessHandle write = Native.Open(row.Pid, true))
                {
                    Native.ValidateTarget(write, row.Pid, row.Created);
                    uint? granted = Native.GrantedAccess(write);
                    Native.EnsureCanSetAffinity(write);
                    result.Success = true;
                    result.Message += granted.HasValue ? " 句柄包含修改权限（0x" + granted.Value.ToString("X8") + "）；实际写入仍可能被系统拒绝。" :
                        " 修改句柄已打开；当前系统无法读取实际权限位，尚不能确认写入能力。";
                }
                result.Message += " 本次只检查权限，未修改亲和性或恢复记录。";
            }
            catch (Exception error) { result.Message = (result.Message ?? "") + " " + Native.Explain(error) + " 本次未修改亲和性或恢复记录。"; }
            return result;
        }

        private void EnsureSupported()
        {
            if (!Topology.CanAssign) throw new InvalidOperationException("检测到多个 Windows 处理器组；第一版仅支持单处理器组（最多 64 个逻辑 CPU）的亲和性修改。");
        }
    }
}
