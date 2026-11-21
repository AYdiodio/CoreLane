using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace CoreAllocator
{
    [DataContract]
    internal sealed class RetainedRule
    {
        [DataMember] public string Path;
        [DataMember(EmitDefaultValue = false)] public string AffinityHex;
        [DataMember(EmitDefaultValue = false)] public string CpuSetsHex;
        [DataMember(EmitDefaultValue = false)] public uint Priority;
        internal bool Empty { get { return AffinityHex == null && CpuSetsHex == null && Priority == 0; } }
        internal RetainedRule Copy() { return (RetainedRule)MemberwiseClone(); }
        internal static ulong Mask(string value) { return ulong.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture); }
    }

    [DataContract]
    internal sealed class RetainedData
    {
        [DataMember] public string TopologyKey;
        [DataMember] public List<RetainedRule> Rules;
    }

    internal sealed class RetainedAssignments
    {
        private readonly string file;
        private readonly Topology topology;
        private readonly Dictionary<string, RetainedRule> draft = new Dictionary<string, RetainedRule>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, RetainedRule> saved = new Dictionary<string, RetainedRule>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> attempted = new HashSet<string>();
        internal bool Active { get; private set; }
        internal bool HasRules { get { return draft.Count > 0; } }

        internal RetainedAssignments(string directory, Topology topology)
        { file = Path.Combine(directory, "retained-assignments.json"); this.topology = topology; }

        internal void Load()
        {
            if (!File.Exists(file)) return;
            RetainedData data;
            using (FileStream stream = File.OpenRead(file))
                data = (RetainedData)new DataContractJsonSerializer(typeof(RetainedData)).ReadObject(stream);
            if (data == null || data.Rules == null) throw new InvalidDataException("保留的分配记录无效。");
            if (data.Rules.Count == 0) return;
            if (!topology.CanAssign || data.TopologyKey != SchemeStore.Fingerprint(topology))
                throw new InvalidDataException("CPU 拓扑已变化，已暂停保留的分配。");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (RetainedRule rule in data.Rules)
            {
                if (rule == null || rule.Empty || !SavedApplication.ValidPath(rule.Path) || !paths.Add(rule.Path) ||
                    (rule.Priority != 0 && (!PriorityLevels.Selectable(rule.Priority) || !PerformanceProfile.EligiblePath(rule.Path))) ||
                    (rule.CpuSetsHex != null && !PerformanceProfile.EligiblePath(rule.Path)))
                    throw new InvalidDataException("保留的分配记录包含无效规则。");
                if (rule.AffinityHex != null) Masks.Validate(RetainedRule.Mask(rule.AffinityHex), topology.Available);
                if (rule.CpuSetsHex != null) Masks.Validate(RetainedRule.Mask(rule.CpuSetsHex), topology.Available);
            }
            foreach (RetainedRule rule in data.Rules) { saved.Add(rule.Path, rule); draft.Add(rule.Path, rule.Copy()); }
            Active = true;
        }

        internal void RememberCpu(ProcessRow row, ulong mask, bool useCpuSets)
        {
            if (!SavedApplication.ValidPath(row.ExecutablePath)) return;
            RetainedRule rule = RuleFor(row);
            if (useCpuSets) rule.CpuSetsHex = mask.ToString("X16");
            else rule.AffinityHex = mask.ToString("X16");
        }

        internal void RememberPriority(ProcessRow row, uint priority)
        {
            if (PerformanceProfile.EligiblePath(row.ExecutablePath)) RuleFor(row).Priority = priority;
        }

        private RetainedRule RuleFor(ProcessRow row)
        {
            RetainedRule rule;
            if (!draft.TryGetValue(row.ExecutablePath, out rule))
                draft.Add(row.ExecutablePath, rule = new RetainedRule { Path = row.ExecutablePath });
            attempted.Add(row.Key);
            return rule;
        }

        internal bool Apply(List<ProcessRow> rows, AffinityService affinity, CpuSetService cpuSets, PriorityService priorities, Action<OperationResult, string> record)
        {
            attempted.IntersectWith(rows.Select(row => row.Key));
            bool changed = false;
            foreach (ProcessRow row in rows)
            {
                RetainedRule rule;
                if (row.ExecutablePath == null || !draft.TryGetValue(row.ExecutablePath, out rule) || !attempted.Add(row.Key)) continue;
                if (rule.AffinityHex != null) { record(affinity.Apply(row, RetainedRule.Mask(rule.AffinityHex)), row.Key); changed = true; }
                if (rule.CpuSetsHex != null && cpuSets != null) { record(cpuSets.Apply(row, RetainedRule.Mask(rule.CpuSetsHex)), row.Key); changed = true; }
                if (rule.Priority != 0 && priorities != null) { record(priorities.Apply(row, rule.Priority), row.Key); changed = true; }
            }
            return changed;
        }

        internal void Save()
        {
            if (!HasRules) return;
            Write(draft.Values);
            ReplaceSaved();
        }
        internal void Pause() { Active = false; }

        internal void Clear()
        {
            if (File.Exists(file)) Write(new RetainedRule[0]);
            saved.Clear(); draft.Clear(); attempted.Clear(); Active = false;
        }

        internal void Forget(string path)
        {
            if (saved.ContainsKey(path))
            {
                Write(saved.Where(item => !string.Equals(item.Key, path, StringComparison.OrdinalIgnoreCase)).Select(item => item.Value));
                saved.Remove(path);
            }
            draft.Remove(path);
            if (!HasRules) Active = false;
        }

        private void ReplaceSaved()
        {
            saved.Clear();
            foreach (RetainedRule rule in draft.Values) saved.Add(rule.Path, rule.Copy());
        }

        private void Write(IEnumerable<RetainedRule> rules)
        { SnapshotStore.SaveAtomically(file, new RetainedData { TopologyKey = SchemeStore.Fingerprint(topology), Rules = rules.ToList() }); }
    }
}
