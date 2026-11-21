using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal sealed class CpuTargetTile : Button
    {
        internal CpuSelectionGroup Group;
        internal bool DropHot;
        private bool hover;
        internal CpuTargetTile()
        {
            ResizeRedraw = true;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = Color.White;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            float scale = UiTheme.Scale(this); Graphics g = e.Graphics;
            g.Clear(Parent == null ? Color.White : Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF frame = new RectangleF(1, 1, Width - 3, Height - 3);
            Color accent = Group != null && Group.Index % 2 == 1 ? UiTheme.Purple : UiTheme.Blue;
            Color tint = UiTheme.Mix(Color.White, accent, .09F);
            UiTheme.FillRound(g, DropHot ? tint : hover ? Color.FromArgb(250, 250, 252) : Color.White, frame, 19 * scale);
            using (GraphicsPath path = UiTheme.Round(frame, 19 * scale))
            using (Pen line = new Pen(DropHot ? accent : UiTheme.Line, DropHot ? 2 * scale : scale)) g.DrawPath(line, path);
            RectangleF icon = new RectangleF(16 * scale, (Height - 48 * scale) / 2, 48 * scale, 48 * scale);
            UiTheme.FillRound(g, Enabled ? tint : UiTheme.Page, icon, 14 * scale);
            UiTheme.Chip(g, RectangleF.Inflate(icon, -11 * scale, -11 * scale), Enabled ? accent : UiTheme.Muted);
            int x = (int)(78 * scale), textWidth = Math.Max(1, Width - x - (int)(12 * scale));
            string name = Group == null ? "CPU" : Group.Name;
            using (Font title = new Font(Font.FontFamily, 12, FontStyle.Regular))
            {
                TextRenderer.DrawText(g, DropHot ? "松手分配至 " + name : name, title,
                    new Rectangle(x, (int)(Height / 2 - 22 * scale), textWidth, (int)(24 * scale)), Enabled ? UiTheme.Ink : UiTheme.Muted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                if (!DropHot && Group != null && Group.ClusterLabel != null)
                {
                    int nameWidth = TextRenderer.MeasureText(name, title, Size.Empty, TextFormatFlags.NoPadding).Width;
                    int badgeX = x + nameWidth + (int)(9 * scale), badgeWidth = (int)(49 * scale);
                    if (badgeX + badgeWidth < Width - 8 * scale)
                    {
                        RectangleF badge = new RectangleF(badgeX, Height / 2 - 20 * scale, badgeWidth, 20 * scale);
                        UiTheme.FillRound(g, UiTheme.Page, badge, 10 * scale);
                        using (Font badgeFont = new Font(Font.FontFamily, 8))
                            TextRenderer.DrawText(g, Group.ClusterLabel, badgeFont, Rectangle.Round(badge), UiTheme.Muted,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    }
                }
            }
            string detail = Group == null ? "" : "CPU " + Masks.Format(Group.Mask);
            if (Group != null && Group.WindowsGroup != 0) detail = "G" + Group.WindowsGroup + " · " + detail;
            TextRenderer.DrawText(g, detail, Font, new Rectangle(x, Height / 2 + (int)(4 * scale), textWidth, (int)(22 * scale)),
                UiTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -5, -5));
        }
    }

    internal sealed class SearchSurface : Button
    {
        private static readonly Color Fill = Color.FromArgb(243, 245, 248);
        private readonly TextBox input;
        private readonly Timer motion = new Timer { Interval = 15 };
        private bool expanded, menuOpen, mouseSearch;
        private UiGlyph hotGlyph;

        internal bool MenuOpen { get { return menuOpen; } set { menuOpen = value; Invalidate(); } }

        internal SearchSurface(TextBox input)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Fill;
            AccessibleName = "更多";
            AccessibleDescription = "按回车打开更多菜单，按 Ctrl+F 搜索应用。";
            this.input = input;
            input.BorderStyle = BorderStyle.None;
            input.BackColor = Fill;
            input.AccessibleName = "搜索应用";
            input.Visible = false;
            Controls.Add(input);
            input.MouseLeave += delegate { CheckPointer(); };
            input.GotFocus += delegate { Invalidate(); };
            input.LostFocus += delegate { Invalidate(); CheckPointer(); };
            input.TextChanged += delegate
            {
                AccessibleDescription = input.TextLength == 0
                    ? "按回车打开更多菜单，按 Ctrl+F 搜索应用。"
                    : "已筛选：" + input.Text + "。按 Ctrl+F 编辑搜索。";
                Invalidate();
            };
            input.KeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.KeyCode != Keys.Escape) return;
                CollapseSearch();
                args.Handled = args.SuppressKeyPress = true;
            };
            motion.Tick += delegate
            {
                if (expanded && mouseSearch) CheckPointer();
                int target = DesiredWidth, delta = target - Width;
                Width += Math.Abs(delta) <= 4 ? delta : delta / 3;
                motion.Interval = Width == target ? 50 : 15;
                if (Width == target && !(expanded && mouseSearch)) motion.Stop();
            };
        }
        private int ButtonWidth { get { return (int)Math.Round(36 * UiTheme.Scale(this)); } }
        private int DesiredWidth { get { return (expanded ? (int)Math.Round(262 * UiTheme.Scale(this)) : ButtonWidth * 2); } }
        private void Expand(bool value)
        {
            if (expanded == value) return;
            expanded = value;
            PerformLayout();
            motion.Interval = 15;
            motion.Start();
        }
        internal void FocusSearch()
        {
            mouseSearch = false;
            FocusEditor();
        }
        private void FocusEditor()
        {
            Expand(true);
            Width = DesiredWidth;
            input.Focus();
        }
        private void CheckPointer()
        {
            if (mouseSearch && !ClientRectangle.Contains(PointToClient(Cursor.Position))) CollapseSearch();
        }
        private void CollapseSearch()
        {
            if (!expanded) return;
            mouseSearch = false;
            bool returnFocus = input.Focused;
            Expand(false);
            if (returnFocus) Focus();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            hotGlyph = e.X >= Width - ButtonWidth ? UiGlyph.More : e.X < ButtonWidth ? UiGlyph.Search : UiGlyph.None;
            Cursor = hotGlyph == UiGlyph.None ? Cursors.IBeam : Cursors.Hand;
            if (hotGlyph == UiGlyph.Search && !expanded)
            {
                mouseSearch = true;
                Expand(true);
            }
            Invalidate();
        }
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hotGlyph = UiGlyph.None;
            CheckPointer();
            Invalidate();
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && e.X < Width - ButtonWidth)
            {
                mouseSearch = true;
                FocusEditor();
                motion.Start();
                return;
            }
            base.OnMouseDown(e);
        }
        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            motion.Stop();
            Width = DesiredWidth;
            PerformLayout();
            if (expanded && mouseSearch) motion.Start();
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (input == null) return;
            float scale = UiTheme.Scale(this);
            int gap = (int)Math.Round(4 * scale), left = ButtonWidth + gap;
            int width = Width - ButtonWidth - left - gap;
            input.Bounds = new Rectangle(left, Math.Max(0, (Height - input.PreferredHeight) / 2), Math.Max(1, width), input.PreferredHeight);
            input.Visible = expanded && width >= 20 * scale;
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent == null ? Color.White : Parent.BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = UiTheme.Scale(this);
            RectangleF bounds = new RectangleF(1, 1, Width - 2, Height - 2);
            UiTheme.FillRound(g, Fill, bounds, bounds.Height / 2);
            if (input != null && input.Focused)
                using (Pen pen = new Pen(UiTheme.Blue, 1.5F * scale))
                using (GraphicsPath path = UiTheme.Round(bounds, bounds.Height / 2)) g.DrawPath(pen, path);
            PaintGlyph(g, UiGlyph.Search, 0, expanded || hotGlyph == UiGlyph.Search || input.TextLength > 0);
            PaintGlyph(g, UiGlyph.More, Width - ButtonWidth, menuOpen || hotGlyph == UiGlyph.More);
        }
        private void PaintGlyph(Graphics g, UiGlyph glyph, int left, bool highlighted)
        {
            float scale = UiTheme.Scale(this), size = 16 * scale;
            RectangleF ring = RectangleF.Inflate(new RectangleF(left, 0, ButtonWidth, Height), -3 * scale, -3 * scale);
            if (highlighted) UiTheme.FillRound(g, UiTheme.BlueWash, ring, ring.Height / 2);
            UiGlyphs.Draw(g, glyph, new RectangleF(left + (ButtonWidth - size) / 2, (Height - size) / 2, size, size), highlighted ? UiTheme.Blue : UiTheme.Muted);
            if (glyph == UiGlyph.More && Focused && ShowFocusCues)
                using (Pen pen = new Pen(UiTheme.Blue, scale))
                using (GraphicsPath path = UiTheme.Round(ring, ring.Height / 2)) g.DrawPath(pen, path);
        }
        protected override void Dispose(bool disposing) { if (disposing) motion.Dispose(); base.Dispose(disposing); }
    }
}
