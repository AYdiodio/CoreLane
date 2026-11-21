using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal sealed partial class MainForm
    {
        private PriorityService priorityService;
        private readonly BackgroundProtection backgroundProtection = new BackgroundProtection();
        private readonly HashSet<string> priorityAttempts = new HashSet<string>();
        private OriginalPriority[] PriorityRecords { get { return priorityService == null ? new OriginalPriority[0] : priorityService.Records; } }
        private int RecoveryCount { get { return service.Store.Items.Length + PriorityRecords.Length + CpuSetRecords.Length; } }

        private void InitializePriorities()
        {
            try { priorityService = new PriorityService(service.Store.DirectoryPath); }
            catch (Exception error)
            {
                Append("优先级功能无法启动：" + Native.Explain(error), Color.Firebrick);
                return;
            }
            ReleaseBackgroundProtection();
        }

        private void SetSelectedPriority()
        {
            if (busy || Interacting || priorityService == null) return;
            ProcessRow[] targets = SelectedProcesses().Where(row => PerformanceProfile.EligiblePath(row.ExecutablePath)).ToArray();
            if (targets.Length == 0)
            {
                Append("请先勾选具有可确认路径的应用。Windows 系统组件不参与优先级设置。", UiTheme.Muted);
                return;
            }
            CloseProcessDetails();
            using (PriorityDialog dialog = new PriorityDialog(targets))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                StopAutomation("手动调整优先级前已暂停自动方案。");
                RunBatch(delegate
                {
                    foreach (ProcessRow row in targets)
                    {
                        OperationResult result = priorityService.Apply(row, dialog.SelectedPriority);
                        if (result.Success && !result.Skipped) retainedAssignments.RememberPriority(row, dialog.SelectedPriority);
                        Record(result, row.Key);
                    }
                });
            }
        }

        private void ManagePerformance(SchemePick selected)
        {
            if (busy || Interacting || schemeStore == null || priorityService == null) return;
            CloseProcessDetails();
            StopAutomation("编辑优先级与后台保护前已暂停方案。保存后可重新启用。");
            RefreshProcesses();
            using (PerformanceDialog dialog = new PerformanceDialog(schemeStore, selected.Id, selected.Name, processes))
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    Append("已保存「" + dialog.SavedName + "」的优先级与后台保护设置。启用该方案后生效。", UiTheme.Blue);
            UpdateState();
        }

        private bool UpdatePerformance(PerformanceProfile profile, List<ProcessRow> rows, bool allowApply)
        {
            if (priorityService == null) return false;
            bool changed = false;
            if (allowApply)
            {
                var rules = profile == null ? new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase) :
                    profile.Rules.Where(rule => rule.Priority != 0).ToDictionary(rule => rule.Path, rule => rule.Priority, StringComparer.OrdinalIgnoreCase);
                var existing = new HashSet<string>(rows.Select(row => row.Key));
                priorityAttempts.IntersectWith(existing);
                var guarded = new HashSet<string>(PriorityRecords.Where(record => record.TemporaryValue != 0).Select(record => record.Key));
                foreach (ProcessRow row in rows)
                {
                    uint desired;
                    if (row.ExecutablePath == null || !rules.TryGetValue(row.ExecutablePath, out desired) ||
                        guarded.Contains(row.Key) || !priorityAttempts.Add(row.Key)) continue;
                    if (row.Priority == desired) continue;
                    OperationResult result = priorityService.Apply(row, desired);
                    if (result.Success) row.Priority = desired;
                    Record(result, row.Key);
                    changed |= result.Success;
                }
            }
            return backgroundProtection.Update(profile, rows, priorityService, Stopwatch.GetTimestamp(), allowApply, Record) || changed;
        }

        private void ReleaseBackgroundProtection(HashSet<string> pendingReset = null)
        {
            if (priorityService == null) return;
            backgroundProtection.Reset();
            foreach (OriginalPriority record in PriorityRecords.Where(record => record.TemporaryValue != 0 &&
                (pendingReset == null || !pendingReset.Contains(record.Key))))
                Record(priorityService.Restore(record), record.Key);
        }

        private string DescribePriorities(ApplicationGroup app)
        {
            var active = new HashSet<string>(PriorityRecords.Where(record => record.TemporaryValue != 0).Select(record => record.Key));
            string levels = string.Join("、", app.Members.GroupBy(row => row.Priority).Select(group =>
                PriorityLevels.Describe(group.Key) + (app.Members.Count > 1 ? " ×" + group.Count() : "")));
            int protectedCount = app.Members.Count(row => active.Contains(row.Key) && row.Priority == PriorityLevels.BelowNormal);
            return "进程优先级：" + levels + (protectedCount == 0 ? "" : " · 后台保护 " + protectedCount + " 个");
        }
    }
}
