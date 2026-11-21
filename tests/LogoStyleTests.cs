using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static void LogoStyleTests()
        {
            Case("redrawn logos: embedded atlas contains eight distinct transparent usable marks", delegate
            {
                using (RoundedLogoPack pack = new RoundedLogoPack())
                {
                    Check(pack.Count == 8, "all assets embedded and decoded");
                    HashSet<string> fingerprints = new HashSet<string>();
                    foreach (string brand in RoundedLogoPack.Brands)
                    {
                        ProcessVisual logo = pack.Get(brand); Bitmap icon = logo.Icon;
                        Check(icon.GetPixel(0, 0).A == 0 && icon.GetPixel(icon.Width - 1, icon.Height - 1).A == 0, "transparent atlas cell edges");
                        int opaque = 0, transparent = 0;
                        for (int y = 0; y < icon.Height; y += 4) for (int x = 0; x < icon.Width; x += 4)
                        { int alpha = icon.GetPixel(x, y).A; if (alpha > 200) opaque++; if (alpha == 0) transparent++; }
                        Check(opaque > 200 && transparent > 200, "logo and transparent gutters both present: " + brand);
                        Check(fingerprints.Add(logo.ImageKey), "each logo is distinct");
                    }
                }
            });
            Case("redrawn logos: executable plus product/company match; hosts and unknown names keep originals", delegate
            {
                string[] files = { "Discord.exe", "steam.exe", "Weixin.exe", "msedge.exe", "nvcontainer.exe", "firefox.exe", "OneDrive.exe", "Spotify.exe" };
                string[] companies = { "Discord Inc.", "Valve Corporation", "Tencent", "Microsoft Corporation", "NVIDIA Corporation", "Mozilla Corporation", "Microsoft Corporation", "Spotify Ltd" };
                string[] products = { "Discord", "Steam", "Weixin", "Microsoft Edge", "NVIDIA Container", "Firefox", "Microsoft OneDrive", "Spotify" };
                for (int i = 0; i < files.Length; i++)
                    Check(RoundedLogoPack.BrandKey(@"C:\Fixture\" + files[i], new ProcessVisual { CompanyName = companies[i], ProductName = products[i] }) == RoundedLogoPack.Brands[i], "known brand " + files[i]);
                Check(RoundedLogoPack.BrandKey(@"C:\Fixture\Discord.exe", new ProcessVisual { ProductName = "Discord", CompanyName = "Unrelated" }) == null, "filename alone does not select a logo");
                Check(RoundedLogoPack.BrandKey(@"C:\Fixture\msedgewebview2.exe", new ProcessVisual { CompanyName = "Microsoft Corporation", ProductName = "Microsoft Edge WebView2" }) == null, "embedded WebView2 is not Edge");
                Check(RoundedLogoPack.BrandKey(@"C:\Fixture\QQ.exe", new ProcessVisual { CompanyName = "Tencent", ProductName = "QQ" }) == null, "Tencent does not imply WeChat");
                Check(RoundedLogoPack.BrandKey(@"C:\Fixture\unknown.exe", new ProcessVisual()) == null, "unknown product stays original");
            });
            Case("redrawn logos: presentation cannot change source fingerprints, grouping or exact selected targets", delegate
            {
                using (RoundedLogoPack pack = new RoundedLogoPack())
                using (Bitmap original = new Bitmap(24, 24))
                {
                    using (Graphics graphics = Graphics.FromImage(original)) graphics.Clear(Color.Red);
                    ApplicationGroup first = SortingApp(681, "First", @"C:\Fixture\First.exe", original);
                    ApplicationGroup second = SortingApp(682, "Second", @"C:\Fixture\Second.exe", original);
                    string key = first.Visual.ImageKey;
                    first.Visual.Redrawn = pack.Get("discord"); second.Visual.Redrawn = pack.Get("steam");
                    ProcessSelection selected = new ProcessSelection(); selected.Set(first.Members, true);
                    using (Bitmap rendered = new Bitmap(64, 64)) using (Graphics graphics = Graphics.FromImage(rendered))
                    { graphics.Clear(Color.Transparent); UiTheme.DrawAppIcon(graphics, first.Visual, new RectangleF(5, 5, 54, 54), true); Check(rendered.GetPixel(32, 32).B > 100, "custom artwork is actually rendered"); }
                    List<ApplicationIconGroup> groups = ApplicationIconGroup.Build(new[] { first, second });
                    Check(first.Visual.ImageKey == key && second.Visual.ImageKey == key && groups.Count == 1, "display assets never replace original image identity");
                    Check(selected.Targets(groups[0].Members).Single().Pid == 681, "a redraw cannot add operation targets");
                    first.Visual.Redrawn = null;
                    Check(object.ReferenceEquals(groups[0].Visual, second.Visual), "an original-picture bucket can use a verified sibling redraw without regrouping");
                    Check(pack.ForExecutable(@"C:\Fixture\Discord.exe", new ProcessVisual { CompanyName = "Discord", ProductName = "Discord" }) == null, "unavailable original icon retains placeholder behavior");
                }
            });
        }
    }
}
