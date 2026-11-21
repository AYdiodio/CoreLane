using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal sealed partial class MainForm
    {
        private IconSelectionController iconSelection;
        private AppIconTile pressedTile;
        private AppIconTile pendingCoreEditTile;
        private int pointerGesture;
        private Point pressedPoint;
        private bool selectedOnPress, dragStarted, renderDeferred, marqueeAdditive, sizingWindow;
        private string[] marqueeOriginal = new string[0];
        private AppDragPayload activeDrag;
        private AppDragPreview dragPreview;
        private AppIconTile[] dragTiles = new AppIconTile[0];
        private Cursor sourceCursor;
        private bool dragCursorHidden;

        private List<ApplicationIconGroup> VisibleIcons() { return applications.Controls.OfType<AppIconTile>().Select(t => t.Group).ToList(); }
        private bool Interacting { get { return sizingWindow || settingsDialogOpen || OwnedForms.Any(f => f.Modal) || applications.IsSelecting || activeDrag != null || pressedTile != null && (MouseButtons & MouseButtons.Left) != 0; } }
        private void SetupInteractions()
        {
            iconSelection = new IconSelectionController(processSelection);
            applications.SelectionStarted += delegate
            {
                CloseProcessDetails(); marqueeOriginal = processSelection.Snapshot(); marqueeAdditive = (ModifierKeys & Keys.Control) != 0;
                if (!marqueeAdditive) processSelection.Replace(new string[0]); SyncIconSelection(); UpdateState();
            };
            applications.SelectionMoved += delegate
            {
                AppIconTile[] tiles = applications.Controls.OfType<AppIconTile>().ToArray();
                iconSelection.Box(tiles.Select(t => t.Group).ToList(), tiles.Select(t => Rectangle.Inflate(t.Bounds, -Px(7), -Px(7))).ToArray(),
                    applications.SelectionBounds, marqueeOriginal, marqueeAdditive);
                SyncIconSelection(); UpdateState();
            };
            applications.SelectionEnded += delegate { FlushDeferredRender(); };
            applications.SelectionCancelled += delegate { processSelection.Replace(marqueeOriginal); SyncIconSelection(); UpdateState(); FlushDeferredRender(); };
            applications.ViewScrolled += delegate { CloseProcessDetails(); };
            Deactivate += delegate { CloseProcessDetails(); EndApplicationDrag(false, Point.Empty); };
            ResizeBegin += delegate { sizingWindow = true; CloseProcessDetails(); EndApplicationDrag(false, Point.Empty); };
            ResizeEnd += delegate { sizingWindow = false; Invalidate(true); QueueDeferredRender(); };
            FormClosing += delegate { EndApplicationDrag(false, Point.Empty); };
        }
        private void BindApplicationInteraction(AppIconTile tile)
        {
            tile.MouseEnter += delegate
            { if (!busy && !Interacting) { pendingHover = tile; hoverTimer.Stop(); hoverTimer.Start(); } };
            tile.MouseLeave += delegate
            {
                if (pendingHover == tile || detailsPopup != null && detailsPopup.AnchorTile == tile) CloseProcessDetails();
            };
            tile.MouseDown += delegate(object sender, MouseEventArgs args)
            {
                if (args.Button != MouseButtons.Left || busy || settingsDialogOpen) return;
                pointerGesture++;
                bool additive = (tile.PressModifiers & Keys.Control) != 0, range = (tile.PressModifiers & Keys.Shift) != 0;
                // The second press may still become a drag; decide only when it is released.
                pendingCoreEditTile = !additive && args.Clicks == 2 && activeDrag == null ? tile : null;
                CloseProcessDetails(); pressedTile = tile; pressedPoint = tile.PointToScreen(args.Location); selectedOnPress = false; dragStarted = false;
                if (range || processSelection.State(tile.Group.Members) == CheckState.Unchecked)
                {
                    iconSelection.Click(VisibleIcons(), applications.Controls.GetChildIndex(tile), additive, range);
                    selectedOnPress = true; SyncIconSelection(); UpdateState();
                }
            };
            tile.Click += delegate
            {
                // Mouse selection is handled on release, even when Windows reports a double click.
                if (rendering || busy || dragStarted || settingsDialogOpen || pressedTile == tile) return;
                iconSelection.Click(VisibleIcons(), applications.Controls.GetChildIndex(tile), true, false);
                SyncIconSelection(); UpdateState(); FlushDeferredRender();
            };
            tile.MouseMove += delegate(object sender, MouseEventArgs args)
            {
                if (activeDrag != null && pressedTile == tile) { MoveApplicationDrag(tile.PointToScreen(args.Location)); return; }
                if (args.Button != MouseButtons.Left || pressedTile != tile || dragStarted || busy || !service.Topology.CanAssign) return;
                Size threshold = SystemInformation.DragSize;
                if (new Rectangle(pressedPoint.X - threshold.Width / 2, pressedPoint.Y - threshold.Height / 2, threshold.Width, threshold.Height).Contains(tile.PointToScreen(args.Location))) return;
                StartApplicationDrag(tile);
            };
            tile.DragReleased += delegate(object sender, MouseEventArgs args) { EndApplicationDrag(true, tile.PointToScreen(args.Location)); };
            tile.MouseCaptureChanged += delegate
            {
                if (activeDrag == null || pressedTile != tile || tile.Capture || IsDisposed || !IsHandleCreated) return;
                // MouseUp can release capture before its handlers; let a real release finish first.
                BeginInvoke((Action)delegate { if (activeDrag != null && pressedTile == tile && !tile.Capture) EndApplicationDrag(false, Point.Empty); });
            };
            tile.MouseUp += delegate(object sender, MouseEventArgs args)
            {
                if (args.Button != MouseButtons.Left || pressedTile != tile) return;
                bool click = !busy && !settingsDialogOpen && !dragStarted && activeDrag == null && tile.ClientRectangle.Contains(args.Location);
                bool open = click && pendingCoreEditTile == tile;
                pendingCoreEditTile = null;
                if (open)
                {
                    ApplicationIconGroup group = tile.Group; int gesture = pointerGesture;
                    BeginInvoke((Action)delegate { if (!IsDisposed && gesture == pointerGesture && activeDrag == null) OpenApplicationCpu(group); });
                }
                else if (click && !selectedOnPress)
                {
                    bool additive = (tile.PressModifiers & Keys.Control) != 0, range = (tile.PressModifiers & Keys.Shift) != 0;
                    iconSelection.Click(VisibleIcons(), applications.Controls.GetChildIndex(tile), additive, range);
                    SyncIconSelection(); UpdateState();
                }
                QueueInteractionEnd();
            };
            tile.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode == Keys.Down) { ShowProcessDetails(tile); args.Handled = args.SuppressKeyPress = true; }
                if (args.KeyCode == Keys.Enter && !busy && activeDrag == null)
                { ApplicationIconGroup group = tile.Group; args.Handled = args.SuppressKeyPress = true; BeginInvoke((Action)delegate { if (!IsDisposed) OpenApplicationCpu(group); }); }
            };
        }
        private void QueueInteractionEnd()
        {
            if (IsDisposed || !IsHandleCreated) return;
            int endedGesture = pointerGesture;
            BeginInvoke((Action)delegate { if (IsDisposed || endedGesture != pointerGesture) return; if (activeDrag == null) { pressedTile = null; dragStarted = false; selectedOnPress = false; FlushDeferredRender(); } });
        }
        private void FlushDeferredRender() { if (renderDeferred && !Interacting && !busy) { renderDeferred = false; RenderProcesses(); } }
        private void QueueDeferredRender()
        {
            if (renderDeferred && IsHandleCreated && !IsDisposed) BeginInvoke((Action)FlushDeferredRender);
        }
        private void StartApplicationDrag(AppIconTile tile)
        {
            List<ProcessRow> selected = SelectedProcesses(); if (selected.Count == 0) return;
            CloseProcessDetails(); pendingCoreEditTile = null; dragStarted = true; activeDrag = new AppDragPayload(selected);
            dragTiles = applications.Controls.OfType<AppIconTile>().Where(t => t.Group.Members.Any(processSelection.Contains)).ToArray();
            sourceCursor = tile.Cursor;
            try
            {
                DragImagePart[] pictures = new[] { tile }.Concat(dragTiles.Where(t => t != tile)).Take(12).Select(t => t.DragImagePart).ToArray();
                foreach (AppIconTile source in dragTiles) { source.Dragging = true; source.Invalidate(); }
                try
                {
                    dragPreview = new AppDragPreview(pictures, pressedPoint, SelectedApplications().Count, dragTiles.Length);
                    dragPreview.ShowPreview(this);
                }
                catch (Exception error)
                {
                    if (dragPreview != null) { dragPreview.Dispose(); dragPreview = null; }
                    Append("拖影暂不可用，仍可继续拖放：" + Native.Explain(error), UiTheme.Muted);
                }
                // This drag never leaves the app's model: do not enter the Windows OLE
                // drag loop, which can reintroduce a forbidden cursor over blank areas.
                tile.Cursor = Cursors.Arrow; Cursor.Current = Cursors.Arrow; tile.Capture = true;
                // Hide exactly once, only when a visible drag image replaces the pointer.
                if (tile.Capture && dragPreview != null) { Cursor.Hide(); dragCursorHidden = true; }
                MoveApplicationDrag(Cursor.Position);
            }
            catch
            {
                EndApplicationDrag(false, Point.Empty); throw;
            }
        }
        private CpuTargetTile DragTargetAt(Point point)
        {
            if (activeDrag == null || !ClientRectangle.Contains(PointToClient(point))) return null;
            Control hit = this, child;
            while ((child = hit.GetChildAtPoint(hit.PointToClient(point), GetChildAtPointSkip.Invisible | GetChildAtPointSkip.Disabled)) != null) hit = child;
            CpuTargetTile target = hit as CpuTargetTile;
            return target != null && target.Enabled && activeDrag.Accepts(activeDrag.Token, service.Topology, target.Group, cpuGroups, busy) ? target : null;
        }
        private void MoveApplicationDrag(Point point)
        {
            if (activeDrag == null) return;
            Cursor.Current = Cursors.Arrow;
            if (dragPreview != null) dragPreview.FollowPointer();
            CpuTargetTile target = DragTargetAt(point);
            foreach (KeyValuePair<CpuSelectionGroup, CpuTargetTile> pair in targetButtons)
            {
                bool hot = pair.Value == target;
                if (pair.Value.DropHot != hot) { pair.Value.DropHot = hot; pair.Value.Invalidate(); }
            }
        }
        private void EndApplicationDrag(bool drop, Point point)
        {
            pendingCoreEditTile = null;
            RestoreDragCursor();
            if (activeDrag == null) return;
            CpuTargetTile target = drop ? DragTargetAt(point) : null;
            ProcessRow[] targets = activeDrag.Targets;
            activeDrag = null;
            if (pressedTile != null && !pressedTile.IsDisposed) { pressedTile.Cursor = sourceCursor; pressedTile.Capture = false; }
            foreach (AppIconTile source in dragTiles) if (!source.IsDisposed) { source.Dragging = false; source.Invalidate(); }
            dragTiles = new AppIconTile[0];
            if (dragPreview != null) { AppDragPreview old = dragPreview; dragPreview = null; old.Close(); old.Dispose(); }
            foreach (KeyValuePair<CpuSelectionGroup, CpuTargetTile> pair in targetButtons)
                if (pair.Value.DropHot) { pair.Value.DropHot = false; pair.Value.Invalidate(); }
            Cursor.Current = Cursors.Arrow; QueueInteractionEnd();
            if (target != null) ApplyTargets(targets, target.Group.Mask, target.Group.Name);
        }
        private void RestoreDragCursor()
        {
            if (!dragCursorHidden) return;
            dragCursorHidden = false; Cursor.Show();
        }
        private void ApplyTargets(IEnumerable<ProcessRow> selected, ulong mask, string targetName)
        {
            ApplyCpuTargets(selected, mask, targetName, false);
        }
        private void ApplyCpuTargets(IEnumerable<ProcessRow> selected, ulong mask, string targetName, bool useCpuSets)
        {
            if (busy || !service.Topology.CanAssign || mask == 0 || useCpuSets && cpuSetService == null) return;
            ProcessRow[] targets = selected.GroupBy(p => p.Key).Select(g => g.First()).ToArray(); if (targets.Length == 0) return;
            selectedMask = mask;
            StopAutomation("手动分配已暂停自动方案。");
            ProcessRow blockedGame = null;
            RunBatch(delegate
            {
                int success = 0, failed = 0, skipped = 0;
                OperationResult firstFailure = null;
                foreach (ProcessRow row in targets)
                {
                    OperationResult result = useCpuSets ? cpuSetService.Apply(row, mask) : service.Apply(row, mask);
                    Record(result, row.Key);
                    if (result.Success && !result.Skipped)
                    {
                        retainedAssignments.RememberCpu(row, mask, useCpuSets);
                    }
                    if (!useCpuSets && result.AffinityWriteDenied && SteamLaunchAffinity.IsApex(row)) blockedGame = row;
                    if (result.Skipped) skipped++;
                    else if (result.Success) success++;
                    else { failed++; if (firstFailure == null) firstFailure = result; }
                }
                string heading = useCpuSets ? "默认 CPU 集 " + Masks.Format(mask) : targetName;
                string summary = heading + " · 成功 " + success + " 个进程" +
                    (failed == 0 ? "" : " · 失败 " + failed) + (skipped == 0 ? "" : " · 跳过 " + skipped);
                Append(firstFailure == null ? summary : firstFailure + (targets.Length == 1 ? "" : " · " + summary),
                    firstFailure == null ? UiTheme.Blue : Color.Firebrick);
            });
            if (blockedGame != null) OfferSteamLaunchAffinity(blockedGame, mask);
        }

        private void OfferSteamLaunchAffinity(ProcessRow game, ulong mask)
        {
            ProcessRow steam = SteamLaunchAffinity.FindLauncher(processes, game);
            if (steam == null) return;
            string cpus = "CPU " + Masks.Format(mask);
            if (steam.Mask == mask)
            {
                Append("Apex 写入失败 · Steam 已是 " + cpus + "，可从 Steam 重新启动 Apex 后查看结果。", UiTheme.Muted);
                return;
            }
            using (var dialog = new SteamAffinityDialog(Masks.Format(mask)))
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
            RunBatch(delegate
            {
                OperationResult result = service.Apply(steam, mask);
                Record(result, steam.Key);
                if (result.Success)
                {
                    retainedAssignments.RememberCpu(steam, mask, false);
                    Append("Steam 已设为 " + cpus + " · 请保持 Steam 打开并重新启动 Apex。", UiTheme.Blue);
                }
            });
        }
        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (activeDrag != null)
            {
                if (keyData == Keys.Escape) EndApplicationDrag(false, Point.Empty);
                // Keep target/selection shortcuts from changing a frozen drag.
                return true;
            }
            if (keyData == (Keys.Control | Keys.A) && applications.ContainsFocus) { SetVisibleProcessSelection(true); return true; }
            if (keyData == (Keys.Control | Keys.F)) { searchBox.FocusSearch(); return true; }
            if (keyData == Keys.Escape && detailsPopup != null) { CloseProcessDetails(); return true; }
            return base.ProcessCmdKey(ref message, keyData);
        }
    }
}
