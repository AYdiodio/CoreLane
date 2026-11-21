using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CoreAllocator
{
    internal enum UiGlyph { None, Pointer, Sparkles, Refresh, Adjust, Cpu, Arrow, Undo, List, Check, Play, Pause, More, Search, ChevronRight, ChevronLeft, Plus, Uninstall }

    internal static class UiGlyphs
    {
        internal static void Draw(Graphics graphics, UiGlyph glyph, RectangleF bounds, Color color)
        {
            if (glyph == UiGlyph.None || bounds.Width <= 0 || bounds.Height <= 0) return;
            GraphicsState saved = graphics.Save();
            try
            {
                graphics.TranslateTransform(bounds.Left, bounds.Top); graphics.ScaleTransform(bounds.Width / 20F, bounds.Height / 20F);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen pen = new Pen(color, 1.55F) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    switch (glyph)
                    {
                        case UiGlyph.Pointer: graphics.DrawPolygon(pen, new[] { new PointF(4, 3), new PointF(16, 9), new PointF(11, 11), new PointF(9, 16) }); graphics.DrawLine(pen, 11, 12, 15, 16); break;
                        case UiGlyph.Sparkles: Star(graphics, pen, 10, 10, 6); Star(graphics, pen, 16, 3, 2); graphics.DrawLine(pen, 3, 3, 3, 6); graphics.DrawLine(pen, 1.5F, 4.5F, 4.5F, 4.5F); break;
                        case UiGlyph.Refresh: graphics.DrawArc(pen, 3, 3, 14, 14, 25, 290); graphics.DrawLines(pen, new[] { new PointF(13, 2), new PointF(16.5F, 4.5F), new PointF(12, 6) }); break;
                        case UiGlyph.Adjust: for (int i = 0; i < 3; i++) { float y = 4 + 6 * i, x = i == 1 ? 13 : 7; graphics.DrawLine(pen, 2, y, x - 2, y); graphics.DrawLine(pen, x + 2, y, 18, y); graphics.DrawEllipse(pen, x - 2, y - 2, 4, 4); } break;
                        case UiGlyph.Cpu: UiTheme.Chip(graphics, new RectangleF(1, 1, 18, 18), color); break;
                        case UiGlyph.Uninstall:
                            graphics.DrawLines(pen, new[] { new PointF(5, 7), new PointF(6, 17), new PointF(14, 17), new PointF(15, 7) });
                            graphics.DrawLine(pen, 3, 5, 17, 5);
                            graphics.DrawLine(pen, 8, 3, 12, 3);
                            break;
                        case UiGlyph.Arrow: graphics.DrawLine(pen, 3, 10, 17, 10); graphics.DrawLines(pen, new[] { new PointF(11, 4), new PointF(17, 10), new PointF(11, 16) }); break;
                        case UiGlyph.ChevronRight: graphics.DrawLines(pen, new[] { new PointF(7, 4), new PointF(13, 10), new PointF(7, 16) }); break;
                        case UiGlyph.ChevronLeft: graphics.DrawLines(pen, new[] { new PointF(13, 4), new PointF(7, 10), new PointF(13, 16) }); break;
                        case UiGlyph.Plus: graphics.DrawLine(pen, 4, 10, 16, 10); graphics.DrawLine(pen, 10, 4, 10, 16); break;
                        case UiGlyph.Undo: graphics.DrawLines(pen, new[] { new PointF(7, 3), new PointF(3, 7), new PointF(7, 11) }); graphics.DrawLine(pen, 3, 7, 12, 7); graphics.DrawArc(pen, 8, 7, 9, 9, 270, 240); break;
                        case UiGlyph.List: for (int i = 0; i < 3; i++) { float y = 4 + 6 * i; graphics.DrawLine(pen, 7, y, 18, y); graphics.DrawLine(pen, 2, y, 3, y); } break;
                        case UiGlyph.Check: graphics.DrawLines(pen, new[] { new PointF(3, 10), new PointF(7, 14), new PointF(16, 5) }); break;
                        case UiGlyph.Play: graphics.DrawPolygon(pen, new[] { new PointF(6, 3), new PointF(16, 10), new PointF(6, 17) }); break;
                        case UiGlyph.Pause: graphics.DrawLine(pen, 7, 4, 7, 16); graphics.DrawLine(pen, 13, 4, 13, 16); break;
                        case UiGlyph.More: using (Brush brush = new SolidBrush(color)) for (int i = 0; i < 3; i++) graphics.FillEllipse(brush, 3 + 6 * i, 9, 2, 2); break;
                        case UiGlyph.Search: graphics.DrawEllipse(pen, 2, 2, 11, 11); graphics.DrawLine(pen, 12, 12, 18, 18); break;
                    }
                }
            }
            finally { graphics.Restore(saved); }
        }
        private static void Star(Graphics graphics, Pen pen, float x, float y, float radius)
        {
            graphics.DrawPolygon(pen, new[] { new PointF(x, y - radius), new PointF(x + radius * .3F, y - radius * .3F), new PointF(x + radius, y), new PointF(x + radius * .3F, y + radius * .3F), new PointF(x, y + radius), new PointF(x - radius * .3F, y + radius * .3F), new PointF(x - radius, y), new PointF(x - radius * .3F, y - radius * .3F) });
        }
    }
}
