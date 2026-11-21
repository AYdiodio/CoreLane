using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal static class DialogUi
    {
        internal static void Setup(Form form, string title, Size size)
        {
            form.Text = title; form.Font = new Font("Microsoft YaHei UI", 9.5F);
            form.BackColor = UiTheme.Page; form.ForeColor = UiTheme.Ink;
            form.StartPosition = FormStartPosition.CenterParent; form.ClientSize = size;
            form.MinimizeBox = false; form.MaximizeBox = false; form.ShowIcon = false;
            form.AutoScaleDimensions = new SizeF(96, 96); form.AutoScaleMode = AutoScaleMode.Dpi;
        }
        internal static Label Label(string text, float size, Color color)
        { return new Label { Text = text, Dock = DockStyle.Fill, AutoEllipsis = true, UseMnemonic = false, ForeColor = color, Font = new Font("Microsoft YaHei UI", size), TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }; }
        internal static TableLayoutPanel Rows(params int[] heights)
        {
            TableLayoutPanel p = new RepaintTable { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = heights.Length, Padding = new Padding(24), Margin = Padding.Empty };
            p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            foreach (int h in heights) p.RowStyles.Add(new RowStyle(h < 0 ? SizeType.Percent : SizeType.Absolute, h < 0 ? 100 : h));
            return p;
        }
        internal static FlowLayoutPanel Actions()
        { return new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty, Padding = new Padding(0, 4, 0, 0) }; }
    }
    internal sealed class RoundedSurface : Panel
    {
        internal RoundedSurface() { DoubleBuffered = ResizeRedraw = true; BackColor = UiTheme.Page; }
        protected override void OnPaintBackground(PaintEventArgs e)
        { e.Graphics.Clear(Parent == null ? Color.White : Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; UiTheme.FillRound(e.Graphics, BackColor, new RectangleF(0, 0, Width - 1, Height - 1), 18 * UiTheme.Scale(this)); }
    }
    internal sealed class CoreGroupPanel : TableLayoutPanel
    {
        internal CoreGroupPanel() { DoubleBuffered = ResizeRedraw = true; BackColor = UiTheme.Page; }
        protected override void OnPaintBackground(PaintEventArgs e)
        { e.Graphics.Clear(Parent == null ? Color.White : Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; UiTheme.FillRound(e.Graphics, BackColor, new RectangleF(0, 0, Width - 1, Height - 1), 18 * UiTheme.Scale(this)); }
    }
    internal sealed class CoreCheckBox : CheckBox
    {
        internal Color Accent = UiTheme.Blue;
        private bool mouseClick;
        internal CoreCheckBox()
        {
            ResizeRedraw = true;
            AutoCheck = false; Appearance = Appearance.Button; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            TextAlign = ContentAlignment.MiddleCenter; Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) mouseClick = true; base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); mouseClick = false; }
        protected override void OnKeyDown(KeyEventArgs e) { mouseClick = false; base.OnKeyDown(e); }
        protected override void OnClick(EventArgs e) { if (!mouseClick && Enabled) Checked = !Checked; base.OnClick(e); }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? UiTheme.Page : Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            UiTheme.FillRound(e.Graphics, !Enabled ? UiTheme.Line : Checked ? Accent : UiTheme.Page, new RectangleF(0, 0, Width - 1, Height - 1), Height / 2F);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled && Checked ? Color.White : UiTheme.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5));
        }
    }

    internal sealed class SettingsSectionHeader : Button
    {
        private readonly UiGlyph glyph;
        private bool expanded;
        internal bool Expanded { get { return expanded; } set { expanded = value; AccessibleDescription = value ? "已展开" : "已收起"; Invalidate(); } }
        internal SettingsSectionHeader(string text, UiGlyph glyph)
        {
            ResizeRedraw = true;
            Text = text; AccessibleName = text; this.glyph = glyph; Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = Color.White;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            Expanded = false;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            float s = UiTheme.Scale(this); Graphics g = e.Graphics; g.Clear(Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            UiTheme.FillRound(g, Color.White, new RectangleF(0, 0, Width - 1, Height - 1), 16 * s);
            UiTheme.FillRound(g, expanded ? UiTheme.BlueWash : UiTheme.Page, new RectangleF(10 * s, (Height - 32 * s) / 2, 32 * s, 32 * s), 10 * s);
            UiGlyphs.Draw(g, glyph, new RectangleF(17 * s, (Height - 18 * s) / 2, 18 * s, 18 * s), expanded ? UiTheme.Blue : UiTheme.Ink);
            TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(54 * s), 0, Math.Max(1, Width - (int)(96 * s)), Height), UiTheme.Ink, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            GraphicsState state = g.Save(); g.TranslateTransform(Width - 24 * s, Height / 2F); if (expanded) g.RotateTransform(90);
            UiGlyphs.Draw(g, UiGlyph.ChevronRight, new RectangleF(-7 * s, -7 * s, 14 * s, 14 * s), UiTheme.Muted); g.Restore(state);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -5, -5));
        }
    }

    // Mouse capture stays on the starting button. Each entry (including re-entry) toggles a draft core.
    internal sealed class CpuCoreSweep
    {
        private readonly System.Collections.Generic.List<CoreCheckBox> boxes = new System.Collections.Generic.List<CoreCheckBox>();
        private CoreCheckBox source, last;
        private Point previous;
        internal void Attach(CoreCheckBox box)
        {
            boxes.Add(box);
            box.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || !box.Enabled) return;
                source = box; last = null; previous = box.PointToScreen(e.Location); box.Capture = true; EnterAt(previous);
            };
            box.MouseMove += delegate(object sender, MouseEventArgs e) { if (source == box) MoveTo(box.PointToScreen(e.Location)); };
            box.MouseUp += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) End(); };
            box.MouseCaptureChanged += delegate { if (source == box && !box.Capture) End(); };
        }
        internal void MoveTo(Point position)
        {
            if (source == null) return;
            int dx = position.X - previous.X, dy = position.Y - previous.Y;
            int steps = Math.Max(1, (Math.Max(Math.Abs(dx), Math.Abs(dy)) + 3) / 4);
            for (int i = 1; i <= steps; i++) EnterAt(new Point(previous.X + dx * i / steps, previous.Y + dy * i / steps));
            previous = position;
        }
        private void EnterAt(Point point)
        {
            CoreCheckBox hit = null;
            foreach (CoreCheckBox box in boxes)
            {
                if (!box.Enabled || !box.Visible || !box.RectangleToScreen(box.ClientRectangle).Contains(point)) continue;
                Control parent = box.Parent; bool clipped = false;
                while (parent != null) { if (!parent.RectangleToScreen(parent.ClientRectangle).Contains(point)) { clipped = true; break; } parent = parent.Parent; }
                if (!clipped) { hit = box; break; }
            }
            if (hit != last) { last = hit; if (hit != null) hit.Checked = !hit.Checked; }
        }
        internal void End() { CoreCheckBox old = source; source = null; last = null; if (old != null && !old.IsDisposed) old.Capture = false; }
    }
    internal sealed class ExitSettingsDialog : Form
    {
        internal ExitSettingsDialog()
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            DialogUi.Setup(this, "退出 " + Program.DisplayName, new Size(360, 70));
            Padding = new Padding(12);
            var capsule = new CapsulePanel { Dock = DockStyle.Fill, BackColor = UiTheme.Line };
            var root = new RepaintTable { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = capsule.BackColor };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(capsule);
            capsule.Controls.Add(root);
            SoftButton keep = GameQuestionDialog.MakeButton("保留并退出", 152, delegate { DialogResult = DialogResult.No; }, true);
            SoftButton restore = GameQuestionDialog.MakeButton("恢复并退出", 152, delegate { DialogResult = DialogResult.Yes; }, true);
            keep.Dock = restore.Dock = DockStyle.Fill;
            keep.Margin = restore.Margin = new Padding(2);
            restore.Tone = ButtonTone.Purple;
            restore.AccessibleDescription = "将有恢复记录的进程交还 Windows 调度后退出";
            root.Controls.Add(keep, 0, 0);
            root.Controls.Add(restore, 1, 0);
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
            return base.ProcessDialogKey(keyData);
        }
    }

    internal sealed class SteamAffinityDialog : Form
    {
        internal SteamAffinityDialog(string cpuRange)
        {
            FormBorderStyle = FormBorderStyle.FixedDialog;
            DialogUi.Setup(this, "分配 Steam 启动器", new Size(494, 218));
            TableLayoutPanel root = DialogUi.Rows(-1, 52);
            Controls.Add(root);
            var card = new RoundedSurface { BackColor = Color.White, Dock = DockStyle.Fill, Margin = Padding.Empty };
            TableLayoutPanel content = DialogUi.Rows(32, -1);
            content.Padding = Padding.Empty;
            card.Controls.Add(content);
            root.Controls.Add(card, 0, 0);
            Label target = DialogUi.Label("Steam → CPU " + cpuRange, 12.5F, UiTheme.Blue);
            Label details = DialogUi.Label("Apex 拒绝修改硬亲和性，已确认由 Steam 启动。\r\n设置后，请保持 Steam 打开，再从 Steam 重启 Apex。\r\nSteam 启动的其他程序也可能继承这个范围。\r\n可在「恢复原设置」中将 Steam 交还 Windows 调度。", 9.5F, UiTheme.Ink);
            details.TextAlign = ContentAlignment.TopLeft;
            content.Controls.Add(target, 0, 0);
            content.Controls.Add(details, 0, 1);
            FlowLayoutPanel actions = DialogUi.Actions();
            SoftButton confirm = GameQuestionDialog.MakeButton("设置 Steam", 136, delegate { DialogResult = DialogResult.OK; }, true);
            SoftButton cancel = GameQuestionDialog.MakeButton("取消", 88, delegate { DialogResult = DialogResult.Cancel; }, false);
            actions.Controls.AddRange(new Control[] { confirm, cancel });
            root.Controls.Add(actions, 0, 1);
            CancelButton = cancel;
            ActiveControl = cancel;
            Load += delegate
            {
                float scale = UiTheme.Scale(this);
                Func<int, int> px = value => (int)Math.Round(value * scale);
                root.Padding = new Padding(px(16));
                card.Padding = new Padding(px(20), px(12), px(20), px(12));
                int width = px(494), textWidth = width - root.Padding.Horizontal - card.Padding.Horizontal;
                const TextFormatFlags format = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;
                int bodyHeight;
                using (Graphics graphics = CreateGraphics())
                {
                    content.RowStyles[0].Height = TextRenderer.MeasureText(graphics, target.Text, target.Font, new Size(textWidth, int.MaxValue), format).Height + px(8);
                    bodyHeight = TextRenderer.MeasureText(graphics, details.Text, details.Font, new Size(textWidth, int.MaxValue), format).Height + px(4);
                }
                actions.Padding = new Padding(0, px(10), 0, 0);
                confirm.Size = new Size(px(136), px(36));
                cancel.Size = new Size(px(88), px(36));
                root.RowStyles[1].Height = px(54);
                ClientSize = new Size(width, root.Padding.Vertical + card.Padding.Vertical + (int)content.RowStyles[0].Height + bodyHeight + px(54));
            };
        }
    }

    internal sealed class RestoreSettingsDialog : Form
    {
        internal bool RestoreAll;
        internal RestoreSettingsDialog(SchedulingRestoreEntry[] entries, int selectedCount, bool clearRetained = false)
        {
            SchedulingRestoreEntry[] processes = entries.GroupBy(entry => entry.Key).Select(group => group.First()).ToArray();
            int total = processes.Length;
            DialogUi.Setup(this, "恢复 Windows 调度", new Size(660, Math.Min(485, 280 + total * 46))); MinimumSize = new Size(600, 270);
            TableLayoutPanel root = DialogUi.Rows(44, 62, -1, 40, 48); Controls.Add(root);
            root.Controls.Add(DialogUi.Label("恢复 Windows 调度", 18, UiTheme.Ink), 0, 0);
            string scope = selectedCount == 0 ? "" : " · 已选 " + selectedCount + " 个可恢复进程";
            string summary = total > 0 ? total + " 个进程有恢复记录" + scope + "\r\n开放全部可用 CPU，清除默认 CPU 集，优先级设为正常。" : "当前没有要恢复的进程。";
            root.Controls.Add(DialogUi.Label(summary, 9.5F, UiTheme.Muted), 0, 1);
            CardPanel card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(16), Margin = new Padding(0, 8, 0, 8) };
            ManualRuleList list = new ManualRuleList { Dock = DockStyle.Fill, RowHeight = 36 };
            list.SuspendLayout();
            foreach (SchedulingRestoreEntry process in processes)
            {
                Label row = DialogUi.Label(process.Name + "  [" + process.Pid + "]", 9.5F, UiTheme.Ink);
                row.Dock = DockStyle.None;
                list.Items.Add(row);
            }
            list.ResumeLayout(true);
            card.Controls.Add(list); root.Controls.Add(card, 0, 2);
            card.Visible = total > 0;
            root.Controls.Add(DialogUi.Label(total > 0 ? "无法恢复的记录会保留，便于重试。" : clearRetained ? "确认后会清除保留的分配，停止下次自动应用。" : "", 9, UiTheme.Muted), 0, 3);
            FlowLayoutPanel actions = DialogUi.Actions();
            SoftButton all = GameQuestionDialog.MakeButton(selectedCount > 0 ? "恢复全部" : "确认恢复", 124, delegate { RestoreAll = true; DialogResult = DialogResult.OK; }, true); all.Enabled = total > 0 || clearRetained;
            SoftButton cancel = GameQuestionDialog.MakeButton("取消", 90, delegate { DialogResult = DialogResult.Cancel; }, false);
            actions.Controls.Add(all);
            if (selectedCount > 0)
                actions.Controls.Add(GameQuestionDialog.MakeButton("恢复已选", 124, delegate { RestoreAll = false; DialogResult = DialogResult.OK; }, false));
            actions.Controls.Add(cancel); root.Controls.Add(actions, 0, 4); CancelButton = cancel; ActiveControl = cancel;
        }
    }
}
