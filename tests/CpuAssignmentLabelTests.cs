using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static void CpuAssignmentLabelTests(string working)
        {
            Topology topology = Topology.Parse(Fixture(false));
            List<CpuSelectionGroup> groups = CpuSelectionGroup.FromTopology(topology);
            Func<ulong, ProcessRow> row = mask => new ProcessRow { Pid = 901, Created = 901, Name = "label.exe", ExecutablePath = @"C:\LabelFixture\label.exe", Mask = mask };
            Case("CPU labels: exact topology groups, cross-group and custom masks reflect read affinity", delegate
            {
                CpuAssignmentLabel first = CpuAssignmentLabel.From(new[] { row(0xffff) }, topology, groups);
                CpuAssignmentLabel second = CpuAssignmentLabel.From(new[] { row(0xffff0000) }, topology, groups);
                Check(first.Text == "CPU 组 1" && first.Ink == UiTheme.Blue && second.Text == "CPU 组 2" && second.Ink == UiTheme.Purple, "group labels and colors");
                Check(CpuAssignmentLabel.From(new[] { row(0xffffffff) }, topology, groups).Text == "跨组", "both complete groups");
                Check(CpuAssignmentLabel.From(new[] { row(3) }, topology, groups).Text == "自定义", "partial first group is custom");
                Check(CpuAssignmentLabel.From(new[] { row(0x10001) }, topology, groups).Text == "自定义", "partial selection across groups is custom");
            });
            Case("CPU labels: mixed and unreadable members never masquerade as a successful target", delegate
            {
                ProcessRow a = row(0xffff), b = row(0xffff0000); b.Pid++;
                Check(CpuAssignmentLabel.From(new[] { a, b }, topology, groups).Text == "混合分配", "different known masks");
                b.ReadError = "访问被拒绝";
                Check(CpuAssignmentLabel.From(new[] { a, b }, topology, groups).Text == "部分无法读取", "unknown member takes priority over known masks");
                Check(CpuAssignmentLabel.From(new[] { b }, topology, groups).Text == "无法读取", "all denied members");
                Check(CpuAssignmentLabel.From(new[] { row(0) }, topology, groups).Text == "无法读取", "missing mask is not CPU group one");
                Check(CpuAssignmentLabel.From(new ProcessRow[0], topology, groups).Text == "无法读取", "empty group");
                Check(CpuAssignmentLabel.From(new[] { row(1UL << 63) }, topology, groups).Text == "无法确认", "bits absent from actual topology");
            });
            Case("CPU labels: sparse high bits, additional groups and native multi-group ambiguity are preserved", delegate
            {
                Topology sparse = new Topology(); sparse.Groups.Add(new CpuGroup { Number = 0, Mask = 0x8000000000000005 });
                sparse.Clusters.Add(new CpuCluster { Group = 0, Mask = 1 });
                sparse.Clusters.Add(new CpuCluster { Group = 0, Mask = 4 });
                sparse.Clusters.Add(new CpuCluster { Group = 0, Mask = 1UL << 63 });
                List<CpuSelectionGroup> native = CpuSelectionGroup.FromTopology(sparse);
                Check(CpuAssignmentLabel.From(new[] { row(1UL << 63) }, sparse, native).Text == "CPU 组 3", "third native L3 group not flattened into group two");
                Check(CpuAssignmentLabel.From(new[] { row(sparse.Available) }, sparse, native).Text == "跨组", "sparse union including high bit");
                Topology multiple = Topology.Parse(Fixture(true));
                Check(CpuAssignmentLabel.From(new[] { row(1) }, multiple, CpuSelectionGroup.FromTopology(multiple)).Text == "组别未确认", "same numeric mask cannot identify a native processor group");
                Topology hybrid = HybridFixture(); ulong performance = IntelHybridInfo.Detect(hybrid).MaskFor(0, true);
                Check(CpuAssignmentLabel.From(new[] { row(performance) }, hybrid, CpuSelectionGroup.FromTopology(hybrid)).Text == "大核 P", "confirmed Intel P-only affinity shown explicitly");
                hybrid.CpuVendor = "AuthenticAMD";
                Check(CpuAssignmentLabel.From(new[] { row(performance) }, hybrid, CpuSelectionGroup.FromTopology(hybrid)).Text == "自定义", "AMD does not inherit an Intel label");
            });
            Case("CPU labels: merged picture summarizes all constituent applications without changing identities", delegate
            {
                ApplicationGroup a = SortingApp(902, "A", @"C:\LabelFixture\A.exe", null);
                ApplicationGroup b = SortingApp(903, "B", @"D:\LabelFixture\B.exe", null);
                a.Members[0].Mask = 0xffff; b.Members[0].Mask = 0xffff0000;
                ApplicationIconGroup bucket = ApplicationIconGroup.Build(new[] { a, b }).Single();
                string identity = bucket.InstanceKey;
                Check(CpuAssignmentLabel.From(bucket.Members, topology, groups).Text == "混合分配", "same picture does not assume same affinity");
                Check(bucket.InstanceKey == identity && bucket.Members.Count == 2 && a.Members[0].Mask == 0xffff && b.Members[0].Mask == 0xffff0000, "presentation does not change members or masks");
            });
            Case("CPU labels: single view displays live labels while preserving selection and drag geometry", delegate
            {
                AffinityService service = new AffinityService(topology, new SnapshotStore(Path.Combine(working, "label-ui", "original.json")));
                using (MainForm form = new MainForm(service))
                {
                    ProcessRow item = row(0xffff);
                    SchemeStore store = new SchemeStore(Path.Combine(working, "label-ui", "schemes.json"));
                    store.Classify(ApplicationGroup.Build(new[] { item }, delegate { return null; }).Single(), true);
                    typeof(MainForm).GetField("schemeStore", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(form, store);
                    typeof(MainForm).GetField("processes", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(form, new List<ProcessRow> { item });
                    StartupInvoke(form, "RenderProcesses");
                    AppIconPanel panel = (AppIconPanel)StartupField(form, "applications");
                    AppIconTile tile = panel.Controls.OfType<AppIconTile>().Single();
                    Check(tile.ShowCpuAssignment && tile.AssignmentLabel.Text == "CPU 组 1" && tile.Height > tile.Width, "layout reserves label room");
                    ((ProcessSelection)StartupField(form, "processSelection")).Set(new[] { item }, true);
                    item.Mask = 0xffff0000; StartupInvoke(form, "RenderProcesses");
                    Check(object.ReferenceEquals(tile, panel.Controls.OfType<AppIconTile>().Single()) && tile.AssignmentLabel.Text == "CPU 组 2", "latest read replaces label even when tile is reused");
                    Check(tile.AccessibleDescription.Contains("CPU 组 2"), "accessible description agrees with visible label");
                    Check(tile.ShowCpuAssignment && tile.Height > tile.Width && tile.AssignmentLabel.Text == "CPU 组 2" && tile.CheckState == CheckState.Checked, "selection survives refresh");
                    item.Mask = 0xffffffff; StartupInvoke(form, "RenderProcesses");
                    Check(object.ReferenceEquals(tile, panel.Controls.OfType<AppIconTile>().Single()) && tile.AssignmentLabel.Text == "跨组", "manual refresh updates reused tile from actual read mask");
                    Check(tile.DragImagePart.ScreenBounds.Bottom < tile.PointToScreen(new System.Drawing.Point(0,tile.Height-28*(int)UiTheme.Scale(tile))).Y, "drag image contains the icon above the label");
                    item.Mask = 0xffff0000; StartupInvoke(form, "RenderProcesses");
                    Check(service.Store.Items.Length == 0 && item.Mask == 0xffff0000, "view changes do not write affinity or restore records");
                }
            });
        }
    }
}
