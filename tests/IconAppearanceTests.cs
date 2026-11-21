using System;
using System.Drawing;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static void IconAppearanceTests()
        {
            Case("automatic icon appearance: unknown software is styled without changing pixels or identity; raw display remains available", delegate
            {
                using (Bitmap original = new Bitmap(40, 40))
                using (Bitmap raw = new Bitmap(60, 60))
                using (Bitmap styled = new Bitmap(60, 60))
                {
                    using (Graphics graphics = Graphics.FromImage(original)) graphics.Clear(Color.Red);
                    ProcessVisual visual = new ProcessVisual { Icon = original, ProductName = "NeverSeenBefore 912", CompanyName = "Unknown" };
                    string key = visual.ImageKey;
                    using (Graphics graphics = Graphics.FromImage(raw)) { graphics.Clear(Color.Transparent); UiTheme.DrawAppIcon(graphics, visual, new RectangleF(10, 10, 40, 40), false); }
                    using (Graphics graphics = Graphics.FromImage(styled)) { graphics.Clear(Color.Transparent); UiTheme.DrawAppIcon(graphics, visual, new RectangleF(10, 10, 40, 40), true); }
                    Check(raw.GetPixel(11, 11).A > 0 && styled.GetPixel(11, 11).A == 0, "automatic inset/rounded edge applies without a brand entry");
                    Color center = styled.GetPixel(30, 30);
                    Check(center.A == 255 && center.R > 180 && center.G < 40 && center.B < 40, "unknown logo remains recognizable and opaque");
                    Check(visual.ImageKey == key && ApplicationIconGroup.Fingerprint(original) == key, "source image and grouping identity remain unchanged");
                    IconAppearance first = visual.Appearance;
                    Check(object.ReferenceEquals(first, visual.Appearance), "analysis is cached between paints");
                    using (Bitmap replacement = new Bitmap(40, 40))
                    {
                        using (Graphics graphics = Graphics.FromImage(replacement)) graphics.Clear(Color.Gray);
                        visual.Icon = replacement;
                        Check(!object.ReferenceEquals(first, visual.Appearance), "a newly loaded source does not reuse stale appearance");
                    }
                }
            });
            Case("automatic icon appearance: dense marks are balanced without cropping narrow logos; neutral icons retain their palette", delegate
            {
                using (Bitmap square = new Bitmap(40, 40))
                using (Bitmap wide = new Bitmap(100, 20))
                using (Bitmap neutral = new Bitmap(40, 40))
                {
                    using (Graphics graphics = Graphics.FromImage(square)) graphics.Clear(Color.Lime);
                    using (Graphics graphics = Graphics.FromImage(wide)) graphics.Clear(Color.Lime);
                    using (Graphics graphics = Graphics.FromImage(neutral)) graphics.Clear(Color.Gray);
                    ProcessVisual dense = new ProcessVisual { Icon = square }, narrow = new ProcessVisual { Icon = wide }, gray = new ProcessVisual { Icon = neutral };
                    Check(dense.Appearance.OpticalScale < narrow.Appearance.OpticalScale && narrow.Appearance.OpticalScale <= 1, "dense marks are smaller, wide marks retain available width");
                    RectangleF slot = new RectangleF(5, 7, 40, 40), destination = narrow.Appearance.Destination(slot, narrow.DisplayBounds);
                    Check(destination.Left >= slot.Left && destination.Right <= slot.Right && destination.Top >= slot.Top && destination.Bottom <= slot.Bottom, "complete logo fits the slot");
                    Check(Math.Abs(destination.Width / destination.Height - 5) < .01F, "source aspect ratio is retained");
                    Check(dense.Appearance.Saturation < gray.Appearance.Saturation && gray.Appearance.Saturation == 1, "vivid marks soften without tinting neutral marks");
                }
            });
        }
    }
}
