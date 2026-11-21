using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static Bitmap LogoFixture(Color color, bool cross, bool framed)
        {
            int size = framed ? 64 : 32;
            Bitmap bitmap = new Bitmap(size, size);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(framed ? Color.FromArgb(32, 32, 32) : Color.Black);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float scale = size / 32F;
                if (framed) using (Pen border = new Pen(Color.SlateGray, scale * 2)) graphics.DrawRectangle(border, 1, 1, size - 3, size - 3);
                using (Pen pen = new Pen(color, scale * 2.4F))
                {
                    if (cross) { graphics.DrawLine(pen, 8 * scale, 16 * scale, 24 * scale, 16 * scale); graphics.DrawLine(pen, 16 * scale, 8 * scale, 16 * scale, 24 * scale); }
                    else graphics.DrawEllipse(pen, 7 * scale, 7 * scale, 18 * scale, 18 * scale);
                }
            }
            return bitmap;
        }
        private static void IconSimilarityTests()
        {
            Case("icon similarity: antialiasing resolution neutral frame and slight palette changes can match", delegate
            {
                using (Bitmap first = LogoFixture(Color.FromArgb(210, 140, 20), false, false))
                using (Bitmap framed = LogoFixture(Color.FromArgb(216, 145, 24), false, true))
                {
                    Check(ApplicationIconGroup.Fingerprint(first) != ApplicationIconGroup.Fingerprint(framed), "fixture bypasses exact pixel equality");
                    IconDescriptor a = IconSimilarity.Describe(first), b = IconSimilarity.Describe(framed);
                    Check(a.Matches(b) && b.Matches(a), "tolerant match remains symmetric");
                    Check(ApplicationIconGroup.Build(new[] { SortingApp(201, "A", @"C:\ImageFixture\a.exe", first), SortingApp(202, "B", @"D:\ImageFixture\b.exe", framed) }).Count == 1, "unrelated paths use tolerant image grouping");
                }
            });
            Case("icon similarity: different shapes or colors do not match merely by dominant background", delegate
            {
                using (Bitmap ring = LogoFixture(Color.LimeGreen, false, false))
                using (Bitmap cross = LogoFixture(Color.LimeGreen, true, false))
                using (Bitmap blue = LogoFixture(Color.RoyalBlue, false, false))
                {
                    IconDescriptor a = IconSimilarity.Describe(ring);
                    Check(!a.Matches(IconSimilarity.Describe(cross)), "same palette different logo rejected");
                    Check(!a.Matches(IconSimilarity.Describe(blue)), "same shape different brand color rejected");
                    Check(!a.Matches(null) && IconSimilarity.Describe(null) == null, "missing icon cannot fuzzy-match a real logo");
                }
            });
            Case("icon similarity: close pairs cannot create an unlimited transitive grouping chain", delegate
            {
                using (Bitmap first = new Bitmap(32, 32))
                using (Bitmap middle = new Bitmap(32, 32))
                using (Bitmap last = new Bitmap(32, 32))
                {
                    using (Graphics g = Graphics.FromImage(first)) g.Clear(Color.FromArgb(100, 100, 100));
                    using (Graphics g = Graphics.FromImage(middle)) g.Clear(Color.FromArgb(108, 108, 108));
                    using (Graphics g = Graphics.FromImage(last)) g.Clear(Color.FromArgb(116, 116, 116));
                    IconDescriptor a = IconSimilarity.Describe(first), b = IconSimilarity.Describe(middle), c = IconSimilarity.Describe(last);
                    Check(a.Matches(b) && b.Matches(c) && !a.Matches(c), "fixture includes a non-transitive similarity chain");
                    ApplicationGroup[] apps = { SortingApp(203, "A", @"C:\ImageFixture\a.exe", first), SortingApp(204, "B", @"C:\ImageFixture\b.exe", middle), SortingApp(205, "C", @"C:\ImageFixture\c.exe", last) };
                    Check(ApplicationIconGroup.Build(apps).Count == 2 && ApplicationIconGroup.Build(apps.Reverse()).Count == 2, "all distinct images within a bucket must agree");
                    ProcessSelection selection = new ProcessSelection(); selection.Set(apps[0].Members, true);
                    Check(selection.Targets(ApplicationIconGroup.Build(apps).SelectMany(g => g.Members)).Single() == apps[0].Representative, "similarity grouping cannot expand individual selection");
                }
            });
            Case("icon similarity: installed NVIDIA and WeGame variants merge while unrelated logos stay separate", delegate
            {
                // Optional local fixtures are read from installed files; no vendor artwork is bundled in source/releases.
                string nvidia = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string wegame = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "WeGame");
                string[] paths = { Path.Combine(nvidia, @"NVIDIA Corporation\NvContainer\nvcontainer.exe"),
                    Path.Combine(nvidia, @"NVIDIA Corporation\NVIDIA App\NvCpl\nvSmartMaxapp.exe"),
                    Path.Combine(wegame, "wegame.exe"), Path.Combine(wegame, @"apps\df\df_helper_main.exe") };
                if (!paths.All(File.Exists)) { lines.Add("INFO local vendor image fixtures unavailable; synthetic checks remain active"); return; }
                ProcessVisual[] visuals = paths.Select(p => (ProcessVisual)typeof(ProcessVisualCache).GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { p })).ToArray();
                try
                {
                    if (visuals.Any(v => v.Icon == null)) { lines.Add("INFO vendor executable has no extractable icon"); return; }
                    Check(visuals[0].ImageKey != visuals[1].ImageKey && visuals[2].ImageKey != visuals[3].ImageKey, "real fixtures include exact-pixel differences");
                    Check(visuals[0].Descriptor.Matches(visuals[1].Descriptor) && visuals[2].Descriptor.Matches(visuals[3].Descriptor), "actual duplicate-looking NVIDIA and WeGame pairs merge");
                    Check(!visuals[0].Descriptor.Matches(visuals[2].Descriptor), "NVIDIA and WeGame remain different");
                }
                finally { foreach (ProcessVisual visual in visuals) if (visual.Icon != null) visual.Icon.Dispose(); }
            });
        }
    }
}
