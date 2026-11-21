using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal static class UiTheme
    {
        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr window);
        internal static float Scale(Control control)
        {
            uint dpi = control.IsHandleCreated ? GetDpiForWindow(control.Handle) : 96;
            return (dpi == 0 ? 96 : dpi) / 96F;
        }
        internal static readonly Color Page = Color.FromArgb(245, 245, 247);
        internal static readonly Color Ink = Color.FromArgb(29, 29, 31);
        internal static readonly Color Muted = Color.FromArgb(115, 121, 134);
        internal static readonly Color Blue = Color.FromArgb(0, 122, 255);
        internal static readonly Color Purple = Color.FromArgb(139, 92, 246);
        internal static readonly Color Line = Color.FromArgb(229, 231, 239);
        internal static readonly Color BlueWash = Color.FromArgb(236, 245, 255);
        internal static readonly Color PurpleWash = Color.FromArgb(245, 240, 255);
        internal static readonly Color StartInk = Color.FromArgb(20, 118, 71), StartWash = Color.FromArgb(229, 245, 236);
        internal static readonly Color PauseInk = Color.FromArgb(172, 93, 24), PauseWash = Color.FromArgb(255, 240, 221);
        internal static readonly Color IconBase = Color.FromArgb(246, 248, 251);
        internal static readonly Color IconHover = Color.FromArgb(240, 244, 249);
        internal const float AppIconSize = 36, AppBaseSize = 58, AppBaseRadius = 18, AppHoverLift = 2, AppShadowOffset = 2;

        internal static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { path.AddRectangle(r); return path; }
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
        internal static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }
        internal static void FillRound(Graphics g, Color color, RectangleF rect, float radius)
        {
            using (GraphicsPath path = Round(rect, radius))
            using (Brush brush = new SolidBrush(color)) g.FillPath(brush, path);
        }
        internal static void DrawAppIcon(Graphics graphics, ProcessVisual visual, RectangleF bounds, bool harmonized = false)
        {
            bool redrawn = harmonized && visual != null && visual.Redrawn != null;
            if (redrawn) visual = visual.Redrawn;
            if (visual != null && visual.Icon != null)
            {
                Rectangle source = visual.DisplayBounds;
                float factor = Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
                RectangleF destination = new RectangleF(bounds.X + (bounds.Width - source.Width * factor) / 2, bounds.Y + (bounds.Height - source.Height * factor) / 2, source.Width * factor, source.Height * factor);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic; graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                if (!harmonized) graphics.DrawImage(visual.Icon, destination, source, GraphicsUnit.Pixel);
                else
                {
                    IconAppearance appearance = visual.Appearance;
                    destination = appearance.Destination(bounds, source);
                    RectangleF clipBounds = appearance.RoundOpaqueSquare ? destination : bounds;
                    GraphicsState state = graphics.Save();
                    try
                    {
                        using (GraphicsPath clip = Round(clipBounds, Math.Min(clipBounds.Width, clipBounds.Height) * 7 / 38))
                        using (ImageAttributes attributes = new ImageAttributes())
                        {
                            graphics.SetClip(clip, CombineMode.Intersect); attributes.SetColorMatrix(appearance.Colors);
                            graphics.DrawImage(visual.Icon, new[] { new PointF(destination.Left, destination.Top), new PointF(destination.Right, destination.Top),
                                new PointF(destination.Left, destination.Bottom) }, (RectangleF)source, GraphicsUnit.Pixel, attributes);
                        }
                    }
                    finally { graphics.Restore(state); }
                }
            }
            else
            {
                FillRound(graphics, Color.FromArgb(230, 237, 250), bounds, bounds.Width * .24F);
                Chip(graphics, RectangleF.Inflate(bounds, -bounds.Width * .22F, -bounds.Height * .22F), Color.FromArgb(118, 143, 183));
            }
        }
        internal static void Chip(Graphics g, RectangleF rect, Color color)
        {
            using (Pen pen = new Pen(color, Math.Max(1.5F, rect.Width / 18)))
            {
                float inset = rect.Width * .22F;
                RectangleF body = new RectangleF(rect.X + inset, rect.Y + inset, rect.Width - inset * 2, rect.Height - inset * 2);
                using (GraphicsPath path = Round(body, rect.Width * .09F)) g.DrawPath(pen, path);
                for (int i = 1; i <= 3; i++)
                {
                    float x = body.Left + body.Width * i / 4;
                    float y = body.Top + body.Height * i / 4;
                    g.DrawLine(pen, x, rect.Top + 1, x, body.Top);
                    g.DrawLine(pen, x, body.Bottom, x, rect.Bottom - 1);
                    g.DrawLine(pen, rect.Left + 1, y, body.Left, y);
                    g.DrawLine(pen, body.Right, y, rect.Right - 1, y);
                }
                g.DrawRectangle(pen, body.X + body.Width * .28F, body.Y + body.Height * .28F, body.Width * .44F, body.Height * .44F);
            }
        }
    }

    internal sealed class CardPanel : Panel
    {
        internal CardPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            BackColor = Color.White;
            Padding = new Padding(20);
        }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? UiTheme.Page : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            UiTheme.FillRound(e.Graphics, Color.White, new RectangleF(0, 0, Width - 1, Height - 1), 22 * UiTheme.Scale(this));
        }
    }

    internal enum ButtonTone { Default, Purple, Start, Pause }

    internal class SoftButton : Button
    {
        private readonly Timer animation = new Timer();
        private bool hover;
        private bool pressed;
        private float progress;
        internal bool Primary;
        internal UiGlyph Glyph { get; set; }
        internal bool Destructive { get; set; }
        internal bool Chosen { get; set; }
        internal ButtonTone Tone { get; set; }
        internal bool SurfaceButton { get; set; }
        internal bool TrailingCheck { get; set; }
        internal int BadgeCount { get; set; }
        internal SoftButton()
        {
            ResizeRedraw = true;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.White;
            ForeColor = UiTheme.Ink;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            animation.Interval = 15;
            animation.Tick += delegate
            {
                float target = hover ? 1 : 0;
                progress += (target - progress) * .28F;
                if (Math.Abs(target - progress) < .02F) { progress = target; animation.Stop(); }
                Invalidate();
            };
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; animation.Start(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; animation.Start(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) { pressed = false; Invalidate(); } }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Color.White : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = Primary ? UiTheme.Blue : Chosen ? UiTheme.BlueWash : Color.FromArgb(245, 245, 248);
            if (!Primary && !Chosen && Parent != null && Parent.BackColor == UiTheme.Page) fill = Color.White;
            Color hot = Primary ? Color.FromArgb(0, 100, 230) : Color.FromArgb(229, 238, 251);
            Color text = Primary ? Color.White : Destructive ? Color.FromArgb(220, 70, 70) : Chosen ? UiTheme.Blue : UiTheme.Ink;
            if (SurfaceButton && !Primary && !Chosen) fill = Color.White;
            if (Tone == ButtonTone.Purple)
            {
                fill = Primary ? UiTheme.Purple : Chosen ? UiTheme.PurpleWash : SurfaceButton ? Color.White : UiTheme.Page;
                hot = Primary ? Color.FromArgb(113, 66, 222) : UiTheme.PurpleWash;
                text = Primary ? Color.White : Chosen ? UiTheme.Purple : UiTheme.Ink;
            }
            if (Tone == ButtonTone.Start) { fill = UiTheme.StartWash; hot = Color.FromArgb(212, 239, 223); text = UiTheme.StartInk; }
            if (Tone == ButtonTone.Pause) { fill = UiTheme.PauseWash; hot = Color.FromArgb(255, 229, 195); text = UiTheme.PauseInk; }
            if (!Enabled) { fill = Color.FromArgb(239, 239, 243); text = Color.FromArgb(175, 175, 183); }
            else fill = UiTheme.Mix(fill, hot, progress);
            float scale = UiTheme.Scale(this), inset = pressed && Enabled ? scale * 1.2F : 0;
            RectangleF frame = new RectangleF(inset, inset, Width - 1 - inset * 2, Height - 1 - inset * 2);
            UiTheme.FillRound(e.Graphics, fill, frame, frame.Height / 2);
            if (Glyph == UiGlyph.None)
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            else
            {
                int icon = (int)(16 * scale), gap = Text.Length == 0 ? 0 : (int)(7 * scale);
                int textWidth = Text.Length == 0 ? 0 : TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
                int badgeWidth = BadgeCount > 0 ? Math.Max((int)(24 * scale), TextRenderer.MeasureText(BadgeCount.ToString(), Font, Size.Empty, TextFormatFlags.NoPadding).Width + (int)(10 * scale)) : 0;
                int trailing = badgeWidth > 0 ? badgeWidth + gap : TrailingCheck ? icon + gap : 0;
                int x = Math.Max((int)(10 * scale), (Width - textWidth - icon - gap - trailing) / 2);
                UiGlyphs.Draw(e.Graphics, Glyph, new RectangleF(x, (Height - icon) / 2F + inset, icon, icon), text);
                if (Text.Length != 0) TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(x + icon + gap, (int)inset, Math.Max(1, Width - x - icon - gap - trailing - (int)(8 * scale)), Height - (int)inset), text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                if (badgeWidth > 0)
                {
                    Rectangle badge = new Rectangle(Width - badgeWidth - (int)(10 * scale), (Height - (int)(24 * scale)) / 2, badgeWidth, (int)(24 * scale));
                    UiTheme.FillRound(e.Graphics, Enabled ? UiTheme.BlueWash : UiTheme.Page, badge, badge.Height / 2F);
                    TextRenderer.DrawText(e.Graphics, BadgeCount.ToString(), Font, badge, Enabled ? UiTheme.Blue : UiTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                else if (TrailingCheck) UiGlyphs.Draw(e.Graphics, UiGlyph.Check, new RectangleF(Width - icon - 12 * scale, (Height - icon) / 2F, icon, icon), text);
            }
            if (Focused && ShowFocusCues)
                using (Pen focus = new Pen(text) { DashStyle = DashStyle.Dot })
                using (GraphicsPath path = UiTheme.Round(RectangleF.Inflate(frame, -4 * scale, -4 * scale), Math.Max(1, frame.Height / 2 - 4 * scale))) e.Graphics.DrawPath(focus, path);
        }
        protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }
    }

    internal sealed class PillCheckBox : CheckBox
    {
        private bool hover;
        internal PillCheckBox()
        {
            AutoSize = false; Appearance = Appearance.Button; FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand; Width = 116; Height = 32;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnCheckStateChanged(EventArgs e) { base.OnCheckStateChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(Parent == null ? Color.White : Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = UiTheme.Scale(this); Color ink = !Enabled ? UiTheme.Muted : CheckState == CheckState.Unchecked ? UiTheme.Ink : UiTheme.Blue;
            UiTheme.FillRound(g, hover || CheckState != CheckState.Unchecked ? UiTheme.BlueWash : UiTheme.Page, new RectangleF(0, 0, Width - 1, Height - 1), Height / 2F);
            UiGlyphs.Draw(g, UiGlyph.Check, new RectangleF(12 * scale, (Height - 16 * scale) / 2, 16 * scale, 16 * scale), ink);
            TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(36 * scale), 0, Math.Max(1, Width - (int)(43 * scale)), Height), ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -5, -5), ink, BackColor);
        }
    }

    internal sealed class SearchTextBox : TextBox
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr parameter, string value);
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); SendMessage(Handle, 0x1501, new IntPtr(1), "搜索应用…"); }
    }

    internal sealed class SmoothGrid : DataGridView
    {
        internal SmoothGrid() { DoubleBuffered = true; }
    }
}
