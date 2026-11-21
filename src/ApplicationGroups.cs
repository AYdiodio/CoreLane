using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoreAllocator
{
    // Grouping is only a presentation layer. Every operation still validates each original process instance.
    internal sealed class ApplicationGroup
    {
        internal readonly List<ProcessRow> Members = new List<ProcessRow>();
        internal ProcessRow Representative;
        internal ProcessVisual Visual;
        internal string Title;
        private string directory;
        private string product;
        private string company;

        internal static List<ApplicationGroup> Build(IEnumerable<ProcessRow> processes, Func<string, ProcessVisual> visualLookup)
        {
            List<ApplicationGroup> exact = new List<ApplicationGroup>();
            foreach (IGrouping<string, ProcessRow> bucket in processes.GroupBy(Identity, StringComparer.OrdinalIgnoreCase))
            {
                ProcessRow first = bucket.OrderBy(p => p.Pid).First();
                ProcessVisual visual = visualLookup(first.ExecutablePath);
                string product = visual == null ? "" : Clean(visual.ProductName);
                string display = visual == null ? "" : Clean(visual.DisplayName);
                string directory = DirectoryOf(first.ExecutablePath);
                ApplicationGroup app = new ApplicationGroup { Representative = first, Visual = visual,
                    Title = product.Length > 0 && !SharedFolder(directory) ? product : display.Length > 0 ? display : Path.GetFileNameWithoutExtension(first.Name),
                    directory = directory, product = product,
                    company = visual == null ? "" : Clean(visual.CompanyName) };
                app.Members.AddRange(bucket);
                exact.Add(app);
            }
            List<ApplicationGroup> result = new List<ApplicationGroup>();
            // Only merge distinct executables when product family, publisher and an actual install folder agree.
            // No grouping solely by a display name, publisher, drive root or shared Windows directory.
            foreach (ApplicationGroup app in exact.OrderBy(a => a.directory.Length).ThenBy(a => a.directory, StringComparer.OrdinalIgnoreCase))
            {
                ApplicationGroup owner = result.FirstOrDefault(a => CanMerge(a, app));
                if (owner == null) result.Add(app);
                else
                {
                    owner.Members.AddRange(app.Members);
                    if ((owner.Visual == null || owner.Visual.Icon == null) && app.Visual != null && app.Visual.Icon != null)
                        owner.Visual = app.Visual;
                }
            }
            return result.OrderBy(a => a.Title, StringComparer.CurrentCultureIgnoreCase).ThenBy(a => a.directory, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string Clean(string value) { return (value ?? "").Trim(); }
        private static string Identity(ProcessRow row) { return string.IsNullOrWhiteSpace(row.ExecutablePath) ? "instance:" + row.Key : "path:" + row.ExecutablePath; }
        private static string DirectoryOf(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            try { return Path.GetDirectoryName(path) ?? ""; }
            catch (ArgumentException) { return ""; }
        }
        private static bool Inside(string path, string root)
        {
            if (string.IsNullOrEmpty(root)) return false;
            root = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(path, root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        private static bool SharedFolder(string directory)
        {
            if (directory.Length == 0 || string.Equals(directory, Path.GetPathRoot(directory), StringComparison.OrdinalIgnoreCase)) return true;
            string[] protectedRoots = { Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86), Path.GetTempPath() };
            if (protectedRoots.Any(root => Inside(directory, root))) return true;
            string[] broadRoots = { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
            return broadRoots.Any(root => string.Equals(directory.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
        }
        private static bool CanMerge(ApplicationGroup owner, ApplicationGroup child)
        {
            bool sameProduct = string.Equals(owner.product, child.product, StringComparison.OrdinalIgnoreCase);
            // A nested helper can use a longer product name, e.g. "Example System Helper" under "Example".
            bool nestedProduct = !string.Equals(owner.directory, child.directory, StringComparison.OrdinalIgnoreCase) &&
                child.product.StartsWith(owner.product + " ", StringComparison.OrdinalIgnoreCase);
            return owner.product.Length > 0 && owner.company.Length > 0 &&
                (sameProduct || nestedProduct) &&
                string.Equals(owner.company, child.company, StringComparison.OrdinalIgnoreCase) &&
                !SharedFolder(owner.directory) && !SharedFolder(child.directory) && Inside(child.directory, owner.directory);
        }

        internal bool Matches(string[] tokens, Func<string, ProcessVisual> visualLookup)
        {
            return tokens.Length == 0 || tokens.Any(token => Title.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0 ||
                Members.Any(p => p.Name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0 || p.Pid.ToString().Contains(token) ||
                    (visualLookup(p.ExecutablePath) == null ? "" : visualLookup(p.ExecutablePath).DisplayName ?? "").IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0));
        }
        internal bool WasSelected(ISet<string> selectedInstances) { return Members.Count > 0 && Members.All(p => selectedInstances.Contains(p.Key)); }
        internal string Allocation(IList<CpuSelectionGroup> groups)
        {
            int failed = Members.Count(p => p.ReadError != null);
            if (failed == Members.Count) return "无法读取";
            if (failed > 0) return "部分无法读取";
            return Members.Select(p => p.Mask).Distinct().Count() == 1 ? CpuSelectionGroup.Describe(Members[0].Mask, groups) : "多种分配";
        }
        internal string AllocationDetails()
        {
            return string.Join(Environment.NewLine, Members.GroupBy(p => p.ReadError == null ? "CPU " + Masks.Format(p.Mask) : "无法读取：" + p.ReadError)
                .Select(g => g.Key + " · " + g.Count() + " 个进程"));
        }
        internal string ResultText(IDictionary<string, OperationResult> results)
        {
            List<OperationResult> known = Members.Where(p => results.ContainsKey(p.Key)).Select(p => results[p.Key]).ToList();
            if (known.Count == 0) return Members.Any(p => p.ReadError != null) ? "部分进程无法读取" : "未修改";
            int success = known.Count(r => r.Success && !r.Skipped), skipped = known.Count(r => r.Skipped), failed = known.Count(r => !r.Success && !r.Skipped);
            int pending = Members.Count - known.Count;
            if (failed > 0) return (success > 0 ? "部分失败" : "失败") + " · " + failed + " 个" + (success == 0 ? "" : "，成功 " + success + " 个") + (pending == 0 ? "" : "，未操作 " + pending + " 个");
            if (skipped > 0) return "跳过 " + skipped + " 个" + (success == 0 ? "" : "，成功 " + success + " 个") + (pending == 0 ? "" : "，未操作 " + pending + " 个");
            return pending == 0 ? "成功 · " + success + " 个进程" : "部分完成 · 成功 " + success + " 个，未操作 " + pending + " 个";
        }
        internal static List<ProcessRow> Targets(IEnumerable<ApplicationGroup> apps)
        {
            return apps.SelectMany(a => a.Members).GroupBy(p => p.Key).Select(g => g.First()).ToList();
        }
    }
}
