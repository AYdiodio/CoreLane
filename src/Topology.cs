using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CoreAllocator
{
    internal sealed class CpuGroup
    {
        internal int Number;
        internal ulong Mask;
    }

    internal sealed class CpuCluster
    {
        internal int Group;
        internal ulong Mask;
        internal uint CacheBytes;
        public override string ToString()
        {
            return "L3 " + (CacheBytes / 1048576.0).ToString("0.#") + " MB · G" + Group + " · CPU " + Masks.Format(Mask);
        }
    }

    internal sealed class CpuCore
    {
        internal int Number;
        internal int Group;
        internal ulong Mask;
    }

    internal static class Masks
    {
        internal static IEnumerable<int> Bits(ulong mask)
        {
            for (int i = 0; i < 64; i++) if ((mask & (1UL << i)) != 0) yield return i;
        }
        internal static string Format(ulong mask)
        {
            List<int> bits = Bits(mask).ToList();
            List<string> parts = new List<string>();
            for (int i = 0; i < bits.Count; i++)
            {
                int first = bits[i], last = first;
                while (i + 1 < bits.Count && bits[i + 1] == last + 1) last = bits[++i];
                parts.Add(first == last ? first.ToString() : first + "–" + last);
            }
            return parts.Count == 0 ? "（无）" : string.Join(", ", parts);
        }
        internal static void Validate(ulong mask, ulong available)
        {
            if (mask == 0) throw new InvalidOperationException("请至少选择一个逻辑 CPU。");
            if ((mask & ~available) != 0) throw new InvalidOperationException("所选 CPU 不在目标进程允许使用的系统核心范围内。");
        }
    }

    internal sealed class Topology
    {
        internal string CpuName;
        internal readonly List<CpuGroup> Groups = new List<CpuGroup>();
        internal readonly List<CpuCore> Cores = new List<CpuCore>();
        internal readonly List<CpuCluster> Clusters = new List<CpuCluster>();
        internal bool CanAssign { get { return Groups.Count == 1 && Groups[0].Number == 0; } }
        internal ulong Available { get { return CanAssign ? Groups[0].Mask : 0; } }

        internal static Topology Read()
        {
            if (IntPtr.Size != 8) throw new InvalidOperationException("请运行 x64 版本。");
            uint length = 0;
            Native.GetLogicalProcessorInformationEx(0xffff, IntPtr.Zero, ref length);
            if (length == 0 || length > 16 * 1024 * 1024) throw Native.LastError();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                IntPtr buffer = Marshal.AllocHGlobal((int)length);
                try
                {
                    uint used = length;
                    if (Native.GetLogicalProcessorInformationEx(0xffff, buffer, ref used))
                    {
                        byte[] bytes = new byte[used];
                        Marshal.Copy(buffer, bytes, 0, bytes.Length);
                        Topology result = Parse(bytes);
                        result.CpuName = "CPU";
                        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                            if (key != null) result.CpuName = Convert.ToString(key.GetValue("ProcessorNameString", "CPU")).Trim();
                        return result;
                    }
                    int error = Marshal.GetLastWin32Error();
                    if (error != 122 || used > 16 * 1024 * 1024) throw new System.ComponentModel.Win32Exception(error);
                    length = used;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            throw new InvalidOperationException("CPU 拓扑在读取时变化，请重新启动工具。");
        }

        // x64 Windows native structures: 8-byte record header, 16-byte GROUP_AFFINITY.
        // Validate each variable-length record before reading its masks.
        internal static Topology Parse(byte[] data)
        {
            Topology result = new Topology();
            int offset = 0, coreNumber = 0;
            while (offset < data.Length)
            {
                Require(data, offset, 8, data.Length);
                int relation = BitConverter.ToInt32(data, offset);
                uint rawSize = BitConverter.ToUInt32(data, offset + 4);
                if (rawSize < 8 || rawSize > int.MaxValue) throw new InvalidDataException("无效 CPU 拓扑记录。");
                int size = (int)rawSize;
                Require(data, offset, size, data.Length);
                int end = offset + size;
                if (relation == 4)
                {
                    Require(data, offset + 8, 24, end);
                    int count = BitConverter.ToUInt16(data, offset + 10);
                    for (int i = 0; i < count; i++)
                    {
                        int p = offset + 32 + i * 48;
                        Require(data, p, 48, end);
                        result.Groups.Add(new CpuGroup { Number = i, Mask = BitConverter.ToUInt64(data, p + 40) });
                    }
                }
                else if (relation == 0 || relation == 2)
                {
                    int countOffset = relation == 0 ? 30 : 38;
                    int maskOffset = relation == 0 ? 32 : 40;
                    Require(data, offset + 8, maskOffset - 8, end);
                    int count = BitConverter.ToUInt16(data, offset + countOffset);
                    // Older CACHE_RELATIONSHIP uses the reserved GroupCount field as zero.
                    if (relation == 2 && count == 0) count = 1;
                    for (int i = 0; i < count; i++)
                    {
                        int p = offset + maskOffset + i * 16;
                        Require(data, p, 16, end);
                        ulong mask = BitConverter.ToUInt64(data, p);
                        int group = BitConverter.ToUInt16(data, p + 8);
                        if (relation == 0) result.Cores.Add(new CpuCore { Number = coreNumber, Group = group, Mask = mask });
                        else if (data[offset + 8] == 3)
                            result.Clusters.Add(new CpuCluster { Group = group, Mask = mask, CacheBytes = BitConverter.ToUInt32(data, offset + 12) });
                    }
                    if (relation == 0) coreNumber++;
                }
                offset = end;
            }
            if (result.Groups.Count == 0 || result.Groups.Any(g => g.Mask == 0)) throw new InvalidDataException("未读到有效的 Windows 处理器组。");
            foreach (CpuCore core in result.Cores)
                if (!result.Groups.Any(g => g.Number == core.Group && (core.Mask & ~g.Mask) == 0 && core.Mask != 0))
                    throw new InvalidDataException("物理核心拓扑与处理器组不一致。");
            foreach (CpuCluster cluster in result.Clusters)
                if (!result.Groups.Any(g => g.Number == cluster.Group && (cluster.Mask & ~g.Mask) == 0 && cluster.Mask != 0))
                    throw new InvalidDataException("缓存拓扑与处理器组不一致。");
            result.Clusters.Sort((a, b) => a.Group != b.Group ? a.Group.CompareTo(b.Group) : Masks.Bits(a.Mask).First().CompareTo(Masks.Bits(b.Mask).First()));
            return result;
        }

        private static void Require(byte[] data, int offset, int length, int end)
        {
            if (offset < 0 || length < 0 || offset > end || length > end - offset || end > data.Length)
                throw new InvalidDataException("CPU 拓扑记录长度不正确。");
        }
    }
}
