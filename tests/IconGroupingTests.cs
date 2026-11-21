using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static void IconGroupingTests()
        {
            Case("WeChat tile: client and known helpers share one icon across installations and search", delegate
            {
                string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                using (Bitmap clientIcon = new Bitmap(32, 32))
                using (Bitmap pluginIcon = new Bitmap(32, 32))
                using (Bitmap crashIcon = new Bitmap(32, 32))
                {
                    using (Graphics graphics = Graphics.FromImage(clientIcon)) graphics.Clear(Color.Green);
                    using (Graphics graphics = Graphics.FromImage(pluginIcon)) graphics.Clear(Color.Blue);
                    using (Graphics graphics = Graphics.FromImage(crashIcon)) graphics.Clear(Color.Red);
                    ApplicationGroup client = SortingApp(121, "Weixin", @"D:\Apps\Weixin\Weixin.exe", clientIcon);
                    client.Visual.CompanyName = "Tencent"; client.Visual.ProductName = "Weixin";
                    ApplicationGroup plugin = SortingApp(122, "WeChatAppEx x64", Path.Combine(roaming, @"Tencent\xwechat\xplugin\plugins\RadiumWMPF\WeChatAppEx.exe"), pluginIcon);
                    plugin.Visual.CompanyName = "Tencent LLC"; plugin.Visual.ProductName = "WeChatAppEx x64";
                    ApplicationGroup reporter = SortingApp(123, "crashpad_handler", @"D:\Apps\Weixin\4.2.0.13\crashpad_handler.exe", crashIcon);
                    ApplicationGroup share = SortingApp(124, "shareSender", Path.Combine(roaming, @"Tencent\xwechat\xplugin\plugins\WinWeixinShare\2007\extracted\shareSender.exe"), null);
                    ApplicationGroup other = SortingApp(125, "Other crashpad", @"D:\Apps\WeixinTools\4.2.0.13\crashpad_handler.exe", crashIcon);
                    ApplicationGroup copy = SortingApp(126, "Unrelated WeChat", @"D:\Apps\Unrelated\WeChat.exe", clientIcon);
                    copy.Visual.CompanyName = "Unrelated"; copy.Visual.ProductName = "WeChat";
                    ApplicationGroup[] apps = { reporter, client, plugin, share, other, copy };
                    List<ApplicationIconGroup> icons = ApplicationIconGroup.Build(apps);
                    ApplicationIconGroup weChat = icons.Single(icon => icon.Title == "微信");
                    Check(icons.Count == 3 && weChat.Members.Select(p => p.Pid).OrderBy(pid => pid).SequenceEqual(new[] { 121, 122, 123, 124 }),
                        "only confirmed WeChat paths appear under the tile, even when unrelated processes reuse an image");
                    Check(object.ReferenceEquals(weChat.Visual, client.Visual) && client.Members.Count == 1,
                        "the client supplies the icon and the source application groups are preserved");
                    Check(ApplicationIconGroup.Build(apps, new[] { "微信" }, path => null).Single().InstanceKey == weChat.InstanceKey &&
                        ApplicationIconGroup.Build(apps, new[] { "WeChatAppEx" }, path => null).Single().InstanceKey == weChat.InstanceKey,
                        "searching by the title or a helper returns the complete tile");
                    ProcessSelection selection = new ProcessSelection(); selection.Set(new[] { client.Representative }, true);
                    Check(selection.State(weChat.Members) == CheckState.Indeterminate && selection.Targets(weChat.Members).Single() == client.Representative,
                        "grouping never expands an existing selection");
                }
            });
            Case("icon buckets: equal pixels across different images and unrelated software merge, different pixels stay separate", delegate
            {
                using (Bitmap first = new Bitmap(32, 32))
                using (Bitmap copy = new Bitmap(32, 32))
                using (Bitmap other = new Bitmap(32, 32))
                {
                    using (Graphics g = Graphics.FromImage(first)) g.Clear(Color.Red);
                    using (Graphics g = Graphics.FromImage(copy)) g.Clear(Color.Red);
                    using (Graphics g = Graphics.FromImage(other)) g.Clear(Color.Blue);
                    ApplicationGroup a = SortingApp(101, "One", @"C:\IconFixture\One\app.exe", first);
                    ApplicationGroup b = SortingApp(102, "Two", @"D:\IconFixture\Two\helper.exe", copy);
                    ApplicationGroup c = SortingApp(103, "Three", @"C:\IconFixture\Three\app.exe", other);
                    List<ApplicationIconGroup> buckets = ApplicationIconGroup.Build(new[] { a, b, c });
                    Check(buckets.Count == 2 && buckets[0].Applications.SequenceEqual(new[] { a, b }), "same image, independent executable paths");
                    Check(buckets[0].Members.Count == 2 && buckets[1].Members.Single() == c.Members[0], "membership retained");
                    Check(a.Members.Count == 1 && b.Members.Count == 1 && a.Title == "One", "underlying automatic-rule application groups untouched");
                }
            });
            Case("icon buckets: shared placeholder collapses and valid image remains separate", delegate
            {
                using (Bitmap blank = new Bitmap(32, 32))
                {
                    ApplicationGroup a = SortingApp(104, "A", @"C:\IconFixture\a.exe", null);
                    ApplicationGroup b = SortingApp(105, "B", @"C:\IconFixture\b.exe", null);
                    b.Visual = new ProcessVisual();
                    ApplicationGroup c = SortingApp(106, "C", @"C:\IconFixture\c.exe", blank);
                    List<ApplicationIconGroup> buckets = ApplicationIconGroup.Build(new[] { c, a, b });
                    Check(buckets.Count == 2 && buckets[0].Members.Single() == c.Members[0] && buckets[1].Members.Count == 2, "all fallback CPU pictures share a bucket, not an extracted transparent image");
                }
            });
            Case("icon selection: outside selects whole bucket; inside selects individual process without leaking to other apps", delegate
            {
                ApplicationGroup a = SortingApp(107, "A", @"C:\IconFixture\a.exe", null);
                a.Members.Add(new ProcessRow { Pid = 108, Created = 1080, Name = "a.exe", ExecutablePath = a.Representative.ExecutablePath, Mask = 8 });
                ApplicationGroup b = SortingApp(109, "B", @"C:\IconFixture\b.exe", null);
                ApplicationIconGroup bucket = ApplicationIconGroup.Build(new[] { a, b }).Single();
                ProcessSelection selection = new ProcessSelection();
                selection.Set(new[] { a.Members[1] }, true);
                Check(selection.State(bucket.Members) == CheckState.Indeterminate && selection.Targets(bucket.Members).Single() == a.Members[1], "individual child only");
                selection.Set(bucket.Members, true);
                Check(selection.State(bucket.Members) == CheckState.Checked && selection.Targets(bucket.Members).Count == 3, "outer select all");
                selection.Set(new[] { b.Members[0] }, false);
                Check(selection.State(bucket.Members) == CheckState.Indeterminate && selection.Targets(bucket.Members).Count == 2, "individual uncheck preserves siblings");
                selection.Set(bucket.Members, false);
                Check(selection.State(bucket.Members) == CheckState.Unchecked, "outer deselect all");
            });
            Case("icon selection: refresh, new children, filtering and PID reuse never enlarge a selection", delegate
            {
                ApplicationGroup a = SortingApp(110, "A", @"C:\IconFixture\a.exe", null);
                ProcessRow original = a.Members[0];
                ProcessRow added = new ProcessRow { Pid = 111, Created = 1110, Name = "a.exe", ExecutablePath = original.ExecutablePath };
                ProcessRow reused = new ProcessRow { Pid = original.Pid, Created = original.Created + 1, Name = original.Name, ExecutablePath = original.ExecutablePath };
                ProcessSelection selection = new ProcessSelection(); selection.Set(a.Members, true);
                selection.Retain(new[] { original, added });
                Check(selection.Targets(new[] { original, added }).Single() == original, "new member unselected");
                Check(selection.State(new[] { original, added }) == CheckState.Indeterminate, "partial retained after regroup");
                Check(selection.Targets(new[] { original, original }).Count == 1, "deduplicated actual operation targets");
                selection.Retain(new[] { reused, added });
                Check(selection.Targets(new[] { reused, added }).Count == 0, "PID reuse cannot inherit choice");
                selection.Set(new[] { reused }, true); selection.Retain(new ProcessRow[0]);
                Check(selection.Targets(new[] { reused }).Count == 0, "filtered-out choices cleared");
            });
            Case("icon selection: asynchronous image loading changes presentation without losing or broadening selected instances", delegate
            {
                using (Bitmap first = new Bitmap(32, 32))
                using (Bitmap second = new Bitmap(32, 32))
                {
                    using (Graphics g = Graphics.FromImage(first)) g.Clear(Color.Red);
                    using (Graphics g = Graphics.FromImage(second)) g.Clear(Color.Blue);
                    ApplicationGroup a = SortingApp(112, "A", @"C:\IconFixture\a.exe", null);
                    ApplicationGroup b = SortingApp(113, "B", @"C:\IconFixture\b.exe", null);
                    ProcessSelection selection = new ProcessSelection(); selection.Set(a.Members, true);
                    Check(ApplicationIconGroup.Build(new[] { a, b }).Count == 1, "both pending images use placeholder");
                    a.Visual = new ProcessVisual { Icon = first }; b.Visual = new ProcessVisual { Icon = second };
                    List<ApplicationIconGroup> separated = ApplicationIconGroup.Build(new[] { a, b });
                    Check(separated.Count == 2 && selection.State(separated[0].Members) == CheckState.Checked && selection.State(separated[1].Members) == CheckState.Unchecked, "image split preserves exact choice");
                    b.Visual.Icon = first;
                    ApplicationIconGroup joined = ApplicationIconGroup.Build(new[] { a, b }).Single();
                    Check(selection.State(joined.Members) == CheckState.Indeterminate && selection.Targets(joined.Members).Single() == a.Members[0], "image merge cannot choose other app");
                }
            });
        }
    }
}
