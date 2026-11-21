using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int ScrollWindowStyle(IntPtr window, int index);
        [StructLayout(LayoutKind.Sequential)] private struct DirtyBounds { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetUpdateRect(IntPtr window, out DirtyBounds bounds, bool erase);
        private static void RequiresFullRepaint(Control control, string label)
        {
            // Native invalidation is observable even when another application occludes this window.
            // A desktop CopyFromScreen comparison can falsely pass by capturing that other app twice.
            DirtyBounds dirty;
            Check(GetUpdateRect(control.Handle, out dirty, false) && dirty.Left <= 0 && dirty.Top <= 0 && dirty.Right >= control.ClientSize.Width && dirty.Bottom >= control.ClientSize.Height,
                label + ": incomplete repaint region " + dirty.Left + "," + dirty.Top + "," + dirty.Right + "," + dirty.Bottom);
        }
        private static void ResizeScrollTests(string working)
        {
            Case("settings layout: content-sized priorities, exact picker alignment and a common card edge for one and ten processes", delegate
            {
                foreach (int count in new[] { 1, 10 })
                {
                    ProcessRow[] targets = Enumerable.Range(0, count).Select(i => new ProcessRow { Pid = 80000 + i, Created = i + 1, Name = "fixture.exe", ExecutablePath = @"C:\Fixture\app.exe", Priority = PriorityLevels.Normal }).ToArray();
                    using (CpuMaskDialog dialog = new CpuMaskDialog(Topology.Parse(Fixture(false)), 1, false, "布局测试", null, true, null, targets, true))
                    {
                        dialog.Show(); Application.DoEvents();
                        SettingsSectionHeader priority = ChildControls(dialog).OfType<SettingsSectionHeader>().Single(h => h.Text == "进程优先级"); priority.PerformClick();
                        CapsuleScrollPanel viewport = ChildControls(dialog).OfType<CapsuleScrollPanel>().Single(p => p.AccessibleName == "应用设置内容");
                        ApplicationPriorityEditor editor = ChildControls(dialog).OfType<ApplicationPriorityEditor>().Single();
                        RoundedSurface info = ChildControls(dialog).OfType<Label>().Single(l => l.Text.StartsWith("最近读取的允许范围")).Parent as RoundedSurface;
                        foreach (Size size in new[] { new Size(780, 700), new Size(950, 820), new Size(700, 620) })
                        {
                            dialog.ClientSize = size; Application.DoEvents();
                            float scale = UiTheme.Scale(dialog);
                            Check(Math.Abs(editor.Height - (48 + 24 + 76 * count) * scale) <= 2, "priority height follows the actual row count");
                            CapsuleSchemePicker[] pickers = ChildControls(editor).OfType<CapsuleSchemePicker>().ToArray();
                            Check(pickers.Length == count + 1 && pickers.All(p => p.PointToScreen(Point.Empty).X == pickers[0].PointToScreen(Point.Empty).X && p.Width == pickers[0].Width), "bulk and individual capsules share left and right edges");
                            Check(info.PointToScreen(Point.Empty).X == priority.PointToScreen(Point.Empty).X && info.Width == priority.Width && editor.Width == info.Width, "top card, accordion and priority card share bounds");
                            Check(!viewport.AutoScroll && viewport.Rail.Width == (int)Math.Round(26 * scale) && viewport.Rail.TrackBounds.Width == (int)Math.Round(8 * scale), "main-view rail geometry is reused");
                        }
                        if (count == 1) { dialog.ClientSize = new Size(1100, 1000); Application.DoEvents(); Check(viewport.MaximumScroll == 0, "a single row fits the normal-height window without scroll"); }
                        else
                        {
                            Check(viewport.MaximumScroll > 0, "ten rows overflow the common viewport");
                            viewport.ScrollTo(viewport.MaximumScroll); Check(viewport.Content.Bottom == viewport.ClientSize.Height, "last row is reachable");
                            priority.PerformClick(); Check(viewport.ScrollOffset == 0 && viewport.MaximumScroll == 0, "collapsing clamps the previous offset");
                        }
                        Check(ChildControls(dialog).OfType<ScrollableControl>().All(p => !p.AutoScroll), "no nested native scrolling remains"); dialog.Close();
                    }
                }
            });
            Case("shared scrollbar: wide hit area drags to both ends and wheel works over child content", delegate
            {
                using (Form form = new Form { ClientSize = new Size(600, 350) })
                using (CapsuleScrollPanel viewport = new CapsuleScrollPanel { Dock = DockStyle.Fill })
                {
                    form.Controls.Add(viewport); viewport.Content.Controls.Add(new Label { Text = "内容", Width = 180, Height = 100 }); viewport.SetContentHeight(1500);
                    form.Show(); Application.DoEvents();
                    CapsuleScrollRail rail = viewport.Rail;
                    Action<uint, Point> mouse = delegate(uint msg, Point p) { SendCoreTestMessage(rail.Handle, msg, msg == 0x202 ? IntPtr.Zero : new IntPtr(1), new IntPtr((p.X & 0xffff) | ((p.Y & 0xffff) << 16))); };
                    mouse(0x201, new Point(1, rail.Height - 3)); Check(rail.Capture && viewport.ScrollOffset == viewport.MaximumScroll, "the rail's wider side area accepts a drag");
                    mouse(0x200, new Point(1, 0)); mouse(0x202, new Point(1, 0)); Check(!rail.Capture && viewport.ScrollOffset == 0, "dragging returns to top and releases capture");
                    int lparam = 0;
                    SendCoreTestMessage(viewport.Content.Controls[0].Handle, 0x20a, new IntPtr((-120 & 0xffff) << 16), new IntPtr(lparam));
                    Check(viewport.ScrollOffset > 0, "wheel on a native child bubbles to the common scroll viewport");
                    Check((ScrollWindowStyle(viewport.Handle, -16) & 0x00300000) == 0, "no native horizontal or vertical scrollbar styles");
                    viewport.SetContentHeight(20); Check(viewport.MaximumScroll == 0 && viewport.ScrollOffset == 0, "shrinking removes overflow and resets scroll"); form.Close();
                }
            });
            Case("dropdown and message scrolling: long content uses shared capsules and selected options stay reachable", delegate
            {
                using (Form form = new Form())
                using (CapsuleSchemePicker picker = new CapsuleSchemePicker { Width = 220, Height = 40 })
                {
                    form.Controls.Add(picker); picker.Items.AddRange(Enumerable.Range(0, 50).Select(i => (object)("方案 " + i))); picker.SelectedIndex = 40;
                    form.Show(); picker.ToggleDropDown(); Application.DoEvents();
                    CapsuleDropDown popup = (CapsuleDropDown)typeof(CapsuleSchemePicker).GetField("popup", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(picker);
                    CapsuleScrollPanel viewport = ChildControls(popup).OfType<CapsuleScrollPanel>().Single();
                    CapsuleMenuOption chosen = ChildControls(popup).OfType<CapsuleMenuOption>().Single(option => option.Text == "方案 40");
                    Check(viewport.MaximumScroll > 0 && viewport.ScrollOffset > 0 && viewport.RectangleToClient(chosen.RectangleToScreen(chosen.ClientRectangle)).Bottom <= viewport.Height, "selected option is revealed in long lists");
                    chosen.PerformClick(); Check(picker.SelectedIndex == 40, "same selection and original close behavior"); form.Close();
                }
                using (AppMessageDialog message = new AppMessageDialog(string.Join("\r\n", Enumerable.Repeat("用于验证长消息与日志滚动的说明文字。", 80)), "长消息测试", MessageBoxButtons.OK))
                {
                    message.Show(); Application.DoEvents();
                    CapsuleScrollPanel viewport = ChildControls(message).OfType<CapsuleScrollPanel>().Single();
                    Check(viewport.MaximumScroll > 0 && ChildControls(message).OfType<TextBox>().Single().ScrollBars == ScrollBars.None, "long message uses the shared rail");
                    viewport.ScrollTo(viewport.MaximumScroll); Check(viewport.Content.Bottom == viewport.ClientSize.Height, "message end is reachable"); message.Close();
                }
            });
            Case("rapid resize painting: native full repaint regions cover rounded edges, reflowed labels and scroll damage", delegate
            {
                using (Form form = new Form { AutoScaleMode = AutoScaleMode.None, ClientSize = new Size(900, 620), BackColor = UiTheme.Page, StartPosition = FormStartPosition.Manual, Location = new Point(30, 40) })
                using (RoundedSurface card = new RoundedSurface { BackColor = Color.White, Bounds = new Rectangle(10, 10, 400, 80) })
                using (AppIconPanel grid = new AppIconPanel { BackColor = Color.White, Bounds = new Rectangle(10, 110, 760, 480) })
                {
                    form.Controls.Add(card); form.Controls.Add(grid); form.Show(); form.Activate(); Application.DoEvents();
                    Topology topology = Topology.Parse(Fixture(false)); ApplicationIconGroup group = InteractionIcons()[0];
                    CpuAssignmentLabel label = CpuAssignmentLabel.From(group.Members, topology, CpuSelectionGroup.FromTopology(topology));
                    float scale = UiTheme.Scale(grid);
                    for (int i = 0; i < 30; i++) grid.Controls.Add(new AppIconTile { Width = (int)Math.Round(84 * scale), Height = (int)Math.Round(90 * scale), Group = group, ShowCpuAssignment = true, AssignmentLabel = label });
                    for (int i = 0; i < 32; i++)
                    {
                        card.Refresh(); grid.Refresh();
                        card.Width = 280 + (i * 67 % 440); grid.Width = 480 + (i * 97 % 300);
                        RequiresFullRepaint(card, "rounded card resize " + i);
                        RequiresFullRepaint(grid, "app grid reflow " + i);
                        if (i % 2 == 0) Application.DoEvents();
                    }
                    card.Width = 750; grid.Width = 760; grid.Refresh(); grid.ScrollTo(grid.MaximumScroll);
                    RequiresFullRepaint(grid, "scroll to last row"); grid.Refresh(); grid.ScrollTo(0); RequiresFullRepaint(grid, "scroll back to top"); Application.DoEvents();
                    AppIconTile source = grid.Controls.OfType<AppIconTile>().First(); source.Checked = true; source.Capture = true; source.Dragging = true;
                    using (AppDragPreview preview = new AppDragPreview(new[] { source.DragImagePart }, Cursor.Position, 1))
                    {
                        preview.ShowPreview(form); Application.DoEvents(); Check(source.Capture && form.ContainsFocus, "drag starts normally after reflow"); preview.Close();
                    }
                    source.Capture = false; source.Dragging = false; grid.Invalidate(true); Application.DoEvents();
                    Check(grid.Controls.OfType<AppIconTile>().All(tile => tile.AssignmentLabel == label), "reflow and cancelled preview retain the original assignment labels"); form.Close();
                }
            });
            Case("CPU target overflow: additional groups use the same capsule rail without native scrollbar styles", delegate
            {
                Topology topology = new Topology { CpuName = "UI fixture with four L3 groups" };
                topology.Groups.Add(new CpuGroup { Number = 0, Mask = 15 });
                for (int i = 0; i < 4; i++) topology.Clusters.Add(new CpuCluster { Group = 0, Mask = 1UL << i });
                using (MainForm form = new MainForm(new AffinityService(topology, new SnapshotStore(Path.Combine(working, "four-targets", "original.json")))))
                {
                    form.Show(); Application.DoEvents();
                    Check(ChildControls(form).OfType<CpuTargetTile>().Count() == 4, "all detected targets remain present");
                    CapsuleScrollPanel targets = ChildControls(form).OfType<CapsuleScrollPanel>().Single();
                    Check(targets.MaximumScroll > 0 && !targets.AutoScroll, "target overflow uses shared scrolling");
                    targets.ScrollTo(targets.MaximumScroll); Check(targets.Content.Bottom == targets.ClientSize.Height, "last target row is accessible");
                }
            });
        }
    }
}
