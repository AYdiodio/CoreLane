using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static IEnumerable<Control> ChildControls(Control parent)
        {
            foreach (Control child in parent.Controls) { yield return child; foreach (Control nested in ChildControls(child)) yield return nested; }
        }
        private static void CpuPickerTests()
        {
            Case("CPU settings: collapsed sections preserve drafts and use stable content spacing on resize", delegate
            {
                ProcessRow target = new ProcessRow { Pid = 123, Created = 456, Name = "fixture.exe", ExecutablePath = "C:\\Games\\fixture.exe", Priority = PriorityLevels.Normal };
                using (CpuMaskDialog dialog = new CpuMaskDialog(Topology.Parse(Fixture(false)), 1, false, "测试应用", null, true, null, new[] { target }, true))
                {
                    dialog.Show(); Application.DoEvents();
                    SettingsSectionHeader[] headers = ChildControls(dialog).OfType<SettingsSectionHeader>().ToArray();
                    SettingsSectionHeader cores = headers.Single(h => h.Text == "核心分配"), priority = headers.Single(h => h.Text == "进程优先级");
                    Check(headers.All(h => !h.Expanded), "both start collapsed");
                    Check(!ChildControls(dialog).Any(c => c.Text == "全部 CPU" || c.Text == "清空选择"), "global selection buttons removed");
                    cores.PerformClick(); Application.DoEvents();
                    CoreCheckBox box = ChildControls(dialog).OfType<CoreCheckBox>().Single(c => c.Text == "CPU 3"); box.Checked = true;
                    int gapBefore = priority.Top - (cores.Bottom + cores.Parent.Controls.OfType<Panel>().Single(p => p.Controls.OfType<FlowLayoutPanel>().Any()).Height);
                    dialog.Height += 160; Application.DoEvents();
                    int gapAfter = priority.Top - (cores.Bottom + cores.Parent.Controls.OfType<Panel>().Single(p => p.Controls.OfType<FlowLayoutPanel>().Any()).Height);
                    Check(gapBefore == gapAfter, "window height does not create an expanding gap");
                    priority.PerformClick(); Application.DoEvents();
                    CapsuleSchemePicker picker = ChildControls(dialog).OfType<CapsuleSchemePicker>().Single(p => p.AccessibleName == target.Key + " 进程优先级"); picker.SelectedIndex = 3;
                    cores.PerformClick(); priority.PerformClick(); cores.PerformClick(); priority.PerformClick();
                    Check(box.Checked && dialog.SelectedMask == 9 && dialog.PriorityChanges[target.Key] == PriorityLevels.AboveNormal, "folding preserves both drafts");
                    dialog.Close();
                }
            });
            Case("CPU sweep: every re-entry toggles, stationary moves do not, mouse release does not double-toggle", delegate
            {
                using (CpuMaskDialog dialog = new CpuMaskDialog(Topology.Parse(Fixture(false)), 0, false))
                {
                    dialog.Show(); Application.DoEvents();
                    CoreCheckBox[] boxes = ChildControls(dialog).OfType<CoreCheckBox>().ToArray();
                    CoreCheckBox first = boxes.Single(c => c.Text == "CPU 0"), second = boxes.Single(c => c.Text == "CPU 1");
                    Action<Control, uint, Point> mouse = delegate(Control c, uint message, Point point)
                    { SendCoreTestMessage(c.Handle, message, message == 0x202 ? IntPtr.Zero : new IntPtr(1), new IntPtr((point.X & 0xffff) | ((point.Y & 0xffff) << 16))); };
                    Point a = new Point(first.Width / 2, first.Height / 2), b = first.PointToClient(second.PointToScreen(new Point(second.Width / 2, second.Height / 2)));
                    mouse(first, 0x201, a); Check(first.Checked && first.Capture, "press selects and captures");
                    mouse(first, 0x200, b); Check(first.Checked && second.Checked, "sweep selects second");
                    mouse(first, 0x200, a); Check(!first.Checked && second.Checked, "re-entering first toggles it off");
                    mouse(first, 0x200, new Point(a.X + 1, a.Y)); Check(!first.Checked, "moving within one core is not another entry");
                    mouse(first, 0x200, b); Check(!second.Checked, "second re-entry toggles off too");
                    mouse(first, 0x202, b); Check(!first.Checked && !second.Checked && !first.Capture, "release neither adds a click nor keeps capture");
                    typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(first, new object[] { new KeyEventArgs(Keys.Space) });
                    typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(first, new object[] { EventArgs.Empty });
                    Check(first.Checked, "keyboard toggle remains available after a mouse stroke"); dialog.Close();
                }
            });
            Case("marquee: single transparent surface keeps capture above child controls and is disposed on release", delegate
            {
                using (Form form = new Form { Width = 700, Height = 450 })
                using (AppIconPanel panel = new AppIconPanel { Dock = DockStyle.Fill })
                {
                    form.Controls.Add(panel); panel.Controls.Add(new AppIconTile { Width = 84, Height = 90 }); form.Show(); Application.DoEvents();
                    Action<uint, Point> mouse = delegate(uint message, Point point)
                    { SendCoreTestMessage(panel.Handle, message, message == 0x202 ? IntPtr.Zero : new IntPtr(1), new IntPtr(point.X | point.Y << 16)); };
                    form.Activate(); Application.DoEvents(); Form activeBefore = Form.ActiveForm;
                    mouse(0x201, new Point(1, 1)); mouse(0x200, new Point(500, 300)); Application.DoEvents();
                    SelectionMarquee overlay = Application.OpenForms.OfType<SelectionMarquee>().Single();
                    Check(panel.IsSelecting && panel.Capture && overlay.Owner == form && overlay.Visible && Form.ActiveForm == activeBefore,
                        "overlay does not steal focus or capture: selecting=" + panel.IsSelecting + "; capture=" + panel.Capture + "; visible=" + overlay.Visible + "; same active=" + (Form.ActiveForm == activeBefore));
                    mouse(0x200, new Point(100, 80)); Check(panel.SelectionBounds.Width == 99 && panel.Capture, "shrinking keeps current rectangle and capture");
                    mouse(0x202, new Point(100, 80)); Check(!panel.IsSelecting && overlay.IsDisposed, "release removes surface");
                    mouse(0x201, new Point(1, 1)); mouse(0x200, new Point(200, 150)); form.Width += 40; Application.DoEvents();
                    Check(!panel.IsSelecting && !Application.OpenForms.OfType<SelectionMarquee>().Any(), "resize cancels and removes the surface"); form.Close();
                }
            });
            Case("CPU picker: actual sparse bits single CPU and cross-group high bits stay exact", delegate
            {
                Topology topology = new Topology { CpuName = "Sparse fixture" };
                topology.Groups.Add(new CpuGroup { Number = 0, Mask = 0x8000000000000005UL });
                topology.Clusters.Add(new CpuCluster { Group = 0, Mask = 5, CacheBytes = 96 * 1048576 });
                topology.Clusters.Add(new CpuCluster { Group = 0, Mask = 1UL << 63, CacheBytes = 32 * 1048576 });
                using (CpuMaskDialog dialog = new CpuMaskDialog(topology, 1, false))
                {
                    CheckBox[] boxes = ChildControls(dialog).OfType<CheckBox>().ToArray();
                    Check(boxes.Length == 3 && boxes.Any(b => b.Text == "CPU 63") && boxes.All(b => b.Text != "CPU 1"), "only actual CPU numbers rendered");
                    foreach (CheckBox box in boxes) box.Checked = false;
                    Button save = ChildControls(dialog).OfType<Button>().Single(b => b.Text == "确认选择");
                    Check(dialog.SelectedMask == 0 && !save.Enabled, "empty mask cannot be confirmed");
                    boxes.Single(b => b.Text == "CPU 63").Checked = true;
                    Check(dialog.SelectedMask == (1UL << 63) && save.Enabled, "single high bit selectable");
                    boxes.Single(b => b.Text == "CPU 2").Checked = true;
                    Check(dialog.SelectedMask == 0x8000000000000004UL, "cross-L3 selections preserved together");
                }
            });
            Case("CPU picker: multiple Windows groups and device detail view cannot enable writes", delegate
            {
                Topology topology = Topology.Parse(Fixture(true));
                using (CpuMaskDialog dialog = new CpuMaskDialog(topology, 0, false))
                {
                    CheckBox[] boxes = ChildControls(dialog).OfType<CheckBox>().ToArray();
                    Check(boxes.Length == 34 && boxes.All(b => !b.Enabled), "all native groups shown read-only");
                    Check(boxes.Any(b => b.AccessibleName.Contains("G1")), "Windows group identity retained in accessible labels");
                    Check(!ChildControls(dialog).OfType<Button>().Single(b => b.Text == "确认选择").Enabled, "unsupported topology cannot save");
                }
                using (CpuMaskDialog details = new CpuMaskDialog(Topology.Parse(Fixture(false)), 1, true))
                    Check(ChildControls(details).OfType<CheckBox>().All(b => !b.Enabled), "device info view never edits target selection");
            });
        }
    }
}
