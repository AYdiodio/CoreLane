using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoreAllocator
{
    // Presentation only. This never changes application membership or operation targets.
    internal static class ApplicationOrdering
    {
        internal static int Category(ApplicationGroup app, IEnumerable<GameClassification> classifications)
        {
            if (app.Members.Count > 0 && app.Members.All(p => IsSystemPath(p.ExecutablePath))) return 2;
            GameClassification[] matches = classifications.Where(c => c.Application.Matches(app)).ToArray();
            return matches.Any(c => c.IsGame) && !matches.Any(c => !c.IsGame) ? 0 : 1;
        }
        internal static string CategoryName(int category) { return category == 0 ? "已标记游戏" : category == 2 ? "系统程序" : "普通应用"; }
        internal static List<ApplicationGroup> Sort(IEnumerable<ApplicationGroup> apps, IEnumerable<GameClassification> classifications)
        {
            GameClassification[] saved = classifications.ToArray();
            return apps.OrderBy(app => app.Visual != null && app.Visual.Icon != null ? 0 : 1)
                .ThenBy(app => Category(app, saved))
                .ThenBy(app => app.Title, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(app => app.Representative.ExecutablePath ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(app => app.Representative.Pid).ThenBy(app => app.Representative.Created).ToList();
        }
        internal static bool IsSystemPath(string path)
        {
            if (!SavedApplication.ValidPath(path)) return false;
            if (SavedApplication.WindowsComponent(path)) return true;
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (string.IsNullOrEmpty(programFiles)) return false;
            if (Inside(path, Path.Combine(programFiles, "Windows Defender")) ||
                Inside(path, Path.Combine(programFiles, "Windows Defender Advanced Threat Protection"))) return true;
            string packages = Path.Combine(programFiles, "WindowsApps");
            if (!Inside(path, packages)) return false;
            string relative = path.Substring(packages.Length + 1);
            int separator = relative.IndexOf('\\');
            if (separator < 0) return false;
            string package = relative.Substring(0, separator);
            // OS package families only; Microsoft publishers and WindowsApps location alone are not sufficient.
            return package.StartsWith("MicrosoftWindows.Client.", StringComparison.OrdinalIgnoreCase) ||
                package.StartsWith("Microsoft.Windows.", StringComparison.OrdinalIgnoreCase) ||
                package.StartsWith("Microsoft.WidgetsPlatformRuntime_", StringComparison.OrdinalIgnoreCase) ||
                package.StartsWith("Microsoft.SecHealthUI_", StringComparison.OrdinalIgnoreCase);
        }
        private static bool Inside(string path, string directory) { return path.StartsWith(directory.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase); }
    }
}
