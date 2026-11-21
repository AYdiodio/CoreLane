using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal sealed partial class MainForm
    {
        private readonly List<KeyValuePair<CpuSelectionGroup, CpuTargetTile>> targetButtons = new List<KeyValuePair<CpuSelectionGroup, CpuTargetTile>>();
        private readonly ContextMenuStrip moreMenu = new CapsuleContextMenu();
        private readonly ContextMenuStrip automationMenu = new CapsuleContextMenu();
        private SearchSurface searchBox;
        private ToolStripItem restoreSettingsItem;
        private ToolStripItem permissionCheckItem;
        private readonly Label recentOperation = new Label();
        private readonly Icon applicationIcon = new Icon(typeof(MainForm), "BlueViolet.ico");
        private readonly IntelHybridInfo hybrid;

        protected override CreateParams CreateParams
        {
            get
            {
                const int redrawOnResize = 0x0001 | 0x0002; // CS_VREDRAW | CS_HREDRAW
                CreateParams parameters = base.CreateParams;
                // Repaint the whole client area when either dimension changes.
                parameters.ClassStyle |= redrawOnResize;
                return parameters;
            }
        }

        internal MainForm(AffinityService service)
        {
            Icon = applicationIcon;
            DoubleBuffered = ResizeRedraw = true;
            SuspendLayout();
            this.service = service; cpuGroups = CpuSelectionGroup.FromTopology(service.Topology);
            retainedAssignments = new RetainedAssignments(service.Store.DirectoryPath, service.Topology);
            hybrid = IntelHybridInfo.Detect(service.Topology);
            Text = Program.DisplayName + " · v0.13.13"; Font = new Font("Microsoft YaHei", 9.75F);
            ForeColor = UiTheme.Ink; BackColor = UiTheme.Page; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1080, 680); MinimumSize = new Size(680, 470);
            TableLayoutPanel root = CreateLayout(1, 3, UiTheme.Page);
            root.Padding = new Padding(22, 8, 22, 14);
            Rows(root, 44, -1, 25);
            Controls.Add(root);

            Panel toolbar = new RepaintPanel { Dock = DockStyle.Fill, BackColor = UiTheme.Page, Margin = Padding.Empty };
            CapsuleLabel hardware = new CapsuleLabel { Text = service.Topology.CpuName, Font = new Font(Font.FontFamily, 8.5F),
                ForeColor = UiTheme.Muted, BackColor = Color.White, AccessibleName = "CPU 型号" };
            tips.SetToolTip(hardware, service.Topology.CpuName + "\r\nv0.13.13 · " + (Native.IsAdministrator ? "管理员权限" : "普通用户权限"));
            searchBox = new SearchSurface(search) { Width = 72, Margin = Padding.Empty };
            search.TextChanged += delegate { applications.ScrollTo(0); RenderProcesses(); };
            searchBox.Click += delegate { ShowToolbarMenu(moreMenu); };
            moreMenu.Closed += delegate { searchBox.MenuOpen = false; };
            automationMenu.Closed += delegate { searchBox.MenuOpen = false; };
            toolbar.Controls.AddRange(new Control[] { hardware, searchBox });
            toolbar.Layout += delegate
            {
                int height = toolbar.ClientSize.Height;
                // The toolbar and the white panel occupy the same column with no margins.
                searchBox.Bounds = new Rectangle(toolbar.ClientSize.Width - searchBox.Width, (height - Px(36)) / 2, searchBox.Width, Px(36));
                int captionWidth = TextRenderer.MeasureText(hardware.Text, hardware.Font, Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + Px(24);
                hardware.Bounds = new Rectangle(0, (height - Px(28)) / 2,
                    Math.Max(1, Math.Min(captionWidth, searchBox.Left - Px(12))), Px(28));
            };
            searchBox.SizeChanged += delegate { toolbar.PerformLayout(); };
            root.Controls.Add(toolbar, 0, 0);
            bool scrollTargets = cpuGroups.Count > 3;
            CardPanel listCard = new CardPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(14, 8, 6, 16) };
            TableLayoutPanel list = CreateLayout(1, 2, Color.White);
            Rows(list, -1, scrollTargets ? 122 : 100);
            listCard.Controls.Add(list);
            SetupApplications();
            Panel viewport = new RepaintPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.White };
            AppScrollRail rail = new AppScrollRail(applications) { Dock = DockStyle.Right, Width = 26 };
            viewport.Controls.Add(applications);
            viewport.Controls.Add(rail);
            list.Controls.Add(viewport, 0, 0);
            Panel targets;
            CapsuleScrollPanel targetScroll = null;
            if (scrollTargets)
            {
                targetScroll = new CapsuleScrollPanel { Dock = DockStyle.Fill, BackColor = Color.White };
                FlowLayoutPanel flowTargets = new FlowLayoutPanel { BackColor = Color.White, WrapContents = true, Margin = Padding.Empty };
                targets = flowTargets; targetScroll.Content.Controls.Add(targets);
                Action arrangeTargets = delegate
                {
                    targets.Width = targetScroll.ContentWidth;
                    targets.Height = flowTargets.GetPreferredSize(new Size(targets.Width, 0)).Height;
                    targetScroll.SetContentHeight(targets.Height);
                };
                targetScroll.ViewportChanged += delegate { arrangeTargets(); };
                targets.Layout += delegate { arrangeTargets(); };
            }
            else
            {
                var columns = CreateLayout(Math.Max(1, cpuGroups.Count), 1, Color.White);
                Rows(columns, -1);
                for (int i = 0; i < cpuGroups.Count; i++)
                    columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / cpuGroups.Count));
                targets = columns;
            }
            applications.ViewChanged += delegate
            {
                rail.Invalidate();
                int inset = applications.GridInset;
                targets.Padding = new Padding(inset, Px(4), inset + (scrollTargets ? 0 : rail.Width), 0);
                recentOperation.Margin = new Padding(listCard.Padding.Left + inset, 0, listCard.Padding.Right + rail.Width + inset, 0);
            };
            targets.AccessibleName = "CPU 分组";
            foreach (CpuSelectionGroup group in cpuGroups)
            {
                int index = targetButtons.Count;
                CpuTargetTile tile = new CpuTargetTile { Group = group, Text = group.Name, Width = 240, Height = 96,
                    Dock = scrollTargets ? DockStyle.None : DockStyle.Top,
                    Margin = new Padding(!scrollTargets && index > 0 ? 6 : 0, 0,
                        index == cpuGroups.Count - 1 ? 0 : scrollTargets ? 12 : 6, 0), Font = Font,
                    AccessibleName = group.Name, AccessibleDescription = "CPU " + Masks.Format(group.Mask) };
                tile.Click += delegate { OpenCpuGroup(group); };
                targetButtons.Add(new KeyValuePair<CpuSelectionGroup, CpuTargetTile>(group, tile)); targets.Controls.Add(tile);
                tips.SetToolTip(tile, "CPU " + Masks.Format(group.Mask) + " · Windows G" + group.WindowsGroup + (group.ClusterLabel == null ? "" : "\r\n" + group.ClusterLabel + " 按 L3 共享范围标注，编号为本工具的显示顺序。"));
            }
            list.Controls.Add(targetScroll == null ? targets : targetScroll, 0, 1);
            root.Controls.Add(listCard, 0, 1);

            recentOperation.Dock = DockStyle.Fill; recentOperation.AutoEllipsis = true; recentOperation.TextAlign = ContentAlignment.MiddleLeft; recentOperation.ForeColor = UiTheme.Muted; recentOperation.Font = new Font(Font.FontFamily, 8.5F);
            recentOperation.AccessibleName = "操作结果";
            root.Controls.Add(recentOperation, 0, 2);
            BuildMoreMenu();
            FormClosing += OnClosing;
            Shown += delegate
            {
                InitializeTray();
                InitializePriorities();
                try { cpuSetService = new CpuSetService(service.Topology, service.Store.DirectoryPath); }
                catch (Exception error) { Append("无法读取 CPU 集恢复记录：" + Native.Explain(error), Color.Firebrick); }
                Rectangle area = Screen.FromControl(this).WorkingArea;
                if (Width > area.Width || Height > area.Height)
                {
                    MinimumSize = new Size(Math.Min(MinimumSize.Width, area.Width), Math.Min(MinimumSize.Height, area.Height));
                    Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height)); Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
                }
                RefreshProcesses();
                InitializeAutomation();
            };
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi; ResumeLayout(true);
            applications.PerformLayout();
        }

        private void ShowToolbarMenu(ContextMenuStrip menu)
        {
            SizeMenu((CapsuleContextMenu)menu, 44, 224);
            searchBox.MenuOpen = true;
            menu.Show(searchBox, new Point(searchBox.Width, searchBox.Height + Px(6)), ToolStripDropDownDirection.BelowLeft);
            searchBox.MenuOpen = menu.Visible;
        }

        private void SizeMenu(CapsuleContextMenu menu, int rowHeight, int minimumWidth)
        {
            menu.Handle.ToInt64();
            menu.Font = Font; menu.Padding = new Padding(Px(8));
            menu.ImageScalingSize = new Size(Px(18), Px(18));
            int contentWidth = Px(minimumWidth);
            foreach (ToolStripItem item in menu.Items)
                if (!(item is ToolStripSeparator)) contentWidth = Math.Max(contentWidth,
                    TextRenderer.MeasureText(item.Text, menu.Font, Size.Empty, TextFormatFlags.NoPadding).Width + Px(82));
            foreach (ToolStripItem item in menu.Items)
            {
                item.AutoSize = false; item.Margin = Padding.Empty; item.Padding = Padding.Empty;
                item.Size = new Size(contentWidth, Px(item is ToolStripSeparator ? 12 : rowHeight));
            }
            menu.MinimumSize = new Size(contentWidth + menu.Padding.Horizontal, 0);
            menu.ConfigureRows(new Size(contentWidth + Px(16), menu.Items.Cast<ToolStripItem>().Sum(item => item.Height) + Px(16)));
        }

        private void BuildMoreMenu()
        {
            ToolStripItem schemes = new GlyphMenuItem("自动方案", UiGlyph.Sparkles, delegate { BeginInvoke((Action)ShowAutomationMenu); }, true);
            moreMenu.Items.Add(schemes);
            moreMenu.Items.Add(new GlyphMenuItem("刷新应用", UiGlyph.Refresh, delegate { QueueProcessRefresh(true); }));
            restoreSettingsItem = new GlyphMenuItem("恢复原设置…", UiGlyph.Undo, delegate { OpenRestoreSettings(); });
            moreMenu.Items.Add(restoreSettingsItem);

            moreMenu.Items.Add(new GlyphMenuItem("CPU 与设备信息", UiGlyph.Cpu, delegate { OpenDeviceInfo(); }));
            permissionCheckItem = new GlyphMenuItem("检查所选应用权限…", UiGlyph.Search, delegate { OpenPermissionCheck(); });
            moreMenu.Items.Add(permissionCheckItem);
            var startup = new GlyphMenuItem("开机自启动", UiGlyph.Play, delegate { ToggleStartup(); });
            moreMenu.Items.Add(startup);
            moreMenu.Items.Add(new CapsuleMenuSeparator());
            var uninstall = new GlyphMenuItem("卸载…", UiGlyph.Uninstall, delegate { OpenUninstall(); });
            moreMenu.Items.Add(uninstall);
            moreMenu.Opening += delegate
            {
                CloseProcessDetails();
                schemes.Enabled = !busy && schemeStore != null;
                restoreSettingsItem.Enabled = !busy && CanRestoreScheduling;
                uninstall.Enabled = !busy;
                try
                {
                    StartupMode mode = AppStartup.Mode();
                    startup.Checked = mode == StartupMode.Elevated;
                    startup.Text = mode == StartupMode.Elevated ? "开机自启动 · 管理员" :
                        mode == StartupMode.Legacy ? "开机自启动 · 升级为管理员" : "开机自启动 · 已关闭";
                    startup.Enabled = !busy;
                    startup.ToolTipText = startup.Checked ? "点击关闭登录自启动" : "点击授权。登录或自动登录 Windows 后以管理员权限启动";
                }
                catch (Exception error)
                {
                    startup.Text = "开机自启动 · 无法读取";
                    startup.Checked = false;
                    startup.Enabled = false;
                    startup.ToolTipText = Native.Explain(error);
                }
            };
            UpdateState();
        }

        private void ToggleStartup()
        {
            if (busy) return;
            try
            {
                bool enabled = AppStartup.Mode() != StartupMode.Elevated;
                AppStartup.SetEnabled(enabled, Application.ExecutablePath);
                Append(enabled ? "开机自启动已开启。登录 Windows 后以管理员权限打开工具。" : "开机自启动已关闭。", UiTheme.Blue);
            }
            catch (Exception error) { Append("无法修改开机自启动 · " + Native.Explain(error), Color.Firebrick); }
        }
    }
}
