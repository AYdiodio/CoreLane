using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CoreAllocator
{
    internal static class LayeredDragSurface
    {
        [StructLayout(LayoutKind.Sequential)] private struct Header
        {
            internal uint Size; internal int Width, Height; internal ushort Planes, Bits;
            internal uint Compression, ImageSize; internal int XResolution, YResolution; internal uint Used, Important;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Info { internal Header Header; internal uint Colors; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { internal byte Operation, Flags, Opacity, Format; }
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref Info info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destination, ref Point position,
            ref Size size, IntPtr source, ref Point sourcePosition, uint key, ref Blend blend, uint flags);
        internal static void SetImage(IntPtr window, Bitmap pixels, Point position, byte opacity)
        {
            IntPtr screen = IntPtr.Zero, memory = IntPtr.Zero, bitmap = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                screen = GetDC(IntPtr.Zero); if (screen == IntPtr.Zero) throw new Win32Exception();
                memory = CreateCompatibleDC(screen); if (memory == IntPtr.Zero) throw new Win32Exception();
                Info info = new Info { Header = new Header { Size = (uint)Marshal.SizeOf(typeof(Header)), Width = pixels.Width, Height = -pixels.Height, Planes = 1, Bits = 32 } };
                IntPtr bits; bitmap = CreateDIBSection(screen, ref info, 0, out bits, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero || bits == IntPtr.Zero) throw new Win32Exception();
                BitmapData data = pixels.LockBits(new Rectangle(Point.Empty, pixels.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    int length = pixels.Width * 4; byte[] row = new byte[length];
                    for (int y = 0; y < pixels.Height; y++)
                    { Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, length); Marshal.Copy(row, 0, IntPtr.Add(bits, y * length), length); }
                }
                finally { pixels.UnlockBits(data); }
                previous = SelectObject(memory, bitmap);
                if (previous == IntPtr.Zero || previous == new IntPtr(-1)) throw new Win32Exception();
                Size size = pixels.Size; Point sourcePosition = Point.Empty;
                Blend blend = new Blend { Opacity = opacity, Format = 1 };
                if (!UpdateLayeredWindow(window, screen, ref position, ref size, memory, ref sourcePosition, 0, ref blend, 2)) throw new Win32Exception();
            }
            finally
            {
                if (previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(memory, previous);
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (memory != IntPtr.Zero) DeleteDC(memory);
                if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}
