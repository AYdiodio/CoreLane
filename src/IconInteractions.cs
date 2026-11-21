using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace CoreAllocator
{
    internal sealed class IconSelectionController
    {
        private readonly ProcessSelection selection;
        private string anchor;
        internal IconSelectionController(ProcessSelection selection) { this.selection = selection; }
        internal void Click(IList<ApplicationIconGroup> icons, int index, bool additive, bool range)
        {
            if (index < 0 || index >= icons.Count) return;
            int start = anchor == null ? -1 : icons.ToList().FindIndex(g => g.InstanceKey == anchor);
            if (range && start >= 0)
            {
                if (!additive) selection.Set(icons.SelectMany(g => g.Members), false);
                for (int i = Math.Min(start, index); i <= Math.Max(start, index); i++) selection.Set(icons[i].Members, true);
                return;
            }
            bool wasSelected = selection.State(icons[index].Members) == System.Windows.Forms.CheckState.Checked;
            if (!additive) selection.Set(icons.SelectMany(g => g.Members), false);
            selection.Set(icons[index].Members, !additive || !wasSelected); anchor = icons[index].InstanceKey;
        }
        internal void Box(IList<ApplicationIconGroup> icons, Rectangle[] bounds, Rectangle box, IEnumerable<string> original, bool additive)
        {
            HashSet<string> keys = new HashSet<string>(additive ? original : new string[0], StringComparer.Ordinal);
            for (int i = 0; i < icons.Count; i++) if (i < bounds.Length && box.Width > 0 && box.Height > 0 && bounds[i].IntersectsWith(box))
                foreach (ProcessRow row in icons[i].Members) keys.Add(row.Key);
            selection.Replace(keys); selection.Retain(icons.SelectMany(g => g.Members));
        }
    }

    internal sealed class AppDragPayload
    {
        internal readonly string Token = "CCD-Scheduler:" + Guid.NewGuid().ToString("N");
        private readonly ProcessRow[] rows;
        internal AppDragPayload(IEnumerable<ProcessRow> selected)
        {
            rows = selected.GroupBy(p => p.Key).Select(g => g.First()).Select(p => new ProcessRow {
                Pid = p.Pid, Created = p.Created, Name = p.Name, ExecutablePath = p.ExecutablePath, Mask = p.Mask,
                CpuSetMask = p.CpuSetMask, Priority = p.Priority, ReadError = p.ReadError }).ToArray();
        }
        internal ProcessRow[] Targets { get { return rows.Select(p => new ProcessRow { Pid = p.Pid, Created = p.Created, Name = p.Name,
            ExecutablePath = p.ExecutablePath, Mask = p.Mask, CpuSetMask = p.CpuSetMask, Priority = p.Priority, ReadError = p.ReadError }).ToArray(); } }
        internal bool Accepts(string token, Topology topology, CpuSelectionGroup target, IEnumerable<CpuSelectionGroup> available, bool busy)
        {
            return !busy && rows.Length > 0 && token == Token && topology.CanAssign && target != null && target.WindowsGroup == 0 && target.Mask != 0 &&
                (target.Mask & ~topology.Available) == 0 && available.Any(g => object.ReferenceEquals(g, target));
        }
    }
}
