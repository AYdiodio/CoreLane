using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static void CapsuleControlsTests(string working)
        {
            Case("toolbar menus: shared full-width rows, aligned columns, separators and distinct enable tone", delegate
            {
                AffinityService service = new AffinityService(Topology.Parse(Fixture(false)), new SnapshotStore(Path.Combine(working, "menu-layout", "original.json")));
                using (MainForm form = new MainForm(service))
                {
                    form.Show(); Application.DoEvents();
                    StartupInvoke(form, "StopAutomation", "隔离菜单测试");
                    ContextMenuStrip more = (ContextMenuStrip)StartupField(form, "moreMenu");
                    StartupInvoke(form, "ShowToolbarMenu", more);
                    float scale = UiTheme.Scale(more);
                    Check(more.Items.OfType<GlyphMenuItem>().All(item => item.Bounds.Left == 0 && item.Width == more.ClientSize.Width && Math.Abs(item.Height - 44 * scale) <= 1), "every main menu row has the same full width and height");
                    Check(more.Items[0].Bounds.Top >= 8 * scale && more.Items[more.Items.Count - 1].Bounds.Bottom <= more.ClientSize.Height, "rows fit inside rounded border padding");
                    more.Close(); StartupInvoke(form, "ShowAutomationMenu");
                    ContextMenuStrip automatic = (ContextMenuStrip)StartupField(form, "automationMenu");
                    Check(automatic.Items.OfType<GlyphMenuItem>().All(item => item.Width == automatic.ClientSize.Width && item.Bounds.Left == 0), "automatic rows use identical columns");
                    Check(automatic.Items.OfType<CapsuleMenuSeparator>().Count() == 2 &&
                        !automatic.Items.OfType<GlyphMenuItem>().Any(item => item.Text == "新建方案") &&
                        automatic.Items.OfType<GlyphMenuItem>().Single(item => item.Text == "保存方案…").Glyph == UiGlyph.Check,
                        "the automation menu has one save entry and retains aligned separators");
                    GlyphMenuItem enable = automatic.Items.OfType<GlyphMenuItem>().Single(item => item.Text == "启用方案");
                    Check((ButtonTone)enable.Tag == ButtonTone.Start && enable.Glyph == UiGlyph.Play, "enable keeps green tone and play icon");
                    automatic.Close();
                }
            });
            Case("hardware settings: P/E partition, AMD L3-derived CCD tags and priority indicators reflect read values", delegate
            {
                Topology intel = HybridFixture();
                CpuSelectionGroup[] groups = CpuSelectionGroup.FromTopology(intel).ToArray();
                Check(groups.Length == 2 && groups[0].Name == "大核 P" && groups[1].Name == "小核 E", "recognized Intel gets two labeled targets");
                Check(groups[0].Mask == IntelHybridInfo.Detect(intel).MaskFor(0, true) &&
                    groups[1].Mask == IntelHybridInfo.Detect(intel).MaskFor(0, false), "non-contiguous CPUs and high bits retained");
                Topology amd = Topology.Parse(Fixture(false)); amd.CpuVendor = "AuthenticAMD";
                Check(CpuSelectionGroup.FromTopology(amd).Select(group => group.ClusterLabel).SequenceEqual(new[] { "CCD 0", "CCD 1" }), "AMD uses actual cache groups");
                Check(PriorityLevels.Indicator(new[] { new ProcessRow { Priority = PriorityLevels.Normal } }) == "", "normal needs no arrow");
                Check(PriorityLevels.Indicator(new[] { new ProcessRow { Priority = PriorityLevels.AboveNormal } }) == "↑", "above normal");
                Check(PriorityLevels.Indicator(new[] { new ProcessRow { Priority = PriorityLevels.BelowNormal } }) == "↓", "below normal");
                Check(PriorityLevels.Indicator(new[] { new ProcessRow { Priority = PriorityLevels.Normal }, new ProcessRow { Priority = PriorityLevels.AboveNormal } }) == "↕", "mixed priorities remain visible");
                Check(PriorityLevels.Indicator(new[] { new ProcessRow() }) == "?", "unknown does not pretend normal");
            });
            Case("capsule selector: original objects, exact index changes and invalid selection protection", delegate
            {
                using (CapsuleSchemePicker picker = new CapsuleSchemePicker())
                {
                    object first = new object(), second = new object(); int changed = 0;
                    picker.Items.Add(first); picker.Items.Add(second); picker.SelectedIndexChanged += delegate { changed++; };
                    picker.SelectedIndex = 0; picker.SelectedIndex = 0;
                    Check(changed == 1 && object.ReferenceEquals(picker.SelectedItem, first), "same selection does not generate a second action");
                    picker.SelectedIndex = 1;
                    Check(changed == 2 && object.ReferenceEquals(picker.SelectedItem, second), "underlying scheme identity retained");
                    Throws(delegate { picker.SelectedIndex = 2; });
                    Check(changed == 2 && picker.SelectedIndex == 1, "out-of-range value cannot change choice");
                    picker.SelectedIndex = -1;
                    Check(picker.SelectedItem == null && changed == 3, "empty selection supported");
                }
            });
            Case("compact UI: search and menu share a surface, recovery depends on records, CPU cards remain visible", delegate
            {
                string directory = Path.Combine(working, "capsule-ui");
                Topology topology = Topology.Parse(Fixture(false));
                AffinityService service = new AffinityService(topology, new SnapshotStore(Path.Combine(directory, "original.json")));
                using (MainForm form = new MainForm(service))
                {
                    Control[] controls = ChildControls(form).ToArray();
                    var search = controls.OfType<SearchTextBox>().Single();
                    var capsule = controls.OfType<SearchSurface>().Single();
                    form.Handle.ToInt64();
                    capsule.Handle.ToInt64();
                    Check(search.Parent == capsule && capsule.Controls.Count == 1 && search.BackColor == capsule.BackColor,
                        "both icons share a painted capsule around the native text field");
                    form.PerformLayout();
                    capsule.PerformLayout();
                    float scale = UiTheme.Scale(capsule);
                    AppIconPanel grid = controls.OfType<AppIconPanel>().Single();
                    AppScrollRail rail = controls.OfType<AppScrollRail>().Single();
                    Func<Control, Rectangle> formBounds = delegate(Control control)
                    {
                        Rectangle bounds = control.Bounds;
                        for (Control parent = control.Parent; parent != form; parent = parent.Parent) bounds.Offset(parent.Location);
                        return bounds;
                    };
                    CapsuleLabel hardware = controls.OfType<CapsuleLabel>().Single(label => label.AccessibleName == "CPU 型号");
                    CardPanel mainPanel = (CardPanel)grid.Parent.Parent.Parent;
                    Action checkToolbar = delegate
                    {
                        Rectangle caption = formBounds(hardware), searchBounds = formBounds(capsule), panelBounds = formBounds(mainPanel);
                        Check(caption.Left == panelBounds.Left && searchBounds.Right == panelBounds.Right,
                            "toolbar aligns with the outer edges of the white panel: " + caption + " / " + searchBounds + " / " + panelBounds);
                        Check(Math.Abs((caption.Top + caption.Bottom) - (searchBounds.Top + searchBounds.Bottom)) <= 2 && caption.Right < searchBounds.Left,
                            "CPU pill and search share a centerline without overlap");
                        Check(formBounds(capsule.Parent).Top <= Math.Round(10 * scale), "toolbar sits near the top of the client area");
                    };
                    Check(!search.Visible && Math.Abs(capsule.Width - Math.Round(72 * scale)) <= 2, "compact by default: " + capsule.Width + " at " + scale);
                    checkToolbar();
                    int right = capsule.Right;
                    search.Text = "example";
                    capsule.FocusSearch();
                    Check(search.BorderStyle == BorderStyle.None && search.Left > 36 * scale &&
                        search.Right < capsule.Width - 36 * scale && capsule.Right == right,
                        "borderless field expands left of the stationary more button");
                    typeof(SearchSurface).GetMethod("CollapseSearch", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(capsule, null);
                    Check(!search.Visible && search.Text == "example", "collapse hides editor and retains the active search");
                    capsule.FocusSearch();
                    Check(search.Text == "example", "reopening can edit the retained search");
                    CpuTargetTile[] cards = controls.OfType<CpuTargetTile>().ToArray();
                    Check(cards.Length == 2, "two CPU cards");
                    Check(cards[0].Height >= 96 && cards[0].Height == cards[1].Height &&
                        cards[0].Top == cards[1].Top && Math.Abs(cards[0].Width - cards[1].Width) <= 1,
                        "the two full-height cards share the same row: " + cards[0].Bounds + " / " + cards[1].Bounds);
                    grid.Handle.ToInt64();
                    rail.Handle.ToInt64();
                    for (int i = 0; i < 50; i++)
                        grid.Controls.Add(new AppIconTile { Width = (int)Math.Round(AppIconPanel.TileWidth * scale), Height = (int)Math.Round(AppIconPanel.TileHeight * scale), ShowCpuAssignment = true });
                    grid.PerformLayout();
                    Func<AppIconTile[]> firstRow = () => grid.Controls.OfType<AppIconTile>().Where(tile => tile.Top == grid.Controls[0].Top).ToArray();
                    Action checkContentEdges = delegate
                    {
                        AppIconTile[] row = firstRow();
                        Rectangle panel = formBounds(mainPanel), leftCard = formBounds(cards[0]), rightCard = formBounds(cards[1]);
                        int iconInset = (int)Math.Round((row[0].Width - UiTheme.AppBaseSize * scale) / 2);
                        Check(Math.Abs(leftCard.Left - formBounds(row[0]).Left - iconInset) <= 1 &&
                            Math.Abs(rightCard.Right - formBounds(row.Last()).Right + iconInset) <= 1, "CPU cards align to the outer app bases");
                        int leftGap = leftCard.Left - panel.Left, rightGap = panel.Right - rightCard.Right;
                        Check(leftGap <= Math.Round(30 * scale) && rightGap <= Math.Round(48 * scale),
                            "white panel side gutters stay compact: " + leftGap + " / " + rightGap);
                        Check(formBounds(row.Last()).Right <= formBounds(rail).Left,
                            "the last tile leaves the entire scroll rail available");
                    };
                    int wideColumns = firstRow().Length;
                    checkContentEdges();
                    checkToolbar();
                    Check(Math.Abs(rail.Width - Math.Round(26 * scale)) <= 1, "scroll rail reserves the wide drag area");
                    grid.ScrollTo(grid.MaximumScroll);
                    Check(grid.ScrollOffset > 0 && grid.Controls[grid.Controls.Count - 1].Bottom <= grid.ClientSize.Height, "scroll to the final row");
                    form.Width -= (int)Math.Round(230 * scale);
                    grid.ScrollTo(0);
                    Check(firstRow().Length < wideColumns, "narrowing the window reflows icon columns");
                    checkContentEdges();
                    checkToolbar();
                    var menu = (ContextMenuStrip)StartupField(form, "moreMenu");
                    Check(menu.Items.Cast<ToolStripItem>().Where(item => !(item is ToolStripSeparator)).Select(item => item.Text).SequenceEqual(
                        new[] { "自动方案", "刷新应用", "恢复原设置…", "CPU 与设备信息", "检查所选应用权限…", "开机自启动", "检查更新…", "卸载…" }), "menu order, startup, updates and uninstall entries");
                    Check(!menu.Items[2].Enabled, "empty recovery is disabled");
                    Check(!menu.Items[4].Enabled, "diagnostics requires a selected application");
                    service.Store.Add(new OriginalAffinity { Pid = 54321, Created = 1, Name = "fixture.exe", MaskHex = "1", OwnerSid = "fixture" });
                    StartupInvoke(form, "UpdateState");
                    Check(menu.Items[2].Enabled && service.Store.Items.Length == 1, "pending record enables the menu without writing");
                }
            });
        }
    }
}
