using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        private static ApplicationGroup SortingApp(int id, string title, string path, Bitmap icon)
        {
            ProcessRow row = new ProcessRow { Pid = id, Created = id * 10, Name = Path.GetFileName(path ?? "unknown.exe"), ExecutablePath = path, Mask = 3 };
            ApplicationGroup app = new ApplicationGroup { Representative = row, Title = title, Visual = icon == null ? null : new ProcessVisual { Icon = icon } };
            app.Members.Add(row); return app;
        }
        private static void ApplicationOrderingTests()
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            Case("app order: every real icon precedes every placeholder before category and name", delegate
            {
                using (Bitmap icon = new Bitmap(2, 2))
                {
                    ApplicationGroup gameIcon = SortingApp(1, "Zulu game", @"C:\SortFixture\Game1.exe", icon);
                    ApplicationGroup gameFallback = SortingApp(2, "Alpha game", @"C:\SortFixture\Game2.exe", null);
                    ApplicationGroup normalIcon = SortingApp(3, "Zulu normal", @"C:\SortFixture\App1.exe", icon);
                    ApplicationGroup normalFallback = SortingApp(4, "Alpha normal", @"C:\SortFixture\App2.exe", null);
                    ApplicationGroup systemIcon = SortingApp(5, "Alpha system", Path.Combine(windows, "System32", "SortFixture1.exe"), icon);
                    ApplicationGroup systemFallback = SortingApp(6, "Zulu system", Path.Combine(windows, "System32", "SortFixture2.exe"), null);
                    GameClassification[] games = { new GameClassification { Application = SavedApplication.From(gameIcon), IsGame = true }, new GameClassification { Application = SavedApplication.From(gameFallback), IsGame = true } };
                    List<ApplicationGroup> sorted = ApplicationOrdering.Sort(new[] { systemIcon, normalFallback, gameFallback, systemFallback, normalIcon, gameIcon }, games);
                    Check(sorted.SequenceEqual(new[] { gameIcon, normalIcon, systemIcon, gameFallback, normalFallback, systemFallback }), "real system icon precedes normal and game placeholders; category applies within each icon section");
                }
            });
            Case("app order: only saved positive games qualify and conflicting or false classifications do not", delegate
            {
                ApplicationGroup app = SortingApp(7, "Game in name", @"C:\SortFixture\Game3.exe", null);
                Check(ApplicationOrdering.Category(app, new GameClassification[0]) == 1, "no filename game guessing");
                GameClassification yes = new GameClassification { Application = SavedApplication.From(app), IsGame = true };
                GameClassification no = new GameClassification { Application = SavedApplication.From(app), IsGame = false };
                Check(ApplicationOrdering.Category(app, new[] { yes }) == 0, "saved classification works independently of active scheme");
                Check(ApplicationOrdering.Category(app, new[] { no }) == 1 && ApplicationOrdering.Category(app, new[] { yes, no }) == 1, "negative and conflicting classification not promoted");
                ApplicationGroup system = SortingApp(8, "System game", Path.Combine(windows, "System32", "SortFixture3.exe"), null);
                Check(ApplicationOrdering.Category(system, new[] { new GameClassification { Application = SavedApplication.From(system), IsGame = true } }) == 2, "stale system misclassification cannot promote OS component");
            });
            Case("app order: system classification respects path boundaries and keeps ordinary Microsoft software normal", delegate
            {
                Check(ApplicationOrdering.IsSystemPath(Path.Combine(windows, "System32", "SortFixture.exe")), "actual Windows folder");
                Check(!ApplicationOrdering.IsSystemPath(windows + "Fake\\System32\\SortFixture.exe"), "directory prefix is not containment");
                Check(!ApplicationOrdering.IsSystemPath(Path.Combine(windows, "..", "SortFixture", "app.exe")), "non-canonical path not mistaken for Windows component");
                Check(!ApplicationOrdering.IsSystemPath(@"C:\SortFixture\svchost.exe") && !ApplicationOrdering.IsSystemPath(null), "name alone and unreadable path not called system");
                Check(ApplicationOrdering.IsSystemPath(Path.Combine(programFiles, "Windows Defender", "SortFixture.exe")), "Windows security component");
                Check(ApplicationOrdering.IsSystemPath(Path.Combine(programFiles, "WindowsApps", "MicrosoftWindows.Client.WebExperience_1.0_x64__fixture", "app.exe")), "recognized OS package family");
                Check(!ApplicationOrdering.IsSystemPath(Path.Combine(programFiles, "WindowsApps", "Microsoft.WindowsCalculator_1.0_x64__fixture", "app.exe")), "WindowsApps alone does not make user app a system process");
                Check(!ApplicationOrdering.IsSystemPath(Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe")), "Microsoft publisher/product alone not enough");
            });
            Case("app order: equal names use stable path and process identities regardless of input sequence", delegate
            {
                ApplicationGroup a = SortingApp(11, "Same app", @"C:\SortFixture\A\app.exe", null);
                ApplicationGroup b = SortingApp(10, "Same app", @"C:\SortFixture\B\app.exe", null);
                ApplicationGroup c = SortingApp(12, "Same app", a.Representative.ExecutablePath, null);
                ApplicationGroup[] expected = { a, c, b };
                Check(ApplicationOrdering.Sort(new[] { b, c, a }, new GameClassification[0]).SequenceEqual(expected), "path then PID tie-break");
                Check(ApplicationOrdering.Sort(expected.Reverse(), new GameClassification[0]).SequenceEqual(expected), "refresh input order cannot reshuffle equal names");
                Check(ApplicationOrdering.Sort(new ApplicationGroup[0], new GameClassification[0]).Count == 0, "empty search result allowed");
            });
            Case("app order: reordering retains exact grouped members and prior selection without automatic writes", delegate
            {
                using (Bitmap icon = new Bitmap(2, 2))
                {
                    ApplicationGroup selected = SortingApp(13, "Zulu app", @"C:\SortFixture\Selected.exe", null);
                    selected.Members.Add(new ProcessRow { Pid = 14, Created = 140, Name = "Selected.exe", ExecutablePath = selected.Representative.ExecutablePath, Mask = 4 });
                    ApplicationGroup other = SortingApp(15, "Alpha app", @"C:\SortFixture\Other.exe", icon);
                    HashSet<string> keys = new HashSet<string>(selected.Members.Select(p => p.Key));
                    List<ApplicationGroup> sorted = ApplicationOrdering.Sort(new[] { selected, other }, new GameClassification[0]);
                    Check(sorted[0] == other && sorted.Single(app => app.WasSelected(keys)) == selected, "selected application survives ordering shift");
                    Check(ApplicationGroup.Targets(sorted.Where(app => app.WasSelected(keys))).Select(p => p.Key).SequenceEqual(selected.Members.Select(p => p.Key)), "exact original target identities retained");
                    Check(selected.Members[0].Mask == 3 && selected.Members[1].Mask == 4, "display ordering does not mutate affinity snapshot");
                }
            });
        }
    }
}
