using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CoreAllocator
{
    // One click-through surface above all child HWNDs. Child repaints cannot split the marquee.
    internal sealed class SelectionMarquee : Form
    {
        internal SelectionMarquee()
        {
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80 | 0x20 | 0x80000; return p; }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x84) { message.Result = new IntPtr(-1); return; }
            if (message.Msg == 0x21) { message.Result = new IntPtr(3); return; }
            base.WndProc(ref message);
        }
        internal void UpdateBounds(Control viewport, Rectangle bounds)
        {
            bounds = Rectangle.Intersect(viewport.ClientRectangle, bounds);
            if (bounds.Width < 2 || bounds.Height < 2) { Hide(); return; }
            using (Bitmap pixels = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                using (Graphics g = Graphics.FromImage(pixels))
                using (Brush fill = new SolidBrush(Color.FromArgb(24, UiTheme.Blue)))
                using (Pen line = new Pen(UiTheme.Blue))
                { g.FillRectangle(fill, 0, 0, pixels.Width, pixels.Height); g.DrawRectangle(line, 0, 0, pixels.Width - 1, pixels.Height - 1); }
                LayeredDragSurface.SetImage(Handle, pixels, viewport.PointToScreen(bounds.Location), 255);
            }
            if (!Visible) Show(Owner);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }
    }

    // Presentation only: a frozen, per-pixel transparent image, independent of affinity targets.
    internal sealed class AppDragPreview : Form
    {
        private readonly DesktopDragImage image;
        private readonly Timer follow = new Timer { Interval = 16 };
        internal AppDragPreview(IEnumerable<DragImagePart> parts, Point pointerOrigin, int applicationCount, int tileCount = 1)
        {
            image = DesktopDragImage.CreateCompact(parts, pointerOrigin, tileCount);
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.None;
            // An owned window already stays above its owner. Form.TopMost uses an
            // activating SetWindowPos during handle creation, despite WS_EX_NOACTIVATE.
            ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            // Do not set Opacity or TransparencyKey: they interfere with UpdateLayeredWindow.
            ClientSize = image.Pixels.Size; AccessibleName = "正在拖动 " + applicationCount + " 个应用";
            follow.Tick += delegate { FollowPointer(); };
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams parameters = base.CreateParams; parameters.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020 | 0x00080000; return parameters; }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; }
            base.WndProc(ref message);
        }
        internal void FollowPointer()
        {
            Point next = image.LocationFor(Cursor.Position);
            if (Location != next) Location = next;
        }
        internal void ShowPreview(IWin32Window owner)
        {
            // Establish ownership before creating a top-level HWND. Creating a hidden,
            // unowned form first can activate its temporary owner and cancel the source drag.
            Owner = owner as Form;
            FollowPointer();
            LayeredDragSurface.SetImage(Handle, image.Pixels, Location, 199);
            Show(owner);
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e); FollowPointer();
            follow.Start();
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { follow.Dispose(); if (image != null) image.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
