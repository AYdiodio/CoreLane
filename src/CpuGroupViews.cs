using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CoreAllocator
{
    // Presentation groups retain native masks; their display numbers never replace Windows CPU/group IDs.
    internal sealed class CpuSelectionGroup
    {
        internal int Index;
        internal int WindowsGroup;
        internal ulong Mask;
        internal uint CacheBytes;
        internal string DisplayName;
        internal string ClusterLabel;
        internal string Name { get { return DisplayName ?? "CPU 组 " + (Index + 1); } }

        internal static CpuSelectionGroup GameTarget(Topology topology)
        {
            IntelHybridInfo hybrid = IntelHybridInfo.Detect(topology);
            if (hybrid.Recognized && topology.CanAssign)
                return new CpuSelectionGroup { WindowsGroup = 0, Mask = hybrid.MaskFor(0, true), DisplayName = "仅大核（P 核）" };
            return FromTopology(topology).First();
        }

        internal static CpuSelectionGroup NonGameTarget(Topology topology)
        {
            // CPU group 2 is a display group from real topology, not a guessed half or E-core mask.
            return topology.CanAssign ? FromTopology(topology).FirstOrDefault(g => g.Index == 1 && g.WindowsGroup == 0) : null;
        }

        internal static List<CpuSelectionGroup> FromTopology(Topology topology)
        {
            List<CpuSelectionGroup> result = new List<CpuSelectionGroup>();
            IntelHybridInfo hybrid = IntelHybridInfo.Detect(topology);
            if (hybrid.Recognized)
            {
                foreach (CpuGroup group in topology.Groups)
                {
                    foreach (bool performance in new[] { true, false })
                    {
                        ulong mask = hybrid.MaskFor(group.Number, performance);
                        if (mask != 0) result.Add(new CpuSelectionGroup { Index = result.Count, WindowsGroup = group.Number,
                            Mask = mask, DisplayName = performance ? "大核 P" : "小核 E" });
                    }
                }
                return result;
            }
            foreach (CpuGroup nativeGroup in topology.Groups)
            {
                ulong covered = 0;
                foreach (CpuCluster cluster in topology.Clusters.Where(c => c.Group == nativeGroup.Number))
                {
                    ulong remaining = cluster.Mask & nativeGroup.Mask & ~covered;
                    if (remaining == 0) continue;
                    result.Add(new CpuSelectionGroup { Index = result.Count, WindowsGroup = nativeGroup.Number, Mask = remaining, CacheBytes = cluster.CacheBytes,
                        ClusterLabel = string.Equals(topology.CpuVendor, "AuthenticAMD", StringComparison.OrdinalIgnoreCase) ? "CCD " + result.Count : null });
                    covered |= remaining;
                }
                ulong other = nativeGroup.Mask & ~covered;
                if (other != 0) result.Add(new CpuSelectionGroup { Index = result.Count, WindowsGroup = nativeGroup.Number, Mask = other });
            }
            return result;
        }

        internal static string Describe(ulong mask, IList<CpuSelectionGroup> groups)
        {
            if (mask == 0) return "未选择";
            List<CpuSelectionGroup> matched = groups.Where(g => g.WindowsGroup == 0 && (mask & g.Mask) != 0).ToList();
            ulong union = 0;
            foreach (CpuSelectionGroup group in matched) union |= group.Mask;
            if (matched.Count == 1 && mask == matched[0].Mask) return matched[0].Name;
            if (matched.Count > 1 && mask == union) return string.Join(" + ", matched.Select(g => g.Name));
            return "自定义 · " + Masks.Bits(mask).Count() + " 个 CPU";
        }
    }

}
