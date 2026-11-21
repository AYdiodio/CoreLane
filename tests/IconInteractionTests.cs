using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static List<ApplicationIconGroup> InteractionIcons()
        {
            return Enumerable.Range(0, 4).Select(i => ApplicationIconGroup.Build(new[] {
                SortingApp(301 + i, "Fixture " + i, @"C:\GestureFixture\" + i + @"\app.exe", null) }).Single()).ToList();
        }
        private static void IconInteractionTests()
        {
            Case("desktop selection: single click replaces, Ctrl toggles, Shift selects the current visible range", delegate
            {
                List<ApplicationIconGroup> icons = InteractionIcons(); ProcessSelection selected = new ProcessSelection();
                IconSelectionController controller = new IconSelectionController(selected);
                controller.Click(icons, 0, false, false); controller.Click(icons, 2, true, false);
                Check(selected.Targets(icons.SelectMany(g => g.Members)).Count == 2, "Ctrl adds to selected app");
                controller.Click(icons, 2, true, false);
                Check(selected.Targets(icons.SelectMany(g => g.Members)).Single() == icons[0].Members[0], "Ctrl deselects one app");
                controller.Click(icons, 1, false, false); controller.Click(icons, 3, false, true);
                Check(selected.Targets(icons.SelectMany(g => g.Members)).Count == 3 && !selected.Contains(icons[0].Members[0]), "Shift range replaces earlier choices");
                controller.Click(icons, 0, false, false);
                Check(selected.Targets(icons.SelectMany(g => g.Members)).Single() == icons[0].Members[0], "ordinary click clears other choices");
            });
            Case("desktop selection: rectangle hits visible icons only; Ctrl preserves its exact initial snapshot", delegate
            {
                List<ApplicationIconGroup> icons = InteractionIcons(); ProcessSelection selected = new ProcessSelection();
                IconSelectionController controller = new IconSelectionController(selected);
                Rectangle[] bounds = { new Rectangle(0, 0, 40, 40), new Rectangle(60, 0, 40, 40), new Rectangle(0, 60, 40, 40), new Rectangle(60, 60, 40, 40) };
                selected.Set(icons[3].Members, true); string[] original = selected.Snapshot();
                controller.Box(icons, bounds, new Rectangle(0, 0, 100, 45), original, true);
                Check(selected.Targets(icons.SelectMany(g => g.Members)).Count == 3, "Ctrl rectangle adds two top icons to original last icon");
                controller.Box(icons, bounds, new Rectangle(0, 0, 45, 45), original, true);
                Check(selected.Targets(icons.SelectMany(g => g.Members)).Count == 2 && !selected.Contains(icons[1].Members[0]), "shrinking rectangle recomputes from snapshot, not accumulated hits");
                controller.Box(icons, bounds, Rectangle.Empty, original, false);
                Check(selected.Targets(icons.SelectMany(g => g.Members)).Count == 0, "ordinary blank rectangle clears selection");
            });
            Case("desktop selection: cancellation can restore partial process selection without choosing new siblings", delegate
            {
                List<ApplicationIconGroup> icons = InteractionIcons(); ProcessSelection selected = new ProcessSelection();
                ProcessRow original = icons[0].Members[0]; selected.Set(new[] { original }, true); string[] snapshot = selected.Snapshot();
                icons[0].Applications[0].Members.Add(new ProcessRow { Pid = 310, Created = 3100, Name = original.Name, ExecutablePath = original.ExecutablePath });
                selected.Set(icons.SelectMany(g => g.Members), true); selected.Replace(snapshot);
                Check(selected.Targets(icons.SelectMany(g => g.Members)).Single() == original && selected.State(icons[0].Members) == CheckState.Indeterminate, "exact snapshot restored, new child stays unchecked");
                Check(original.Mask == 3, "selection does not modify affinity snapshots");
            });
            Case("drag payload: freezes and deduplicates PID plus creation identity, and cannot be enlarged by refresh", delegate
            {
                List<ApplicationIconGroup> icons = InteractionIcons(); ProcessRow original = icons[0].Members[0];
                int pid = original.Pid; long created = original.Created;
                original.Priority = PriorityLevels.AboveNormal;
                AppDragPayload payload = new AppDragPayload(new[] { original, original, icons[1].Members[0] });
                original.Created++; original.Pid = 999; original.Priority = PriorityLevels.BelowNormal; icons.Clear();
                Check(payload.Targets.Length == 2 && payload.Targets[0].Pid == pid && payload.Targets[0].Created == created, "original dragged instance remains exact");
                Check(payload.Targets[0].Priority == PriorityLevels.AboveNormal, "application settings receive the frozen read priority");
                ProcessRow[] returned = payload.Targets; returned[0].Pid = 777;
                Check(payload.Targets[0].Pid == pid, "returned list cannot mutate stored operation targets");
            });
            Case("drag admission: only this live payload token and actual writable topology target are accepted", delegate
            {
                Topology topology = Topology.Parse(Fixture(false)); List<CpuSelectionGroup> targets = CpuSelectionGroup.FromTopology(topology);
                AppDragPayload payload = new AppDragPayload(InteractionIcons()[0].Members);
                Check(payload.Accepts(payload.Token, topology, targets[0], targets, false), "valid local token and real target");
                Check(!payload.Accepts("foreign", topology, targets[0], targets, false), "external/other-window drag rejected");
                Check(!payload.Accepts(payload.Token, topology, targets[0], targets, true), "busy target rejected");
                Check(!payload.Accepts(payload.Token, topology, new CpuSelectionGroup { Mask = targets[0].Mask }, targets, false), "copied mask is not an authorized UI target");
                Check(!new AppDragPayload(new ProcessRow[0]).Accepts(payload.Token, topology, targets[0], targets, false), "empty drag rejected");
            });
            Case("drag admission: multi-group readonly topology never becomes writable through a drop", delegate
            {
                Topology topology = Topology.Parse(Fixture(true)); List<CpuSelectionGroup> targets = CpuSelectionGroup.FromTopology(topology);
                AppDragPayload payload = new AppDragPayload(InteractionIcons()[0].Members);
                Check(targets.All(target => !payload.Accepts(payload.Token, topology, target, targets, false)), "all drag targets remain readonly across Windows groups");
            });
        }
    }
}
