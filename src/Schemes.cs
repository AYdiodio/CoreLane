using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace CoreAllocator
{
    [DataContract]
    internal sealed class SavedApplication
    {
        [DataMember] public string Name;
        [DataMember] public List<string> Paths = new List<string>();
        internal bool Matches(ApplicationGroup app) { return app.Members.Any(p => Paths.Contains(p.ExecutablePath, StringComparer.OrdinalIgnoreCase)); }
        internal static SavedApplication From(ApplicationGroup app)
        {
            List<string> paths = app.Members.Select(p => p.ExecutablePath).Where(ValidPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (paths.Count == 0) throw new InvalidOperationException("无法确认这个应用的完整程序路径，不能保存自动规则。");
            return new SavedApplication { Name = app.Title, Paths = paths };
        }
        internal static bool ValidPath(string path)
        {
            try { return !string.IsNullOrWhiteSpace(path) && !path.StartsWith(@"\\", StringComparison.Ordinal) && Path.IsPathRooted(path) &&
                string.Equals(Path.GetFullPath(path), path, StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }
        internal static bool WindowsComponent(string path)
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');
            return !string.IsNullOrEmpty(windows) && !string.IsNullOrEmpty(path) && path.StartsWith(windows + "\\", StringComparison.OrdinalIgnoreCase);
        }
    }
    [DataContract]
    internal sealed class GameClassification
    {
        [DataMember] public SavedApplication Application;
        [DataMember] public bool IsGame;
    }
    [DataContract]
    internal sealed class SchemeRule
    {
        [DataMember] public SavedApplication Application;
        [DataMember] public string MaskHex;
        [DataMember(EmitDefaultValue = false)] public bool UseCpuSets;
        [DataMember(EmitDefaultValue = false)] public bool ExactPaths;
        [DataMember(EmitDefaultValue = false)] public string CpuSetsHex;
        internal ulong Mask { get { return ulong.Parse(MaskHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture); } }
        internal ulong? CpuSets { get { return CpuSetsHex == null ? (ulong?)null : ulong.Parse(CpuSetsHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture); } }
        internal uint CapturedPriority;
    }
    [DataContract]
    internal sealed class SavedScheme
    {
        [DataMember] public string Id;
        [DataMember] public string Name;
        [DataMember] public string TopologyKey;
        [DataMember] public List<SchemeRule> Rules = new List<SchemeRule>();
    }
    [DataContract]
    internal sealed class SchemeData
    {
        [DataMember] public int Version = 1;
        [DataMember] public List<GameClassification> Games = new List<GameClassification>();
        [DataMember] public List<SavedScheme> Schemes = new List<SavedScheme>();
        [DataMember(EmitDefaultValue = false)] public string LastSelectedSchemeId;
        [DataMember(EmitDefaultValue = false)] public SavedScheme GameOverrides;
        [DataMember(EmitDefaultValue = false)] public List<PerformanceProfile> Performance;
        internal PerformanceProfile PerformanceFor(string id) { return Performance == null ? null : Performance.FirstOrDefault(profile => profile.SchemeId == id); }
    }
    internal sealed class SchemeStore
    {
        private readonly string path;
        private SchemeData data;
        internal SchemeData Read() { return Clone(data); }
        internal string StartupSchemeId { get { return data.LastSelectedSchemeId ?? "game"; } }
        internal SchemeStore(string path)
        {
            this.path = path;
            data = new SchemeData();
            if (!File.Exists(path)) return;
            try
            {
                using (FileStream stream = File.OpenRead(path)) data = (SchemeData)new DataContractJsonSerializer(typeof(SchemeData)).ReadObject(stream);
                Validate(data);
            }
            catch (Exception error) { throw new InvalidDataException("无法读取方案文件，自动功能已停止；请保留文件：" + path + "\r\n" + error.Message, error); }
        }
        internal static string Fingerprint(Topology topology)
        {
            return (topology.CpuName ?? "") + "|" + string.Join(";", topology.Groups.Select(g => g.Number + ":" + g.Mask.ToString("X16"))) + "|" +
                string.Join(";", topology.Cores.Select(c => c.Group + ":" + c.Mask.ToString("X16"))) + "|" +
                string.Join(";", topology.Clusters.Select(c => c.Group + ":" + c.Mask.ToString("X16") + ":" + c.CacheBytes));
        }
        internal void Update(Action<SchemeData> change)
        {
            SchemeData next = Clone(data);
            change(next);
            if (HasRules(next, rule => rule.CpuSetsHex != null)) next.Version = 5;
            else if (next.Version < 4 && ContainsExactRules(next)) next.Version = 4;
            else if (next.Performance != null && next.Version < 3) next.Version = 3;
            else if (next.GameOverrides != null && next.Version < 2) next.Version = 2;
            Validate(next);
            SnapshotStore.SaveAtomically(path, next);
            data = next;
        }
        internal void Classify(ApplicationGroup app, bool game)
        {
            SavedApplication saved = SavedApplication.From(app);
            Update(delegate(SchemeData next)
            {
                next.Games.RemoveAll(g => g.Application.Paths.Intersect(saved.Paths, StringComparer.OrdinalIgnoreCase).Any());
                next.Games.Add(new GameClassification { Application = saved, IsGame = game });
            });
        }
        internal void SelectScheme(string id)
        {
            if (id != "game" && !data.Schemes.Any(s => s.Id == id)) throw new InvalidOperationException("找不到要记住的方案。");
            if (data.LastSelectedSchemeId == id) return;
            Update(delegate(SchemeData next) { next.LastSelectedSchemeId = id; });
        }
        internal void DeleteScheme(string id)
        {
            Update(delegate(SchemeData next)
            {
                if (id == "game") { next.GameOverrides = null; return; }
                next.Schemes.RemoveAll(s => s.Id == id);
                if (next.Performance != null) next.Performance.RemoveAll(profile => profile.SchemeId == id);
                if (next.LastSelectedSchemeId == id) next.LastSelectedSchemeId = "game";
            });
        }
        private static SchemeData Clone(SchemeData value)
        {
            using (MemoryStream stream = new MemoryStream())
            { DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(SchemeData)); serializer.WriteObject(stream, value); stream.Position = 0; return (SchemeData)serializer.ReadObject(stream); }
        }
        private static void Validate(SchemeData value)
        {
            if (value == null || value.Version < 1 || value.Version > 5 || value.Games == null || value.Schemes == null ||
                value.Version == 1 && value.GameOverrides != null || value.Version < 3 && value.Performance != null)
                throw new InvalidDataException("方案版本或内容无效。");
            if (value.LastSelectedSchemeId != null && (string.IsNullOrWhiteSpace(value.LastSelectedSchemeId) || value.LastSelectedSchemeId.Length > 256))
                throw new InvalidDataException("上次选择的方案编号无效。");
            HashSet<string> classified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameClassification game in value.Games)
            {
                if (game == null) throw new InvalidDataException("分类记录无效。");
                ValidateApplication(game.Application);
                if (game.Application.Paths.Any(p => !classified.Add(p))) throw new InvalidDataException("同一程序存在重复分类。");
            }
            if (value.GameOverrides != null)
            {
                if (value.GameOverrides.Id != "game" || value.GameOverrides.Name != "游戏" || string.IsNullOrEmpty(value.GameOverrides.TopologyKey) || value.GameOverrides.Rules == null)
                    throw new InvalidDataException("游戏方案的手动规则无效。");
                ValidateRules(value.GameOverrides);
            }
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase), names = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
            foreach (SavedScheme scheme in value.Schemes)
            {
                if (scheme == null || string.IsNullOrWhiteSpace(scheme.Id) || scheme.Id == "game" || !ids.Add(scheme.Id) || string.IsNullOrWhiteSpace(scheme.Name) ||
                    scheme.Name == "游戏" || scheme.Name.Length > 40 || !names.Add(scheme.Name) || string.IsNullOrEmpty(scheme.TopologyKey) || scheme.Rules == null)
                    throw new InvalidDataException("方案名称、编号或规则无效；名称不能重复或使用“游戏”。");
                ValidateRules(scheme);
            }
            if (value.Performance != null)
            {
                var profiles = new HashSet<string>(StringComparer.Ordinal);
                foreach (PerformanceProfile profile in value.Performance)
                {
                    if (profile == null || !profiles.Add(profile.SchemeId) ||
                        (profile.SchemeId != "game" && !value.Schemes.Any(s => s.Id == profile.SchemeId)))
                        throw new InvalidDataException("优先级与后台保护的方案编号无效。");
                    profile.Validate();
                }
            }
            if (value.Version < 4 && ContainsExactRules(value)) throw new InvalidDataException("方案版本不支持按路径分配或 CPU 集。");
            if (value.Version < 5 && HasRules(value, rule => rule.CpuSetsHex != null)) throw new InvalidDataException("方案版本不支持同时保存硬亲和性和默认 CPU 集。");
        }
        private static bool ContainsExactRules(SchemeData data)
        {
            return HasRules(data, rule => rule.ExactPaths || rule.UseCpuSets);
        }
        private static bool HasRules(SchemeData data, Func<SchemeRule, bool> predicate)
        {
            return data.Schemes.Concat(new[] { data.GameOverrides }).Any(scheme => scheme != null && scheme.Rules != null &&
                scheme.Rules.Any(rule => rule != null && predicate(rule)));
        }
        private static void ValidateRules(SavedScheme scheme)
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SchemeRule rule in scheme.Rules)
            {
                if (rule == null || rule.Mask == 0 && !rule.CpuSets.HasValue) throw new InvalidDataException("方案 CPU 范围无效。");
                ValidateApplication(rule.Application);
                if (rule.UseCpuSets && !rule.ExactPaths) throw new InvalidDataException("CPU 集规则必须匹配已确认的完整路径。");
                if (rule.CpuSets.HasValue && (!rule.ExactPaths || rule.UseCpuSets || rule.Mask != 0 && (rule.CpuSets.Value & ~rule.Mask) != 0))
                    throw new InvalidDataException("方案中的默认 CPU 集与硬亲和性不兼容。");
                if (rule.Application.Paths.Any(p => !paths.Add(p))) throw new InvalidDataException("同一方案内不能重复设置一个程序。");
            }
        }
        private static void ValidateApplication(SavedApplication app)
        {
            if (app == null || string.IsNullOrWhiteSpace(app.Name) || app.Paths == null || app.Paths.Count == 0 || app.Paths.Any(p => !SavedApplication.ValidPath(p)) ||
                app.Paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != app.Paths.Count) throw new InvalidDataException("应用规则缺少有效的本地 EXE 路径。");
        }
    }
    internal sealed class AutomaticTarget
    {
        internal ProcessRow Process;
        internal ulong Mask;
        internal bool UseCpuSets;
        internal ulong? CpuSets;
    }
    // Pure planning: every actual write still passes through AffinityService's owner/creation/permission checks.
    internal sealed class AutomationSession
    {
        private readonly HashSet<string> baseline;
        private readonly Dictionary<string, OperationResult> attempted = new Dictionary<string, OperationResult>();
        private readonly HashSet<string> deferred = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal readonly string SchemeId;
        internal AutomationSession(string id, IEnumerable<ProcessRow> existing) { SchemeId = id; baseline = new HashSet<string>(existing.Select(p => p.Key)); }
        internal List<AutomaticTarget> Plan(IList<ApplicationGroup> apps, SchemeData data, Topology topology, bool includeAttempted = false)
        {
            if (!topology.CanAssign) throw new InvalidOperationException("当前处理器组不支持自动分配。");
            List<AutomaticTarget> targets = new List<AutomaticTarget>();
            SavedScheme scheme = SchemeId == "game" ? data.GameOverrides : data.Schemes.FirstOrDefault(s => s.Id == SchemeId);
            if (SchemeId != "game" && (scheme == null || scheme.TopologyKey != SchemeStore.Fingerprint(topology)))
                throw new InvalidOperationException("方案不存在或 CPU 拓扑已变化，请在“方案”中重新核对并保存规则。");
            if (SchemeId == "game" && scheme != null && scheme.Rules.Count > 0 && scheme.TopologyKey != SchemeStore.Fingerprint(topology))
                throw new InvalidOperationException("游戏方案的手动规则 CPU 拓扑已变化，请先在管理方案中核对。");
            foreach (ApplicationGroup app in apps)
            {
                List<SchemeRule> matches = scheme == null ? new List<SchemeRule>() : scheme.Rules.Where(r => r.Application.Matches(app)).ToList();
                ulong? fallback = null;
                foreach (ProcessRow row in app.Members)
                {
                    SchemeRule exact = matches.FirstOrDefault(rule => rule.ExactPaths && rule.Application.Paths.Contains(row.ExecutablePath, StringComparer.OrdinalIgnoreCase));
                    if (exact == null && !fallback.HasValue) fallback = GroupTarget(matches, app, data, topology);
                    ulong mask = exact == null ? fallback.Value : exact.Mask;
                    ulong? cpuSets = exact == null ? null : exact.CpuSets;
                    if (mask == 0 && !cpuSets.HasValue) continue;
                    if (mask != 0) Masks.Validate(mask, topology.Available);
                    if (cpuSets.GetValueOrDefault() != 0) Masks.Validate(cpuSets.Value, topology.Available);
                    AutomaticTarget target = new AutomaticTarget { Process = row, Mask = mask, UseCpuSets = exact != null && exact.UseCpuSets, CpuSets = cpuSets };
                    if (includeAttempted || !attempted.ContainsKey(AttemptKey(target))) targets.Add(target);
                }
            }
            return targets.GroupBy(t => t.Process.Key).Select(g => g.First()).ToList();
        }
        private ulong GroupTarget(List<SchemeRule> matches, ApplicationGroup app, SchemeData data, Topology topology)
        {
            ulong[] masks = matches.Where(rule => !rule.ExactPaths).Select(rule => rule.Mask).Distinct().ToArray();
            if (masks.Length > 1) throw new InvalidOperationException("此应用匹配到不同 CPU 规则，请在管理方案中核对。");
            if (masks.Length == 1) return masks[0];
            if (SchemeId != "game" || app.Members.Any(p => SavedApplication.WindowsComponent(p.ExecutablePath))) return 0;
            bool[] classes = data.Games.Where(g => g.Application.Matches(app)).Select(g => g.IsGame).Distinct().ToArray();
            if (classes.Length > 1) throw new InvalidOperationException("应用分类冲突，请在“方案”中移除旧分类后重新确认。");
            if (classes.Length == 0) return 0;
            CpuSelectionGroup group = classes[0] ? CpuSelectionGroup.GameTarget(topology) : CpuSelectionGroup.NonGameTarget(topology);
            return group == null ? 0 : group.Mask;
        }
        private static string AttemptKey(AutomaticTarget target) { return target.Process.Key + (target.UseCpuSets ? ":set:" : ":") + target.Mask.ToString("X16") + ":" + target.CpuSets; }
        internal void MarkAttempted(AutomaticTarget target, OperationResult result = null) { attempted[AttemptKey(target)] = result; }
        internal string ExecutionStatus(AutomaticTarget target)
        {
            OperationResult result;
            attempted.TryGetValue(AttemptKey(target), out result);
            if (result != null && result.Skipped) return "已跳过";
            if (result != null && !result.Success) return result.Win32Error == 5 ? "访问被拒绝" : "执行失败";
            ulong? current = target.UseCpuSets ? target.Process.CpuSetMask :
                (target.Process.ReadError == null && target.Process.Mask != 0 ? (ulong?)target.Process.Mask : null);
            if (target.Mask != 0 && !current.HasValue || target.CpuSets.HasValue && !target.Process.CpuSetMask.HasValue) return "无法读取当前范围";
            if (target.UseCpuSets && (target.Process.ReadError != null || target.Process.Mask == 0)) return "无法读取硬亲和性";
            bool matches = (target.Mask == 0 || current == target.Mask) && (!target.UseCpuSets || (target.Mask & ~target.Process.Mask) == 0) &&
                (!target.CpuSets.HasValue || target.CpuSets == target.Process.CpuSetMask && (target.CpuSets.Value & ~target.Process.Mask) == 0);
            if (result == null) return matches ? "当前已符合目标" : "待执行";
            return matches ? "已生效" : "已偏离目标";
        }
        internal OperationResult ExecutionResult(AutomaticTarget target)
        {
            OperationResult result;
            attempted.TryGetValue(AttemptKey(target), out result);
            return result;
        }
        internal ApplicationGroup NextQuestion(IList<ApplicationGroup> apps, SchemeData data, Func<ProcessRow, bool> hasWindow)
        {
            if (SchemeId != "game") return null;
            return apps.FirstOrDefault(app => app.Members.All(p => SavedApplication.ValidPath(p.ExecutablePath)) &&
                (data.GameOverrides == null || !data.GameOverrides.Rules.Any(r => r.Application.Matches(app))) &&
                !app.Members.Any(p => SavedApplication.WindowsComponent(p.ExecutablePath)) &&
                !app.Members.Any(p => deferred.Contains(p.ExecutablePath)) && !data.Games.Any(g => g.Application.Matches(app)) &&
                app.Members.Any(p => !baseline.Contains(p.Key) && hasWindow(p)));
        }
        internal void Defer(ApplicationGroup app) { foreach (ProcessRow row in app.Members) if (row.ExecutablePath != null) deferred.Add(row.ExecutablePath); }
    }
}
