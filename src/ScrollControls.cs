using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CoreAllocator
{
    // A single visual and hit-target definition for application-owned scrollbars.
    internal class CapsuleScrollRail : Control
    {
        private readonly Control viewport;
        private readonly Func<int> contentHeight, offset, maximum;
        private readonly Action<int> scroll, wheel;
        private int grabOffset;
        internal CapsuleScrollRail(Control viewport, Func<int> contentHeight, Func<int> offset, Func<int> maximum, Action<int> scroll, Action<int> wheel)
        {
            this.viewport = viewport; this.contentHeight = contentHeight; this.offset = offset; this.maximum = maximum; this.scroll = scroll; this.wheel = wheel;
            DoubleBuffered = ResizeRedraw = true; BackColor = Color.White;
            AccessibleName = "滚动内容"; AccessibleRole = AccessibleRole.ScrollBar; TabStop = false;
        }
        internal Rectangle TrackBounds
        {
            get { float s = UiTheme.Scale(this); int width = (int)Math.Round(8 * s), inset = (int)Math.Round(6 * s); return new Rectangle((Width - width) / 2, inset, width, Math.Max(1, Height - 2 * inset)); }
        }
        internal Rectangle ThumbBounds
        {
            get
            {
                Rectangle track = TrackBounds;
                int height = Math.Min(track.Height, Math.Max((int)Math.Round(36 * UiTheme.Scale(this)), (int)Math.Round((double)track.Height * viewport.ClientSize.Height / Math.Max(1, contentHeight()))));
                int y = track.Y + (int)Math.Round((double)(track.Height - height) * offset() / Math.Max(1, maximum()));
                return new Rectangle(track.X, y, track.Width, height);
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (maximum() == 0) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle track = TrackBounds, thumb = ThumbBounds;
            UiTheme.FillRound(e.Graphics, UiTheme.Page, track, track.Width / 2F);
            Color fill = ClientRectangle.Contains(PointToClient(Cursor.Position)) || Capture ? UiTheme.Muted : Color.FromArgb(160, 168, 183);
            UiTheme.FillRound(e.Graphics, fill, thumb, thumb.Width / 2F);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left || maximum() == 0) return;
            Rectangle thumb = ThumbBounds; grabOffset = e.Y >= thumb.Top && e.Y < thumb.Bottom ? e.Y - thumb.Top : thumb.Height / 2;
            Capture = true; MoveThumb(e.Y);
        }
        private void MoveThumb(int y)
        { int travel = TrackBounds.Height - ThumbBounds.Height; if (travel > 0) scroll((int)Math.Round((double)(y - TrackBounds.Y - grabOffset) * maximum() / travel)); Invalidate(); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) MoveThumb(e.Y); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (e.Button == MouseButtons.Left) Capture = false; Invalidate(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { HandleWheel(e); base.OnMouseWheel(e); }
        private void HandleWheel(MouseEventArgs e) { wheel(e.Delta); HandledMouseEventArgs handled = e as HandledMouseEventArgs; if (handled != null) handled.Handled = true; }
    }

    internal class RepaintPanel : Panel
    {
        internal RepaintPanel() { DoubleBuffered = ResizeRedraw = true; }
    }

    internal class RepaintTable : TableLayoutPanel
    {
        internal RepaintTable() { DoubleBuffered = ResizeRedraw = true; }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); Invalidate(true); }
    }

    // Manual positioning avoids native AutoScroll/ScrollWindowEx copying stale child pixels.
    internal class CapsuleScrollPanel : RepaintPanel
    {
        private sealed class ContentPanel : RepaintPanel
        {
            private readonly CapsuleScrollPanel owner;
            internal ContentPanel(CapsuleScrollPanel owner) { this.owner = owner; }
            protected override void OnMouseWheel(MouseEventArgs e) { owner.HandleWheel(e); base.OnMouseWheel(e); }
        }
        internal readonly Panel Content;
        internal readonly CapsuleScrollRail Rail;
        private int offset;
        private bool arranging;
        internal int ScrollOffset { get { return offset; } }
        internal int ContentHeight { get { return Content.Height; } }
        internal int MaximumScroll { get { return Math.Max(0, Content.Height - ClientSize.Height); } }
        internal int RailWidth { get { return (int)Math.Round(26 * UiTheme.Scale(this)); } }
        internal int ContentWidth { get { return Math.Max(1, ClientSize.Width - RailWidth); } }
        internal event EventHandler ViewportChanged;
        internal event EventHandler ViewScrolled;
        internal CapsuleScrollPanel()
        {
            Margin = Padding.Empty; BackColor = UiTheme.Page;
            Content = new ContentPanel(this) { Margin = Padding.Empty, BackColor = BackColor };
            Rail = new CapsuleScrollRail(this, delegate { return Content.Height; }, delegate { return offset; }, delegate { return MaximumScroll; }, ScrollTo, ScrollWheel) { BackColor = BackColor };
            Controls.Add(Content); Controls.Add(Rail);
            Content.SizeChanged += delegate { ClampScroll(); };
        }
        protected override void OnBackColorChanged(EventArgs e)
        { base.OnBackColorChanged(e); if (Content != null) Content.BackColor = BackColor; if (Rail != null) Rail.BackColor = BackColor; }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (arranging || Content == null) return; arranging = true;
            try { Content.SetBounds(0, -offset, ContentWidth, Content.Height); Rail.SetBounds(ContentWidth, 0, RailWidth, ClientSize.Height); ClampScroll(); }
            finally { arranging = false; }
        }
        protected override void OnSizeChanged(EventArgs e)
        { base.OnSizeChanged(e); PerformLayout(); if (ViewportChanged != null) ViewportChanged(this, EventArgs.Empty); Invalidate(true); }
        protected override void OnDpiChangedAfterParent(EventArgs e)
        { base.OnDpiChangedAfterParent(e); PerformLayout(); if (ViewportChanged != null) ViewportChanged(this, EventArgs.Empty); }
        internal void SetContentHeight(int height) { Content.Height = Math.Max(0, height); ClampScroll(); }
        private void ClampScroll() { if (Content == null) return; offset = Math.Max(0, Math.Min(MaximumScroll, offset)); Content.Top = -offset; Rail.Invalidate(); }
        internal void ScrollTo(int value)
        {
            value = Math.Max(0, Math.Min(MaximumScroll, value)); if (value == offset) return;
            offset = value; Content.Top = -offset; Invalidate(true); Rail.Invalidate();
            if (ViewScrolled != null) ViewScrolled(this, EventArgs.Empty);
        }
        internal void ScrollWheel(int delta)
        {
            int lines = SystemInformation.MouseWheelScrollLines;
            int step = lines < 0 ? ClientSize.Height : (int)Math.Round(24 * UiTheme.Scale(this)) * lines;
            ScrollTo(offset - (int)Math.Round(delta * step / 120.0));
        }
        private void HandleWheel(MouseEventArgs e)
        {
            HandledMouseEventArgs handled = e as HandledMouseEventArgs;
            if (handled != null && handled.Handled) return;
            ScrollWheel(e.Delta); if (handled != null) handled.Handled = true;
        }
        protected override void OnMouseWheel(MouseEventArgs e) { HandleWheel(e); base.OnMouseWheel(e); }
        internal void Reveal(Control control)
        {
            Rectangle bounds = RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
            if (bounds.Top < 0) ScrollTo(offset + bounds.Top);
            else if (bounds.Bottom > ClientSize.Height) ScrollTo(offset + bounds.Bottom - ClientSize.Height);
        }
    }
}
