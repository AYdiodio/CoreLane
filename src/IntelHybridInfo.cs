using System;
using System.Collections.Generic;
using System.Linq;

namespace CoreAllocator
{
    // EfficiencyClass is a relative class, not a fixed 0/1 value or a CPU index.
    // Be conservative: only label Intel P/E when two classes cover every native CPU exactly once.
    internal sealed class IntelHybridInfo
    {
        private readonly List<CpuCore> cores = new List<CpuCore>();
        private byte performanceClass;
        internal bool Recognized { get; private set; }
        internal string Reason { get; private set; }
        internal int PerformanceCount { get { return cores.Where(c => c.EfficiencyClass == performanceClass).Select(c => c.Number).Distinct().Count(); } }
        internal int EfficientCount { get { return cores.Where(c => c.EfficiencyClass != performanceClass).Select(c => c.Number).Distinct().Count(); } }

        internal static IntelHybridInfo Detect(Topology topology)
        {
            IntelHybridInfo result = new IntelHybridInfo();
            result.Reason = "未确认 Intel 大小核；继续使用 CPU 分组或自定义 CPU。";
            if (!string.Equals(topology.CpuVendor, "GenuineIntel", StringComparison.OrdinalIgnoreCase)) return result;
            if (topology.Groups.Any(g => g.Mask == 0) || topology.Groups.Select(g => g.Number).Distinct().Count() != topology.Groups.Count) return result;
            byte[] classes = topology.Cores.Select(c => c.EfficiencyClass).Distinct().OrderBy(c => c).ToArray();
            if (classes.Length != 2)
            {
                result.Reason = "Windows 未提供可确认的两种核心等级；“仅大核”不可用，游戏沿用 CPU 组 1。";
                return result;
            }
            foreach (CpuGroup group in topology.Groups)
            {
                ulong covered = 0;
                foreach (CpuCore core in topology.Cores.Where(c => c.Group == group.Number))
                {
                    if (core.Mask == 0 || (core.Mask & ~group.Mask) != 0 || (covered & core.Mask) != 0) return result;
                    covered |= core.Mask;
                }
                if (covered != group.Mask) return result;
            }
            if (topology.Groups.Count == 0 || topology.Cores.Any(c => !topology.Groups.Any(g => g.Number == c.Group))) return result;
            result.cores.AddRange(topology.Cores); result.performanceClass = classes[1]; result.Recognized = true;
            result.Reason = "Windows 已报告 Intel 大小核：" + result.PerformanceCount + " 个大核（P） / " + result.EfficientCount + " 个小核（E）。";
            return result;
        }
        internal ulong MaskFor(int group, bool performance)
        {
            if (!Recognized) return 0;
            ulong mask = 0;
            foreach (CpuCore core in cores.Where(c => c.Group == group && (c.EfficiencyClass == performanceClass) == performance)) mask |= core.Mask;
            return mask;
        }
        internal string CoreLabel(CpuCore core)
        {
            if (!Recognized || core == null) return "";
            return core.EfficiencyClass == performanceClass ? "大核 P" : "小核 E";
        }
    }
}
