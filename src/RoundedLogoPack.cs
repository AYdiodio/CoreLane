using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace CoreAllocator
{
    // Presentation assets only. Never use these keys to group apps or plan affinity rules.
    internal sealed class RoundedLogoPack : IDisposable
    {
        internal const string ResourceName = "CoreAllocator.Assets.RoundedLogos.png";
        internal static readonly string[] Brands = { "discord", "steam", "wechat", "edge", "nvidia", "firefox", "onedrive", "spotify" };
        private readonly object gate = new object();
        private readonly Dictionary<string, ProcessVisual> pictures = new Dictionary<string, ProcessVisual>(StringComparer.Ordinal);
        internal int Count { get { lock (gate) return pictures.Count; } }
        internal RoundedLogoPack()
        {
            try
            {
                using (Stream stream = typeof(RoundedLogoPack).Assembly.GetManifestResourceStream(ResourceName))
                {
                    if (stream == null) return;
                    using (Bitmap atlas = new Bitmap(stream))
                    {
                        if (atlas.Width < 128 || atlas.Height < 64) return;
                        for (int i = 0; i < Brands.Length; i++)
                        {
                            int column = i % 4, row = i / 4;
                            Rectangle cell = Rectangle.FromLTRB(column * atlas.Width / 4, row * atlas.Height / 2,
                                (column + 1) * atlas.Width / 4, (row + 1) * atlas.Height / 2);
                            pictures.Add(Brands[i], new ProcessVisual { Icon = atlas.Clone(cell, PixelFormat.Format32bppArgb) });
                        }
                    }
                }
            }
            catch (ArgumentException) { Dispose(); }
            catch (IOException) { Dispose(); }
            catch (ExternalException) { Dispose(); }
        }
        internal ProcessVisual Get(string brand)
        {
            if (brand == null) return null;
            lock (gate) { ProcessVisual picture; return pictures.TryGetValue(brand, out picture) ? picture : null; }
        }
        internal ProcessVisual ForExecutable(string path, ProcessVisual original)
        { return original == null || original.Icon == null ? null : Get(BrandKey(path, original)); }
        internal static string BrandKey(string path, ProcessVisual metadata)
        {
            if (metadata == null || string.IsNullOrEmpty(path)) return null;
            string file;
            try { file = Path.GetFileName(path).ToLowerInvariant(); }
            catch (ArgumentException) { return null; }
            string company = (metadata.CompanyName ?? "").ToLowerInvariant();
            string product = (metadata.ProductName ?? "").ToLowerInvariant();
            if ((file == "discord.exe" || file == "discordptb.exe" || file == "discordcanary.exe") && company.Contains("discord") && product.Contains("discord")) return "discord";
            if ((file == "steam.exe" || file == "steamwebhelper.exe") && company.Contains("valve") && product.Contains("steam")) return "steam";
            if ((file == "wechat.exe" || file == "weixin.exe" || file == "wechatappex.exe") && company.Contains("tencent") &&
                (product.Contains("wechat") || product.Contains("weixin") || product.Contains("微信"))) return "wechat";
            // WebView2 belongs to many different applications: it must not become Edge.
            if (file == "msedge.exe" && company.Contains("microsoft") && product.Contains("edge")) return "edge";
            if ((file == "nvcontainer.exe" || file == "nvsmartmaxapp.exe" || file == "nvidia share.exe" || file == "nvidia overlay.exe" || file == "nvbackend.exe") &&
                company.Contains("nvidia") && product.Contains("nvidia")) return "nvidia";
            if (file == "firefox.exe" && company.Contains("mozilla") && product.Contains("firefox")) return "firefox";
            if (file == "onedrive.exe" && company.Contains("microsoft") && product.Contains("onedrive")) return "onedrive";
            if (file == "spotify.exe" && company.Contains("spotify") && product.Contains("spotify")) return "spotify";
            return null;
        }
        public void Dispose()
        {
            lock (gate) { foreach (ProcessVisual picture in pictures.Values) picture.Icon.Dispose(); pictures.Clear(); }
        }
    }
}
