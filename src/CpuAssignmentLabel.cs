using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace CoreAllocator
{
    // Summarize the last read affinity masks, never a scheme's intended target.
    internal sealed class CpuAssignmentLabel
    {
        internal readonly string Text;
        internal readonly Color Ink, Fill;
        private CpuAssignmentLabel(string text, Color ink, Color fill) { Text = text; Ink = ink; Fill = fill; }
        private static CpuAssignmentLabel Neutral(string text) { return new CpuAssignmentLabel(text, UiTheme.Muted, UiTheme.Page); }
        private static CpuAssignmentLabel Unknown(string text)
        { return new CpuAssignmentLabel(text, Color.FromArgb(155, 88, 28), Color.FromArgb(255, 242, 226)); }

        internal static CpuAssignmentLabel ForTile(IEnumerable<ProcessRow> processes, Topology topology, IList<CpuSelectionGroup> groups)
        {
            ProcessRow[] members = processes.ToArray();
            if (members.Any(row => row.CpuSetMask.GetValueOrDefault() != 0))
            {
                ulong? first = members[0].CpuSetMask;
                return members.All(row => row.CpuSetMask == first)
                    ? new CpuAssignmentLabel("CPU 集", UiTheme.Blue, UiTheme.BlueWash)
                    : Neutral("CPU 集混合");
            }
            return From(members, topology, groups);
        }

        internal static CpuAssignmentLabel From(IEnumerable<ProcessRow> processes, Topology topology, IList<CpuSelectionGroup> groups)
        {
            ProcessRow[] members = processes.ToArray();
            int failed = members.Count(p => p.ReadError != null || p.Mask == 0);
            if (members.Length == 0 || failed == members.Length) return Unknown("无法读取");
            if (failed > 0) return Unknown("部分无法读取");
            // Process affinity alone cannot identify a native Windows group on multi-group machines.
            if (!topology.CanAssign) return Unknown("组别未确认");
            if (members.Any(p => (p.Mask & ~topology.Available) != 0)) return Unknown("无法确认");
            ulong[] masks = members.Select(p => p.Mask).Distinct().ToArray();
            if (masks.Length > 1) return Neutral("混合分配");
            ulong mask = masks[0];
            CpuSelectionGroup exact = groups.FirstOrDefault(g => g.WindowsGroup == 0 && g.Mask == mask);
            if (exact != null)
            {
                if (exact.Index == 0) return new CpuAssignmentLabel(exact.Name, UiTheme.Blue, UiTheme.BlueWash);
                if (exact.Index == 1) return new CpuAssignmentLabel(exact.Name, UiTheme.Purple, UiTheme.PurpleWash);
                return Neutral(exact.Name);
            }
            IntelHybridInfo hybrid = IntelHybridInfo.Detect(topology);
            if (hybrid.Recognized && mask == hybrid.MaskFor(0, true)) return new CpuAssignmentLabel("仅大核（P）", UiTheme.Blue, UiTheme.BlueWash);
            CpuSelectionGroup[] matched = groups.Where(g => g.WindowsGroup == 0 && (g.Mask & mask) != 0).ToArray();
            ulong union = 0; foreach (CpuSelectionGroup group in matched) union |= group.Mask;
            return Neutral(matched.Length > 1 && union == mask ? "跨组" : "自定义");
        }
    }
}
