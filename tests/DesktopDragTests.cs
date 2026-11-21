using System;
using System.Drawing;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static void DesktopDragTests()
        {
            Case("multi-drag: compact preview keeps the grabbed icon and draws all nearby bases", delegate
            {
                DragImagePart[] parts = {
                    new DragImagePart { ScreenBounds = new RectangleF(30, 30, 20, 20), BaseBounds = new RectangleF(10, 10, 60, 60) },
                    new DragImagePart { ScreenBounds = new RectangleF(830, 430, 20, 20), BaseBounds = new RectangleF(810, 410, 60, 60) } };
                Point grab = new Point(36, 34);
                using (DesktopDragImage image = DesktopDragImage.CreateCompact(parts, grab, 2))
                {
                    Check(image.Pixels.Width < 134 && image.Pixels.Height < 66, "two bases occupy the same compact row");
                    Check(image.LocationFor(grab) == new Point(9, 9) && image.Pixels.GetPixel(31, 10).A == 255 &&
                        image.Pixels.GetPixel(96, 10).A == 255, "mouse anchor and both rounded bases are present");
                    Check(parts[1].BaseBounds.X == 810, "compacting drag preview keeps original grid placement");
                }
            });
            Case("desktop drag base: rounded bases preserve original icon placement and transparent gaps", delegate
            {
                using (Bitmap icon = new Bitmap(20, 20))
                {
                    using (Graphics graphics = Graphics.FromImage(icon)) graphics.Clear(Color.Red);
                    ProcessVisual visual = new ProcessVisual { Icon = icon };
                    DragImagePart[] parts = {
                        new DragImagePart { Visual = visual, ScreenBounds = new RectangleF(30, 30, 20, 20), BaseBounds = new RectangleF(10, 10, 60, 60) },
                        new DragImagePart { Visual = visual, ScreenBounds = new RectangleF(130, 30, 20, 20), BaseBounds = new RectangleF(110, 10, 60, 60) } };
                    using (DesktopDragImage image = DesktopDragImage.Create(parts, new Point(36, 34)))
                    {
                        Check(image.LocationFor(new Point(46, 54)) == new Point(19, 29), "base extent does not shift the grab point");
                        Check(image.Pixels.GetPixel(31, 10).A == 255 && image.Pixels.GetPixel(131, 10).A == 255, "both base surfaces are present");
                        Check(image.Pixels.GetPixel(0, 0).A == 0 && image.Pixels.GetPixel(80, 30).A == 0, "rounded corners and inter-base gap stay transparent");
                        Check(image.Pixels.GetPixel(31, 31).R > 200 && image.Pixels.GetPixel(131, 31).R > 200, "original icons retain their positions inside bases");
                        Color neutral = image.Pixels.GetPixel(31, 10);
                        Check(neutral.ToArgb() == UiTheme.IconBase.ToArgb(), "drag base uses neutral fill without selected badge or outline");
                    }
                }
            });
            Case("desktop drag image: alpha gaps, four separate originals and signed pointer offsets survive", delegate
            {
                using (Bitmap icon = new Bitmap(20, 20))
                {
                    using (Graphics graphics = Graphics.FromImage(icon)) { graphics.Clear(Color.Transparent); graphics.FillEllipse(Brushes.Red, 3, 3, 14, 14); }
                    ProcessVisual visual = new ProcessVisual { Icon = icon };
                    DragImagePart[] parts = {
                        new DragImagePart { Visual = visual, ScreenBounds = new RectangleF(-80, -40, 20, 20) },
                        new DragImagePart { Visual = visual, ScreenBounds = new RectangleF(20, -40, 20, 20) },
                        new DragImagePart { Visual = visual, ScreenBounds = new RectangleF(-80, 60, 20, 20) },
                        new DragImagePart { Visual = visual, ScreenBounds = new RectangleF(20, 60, 20, 20) } };
                    using (DesktopDragImage image = DesktopDragImage.Create(parts, new Point(-73, -32)))
                    {
                        Check(image.Pixels.GetPixel(0, 0).A == 0 && image.Pixels.GetPixel(60, 60).A == 0, "no card background or pixels between icons");
                        Check(image.Pixels.GetPixel(11, 11).R > 200 && image.Pixels.GetPixel(111, 11).R > 200 &&
                            image.Pixels.GetPixel(11, 111).R > 200 && image.Pixels.GetPixel(111, 111).R > 200, "all four originals keep their row and column spacing");
                        Point original = image.LocationFor(new Point(-73, -32)); Point moved = image.LocationFor(new Point(-43, -12));
                        Check(original == new Point(-81, -41) && moved == new Point(-51, -21), "grab point and negative monitor coordinates remain exact");
                    }
                }
            });
            Case("desktop drag image: frozen pixels do not mutate originals and empty or excessive surfaces are rejected", delegate
            {
                using (Bitmap icon = new Bitmap(20, 20))
                {
                    using (Graphics graphics = Graphics.FromImage(icon)) graphics.Clear(Color.Red);
                    string fingerprint = ApplicationIconGroup.Fingerprint(icon);
                    DragImagePart part = new DragImagePart { Visual = new ProcessVisual { Icon = icon }, ScreenBounds = new RectangleF(10, 20, 20, 20) };
                    using (DesktopDragImage image = DesktopDragImage.Create(new[] { part }, new Point(16, 24)))
                    {
                        Check(ApplicationIconGroup.Fingerprint(icon) == fingerprint, "building a drag image leaves source pixels unchanged");
                        using (Graphics graphics = Graphics.FromImage(icon)) graphics.Clear(Color.Blue);
                        Check(image.Pixels.GetPixel(11, 11).R > 200 && image.Pixels.GetPixel(11, 11).B < 20, "later source changes do not change the frozen drag image");
                    }
                    Throws(delegate { DesktopDragImage.Create(new DragImagePart[0], Point.Empty); });
                    part.ScreenBounds = new RectangleF(0, 0, 100000, 100000);
                    Throws(delegate { DesktopDragImage.Create(new[] { part }, Point.Empty); });
                }
            });
        }
    }
}
