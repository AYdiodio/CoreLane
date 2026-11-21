using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace CoreAllocator
{
    // Image buckets are presentation only: never feed them to saved classifications or automation.
    internal sealed class ApplicationIconGroup
    {
        internal readonly List<ApplicationGroup> Applications = new List<ApplicationGroup>();
        internal string ImageKey;
        private string title;
        private readonly List<IconDescriptor> descriptors = new List<IconDescriptor>();
        internal ProcessVisual Visual
        {
            // Members already share the original picture. Prefer an available redraw
            // for that displayed picture without changing membership or selection.
            get { return Applications.Select(a => a.Visual).FirstOrDefault(v => v != null && v.Redrawn != null) ?? Applications[0].Visual; }
        }
        internal string Title { get { return title ?? (Applications[0].Title + (Applications.Count > 1 ? " 等 " + Applications.Count + " 个应用" : "")); } }
        internal List<ProcessRow> Members { get { return ApplicationGroup.Targets(Applications); } }
        internal string InstanceKey { get { return string.Join("|", Members.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal)); } }

        internal static List<ApplicationIconGroup> Build(IEnumerable<ApplicationGroup> applications)
        {
            return Build(applications, new string[0], path => null);
        }

        internal static List<ApplicationIconGroup> Build(IEnumerable<ApplicationGroup> applications, string[] tokens, Func<string, ProcessVisual> visualLookup)
        {
            List<ApplicationGroup> apps = applications.ToList();
            ApplicationIconGroup weChat = BuildWeChatGroup(apps);
            Func<ApplicationGroup, bool> matches = app => app.Matches(tokens, visualLookup);
            bool showWeChat = weChat != null && (weChat.Applications.Any(matches) || tokens.Any(token => weChat.Title.Contains(token)));
            Dictionary<string, ApplicationIconGroup> byImage = new Dictionary<string, ApplicationIconGroup>(StringComparer.Ordinal);
            List<ApplicationIconGroup> groups = new List<ApplicationIconGroup>();
            foreach (ApplicationGroup app in apps)
            {
                if (weChat != null && weChat.Applications.Contains(app))
                {
                    if (showWeChat && !groups.Contains(weChat)) groups.Add(weChat);
                    continue;
                }
                if (!matches(app)) continue;
                string key = app.Visual == null ? "placeholder" : app.Visual.ImageKey;
                ApplicationIconGroup group;
                if (!byImage.TryGetValue(key, out group))
                {
                    IconDescriptor descriptor = app.Visual == null ? null : app.Visual.Descriptor;
                    // Every distinct picture must match, avoiding A~B~C chains that would merge A with unrelated C.
                    group = descriptor == null ? null : groups.FirstOrDefault(g => g.descriptors.Count > 0 && g.descriptors.All(d => d.Matches(descriptor)));
                    if (group == null) { group = new ApplicationIconGroup { ImageKey = key }; groups.Add(group); }
                    if (descriptor != null) group.descriptors.Add(descriptor);
                    byImage.Add(key, group);
                }
                group.Applications.Add(app);
            }
            return groups;
        }

        private static ApplicationIconGroup BuildWeChatGroup(List<ApplicationGroup> apps)
        {
            List<ApplicationGroup> clients = apps.Where(app => IsWeChatExecutable(app, "Weixin.exe", "Weixin", "微信") ||
                IsWeChatExecutable(app, "WeChat.exe", "WeChat", "微信")).ToList();
            List<ApplicationGroup> extensions = apps.Where(app => IsWeChatExecutable(app, "WeChatAppEx.exe", "WeChatAppEx", "WeChatAppEx x64")).ToList();
            if (clients.Count == 0 && extensions.Count == 0) return null;

            // Crashpad has a generic executable name. Only include the copy inside a recognized WeChat installation.
            string[] clientFolders = clients.Select(app => Path.GetDirectoryName(app.Representative.ExecutablePath)).ToArray();
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string sharePlugin = string.IsNullOrEmpty(roaming) ? null : Path.Combine(roaming, @"Tencent\xwechat\xplugin\plugins\WinWeixinShare") + Path.DirectorySeparatorChar;
            var group = new ApplicationIconGroup { ImageKey = "application:wechat", title = "微信" };
            group.Applications.AddRange(clients);
            group.Applications.AddRange(extensions);
            group.Applications.AddRange(apps.Where(app =>
            {
                string path = app.Representative.ExecutablePath;
                if (string.IsNullOrEmpty(path)) return false;
                string file = Path.GetFileName(path);
                return (string.Equals(file, "crashpad_handler.exe", StringComparison.OrdinalIgnoreCase) &&
                        clientFolders.Any(folder => IsWeChatCrashReporter(path, folder))) ||
                    (string.Equals(file, "shareSender.exe", StringComparison.OrdinalIgnoreCase) && sharePlugin != null &&
                        path.StartsWith(sharePlugin, StringComparison.OrdinalIgnoreCase));
            }));
            return group;
        }

        private static bool IsWeChatCrashReporter(string path, string clientFolder)
        {
            if (string.IsNullOrEmpty(clientFolder)) return false;
            string folder = Path.GetDirectoryName(path);
            if (string.Equals(folder, clientFolder, StringComparison.OrdinalIgnoreCase)) return true;
            Version version;
            return string.Equals(Path.GetDirectoryName(folder), clientFolder, StringComparison.OrdinalIgnoreCase) &&
                Version.TryParse(Path.GetFileName(folder), out version);
        }

        private static bool IsWeChatExecutable(ApplicationGroup app, string file, string product, string alternateProduct)
        {
            ProcessVisual visual = app.Visual;
            if (visual == null || string.IsNullOrEmpty(app.Representative.ExecutablePath) ||
                !string.Equals(Path.GetFileName(app.Representative.ExecutablePath), file, StringComparison.OrdinalIgnoreCase)) return false;
            string publisher = (visual.CompanyName ?? "").Trim(), name = (visual.ProductName ?? "").Trim();
            return (string.Equals(publisher, "Tencent", StringComparison.OrdinalIgnoreCase) || publisher.StartsWith("Tencent ", StringComparison.OrdinalIgnoreCase)) &&
                (string.Equals(name, product, StringComparison.OrdinalIgnoreCase) || string.Equals(name, alternateProduct, StringComparison.OrdinalIgnoreCase));
        }

        internal static string Fingerprint(Bitmap image)
        {
            if (image == null) return "placeholder";
            // Compare rendered pixels, not filenames, Bitmap references, metadata or a fuzzy similarity score.
            using (Bitmap normalized = new Bitmap(44, 44, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(normalized))
                {
                    graphics.Clear(Color.Transparent); graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(image, new Rectangle(0, 0, 44, 44));
                }
                Rectangle bounds = new Rectangle(0, 0, 44, 44);
                BitmapData data = normalized.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                byte[] pixels = new byte[44 * 44 * 4];
                try { for (int y = 0; y < 44; y++) Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * 44 * 4, 44 * 4); }
                finally { normalized.UnlockBits(data); }
                // Invisible RGB data must not split otherwise identical transparent images.
                for (int i = 0; i < pixels.Length; i += 4) if (pixels[i + 3] == 0) pixels[i] = pixels[i + 1] = pixels[i + 2] = 0;
                using (SHA256 hash = SHA256.Create()) return "image:" + Convert.ToBase64String(hash.ComputeHash(pixels));
            }
        }
    }

    // Selection belongs to process instances, independently of regrouping, image loading or PID reuse.
    internal sealed class ProcessSelection
    {
        private readonly HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
        internal bool Contains(ProcessRow row) { return keys.Contains(row.Key); }
        internal string[] Snapshot() { return keys.ToArray(); }
        internal void Replace(IEnumerable<string> selected) { keys.Clear(); keys.UnionWith(selected); }
        internal void Set(IEnumerable<ProcessRow> rows, bool selected)
        { foreach (ProcessRow row in rows) { if (selected) keys.Add(row.Key); else keys.Remove(row.Key); } }
        internal void Retain(IEnumerable<ProcessRow> visible) { keys.IntersectWith(visible.Select(p => p.Key)); }
        internal List<ProcessRow> Targets(IEnumerable<ProcessRow> visible)
        { return visible.Where(Contains).GroupBy(p => p.Key).Select(g => g.First()).ToList(); }
        internal CheckState State(IEnumerable<ProcessRow> rows)
        {
            ProcessRow[] members = rows.ToArray(); int selected = members.Count(Contains);
            return selected == 0 ? CheckState.Unchecked : selected == members.Length ? CheckState.Checked : CheckState.Indeterminate;
        }
    }
}
