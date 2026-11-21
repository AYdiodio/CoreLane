using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace CoreAllocator
{
    [DataContract]
    internal sealed class PerformanceRule
    {
        [DataMember] public string Path;
        [DataMember] public uint Priority;
        [DataMember] public bool Trigger;
        [DataMember] public bool Background;
    }

    [DataContract]
    internal sealed class PerformanceProfile
    {
        [DataMember] public string SchemeId;
        [DataMember] public List<PerformanceRule> Rules = new List<PerformanceRule>();
        [DataMember] public bool GuardEnabled;
        [DataMember] public int Threshold = 80;
        [DataMember] public int Duration = 8;

        internal static bool EligiblePath(string path)
        {
            return SavedApplication.ValidPath(path) && !SavedApplication.WindowsComponent(path);
        }

        internal void Validate()
        {
            if (Threshold < 10 || Threshold > 100 || Duration < 5 || Duration > 60 || Rules == null)
                throw new InvalidDataException("后台保护的阈值或持续时间无效。");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PerformanceRule rule in Rules)
                if (rule == null || !EligiblePath(rule.Path) || !paths.Add(rule.Path) ||
                    rule.Priority != 0 && !PriorityLevels.Selectable(rule.Priority) || rule.Trigger && rule.Background)
                    throw new InvalidDataException("优先级规则无效。触发应用和后台目标须使用不同的本地 EXE。");
            if (GuardEnabled && (!Rules.Any(rule => rule.Trigger) || !Rules.Any(rule => rule.Background)))
                throw new InvalidDataException("开启后台保护前，请选择至少一个触发应用和一个后台目标。");
        }
    }

    internal sealed class BackgroundProtection
    {
        private sealed class Sample
        {
            internal long At, CpuTicks;
            internal double HighSeconds, LowSeconds;
            internal long RetryAfter;
        }

        private readonly Dictionary<string, Sample> samples = new Dictionary<string, Sample>();

        internal void Reset() { samples.Clear(); }

        internal bool Update(PerformanceProfile profile, IList<ProcessRow> rows, PriorityService priority,
            long now, bool allowLower, Action<OperationResult, string> report)
        {
            var active = new Dictionary<string, ProcessRow>();
            if (profile != null && profile.GuardEnabled)
            {
                var triggerPaths = new HashSet<string>(profile.Rules.Where(rule => rule.Trigger).Select(rule => rule.Path), StringComparer.OrdinalIgnoreCase);
                if (rows.Any(row => triggerPaths.Contains(row.ExecutablePath)))
                {
                    var targetPaths = new HashSet<string>(profile.Rules.Where(rule => rule.Background).Select(rule => rule.Path), StringComparer.OrdinalIgnoreCase);
                    foreach (ProcessRow row in rows.Where(row => targetPaths.Contains(row.ExecutablePath))) active[row.Key] = row;
                }
            }

            var guarded = priority.Records.Where(record => record.TemporaryValue != 0).ToDictionary(record => record.Key);
            bool changed = false;
            foreach (string key in samples.Keys.Where(key => !active.ContainsKey(key) && !guarded.ContainsKey(key)).ToArray()) samples.Remove(key);
            foreach (var item in guarded)
            {
                if (active.ContainsKey(item.Key)) continue;
                Sample sample = StateFor(item.Key);
                changed |= Restore(priority, item.Value, sample, now, report);
            }
            foreach (ProcessRow row in active.Values)
            {
                Sample sample = StateFor(row.Key);
                double seconds = (row.SampleTime - sample.At) / (double)Stopwatch.Frequency;
                long cpuDelta = row.CpuTicks - sample.CpuTicks;
                bool usable = sample.At != 0 && seconds > 0 && seconds <= 8 && cpuDelta >= 0;
                sample.At = row.SampleTime;
                sample.CpuTicks = row.CpuTicks;
                double load = usable ? cpuDelta / (seconds * TimeSpan.TicksPerSecond) * 100 : -1;
                sample.HighSeconds = allowLower && load >= profile.Threshold ? sample.HighSeconds + seconds : 0;
                sample.LowSeconds = usable && load < profile.Threshold / 2.0 ? sample.LowSeconds + seconds : 0;

                OriginalPriority record;
                if (guarded.TryGetValue(row.Key, out record))
                {
                    if (sample.LowSeconds >= profile.Duration || row.Priority != 0 && row.Priority != record.TemporaryValue)
                        changed |= Restore(priority, record, sample, now, report);
                    continue;
                }
                if (!allowLower || now < sample.RetryAfter || sample.HighSeconds < profile.Duration ||
                    row.Priority != PriorityLevels.Normal && row.Priority != PriorityLevels.AboveNormal) continue;
                OperationResult result = priority.Apply(row, PriorityLevels.BelowNormal, temporary: true);
                sample.RetryAfter = result.Success ? 0 : now + 15 * Stopwatch.Frequency;
                sample.HighSeconds = 0;
                report(result, row.Key);
                changed |= result.Success;
            }
            return changed;
        }

        private Sample StateFor(string key)
        {
            Sample sample;
            if (!samples.TryGetValue(key, out sample)) samples[key] = sample = new Sample();
            return sample;
        }

        private static bool Restore(PriorityService priority, OriginalPriority record, Sample sample, long now,
            Action<OperationResult, string> report)
        {
            if (now < sample.RetryAfter) return false;
            OperationResult result = priority.Restore(record);
            report(result, record.Key);
            sample.RetryAfter = now + 15 * Stopwatch.Frequency;
            sample.HighSeconds = sample.LowSeconds = 0;
            return result.Success;
        }
    }
}
