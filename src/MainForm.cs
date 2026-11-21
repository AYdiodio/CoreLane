using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal sealed partial class MainForm : Form
    {
        private readonly AffinityService service;
        private readonly RetainedAssignments retainedAssignments;
        private CpuSetService cpuSetService;
        private OriginalCpuSet[] CpuSetRecords { get { return cpuSetService == null ? new OriginalCpuSet[0] : cpuSetService.Records; } }
        private readonly List<CpuSelectionGroup> cpuGroups;
        private readonly AppIconPanel applications = new AppIconPanel();
        private readonly TextBox search = new SearchTextBox();
        private readonly Dictionary<string, OperationResult> results = new Dictionary<string, OperationResult>();
        private readonly ProcessVisualCache visuals = new ProcessVisualCache();
        private readonly ToolTip tips = new ToolTip();
        private readonly ProcessSelection processSelection = new ProcessSelection();
        private readonly Timer hoverTimer = new Timer { Interval = 150 };
        private AppIconTile pendingHover;
        private AppInfoPopup detailsPopup;
        private List<ProcessRow> processes = new List<ProcessRow>();
        private ulong selectedMask;
        private bool busy;
        private bool uninstalling;
        private bool rendering;
        private bool settingsDialogOpen;
        private bool unifiedIcons = true;
        private int unavailable;
        private int refreshGeneration;
        private bool refreshInProgress, refreshRequested;

        private static TableLayoutPanel CreateLayout(int columns, int rows, Color color) { return new RepaintTable { Dock = DockStyle.Fill, ColumnCount = columns, RowCount = rows, Margin = new Padding(0), BackColor = color }; }
        private static void Rows(TableLayoutPanel panel, params int[] heights) { foreach (int height in heights) panel.RowStyles.Add(new RowStyle(height < 0 ? SizeType.Percent : SizeType.Absolute, height < 0 ? 100 : height)); }
        private int Px(float value) { return (int)Math.Round(value * UiTheme.Scale(this)); }
        private Label Label(string text, float size, Color color, bool bold) { return new Label { Text = text, Dock = DockStyle.Fill, AutoEllipsis = true, Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color, Margin = new Padding(0), BackColor = Color.Transparent }; }
        private FlowLayoutPanel Flow() { return new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false, BackColor = Color.White }; }
        private void OpenDeviceInfo()
        {
            if (busy || Interacting) return;
            CloseProcessDetails();
            settingsDialogOpen = true;
            try { using (CpuMaskDialog dialog = new CpuMaskDialog(service.Topology, 0, true)) dialog.ShowDialog(this); }
            finally { settingsDialogOpen = false; FlushDeferredRender(); }
        }
        private void OpenPermissionCheck()
        {
            if (busy || Interacting) return;
            ProcessRow[] targets = SelectedProcesses().ToArray();
            if (targets.Length == 0) return;
            CloseProcessDetails();
            settingsDialogOpen = true;
            try
            {
                string details = string.Join("\r\n\r\n", targets.Select(row => service.InspectPermissions(row).ToString()));
                AppMessageBox.Show(this, details, "亲和性权限检查");
            }
            finally { settingsDialogOpen = false; FlushDeferredRender(); }
        }
        private void OpenCpuGroup(CpuSelectionGroup group)
        {
            if (busy || Interacting) return;
            ProcessRow[] targets = SelectedProcesses().ToArray();
            string description = "CPU " + Masks.Format(group.Mask);
            CloseProcessDetails();
            if (targets.Length == 0 || !service.Topology.CanAssign) { Append(group.Name + " · " + description, UiTheme.Muted); return; }
            int appCount = SelectedApplications().Count;
            settingsDialogOpen = true;
            try
            {
                using (var dialog = new AppMessageDialog(
                    "将 " + appCount + " 个已选应用的 " + targets.Length + " 个进程分配到 " + description + "？", group.Name,
                    MessageBoxButtons.OKCancel, "分配到此组"))
                    if (dialog.ShowDialog(this) == DialogResult.OK) ApplyTargets(targets, group.Mask, group.Name);
            }
            finally { settingsDialogOpen = false; FlushDeferredRender(); }
        }
        private void OpenApplicationCpu(ApplicationIconGroup group)
        {
            if (busy || settingsDialogOpen || !service.Topology.CanAssign || group.Members.Count == 0) return;
            ProcessRow[] targets = new AppDragPayload(group.Members).Targets;
            ulong initial = targets.All(p => p.ReadError == null && p.Mask == targets[0].Mask) ? targets[0].Mask : 0;
            initial &= service.Topology.Available;
            string title = group.Title + " · " + targets.Length + " 个进程";
            settingsDialogOpen = true; CloseProcessDetails(); pressedTile = null; selectedOnPress = false;
            try
            {
                using (CpuMaskDialog dialog = new CpuMaskDialog(service.Topology, initial, false, title, group.Visual, unifiedIcons,
                    string.Join("、", group.Applications.Select(a => a.Title)), targets, priorityService != null, cpuSetService != null))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return;
                    Dictionary<string, uint> priorities = dialog.PriorityChanges;
                    string targetName = title + " → " + CpuSelectionGroup.Describe(dialog.SelectedMask, cpuGroups);
                    ApplyCpuTargets(targets, dialog.SelectedMask, targetName, dialog.UsesCpuSets);
                    if (priorities.Count > 0 && priorityService != null)
                        RunBatch(delegate
                        {
                            foreach (ProcessRow row in targets)
                            {
                                uint priority;
                                if (!priorities.TryGetValue(row.Key, out priority)) continue;
                                OperationResult result = priorityService.Apply(row, priority);
                                if (result.Success && !result.Skipped) retainedAssignments.RememberPriority(row, priority);
                                OperationResult affinityResult;
                                if (results.TryGetValue(row.Key, out affinityResult) && !affinityResult.Success && !affinityResult.Skipped)
                                {
                                    result = new OperationResult { Pid = row.Pid, Name = row.Name, Success = false,
                                        Win32Error = affinityResult.Win32Error, Message = affinityResult.Message + "；优先级操作：" + result.Message };
                                }
                                Record(result, row.Key);
                            }
                        });
                }
            }
            finally { settingsDialogOpen = false; FlushDeferredRender(); UpdateState(); }
        }
        private void OpenRestoreSettings()
        {
            if (busy || settingsDialogOpen || !CanRestoreScheduling) return;
            HashSet<string> keys = new HashSet<string>(SelectedProcesses().Select(p => p.Key));
            SchedulingRestoreEntry[] entries = RecoveryEntries();
            settingsDialogOpen = true; CloseProcessDetails();
            try
            {
                int selectedCount = entries.Select(entry => entry.Key).Distinct().Count(keys.Contains);
                using (var dialog = new RestoreSettingsDialog(entries, selectedCount, retainedAssignments.HasRules))
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        RestoreRecords(entries, dialog.RestoreAll ? null : keys);
            }
            finally { settingsDialogOpen = false; FlushDeferredRender(); }
        }
        private void SetupApplications()
        {
            applications.Dock = DockStyle.Fill; applications.BackColor = Color.White;
            applications.Padding = new Padding(0, 8, 0, 12);
            applications.Margin = new Padding(0);
            applications.AccessibleName = "运行中的应用图标";
            tips.InitialDelay = 150; tips.ReshowDelay = 50; tips.AutoPopDelay = 20000;
            hoverTimer.Tick += delegate
            {
                hoverTimer.Stop(); AppIconTile tile = pendingHover; pendingHover = null;
                if (tile != null && !tile.IsDisposed && tile.RectangleToScreen(tile.ClientRectangle).Contains(Cursor.Position) &&
                    ContainsFocus && !Interacting) ShowProcessDetails(tile);
            };
            SetupInteractions();
        }
        private List<ApplicationGroup> VisibleApplications() { return applications.Controls.OfType<AppIconTile>().SelectMany(tile => tile.Group.Applications).ToList(); }
        private List<ApplicationGroup> SelectedApplications() { return VisibleApplications().Where(app => app.Members.Any(processSelection.Contains)).ToList(); }
        private List<ProcessRow> SelectedProcesses() { return processSelection.Targets(ApplicationGroup.Targets(VisibleApplications())); }
        private void SetVisibleProcessSelection(bool selected)
        {
            if (busy || rendering) return;
            rendering = true;
            try { processSelection.Set(ApplicationGroup.Targets(VisibleApplications()), selected); SyncIconSelection(); }
            finally { rendering = false; UpdateState(); }
        }
        private void SyncIconSelection()
        {
            foreach (AppIconTile tile in applications.Controls.OfType<AppIconTile>())
            {
                tile.CheckState = processSelection.State(tile.Group.Members);
                tile.AccessibleDescription = tile.Group.Title + "，" + tile.Group.Members.Count + " 个进程；" +
                    (tile.ShowCpuAssignment && tile.AssignmentLabel != null ? "分配标记：" + tile.AssignmentLabel.Text + "；" : "") +
                    "优先级标记：" + PriorityLevels.Indicator(tile.Group.Members) + "；" +
                    (tile.CheckState == CheckState.Checked ? "全部已选" : tile.CheckState == CheckState.Indeterminate ? "部分已选" : "未选择") + "。Ctrl 多选，Shift 连选，Enter 选择核心。";
            }
        }
        private void CloseProcessDetails()
        {
            hoverTimer.Stop(); pendingHover = null;
            if (detailsPopup != null) { AppInfoPopup old = detailsPopup; detailsPopup = null; old.Close(); old.Dispose(); }
        }
        private void ShowProcessDetails(AppIconTile tile)
        {
            if (busy || tile.IsDisposed || Interacting) return;
            if (detailsPopup != null && !detailsPopup.IsDisposed && detailsPopup.AnchorTile == tile && detailsPopup.Visible) return;
            CloseProcessDetails();
            ApplicationIconGroup group = tile.Group;
            string information = DescribeApplications(group.Applications.Take(2));
            if (group.Applications.Count > 2) information += "\r\n另有 " + (group.Applications.Count - 2) + " 个应用。";
            information += "\r\n" + string.Join("\r\n", group.Members.Select(p => p.ExecutablePath ?? p.Name).Distinct().Take(2));
            AppInfoPopup popup = new AppInfoPopup(tile, group.Title, information);
            detailsPopup = popup;
            Rectangle area = Rectangle.Intersect(Screen.FromControl(tile).WorkingArea, RectangleToScreen(ClientRectangle));
            Point point = tile.PointToScreen(new Point(tile.Width / 2, tile.Height - Px(5)));
            point.X = Math.Max(area.Left, Math.Min(point.X, area.Right - popup.Width));
            if (point.Y + popup.Height > area.Bottom) point.Y = Math.Max(area.Top, tile.PointToScreen(Point.Empty).Y - popup.Height + Px(5));
            popup.Location = point; popup.Show(this);
        }
        private void RefreshProcesses()
        {
            refreshGeneration++;
            try
            {
                processes = service.Processes(out unavailable);
                RenderProcesses();
                visuals.Prime(processes, VisualsLoaded);
            }
            catch (Exception error) { Append("刷新失败：" + Native.Explain(error), Color.Firebrick); }
        }
        private void QueueProcessRefresh(bool report)
        {
            refreshRequested |= report;
            if (refreshInProgress || !IsHandleCreated || IsDisposed) return;
            refreshInProgress = true;
            int generation = refreshGeneration;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<ProcessRow> snapshot = null;
                int unreadable = 0;
                Exception failure = null;
                try { snapshot = service.Processes(out unreadable); }
                catch (Exception error) { failure = error; }
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        refreshInProgress = false;
                        if (IsDisposed) return;
                        if (generation != refreshGeneration)
                        {
                            if (refreshRequested) QueueProcessRefresh(true);
                            return;
                        }
                        bool reportResult = refreshRequested;
                        refreshRequested = false;
                        if (failure != null) { Append("刷新失败：" + Native.Explain(failure), Color.Firebrick); return; }
                        processes = snapshot;
                        unavailable = unreadable;
                        RenderProcesses();
                        visuals.Prime(processes, VisualsLoaded);
                        if (reportResult) Append("已刷新应用" + (unavailable == 0 ? "" : " · " + unavailable + " 个进程无法读取"), UiTheme.Muted);
                    });
                }
                catch (InvalidOperationException) { /* The window closed while this read was in flight. */ }
            });
        }
        private void VisualsLoaded()
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke((Action)delegate { if (!IsDisposed && !busy) RenderProcesses(); }); }
            catch (InvalidOperationException) { }
        }
        private void RenderProcesses()
        {
            if (hiddenInTray) return;
            if (Interacting) { renderDeferred = true; return; }
            if (rendering) return;
            rendering = true;
            try
            {
                AppIconTile[] previous = applications.Controls.OfType<AppIconTile>().ToArray();
                string[] tokens = search.Text.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                List<ApplicationGroup> apps = ApplicationGroup.Build(processes, visuals.Get);
                SchemeData data = schemeStore == null ? null : schemeStore.Read();
                GameClassification[] classifications = data == null ? new GameClassification[0] : data.Games.ToArray();
                List<ApplicationIconGroup> visible = ApplicationIconGroup.Build(ApplicationOrdering.Sort(apps, classifications), tokens, visuals.Get);
                processSelection.Retain(visible.SelectMany(group => group.Applications).SelectMany(app => app.Members));
                bool reuse = previous.Length == visible.Count && previous.Select(tile => tile.Group.ImageKey + ":" + tile.Group.InstanceKey).SequenceEqual(visible.Select(group => group.ImageKey + ":" + group.InstanceKey));
                applications.SuspendLayout();
                try
                {
                    if (!reuse)
                    {
                        CloseProcessDetails();
                        applications.Controls.Clear();
                        foreach (AppIconTile tile in previous) { tips.SetToolTip(tile, null); tile.Dispose(); }
                    }
                    for (int i = 0; i < visible.Count; i++)
                    {
                        ApplicationIconGroup group = visible[i];
                        AppIconTile tile = reuse ? previous[i] : new AppIconTile { Margin = Padding.Empty, Font = Font };
                        tile.Group = group; tile.AccessibleName = group.Title + "，包含 " + group.Members.Count + " 个进程";
                        tile.UnifiedIcons = unifiedIcons;
                        tile.ShowCpuAssignment = true;
                        tile.AssignmentLabel = CpuAssignmentLabel.ForTile(group.Members, service.Topology, cpuGroups);
                        tile.Size = new Size(Px(AppIconPanel.TileWidth), Px(AppIconPanel.TileHeight));
                        tile.CheckState = processSelection.State(group.Members);
                        if (!reuse)
                        {
                            BindApplicationInteraction(tile);
                            applications.Controls.Add(tile);
                        }
                        tile.Invalidate();
                    }
                }
                finally { applications.ResumeLayout(true); }
                SyncIconSelection();
                applications.EmptyText = "没有匹配的运行中应用。"; applications.Invalidate();
            }
            finally { rendering = false; UpdateState(); }
        }
        private void UpdateState()
        {
            if (restoreSettingsItem != null) restoreSettingsItem.Enabled = !busy && CanRestoreScheduling;
            if (permissionCheckItem != null) permissionCheckItem.Enabled = !busy &&
                applications.Controls.OfType<AppIconTile>().Any(tile => tile.CheckState != CheckState.Unchecked);
            foreach (AppIconTile tile in applications.Controls.OfType<AppIconTile>()) tile.Enabled = !busy;
            foreach (var pair in targetButtons) pair.Value.Enabled = !busy;
        }
        private void Record(OperationResult result, string key) { results[key] = result; Append(result.ToString(), result.Skipped ? UiTheme.Muted : result.Success ? Color.FromArgb(38, 158, 111) : Color.Firebrick); }
        private bool Restore(bool all)
        {
            HashSet<string> selected = all ? null : new HashSet<string>(SelectedProcesses().Select(p => p.Key));
            return RestoreRecords(RecoveryEntries(), selected);
        }
        private bool CanRestoreScheduling { get { return RecoveryCount > 0 || retainedAssignments.HasRules; } }
        private bool RecoveryStoresAvailable
        {
            get
            {
                return (cpuSetService != null || !File.Exists(Path.Combine(service.Store.DirectoryPath, "original-cpu-sets.json"))) &&
                    (priorityService != null || !File.Exists(Path.Combine(service.Store.DirectoryPath, "original-priority.json")));
            }
        }

        private SchedulingRestoreEntry[] RecoveryEntries()
        {
            return service.Store.Items.Select(row => new SchedulingRestoreEntry(row.Pid, row.Created, row.Name, row.OwnerSid, () => service.Store.Remove(row)))
                .Concat(CpuSetRecords.Select(row => new SchedulingRestoreEntry(row.Pid, row.Created, row.Name, row.OwnerSid, () => cpuSetService.Remove(row))))
                .Concat(PriorityRecords.Select(row => new SchedulingRestoreEntry(row.Pid, row.Created, row.Name, row.OwnerSid, () => priorityService.Remove(row))))
                .ToArray();
        }

        private bool RestoreRecords(SchedulingRestoreEntry[] entries, HashSet<string> selected, bool preserveRetained = false)
        {
            if (!RecoveryStoresAvailable)
            {
                Append("无法读取部分恢复记录，恢复未开始。", Color.Firebrick);
                return false;
            }
            if (selected == null)
            {
                try
                {
                    if (preserveRetained) retainedAssignments.Pause();
                    else retainedAssignments.Clear();
                }
                catch (Exception error) { Append("无法清除保留分配，恢复未开始 · " + Native.Explain(error), Color.Firebrick); return false; }
                sessionManualRules.Clear();
            }
            var targets = entries.Where(entry => selected == null || selected.Contains(entry.Key)).GroupBy(entry => entry.Key).ToArray();
            StopAutomationExcept("恢复调度已暂停自动方案。", new HashSet<string>(targets.Select(group => group.Key)));
            lastManualRules.Clear();
            bool complete = true;
            RunBatch(delegate
            {
                OperationResult failure = null;
                int restored = 0;
                foreach (var group in targets)
                {
                    SchedulingRestoreEntry[] receipts = group.ToArray();
                    OperationResult result = WindowsScheduling.Reset(service.Topology, receipts);
                    if (result.Success || result.Skipped)
                    {
                        try
                        {
                            if (selected != null) ForgetRestoredRule(group.Key);
                            foreach (SchedulingRestoreEntry receipt in receipts) receipt.Remove();
                        }
                        catch (Exception error)
                        { result.Success = result.Skipped = false; result.Message = "无法清理恢复记录 · " + Native.Explain(error); }
                    }
                    if (result.Success) restored++;
                    Record(result, group.Key);
                    if (!result.Success && !result.Skipped && failure == null) failure = result;
                }
                complete = failure == null;
                if (failure != null) Append("部分记录未能恢复 · " + failure, Color.Firebrick);
                else Append("已将 " + restored + " 个进程交还 Windows 调度。", UiTheme.Blue);
            });
            return complete;
        }
        private void ForgetRestoredRule(string key)
        {
            ProcessRow row = processes.Find(process => process.Key == key);
            if (row == null || row.ExecutablePath == null) return;
            ManualSchemeRules.RemovePaths(sessionManualRules, new[] { row.ExecutablePath });
            retainedAssignments.Forget(row.ExecutablePath);
        }
        private void RunBatch(Action action) { if (busy) return; CloseProcessDetails(); busy = true; UseWaitCursor = true; UpdateState(); try { action(); } finally { busy = false; UseWaitCursor = false; RefreshProcesses(); UpdateState(); } }
        private void Append(string text, Color color) { recentOperation.Text = text; recentOperation.ForeColor = color; tips.SetToolTip(recentOperation, text); }
        private void OpenUninstall()
        {
            if (busy || Interacting) return;
            CloseProcessDetails();
            using (var dialog = new UninstallDialog())
            {
                DialogResult choice = dialog.ShowDialog(this);
                if (choice != DialogResult.Yes && choice != DialogResult.No) return;
                bool keepSettings = choice == DialogResult.No;
                try
                {
                    if (!Native.IsAdministrator && AppUninstall.NeedsAdministrator())
                        throw new UnauthorizedAccessException("此安装包含管理员自启动。请以管理员身份打开 " + Program.DisplayName + "，再卸载。");
                    if (!RecoveryStoresAvailable)
                        throw new InvalidOperationException("无法读取部分恢复记录，请修复后再卸载。");
                    string executable = AppStartup.OriginExecutable(Application.ExecutablePath);
                    var plan = new AppUninstall(executable, AppStartup.ProtectedDirectory, keepSettings);
                    plan.FindShortcuts();
                    if (keepSettings) retainedAssignments.Save();
                    if (!RestoreRecords(RecoveryEntries(), null, keepSettings) || RecoveryCount != 0)
                        throw new InvalidOperationException("部分调度设置尚未恢复。请查看主界面操作结果后重试。");
                    AppStartup.RemoveForUninstall();
                    using (var current = System.Diagnostics.Process.GetCurrentProcess())
                    using (var cleanup = plan.StartCleanup(current.Id, current.StartTime.ToFileTimeUtc()))
                        if (cleanup == null) throw new InvalidOperationException("无法启动卸载清理程序。");
                    uninstalling = true;
                    Close();
                }
                catch (Exception error) { AppMessageBox.Show(this, Native.Explain(error), "未能卸载 " + Program.DisplayName); }
            }
        }
        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (uninstalling) return;
            if (e.CloseReason == CloseReason.UserClosing && !exitFromTray && trayIcon != null)
            {
                e.Cancel = true;
                HideInTray();
                return;
            }
            bool canPrompt = e.CloseReason != CloseReason.WindowsShutDown && e.CloseReason != CloseReason.TaskManagerClosing;
            DialogResult choice = DialogResult.No;
            if (canPrompt && CanRestoreScheduling)
            {
                using (var dialog = new ExitSettingsDialog()) choice = dialog.ShowDialog(this);
                if (choice != DialogResult.Yes && choice != DialogResult.No) { e.Cancel = true; return; }
                if (choice == DialogResult.No)
                {
                    try { retainedAssignments.Save(); }
                    catch (Exception error) { e.Cancel = true; Append("未退出 · 无法保存分配 · " + Native.Explain(error), Color.Firebrick); return; }
                }
            }
            if (choice == DialogResult.Yes) e.Cancel = !Restore(true);
            else StopAutomation("退出前已暂停自动方案。");
        }
        protected override void Dispose(bool disposing) { if (disposing) { DisposeTray(); EndApplicationDrag(false, Point.Empty); CloseProcessDetails(); hoverTimer.Dispose(); moreMenu.Dispose(); automationMenu.Dispose(); automationTimer.Dispose(); visuals.Dispose(); tips.Dispose(); } base.Dispose(disposing); if (disposing) applicationIcon.Dispose(); }
    }
}
