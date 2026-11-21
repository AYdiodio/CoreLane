using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace CoreAllocator
{
    internal sealed class IconDescriptor
    {
        internal double[] Full;
        internal double[] Foreground;

        internal bool Matches(IconDescriptor other)
        {
            if (other == null) return false;
            // Small color/antialiasing differences may pass, but a shared dominant color is insufficient.
            if (Distance(Full, other.Full) <= 10 && Correlation(Full, other.Full) >= .985) return true;
            // A colored logo can also survive changes to a neutral frame, padding or background shadow.
            return Foreground != null && other.Foreground != null &&
                Distance(Foreground, other.Foreground) <= 22 && Correlation(Foreground, other.Foreground) >= .92;
        }
        internal static double Distance(double[] a, double[] b)
        { double total = 0; for (int i = 0; i < a.Length; i++) total += Math.Abs(a[i] - b[i]); return total / a.Length; }
        internal static double Correlation(double[] a, double[] b)
        {
            double ma = 0, mb = 0;
            for (int i = 0; i < a.Length; i++) { ma += a[i]; mb += b[i]; }
            ma /= a.Length; mb /= b.Length;
            double covariance = 0, va = 0, vb = 0;
            for (int i = 0; i < a.Length; i++) { double x = a[i] - ma, y = b[i] - mb; covariance += x * y; va += x * x; vb += y * y; }
            if (va < .001 && vb < .001) return 1;
            if (va < .001 || vb < .001) return 0;
            return covariance / Math.Sqrt(va * vb);
        }
    }

    internal static class IconSimilarity
    {
        internal static IconDescriptor Describe(Bitmap image)
        {
            if (image == null) return null;
            using (Bitmap original = Render(image, new Rectangle(0, 0, image.Width, image.Height), 40, Color.Transparent))
            {
                IconDescriptor result = new IconDescriptor { Full = Sample(image, new Rectangle(0, 0, image.Width, image.Height), Color.White) };
                // Use the central logo's dominant hue, so a blue/gray executable frame does not become the crop.
                double[] hues = new double[12];
                for (int y = 5; y < 35; y++) for (int x = 5; x < 35; x++)
                { Color pixel = original.GetPixel(x, y); hues[Math.Min(11, (int)(pixel.GetHue() / 30))] += Prominence(pixel); }
                int dominant = 0; for (int i = 1; i < hues.Length; i++) if (hues[i] > hues[dominant]) dominant = i;
                double hue = dominant * 30 + 15, strongest = 0;
                for (int y = 0; y < 40; y++) for (int x = 0; x < 40; x++) strongest = Math.Max(strongest, LogoProminence(original.GetPixel(x, y), hue));
                // Neutral/transparent icons use the stricter full-image comparison only.
                if (strongest < 5000) return result;
                int left = 40, top = 40, right = -1, bottom = -1, count = 0;
                for (int y = 0; y < 40; y++) for (int x = 0; x < 40; x++)
                    if (LogoProminence(original.GetPixel(x, y), hue) >= strongest * .65)
                    { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); count++; }
                if (count < 16 || right - left < 5 || bottom - top < 5) return result;
                using (Bitmap colored = new Bitmap(40, 40, PixelFormat.Format32bppArgb))
                {
                    for (int y = 0; y < 40; y++) for (int x = 0; x < 40; x++)
                    {
                        Color pixel = original.GetPixel(x, y);
                        // Suppress neutral borders and weak colored shadows, preserving the prominent logo.
                        if (LogoProminence(pixel, hue) >= strongest * .35) colored.SetPixel(x, y, pixel);
                    }
                    result.Foreground = Smooth(Sample(colored, Rectangle.FromLTRB(left, top, right + 1, bottom + 1), Color.Black));
                }
                return result;
            }
        }
        private static double Prominence(Color color)
        {
            int high = Math.Max(color.R, Math.Max(color.G, color.B)), low = Math.Min(color.R, Math.Min(color.G, color.B));
            return (high - low) * high * (color.A / 255.0);
        }
        private static double LogoProminence(Color color, double hue)
        { double distance = Math.Abs(color.GetHue() - hue); return Math.Min(distance, 360 - distance) <= 45 ? Prominence(color) : 0; }
        private static double[] Smooth(double[] values)
        {
            // Average a one-pixel neighborhood so low-resolution/antialiased edges do not dominate the score.
            double[] result = new double[values.Length];
            for (int y = 0; y < 20; y++) for (int x = 0; x < 20; x++) for (int c = 0; c < 3; c++)
            {
                double total = 0; int count = 0;
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                { int px = x + dx, py = y + dy; if (px >= 0 && px < 20 && py >= 0 && py < 20) { total += values[(py * 20 + px) * 3 + c]; count++; } }
                result[(y * 20 + x) * 3 + c] = total / count;
            }
            return result;
        }
        private static double[] Sample(Bitmap image, Rectangle crop, Color background)
        {
            using (Bitmap sample = Render(image, crop, 20, background))
            {
                double[] values = new double[20 * 20 * 3];
                for (int y = 0; y < 20; y++) for (int x = 0; x < 20; x++)
                { Color pixel = sample.GetPixel(x, y); int offset = (y * 20 + x) * 3; values[offset] = pixel.R; values[offset + 1] = pixel.G; values[offset + 2] = pixel.B; }
                return values;
            }
        }
        private static Bitmap Render(Bitmap image, Rectangle crop, int size, Color background)
        {
            Bitmap result = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.Clear(background); graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                // Keep proportions: a narrow logo must not become a square logo after cropping.
                double scale = Math.Min((double)size / crop.Width, (double)size / crop.Height);
                int width = Math.Max(1, (int)Math.Round(crop.Width * scale)), height = Math.Max(1, (int)Math.Round(crop.Height * scale));
                graphics.DrawImage(image, new Rectangle((size - width) / 2, (size - height) / 2, width, height), crop, GraphicsUnit.Pixel);
            }
            return result;
        }
    }
}
