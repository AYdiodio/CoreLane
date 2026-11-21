using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal sealed class AppIconPanel : Panel
    {
        internal const int TileWidth = 88, TileHeight = 90;
        private bool arranging;
        private int scrollOffset;
        private Size arrangedSize;
        private int arrangedOffset;
        internal int ScrollOffset { get { return scrollOffset; } }
        internal int ContentHeight { get; private set; }
        internal int GridInset { get; private set; }
        internal int MaximumScroll { get { return Math.Max(0, ContentHeight - ClientSize.Height); } }
        internal event EventHandler ViewChanged;
        internal event EventHandler ViewScrolled;
        internal string EmptyText = "";
        internal bool IsSelecting { get; private set; }
        internal Rectangle SelectionBounds { get; private set; }
        private Point selectionStart;
        private SelectionMarquee marquee;
        internal event EventHandler SelectionStarted;
        internal event EventHandler SelectionMoved;
        internal event EventHandler SelectionEnded;
        internal event EventHandler SelectionCancelled;
        internal AppIconPanel() { DoubleBuffered = ResizeRedraw = true; SetStyle(ControlStyles.Selectable, true); TabStop = false; }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); PerformLayout(); }
        protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); PerformLayout(); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (arranging) return;
            arranging = true;
            try
            {
                AppIconTile[] tiles = Controls.OfType<AppIconTile>().ToArray();
                float scale = UiTheme.Scale(this);
                int available = Math.Max(1, ClientSize.Width - Padding.Horizontal);
                int columns = Math.Max(1, available / Math.Max(1, (int)Math.Round(98 * scale)));
                double slotWidth = Math.Min(available, Math.Round(TileWidth * scale));
                double firstCenter = columns == 1 ? available / 2.0 : slotWidth / 2;
                double pitch = columns == 1 ? 0 : (available - slotWidth) / (columns - 1);
                int rowPitch = (int)Math.Round(102 * scale);
                int previousHeight = ContentHeight, previousInset = GridInset;
                ContentHeight = Padding.Vertical + ((tiles.Length + columns - 1) / columns) * rowPitch;
                scrollOffset = Math.Min(scrollOffset, MaximumScroll);
                GridInset = Padding.Left + Math.Max(0, (int)Math.Round(firstCenter - UiTheme.AppBaseSize * scale / 2));
                bool changed = arrangedSize != ClientSize || arrangedOffset != scrollOffset || previousHeight != ContentHeight || previousInset != GridInset;
                arrangedSize = ClientSize;
                arrangedOffset = scrollOffset;
                for (int i = 0; i < tiles.Length; i++)
                {
                    Control tile = tiles[i];
                    Point location = new Point(Padding.Left + (int)Math.Round(firstCenter + i % columns * pitch - tile.Width / 2.0),
                        Padding.Top + (i / columns) * rowPitch + (rowPitch - tile.Height) / 2 - scrollOffset);
                    if (tile.Location == location) continue;
                    tile.Location = location;
                    changed = true;
                }
                if (!changed) return;
            }
            finally { arranging = false; }
            // Reflow moves opaque child HWNDs. Clear their old positions after the complete layout.
            Invalidate(true);
            if (ViewChanged != null) ViewChanged(this, EventArgs.Empty);
        }
        internal void ScrollTo(int offset)
        {
            offset = Math.Max(0, Math.Min(MaximumScroll, offset));
            if (scrollOffset == offset) return;
            if (IsSelecting) EndSelection(true);
            scrollOffset = offset;
            PerformLayout();
            if (ViewScrolled != null) ViewScrolled(this, EventArgs.Empty);
        }
        internal void RevealTile(Control tile)
        {
            if (tile.Top < 0) ScrollTo(scrollOffset + tile.Top);
            else if (tile.Bottom > ClientSize.Height) ScrollTo(scrollOffset + tile.Bottom - ClientSize.Height);
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollWheel(e.Delta);
        }
        internal void ScrollWheel(int delta)
        {
            int lines = SystemInformation.MouseWheelScrollLines;
            int step = lines < 0 ? ClientSize.Height : (int)Math.Round(24 * UiTheme.Scale(this)) * lines;
            ScrollTo(scrollOffset - (int)Math.Round(delta * step / 120.0));
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return;
            Focus(); IsSelecting = true; selectionStart = new Point(e.X, e.Y + scrollOffset);
            SelectionBounds = Rectangle.Empty; if (SelectionStarted != null) SelectionStarted(this, EventArgs.Empty); Capture = true;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); if (!IsSelecting) return;
            Point start = new Point(selectionStart.X, selectionStart.Y - scrollOffset);
            Point end = new Point(Math.Max(0, Math.Min(ClientSize.Width, e.X)), Math.Max(0, Math.Min(ClientSize.Height, e.Y)));
            Rectangle bounds = Rectangle.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y), Math.Max(start.X, end.X), Math.Max(start.Y, end.Y));
            if (SelectionBounds == bounds) return;
            SelectionBounds = bounds;
            if (SelectionMoved != null) SelectionMoved(this, EventArgs.Empty);
            if (marquee == null) marquee = new SelectionMarquee { Owner = FindForm() };
            marquee.UpdateBounds(this, SelectionBounds);
        }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left && IsSelecting) EndSelection(false); }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (IsSelecting && !Capture) EndSelection(true); }
        protected override bool IsInputKey(Keys keyData) { return (keyData & Keys.KeyCode) == Keys.Escape || base.IsInputKey(keyData); }
        protected override void OnKeyDown(KeyEventArgs e)
        { if (e.KeyCode == Keys.Escape && IsSelecting) { EndSelection(true); e.Handled = e.SuppressKeyPress = true; } base.OnKeyDown(e); }
        private void EndSelection(bool cancel)
        {
            IsSelecting = false; SelectionBounds = Rectangle.Empty;
            if (marquee != null) { marquee.Dispose(); marquee = null; }
            Capture = false; Invalidate(true);
            EventHandler handler = cancel ? SelectionCancelled : SelectionEnded; if (handler != null) handler(this, EventArgs.Empty);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!Controls.OfType<AppIconTile>().Any() && !string.IsNullOrEmpty(EmptyText))
                TextRenderer.DrawText(e.Graphics, EmptyText, Font, Rectangle.Inflate(ClientRectangle, -12, -12), UiTheme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }
        protected override void OnSizeChanged(EventArgs e)
        {
            if (IsSelecting) EndSelection(true);
            base.OnSizeChanged(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && marquee != null) { marquee.Dispose(); marquee = null; }
            base.Dispose(disposing);
        }
    }

    internal sealed class AppScrollRail : CapsuleScrollRail
    {
        internal AppScrollRail(AppIconPanel view) : base(view, delegate { return view.ContentHeight; }, delegate { return view.ScrollOffset; }, delegate { return view.MaximumScroll; }, view.ScrollTo, view.ScrollWheel)
        { AccessibleName = "滚动应用列表"; }
    }
    // A native check box for keyboard/accessibility, painted as an icon only.
    internal sealed class AppIconTile : CheckBox
    {
        private readonly Timer animation = new Timer { Interval = 15 };
        private bool hover;
        private float progress;
        internal ApplicationIconGroup Group;
        internal bool Dragging;
        internal bool UnifiedIcons = true;
        internal bool ShowCpuAssignment;
        internal CpuAssignmentLabel AssignmentLabel;
        internal Keys PressModifiers { get; private set; }
        internal event MouseEventHandler DragReleased;

        internal AppIconTile()
        {
            ResizeRedraw = true;
            Appearance = Appearance.Button; FlatStyle = FlatStyle.Flat; AutoCheck = false;
            FlatAppearance.BorderSize = 0; BackColor = Color.White; Text = "";
            Cursor = Cursors.Hand; TabStop = true;
            SetStyle(ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, true);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            animation.Tick += delegate
            {
                float target = hover ? 1 : 0;
                progress += (target - progress) * .25F;
                if (Math.Abs(progress - target) < .02F) { progress = target; animation.Stop(); }
                Invalidate();
            };
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; animation.Start(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; animation.Start(); }
        protected override void WndProc(ref Message message)
        {
            const int leftDown = 0x0201, leftDoubleClick = 0x0203, shift = 0x0004, control = 0x0008;
            if (message.Msg == leftDown || message.Msg == leftDoubleClick)
            {
                long buttons = message.WParam.ToInt64();
                PressModifiers = (buttons & shift) != 0 ? Keys.Shift : Keys.None;
                if ((buttons & control) != 0) PressModifiers |= Keys.Control;
            }
            base.WndProc(ref message);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            // Finish the internal drag before CheckBox releases capture or raises Click.
            if (Dragging && e.Button == MouseButtons.Left && DragReleased != null) DragReleased(this, e);
            base.OnMouseUp(e);
        }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            AppIconPanel view = Parent as AppIconPanel;
            if (view != null && MouseButtons == MouseButtons.None) view.RevealTile(this);
        }
        protected override void OnCheckStateChanged(EventArgs e) { base.OnCheckStateChanged(e); Invalidate(); }
        protected override bool IsInputKey(Keys keyData) { return (keyData & Keys.KeyCode) == Keys.Down || base.IsInputKey(keyData); }
        private float IconAreaHeight { get { return ShowCpuAssignment ? Math.Max(0, Height - 28 * UiTheme.Scale(this)) : Height; } }
        private RectangleF IconBounds
        {
            get
            {
                float scale = UiTheme.Scale(this), size = (UiTheme.AppIconSize + progress) * scale;
                return new RectangleF((Width - size) / 2, (IconAreaHeight - size) / 2 - progress * scale * UiTheme.AppHoverLift, size, size);
            }
        }
        internal RectangleF IconScreenBounds
        {
            get
            {
                RectangleF bounds = IconBounds; Point origin = PointToScreen(Point.Empty);
                bounds.Offset(origin.X, origin.Y); return bounds;
            }
        }
        private RectangleF BaseBounds
        {
            get
            {
                float scale = UiTheme.Scale(this), size = Math.Min(UiTheme.AppBaseSize * scale, Math.Min(Width, IconAreaHeight) - scale * 2);
                return new RectangleF((Width - size) / 2, (IconAreaHeight - size) / 2 - progress * scale * UiTheme.AppHoverLift, size, size);
            }
        }
        internal DragImagePart DragImagePart
        {
            get
            {
                float scale = UiTheme.Scale(this); Point origin = PointToScreen(Point.Empty);
                RectangleF frame = BaseBounds; frame.Offset(origin.X, origin.Y);
                return new DragImagePart { Visual = Group == null ? null : Group.Visual, ScreenBounds = IconScreenBounds,
                    BaseBounds = frame, Scale = scale, Harmonized = UnifiedIcons };
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; float scale = UiTheme.Scale(this);
            g.Clear(BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF frame = BaseBounds;
            float radius = scale * UiTheme.AppBaseRadius;
            UiTheme.FillRound(g, Checked ? UiTheme.BlueWash : UiTheme.Mix(UiTheme.IconBase, UiTheme.IconHover, progress), frame, radius);
            if (Checked)
                using (Pen outline = new Pen(UiTheme.Blue, scale * 1.5F) { DashStyle = CheckState == CheckState.Indeterminate ? DashStyle.Dash : DashStyle.Solid })
                using (GraphicsPath path = UiTheme.Round(frame, radius)) g.DrawPath(outline, path);
            RectangleF icon = IconBounds;
            ProcessVisual visual = Group == null ? null : Group.Visual;
            UiTheme.DrawAppIcon(g, visual, icon, UnifiedIcons);
            if (ShowCpuAssignment && AssignmentLabel != null)
            {
                using (Font labelFont = new Font(Font.FontFamily, Math.Max(8, Font.Size - .75F), FontStyle.Regular))
                {
                    Size measured = TextRenderer.MeasureText(AssignmentLabel.Text, labelFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                    string indicator = Group == null ? "" : PriorityLevels.Indicator(Group.Members);
                    float padding = 2 * scale;
                    // Reserve the same space on both sides for every label, including those without an indicator.
                    float width = Math.Max(1, Math.Min(Width - 20 * scale, measured.Width + 18 * scale));
                    RectangleF label = new RectangleF((Width - width) / 2, Height - 22 * scale, width, 20 * scale);
                    UiTheme.FillRound(g, AssignmentLabel.Fill, label, 10 * scale);
                    TextRenderer.DrawText(g, AssignmentLabel.Text, labelFont, Rectangle.Round(label), AssignmentLabel.Ink,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                    if (indicator.Length != 0)
                        TextRenderer.DrawText(g, indicator, labelFont, Rectangle.Round(new RectangleF(label.Right + padding, label.Top, Width - label.Right - padding, label.Height)),
                            indicator == "↑" ? UiTheme.Blue : UiTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }
            if (Focused && ShowFocusCues && !Dragging) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -2, -2), UiTheme.Blue, Color.White);
        }
        protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
    }
}
