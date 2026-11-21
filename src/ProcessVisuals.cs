using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace CoreAllocator
{
    internal sealed class ProcessVisual
    {
        internal Bitmap Icon;
        internal string DisplayName;
        internal string ProductName;
        internal string CompanyName;
        internal ProcessVisual Redrawn;
        private Bitmap keyedImage;
        private string imageKey;
        private IconDescriptor descriptor;
        private Bitmap displayImage;
        private Rectangle displayBounds;
        private Bitmap appearanceImage;
        private Rectangle appearanceBounds;
        private IconAppearance appearance;
        internal IconAppearance Appearance
        {
            get
            {
                Rectangle bounds = DisplayBounds;
                if (appearance == null || !object.ReferenceEquals(appearanceImage, Icon) || appearanceBounds != bounds)
                { appearance = IconAppearance.Analyze(Icon, bounds); appearanceImage = Icon; appearanceBounds = bounds; }
                return appearance;
            }
        }
        // Only the drawing rectangle changes; fingerprints and stored images remain untouched.
        internal Rectangle DisplayBounds
        {
            get
            {
                if (Icon == null) return Rectangle.Empty;
                if (!object.ReferenceEquals(displayImage, Icon))
                {
                    int left = Icon.Width, top = Icon.Height, right = -1, bottom = -1;
                    for (int y = 0; y < Icon.Height; y++) for (int x = 0; x < Icon.Width; x++)
                    {
                        if (Icon.GetPixel(x, y).A < 24) continue;
                        left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x); bottom = Math.Max(bottom, y);
                    }
                    displayBounds = right < left || bottom < top ? new Rectangle(0, 0, Icon.Width, Icon.Height) :
                        Rectangle.Intersect(new Rectangle(0, 0, Icon.Width, Icon.Height), Rectangle.FromLTRB(left - 1, top - 1, right + 2, bottom + 2));
                    displayImage = Icon;
                }
                return displayBounds;
            }
        }
        internal string ImageKey
        {
            get
            {
                EnsureImage();
                return imageKey;
            }
        }
        internal IconDescriptor Descriptor { get { EnsureImage(); return descriptor; } }
        private void EnsureImage()
        {
            if (imageKey != null && object.ReferenceEquals(keyedImage, Icon)) return;
            imageKey = ApplicationIconGroup.Fingerprint(Icon); descriptor = IconSimilarity.Describe(Icon); keyedImage = Icon;
        }
    }

    internal sealed class ProcessVisualCache : IDisposable
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, ProcessVisual> cache = new Dictionary<string, ProcessVisual>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> pending = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly RoundedLogoPack logos = new RoundedLogoPack();
        private bool disposed;
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);

        internal ProcessVisual Get(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            lock (gate) { ProcessVisual item; return cache.TryGetValue(path, out item) ? item : null; }
        }

        internal void Prime(IEnumerable<ProcessRow> rows, Action updated)
        {
            string[] candidates = rows.Select(x => x.ExecutablePath).Where(x => !string.IsNullOrEmpty(x) && !x.StartsWith(@"\\", StringComparison.Ordinal)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            List<string> queue = new List<string>();
            lock (gate)
            {
                if (disposed) return;
                foreach (string path in candidates) if (!cache.ContainsKey(path) && pending.Add(path)) queue.Add(path);
            }
            if (queue.Count == 0) return;
            Task.Factory.StartNew(delegate
            {
                int completed = 0;
                foreach (string path in queue)
                {
                    lock (gate) { if (disposed) return; }
                    ProcessVisual visual = Read(path);
                    visual.Redrawn = logos.ForExecutable(path, visual);
                    lock (gate)
                    {
                        pending.Remove(path);
                        if (disposed) { if (visual.Icon != null) visual.Icon.Dispose(); return; }
                        cache[path] = visual;
                    }
                    if (++completed % 8 == 0) updated();
                }
                updated();
            });
        }

        private static ProcessVisual Read(string path)
        {
            ProcessVisual result = new ProcessVisual();
            IntPtr large = IntPtr.Zero, small = IntPtr.Zero;
            try
            {
                if (!File.Exists(path)) return result;
                uint extracted = ExtractIconEx(path, 0, out large, out small, 1);
                IntPtr chosen = large != IntPtr.Zero ? large : small;
                if (extracted > 0 && extracted != uint.MaxValue && chosen != IntPtr.Zero)
                    using (Icon icon = Icon.FromHandle(chosen)) result.Icon = icon.ToBitmap();
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                result.DisplayName = info.FileDescription;
                result.ProductName = info.ProductName;
                result.CompanyName = info.CompanyName;
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            catch (ArgumentException) { }
            catch (ExternalException) { }
            catch (System.Security.SecurityException) { }
            finally
            {
                if (large != IntPtr.Zero) DestroyIcon(large);
                if (small != IntPtr.Zero && small != large) DestroyIcon(small);
            }
            return result;
        }

        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                foreach (ProcessVisual item in cache.Values) if (item.Icon != null) item.Icon.Dispose();
                cache.Clear(); pending.Clear(); logos.Dispose();
            }
        }
    }
}
