using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static void StartupAutomationTests(string working)
        {
            Case("startup scheme: legacy data defaults to game and selected custom choice survives reload", delegate
            {
                string path = Path.Combine(working, "startup-choice.json");
                File.WriteAllText(path, "{\"Version\":1,\"Games\":[],\"Schemes\":[]}");
                SchemeStore store = new SchemeStore(path);
                Check(store.StartupSchemeId == "game", "older files have an explicit first-start default");
                store.Update(delegate(SchemeData data) { data.Schemes.Add(new SavedScheme { Id = "startup-custom", Name = "启动测试", TopologyKey = "fixture" }); });
                store.SelectScheme("startup-custom");
                Check(new SchemeStore(path).StartupSchemeId == "startup-custom", "last selection is durable");
                string before = File.ReadAllText(path);
                Throws(delegate { store.SelectScheme("missing"); });
                Check(File.ReadAllText(path) == before && store.StartupSchemeId == "startup-custom", "invalid selection leaves disk and memory unchanged");
                store.Classify(ApplicationGroup.Build(new[] { new ProcessRow { Pid = 77, Created = 7, Name = "fixture.exe", ExecutablePath = @"C:\StartupFixture\fixture.exe" } }, delegate { return null; }).Single(), false);
                Check(new SchemeStore(path).StartupSchemeId == "startup-custom", "saving classifications preserves startup preference");
            });
            Case("startup scheme: deleting an unrelated scheme preserves choice; deleting selected resets to game", delegate
            {
                SchemeStore store = new SchemeStore(Path.Combine(working, "startup-deletion.json"));
                store.Update(delegate(SchemeData data)
                {
                    data.Schemes.Add(new SavedScheme { Id = "first", Name = "方案一", TopologyKey = "fixture" });
                    data.Schemes.Add(new SavedScheme { Id = "second", Name = "方案二", TopologyKey = "fixture" });
                });
                store.SelectScheme("first"); store.DeleteScheme("second");
                Check(store.StartupSchemeId == "first", "unrelated deletion preserves last choice");
                store.DeleteScheme("first");
                Check(new SchemeStore(Path.Combine(working, "startup-deletion.json")).StartupSchemeId == "game", "deleted selected scheme cannot be resumed");
            });
            Case("game question: topmost, identified icon and missing-icon fallback preserve safe later default", delegate
            {
                using (Bitmap icon = new Bitmap(32, 32))
                {
                    using (Graphics graphics = Graphics.FromImage(icon)) graphics.Clear(Color.CornflowerBlue);
                    ProcessVisual visual = new ProcessVisual { Icon = icon };
                    ApplicationGroup app = new ApplicationGroup { Title = "弹窗图标测试", Visual = visual };
                    app.Members.Add(new ProcessRow { Pid = 79, Created = 9, Name = "fixture.exe", ExecutablePath = @"C:\StartupFixture\fixture.exe" });
                    CpuSelectionGroup target = CpuSelectionGroup.FromTopology(Topology.Parse(Fixture(false)))[0];
                    using (GameQuestionDialog dialog = new GameQuestionDialog(app, target, false))
                    {
                        Check(dialog.TopMost && !dialog.ShowInTaskbar, "only the question is topmost, without a second taskbar entry");
                        ApplicationIconBadge badge = ChildControls(dialog).OfType<ApplicationIconBadge>().Single();
                        Check(object.ReferenceEquals(badge.Visual, visual) && !badge.UnifiedIcons, "corresponding visual and original/unified display setting passed through");
                        Check(((Control)dialog.CancelButton).Text == "稍后" && dialog.DialogResult == DialogResult.None, "no automatic yes or classification");
                        using (Bitmap rendered = new Bitmap(dialog.Width, dialog.Height)) dialog.DrawToBitmap(rendered, dialog.ClientRectangle);
                    }
                    Check(icon.GetPixel(16, 16).ToArgb() == Color.CornflowerBlue.ToArgb(), "dialog does not dispose borrowed source image");
                    app.Visual = null;
                    using (GameQuestionDialog fallback = new GameQuestionDialog(app, target))
                    using (Bitmap rendered = new Bitmap(fallback.Width, fallback.Height)) fallback.DrawToBitmap(rendered, fallback.ClientRectangle);
                }
            });
        }
        private static object StartupField(MainForm form, string name)
        { return typeof(MainForm).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form); }
        private static object StartupInvoke(MainForm form, string method, params object[] args)
        { return typeof(MainForm).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, args); }
        private static void LiveStartupAutomationTest(Topology topology, string working)
        {
            Case("live startup: saved schemes stay inactive until explicit activation, invalid topology does not write", delegate
            {
                string directory = Path.Combine(working, "startup-live"); Directory.CreateDirectory(directory);
                string childPath = Path.Combine(directory, "startup-fixture.exe");
                File.Copy(typeof(SelfTests).Assembly.Location, childPath);
                string configuration = typeof(SelfTests).Assembly.Location + ".config";
                if (File.Exists(configuration)) File.Copy(configuration, childPath + ".config");
                using (Process child = Process.Start(new ProcessStartInfo { FileName = childPath, Arguments = "--test-worker", UseShellExecute = false, CreateNoWindow = true }))
                {
                    ulong original = Read(child.Id);
                    ulong target = 1UL << Masks.Bits(topology.Available).First();
                    SchemeStore choices = new SchemeStore(Path.Combine(directory, "schemes.json"));
                    choices.Update(delegate(SchemeData data)
                    {
                        SavedScheme scheme = new SavedScheme { Id = "startup-live", Name = "只操作自建进程", TopologyKey = SchemeStore.Fingerprint(topology) };
                        scheme.Rules.Add(new SchemeRule { Application = new SavedApplication { Name = "自建测试进程", Paths = new System.Collections.Generic.List<string> { childPath } }, MaskHex = target.ToString("X16") });
                        data.Schemes.Add(scheme);
                    });
                    choices.SelectScheme("startup-live");
                    AffinityService service = new AffinityService(topology, new SnapshotStore(Path.Combine(directory, "original-affinity.json")));
                    try
                    {
                        using (MainForm form = new MainForm(service))
                        {
                            StartupInvoke(form, "InitializeAutomation");
                            Check(StartupField(form, "automation") == null && Read(child.Id) == original && service.Store.Items.Length == 0, "startup displays applications without applying the persisted scheme");
                            StartupInvoke(form, "ActivateScheme", "startup-live");
                            Check(((AutomationSession)StartupField(form, "automation")).SchemeId == "startup-live", "explicit activation uses persisted rules");
                            Check(Read(child.Id) == target && service.Store.Items.All(o => o.Pid == child.Id), "only unique copied fixture executable targeted");
                            StartupInvoke(form, "StopAutomation", "测试暂停");
                            Check(StartupField(form, "automation") == null && Read(child.Id) == target, "pause stops processing and preserves current affinity");
                            Check(new SchemeStore(Path.Combine(directory, "schemes.json")).StartupSchemeId == "startup-live", "pause does not erase next-start choice");
                        }
                        using (MainForm restart = new MainForm(service))
                        {
                            StartupInvoke(restart, "InitializeAutomation");
                            Check(StartupField(restart, "automation") == null && Read(child.Id) == target, "restart leaves scheme stopped and preserves existing assignment");
                            StartupInvoke(restart, "Restore", true);
                            Check(StartupField(restart, "automation") == null && Read(child.Id) == topology.Available && service.Store.Items.Length == 0, "restore pauses and opens the full Windows CPU mask");
                        }
                        choices.Update(delegate(SchemeData data) { data.Schemes[0].TopologyKey = "changed topology"; });
                        using (MainForm invalid = new MainForm(service))
                        {
                            StartupInvoke(invalid, "InitializeAutomation");
                            StartupInvoke(invalid, "ActivateScheme", "startup-live");
                            Check(StartupField(invalid, "automation") == null && Read(child.Id) == topology.Available && service.Store.Items.Length == 0, "changed topology prevents startup writes");
                        }
                        choices.Update(delegate(SchemeData data) { data.LastSelectedSchemeId = "removed-outside-app"; });
                        using (MainForm missing = new MainForm(service))
                        {
                            StartupInvoke(missing, "InitializeAutomation");
                            StartupInvoke(missing, "ActivateScheme", "removed-outside-app");
                            Check(StartupField(missing, "automation") == null && Read(child.Id) == topology.Available && service.Store.Items.Length == 0, "missing saved scheme is not silently replaced with game");
                        }
                    }
                    finally
                    {
                        foreach (OriginalAffinity saved in service.Store.Items.ToArray()) service.Restore(saved);
                        if (!child.HasExited) { child.Kill(); child.WaitForExit(); }
                    }
                }
            });
        }
    }
}
