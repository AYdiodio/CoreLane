using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;

namespace CoreAllocator
{
    internal sealed class DragImagePart
    {
        internal ProcessVisual Visual;
        internal RectangleF ScreenBounds;
        internal RectangleF BaseBounds;
        internal float Scale = 1;
        internal bool Harmonized;
    }
    internal sealed class DesktopDragImage : IDisposable
    {
        internal readonly Bitmap Pixels;
        private readonly Point origin;
        private readonly Point pointerOrigin;
        private DesktopDragImage(Bitmap pixels, Point origin, Point pointerOrigin)
        { Pixels = pixels; this.origin = origin; this.pointerOrigin = pointerOrigin; }
        internal static DesktopDragImage CreateCompact(IEnumerable<DragImagePart> source, Point pointerOrigin, int tileCount)
        {
            const int columns = 4, maximumPreviewTiles = 12;
            DragImagePart[] parts = source.Take(maximumPreviewTiles).ToArray();
            if (parts.Length < 2) return Create(parts, pointerOrigin);

            RectangleF anchor = PartBounds(parts[0]);
            float pitch = parts.Max(part => Math.Max(PartBounds(part).Width, PartBounds(part).Height)) + 4 * parts[0].Scale;
            var arranged = new DragImagePart[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                DragImagePart part = parts[i];
                RectangleF bounds = PartBounds(part), icon = part.ScreenBounds, frame = part.BaseBounds;
                float dx = anchor.Left + (i % columns) * pitch - bounds.Left;
                float dy = anchor.Top + (i / columns) * pitch - bounds.Top;
                icon.Offset(dx, dy);
                if (!frame.IsEmpty) frame.Offset(dx, dy);
                arranged[i] = new DragImagePart { Visual = part.Visual, ScreenBounds = icon, BaseBounds = frame, Scale = part.Scale, Harmonized = part.Harmonized };
            }
            return Create(arranged, pointerOrigin, Math.Max(0, tileCount - parts.Length));
        }

        internal static DesktopDragImage Create(IEnumerable<DragImagePart> source, Point pointerOrigin)
        { return Create(source, pointerOrigin, 0); }

        private static DesktopDragImage Create(IEnumerable<DragImagePart> source, Point pointerOrigin, int hiddenCount)
        {
            DragImagePart[] parts = source.Where(p => p.ScreenBounds.Width > 0 && p.ScreenBounds.Height > 0).ToArray();
            if (parts.Length == 0) throw new ArgumentException("没有可显示的拖动图标。");
            RectangleF union = PartBounds(parts[0]);
            foreach (DragImagePart part in parts.Skip(1)) union = RectangleF.Union(union, PartBounds(part));
            Rectangle bounds = Rectangle.FromLTRB((int)Math.Floor(union.Left) - 1, (int)Math.Floor(union.Top) - 1,
                (int)Math.Ceiling(union.Right) + 1, (int)Math.Ceiling(union.Bottom) + 1);
            if ((long)bounds.Width * bounds.Height > 16777216) throw new ArgumentException("拖动图像超出可显示范围。");
            Bitmap pixels = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppPArgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(pixels))
                {
                    graphics.Clear(Color.Transparent); graphics.CompositingMode = CompositingMode.SourceOver;
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    foreach (DragImagePart part in parts)
                    {
                        if (!part.BaseBounds.IsEmpty)
                        {
                            RectangleF frame = part.BaseBounds; frame.Offset(-bounds.Left, -bounds.Top);
                            RectangleF shadow = frame; shadow.Offset(0, part.Scale * UiTheme.AppShadowOffset);
                            UiTheme.FillRound(graphics, Color.FromArgb(2, 31, 47, 76), shadow, part.Scale * UiTheme.AppBaseRadius);
                            UiTheme.FillRound(graphics, UiTheme.IconBase, frame, part.Scale * UiTheme.AppBaseRadius);
                        }
                        RectangleF target = part.ScreenBounds; target.Offset(-bounds.Left, -bounds.Top);
                        UiTheme.DrawAppIcon(graphics, part.Visual, target, part.Harmonized);
                    }
                    if (hiddenCount > 0)
                    {
                        DragImagePart last = parts[parts.Length - 1];
                        RectangleF frame = PartBounds(last); frame.Offset(-bounds.Left, -bounds.Top);
                        UiTheme.FillRound(graphics, UiTheme.IconBase, frame, last.Scale * UiTheme.AppBaseRadius);
                        using (var font = new Font("Segoe UI", 11 * last.Scale, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var ink = new SolidBrush(UiTheme.Ink))
                        using (var alignment = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            graphics.DrawString("+" + (hiddenCount + 1), font, ink, frame, alignment);
                    }
                }
                return new DesktopDragImage(pixels, bounds.Location, pointerOrigin);
            }
            catch { pixels.Dispose(); throw; }
        }
        private static RectangleF PartBounds(DragImagePart part)
        {
            if (part.BaseBounds.IsEmpty) return part.ScreenBounds;
            RectangleF shadow = part.BaseBounds; shadow.Offset(0, part.Scale * UiTheme.AppShadowOffset);
            return RectangleF.Union(part.ScreenBounds, RectangleF.Union(part.BaseBounds, shadow));
        }
        internal Point LocationFor(Point pointer)
        { return new Point(origin.X + pointer.X - pointerOrigin.X, origin.Y + pointer.Y - pointerOrigin.Y); }
        public void Dispose() { Pixels.Dispose(); }
    }
}
