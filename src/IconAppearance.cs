using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace CoreAllocator
{
    // Computed from pixels, never from a software name or a saved CPU rule.
    internal sealed class IconAppearance
    {
        internal readonly float OpticalScale, Saturation;
        internal readonly bool RoundOpaqueSquare;
        internal readonly ColorMatrix Colors;
        private IconAppearance(float opticalScale, float saturation, float contrast, bool roundOpaqueSquare)
        {
            OpticalScale = opticalScale; Saturation = saturation; RoundOpaqueSquare = roundOpaqueSquare;
            float d = saturation * contrast, r = .213F * (1 - saturation) * contrast,
                g = .715F * (1 - saturation) * contrast, b = .072F * (1 - saturation) * contrast, bias = (1 - contrast) / 2;
            Colors = new ColorMatrix(new[] { new[] { d + r, r, r, 0F, 0F }, new[] { g, d + g, g, 0F, 0F },
                new[] { b, b, d + b, 0F, 0F }, new[] { 0F, 0F, 0F, 1F, 0F }, new[] { bias, bias, bias, 0F, 1F } });
        }
        internal static IconAppearance Analyze(Bitmap image, Rectangle source)
        {
            if (image == null || source.Width < 1 || source.Height < 1) return new IconAppearance(1, 1, 1, false);
            float alpha = 0, chroma = 0;
            const int samples = 24;
            for (int y = 0; y < samples; y++) for (int x = 0; x < samples; x++)
            {
                Color pixel = image.GetPixel(source.Left + Math.Min(source.Width - 1, (2 * x + 1) * source.Width / (2 * samples)),
                    source.Top + Math.Min(source.Height - 1, (2 * y + 1) * source.Height / (2 * samples)));
                float weight = pixel.A / 255F;
                alpha += weight;
                chroma += (Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) - Math.Min(pixel.R, Math.Min(pixel.G, pixel.B))) / 255F * weight;
            }
            float coverage = alpha / (samples * samples);
            float aspect = Math.Min(source.Width, source.Height) / (float)Math.Max(source.Width, source.Height);
            float density = coverage * aspect;
            // Dense square/circular marks become slightly smaller; narrow marks retain
            // their full available width. No source pixels are enlarged past the slot.
            float optical = density <= .62F ? 1 : Math.Max(.86F, (float)Math.Sqrt(.62F / density));
            float vivid = alpha <= 0 ? 0 : Math.Max(0, Math.Min(1, (chroma / alpha - .10F) / .45F));
            return new IconAppearance(optical, 1 - .18F * vivid, .98F - .03F * vivid, coverage > .96F && aspect > .85F);
        }
        internal RectangleF Destination(RectangleF slot, Rectangle source)
        {
            float factor = Math.Min(slot.Width / source.Width, slot.Height / source.Height) * OpticalScale;
            float width = source.Width * factor, height = source.Height * factor;
            return new RectangleF(slot.X + (slot.Width - width) / 2, slot.Y + (slot.Height - height) / 2, width, height);
        }
    }
}
