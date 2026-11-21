using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal sealed partial class MainForm
    {
        private readonly Timer automationTimer = new Timer { Interval = 2500 };
        private SchemeStore schemeStore;
        private AutomationSession automation;
        private bool automationTicking;
        private string toolbarSchemeId;
        private sealed class SchemePick
        {
            internal string Id, Name;
            public override string ToString() { return Name; }
        }
        private string activeSchemeName;
        private CpuSelectionGroup GameCpuTarget { get { return CpuSelectionGroup.GameTarget(service.Topology); } }
        private CpuSelectionGroup NonGameCpuTarget { get { return CpuSelectionGroup.NonGameTarget(service.Topology); } }
        private string NonGameCaption { get { CpuSelectionGroup target = NonGameCpuTarget; return target == null ? "本机没有可用的 CPU 组 2，非游戏只记住分类、不分配" : "非游戏 → " + target.Name + "（CPU " + Masks.Format(target.Mask) + "）"; } }
        private string AutomationCaption { get { return automation != null ? "自动方案：" + activeSchemeName : retainedAssignments.Active ? "保留的分配正在执行" : "自动方案已暂停"; } }

        private IEnumerable<SchemePick> SchemeChoices()
        {
            if (schemeStore == null) yield break;
            yield return new SchemePick { Id = "game", Name = "游戏" };
            foreach (SavedScheme scheme in schemeStore.Read().Schemes)
                yield return new SchemePick { Id = scheme.Id, Name = scheme.Name };
        }

        private void ShowAutomationMenu()
        {
            if (!TrayCommandsAvailable || schemeStore == null) return;
            while (automationMenu.Items.Count > 0)
            {
                ToolStripItem previous = automationMenu.Items[0];
                automationMenu.Items.RemoveAt(0);
                previous.Dispose();
            }
            List<SchemePick> choices = SchemeChoices().ToList();
            if (!choices.Any(choice => choice.Id == toolbarSchemeId))
                toolbarSchemeId = choices.Any(choice => choice.Id == schemeStore.StartupSchemeId) ? schemeStore.StartupSchemeId : "game";
            automationMenu.Items.Add(new GlyphMenuItem("自动方案", UiGlyph.ChevronLeft, delegate { BeginInvoke((Action)delegate { ShowToolbarMenu(moreMenu); }); }));
            automationMenu.Items.Add(new CapsuleMenuSeparator());
            foreach (SchemePick choice in choices)
            {
                var item = new GlyphMenuItem(choice.Name.Replace("&", "&&"), choice.Id == "game" ? UiGlyph.Sparkles : UiGlyph.Adjust, delegate
                {
                    if (!TrayCommandsAvailable) return;
                    if (toolbarSchemeId != choice.Id && automation != null)
                        StopAutomation("已暂停原方案，点击启用后执行所选方案。");
                    toolbarSchemeId = choice.Id;
                    BeginInvoke((Action)ShowAutomationMenu);
                }) { Checked = choice.Id == toolbarSchemeId };
                automationMenu.Items.Add(item);
            }
            automationMenu.Items.Add(new CapsuleMenuSeparator());
            automationMenu.Items.Add(new GlyphMenuItem("新建方案", UiGlyph.Plus, delegate
            {
                OpenSaveManualScheme(true);
                BeginInvoke((Action)ShowAutomationMenu);
            }));
            automationMenu.Items.Add(new GlyphMenuItem("保存方案…", UiGlyph.Check, delegate
            {
                OpenSaveSessionScheme();
                BeginInvoke((Action)ShowAutomationMenu);
            }) { Enabled = sessionManualRules.Count > 0 });
            if (retainedAssignments.Active)
                automationMenu.Items.Add(new GlyphMenuItem("暂停保留的分配", UiGlyph.Pause, delegate { PauseRetainedAssignments(); BeginInvoke((Action)ShowAutomationMenu); }));
            bool active = automation != null;
            ToolStripItem toggle = new GlyphMenuItem(active ? "暂停方案" : "启用方案", active ? UiGlyph.Pause : UiGlyph.Play, delegate
            {
                if (!TrayCommandsAvailable) return;
                if (automation != null) StopAutomation("已暂停方案，当前 CPU 分配保留。");
                else ActivateScheme(toolbarSchemeId);
                BeginInvoke((Action)ShowAutomationMenu);
            });
            automationMenu.Items.Add(toggle);
            toggle.Tag = active ? ButtonTone.Pause : ButtonTone.Start;
            toggle.Enabled = active || service.Topology.CanAssign;
            automationMenu.Items.Add(new GlyphMenuItem("管理方案与游戏分类…", UiGlyph.Adjust, delegate { ManageSchemes(); }));
            ShowToolbarMenu(automationMenu);
        }

        private void InitializeAutomation()
        {
            automationTimer.Tick += delegate { AutomationTick(); };
            automationTimer.Start();
            try
            {
                schemeStore = new SchemeStore(Path.Combine(service.Store.DirectoryPath, "schemes.json"));
            }
            catch (Exception error) { Append("方案功能未启用：" + Native.Explain(error) + "；手动分配与恢复仍可使用。", Color.Firebrick); }
            RenderProcesses();
            try
            {
                retainedAssignments.Load();
                if (retainedAssignments.Active)
                {
                    Append("已启用上次保留的分配。", UiTheme.Blue);
                    AutomationTick();
                }
            }
            catch (Exception error) { Append("无法继续上次保留的分配 · " + Native.Explain(error), Color.Firebrick); }
        }
        private void PauseRetainedAssignments()
        {
            retainedAssignments.Pause();
            Append("已暂停保留的分配，当前设置保留。", UiTheme.Muted);
        }
        private void StopAutomation(string reason)
        { StopAutomationExcept(reason, null); }

        private void StopAutomationExcept(string reason, HashSet<string> pendingReset)
        {
            bool wasActive = automation != null;
            automation = null; activeSchemeName = null;
            priorityAttempts.Clear();
            if (wasActive) ReleaseBackgroundProtection(pendingReset);
            if (wasActive) Append(reason, UiTheme.Muted);
            UpdateState();
        }
        private void ActivateScheme(string id)
        {
            if (schemeStore == null || !service.Topology.CanAssign) return;
            StopAutomation("已切换自动方案。");
            try
            {
                SchemeData data = schemeStore.Read();
                SavedScheme custom = id == "game" ? null : data.Schemes.FirstOrDefault(s => s.Id == id);
                if (id != "game" && custom == null) throw new InvalidOperationException("找不到这个方案。");
                RefreshProcesses();
                AutomationSession next = new AutomationSession(id, processes);
                next.Plan(ApplicationGroup.Build(processes, visuals.Get), data, service.Topology); // Validate before enabling or writing anything.
                schemeStore.SelectScheme(id); // Save the startup choice before any affinity write.
                retainedAssignments.Pause();
                toolbarSchemeId = id;
                automation = next; activeSchemeName = id == "game" ? "游戏" : custom.Name;
                Append("已启用“" + activeSchemeName + "”：对已保存规则的应用执行分配，并监测后续新进程。", UiTheme.Blue);
                if (id == "game") Append("游戏目标：" + GameCpuTarget.Name + "（CPU " + Masks.Format(GameCpuTarget.Mask) + "）。" + (hybrid.Recognized ? "" : hybrid.Reason + " ") + "仅询问启用后新启动、具有窗口的未分类应用；已有未分类应用可在“管理方案”中归类。", UiTheme.Muted);
                if (id == "game") Append(NonGameCaption + "。仅处理已明确分类的应用，未分类应用和系统组件不自动放入组 2。", UiTheme.Muted);
                AutomationTick();
            }
            catch (Exception error) { StopAutomation("方案启动失败，已暂停。"); Append("无法启用方案：" + Native.Explain(error), Color.Firebrick); }
            UpdateState();
        }
        private void AutomationTick()
        {
            if (busy || IsDisposed || sizingWindow) return;
            if (automationTicking || Interacting || moreMenu.Visible || automationMenu.Visible)
            {
                // A modal game question or a long drag must not prevent a pending release.
                if (automation != null && PriorityRecords.Any(record => record.TemporaryValue != 0))
                {
                    int ignored;
                    UpdatePerformance(schemeStore.Read().PerformanceFor(automation.SchemeId), service.Processes(out ignored), false);
                }
                return;
            }
            // The page is also an observer: pause stops writes, not reads of actual affinity.
            if (automation == null && !retainedAssignments.Active) { if (!hiddenInTray) QueueProcessRefresh(false); return; }
            automationTicking = true;
            refreshGeneration++;
            try
            {
                processes = service.Processes(out unavailable); visuals.Prime(processes, VisualsLoaded);
                if (automation == null)
                {
                    if (retainedAssignments.Apply(processes, service, cpuSetService, priorityService, Record)) processes = service.Processes(out unavailable);
                    RenderProcesses();
                    return;
                }
                List<ApplicationGroup> apps = ApplicationGroup.Build(processes, visuals.Get);
                SchemeData data = schemeStore.Read();
                List<AutomaticTarget> targets = automation.Plan(apps, data, service.Topology);
                foreach (AutomaticTarget target in targets)
                {
                    ExecuteAutomaticTarget(target);
                }
                bool priorityChanged = UpdatePerformance(data.PerformanceFor(automation.SchemeId), processes, true);
                if (targets.Count > 0 || priorityChanged) processes = service.Processes(out unavailable);
                RenderProcesses();
                ApplicationGroup question = automation.NextQuestion(apps, data, HasWindow);
                if (question != null)
                {
                    // Wait for file metadata so several children do not each open a separate question.
                    if (question.Members.Any(p => visuals.Get(p.ExecutablePath) == null)) return;
                    using (GameQuestionDialog dialog = new GameQuestionDialog(question, GameCpuTarget, unifiedIcons, NonGameCpuTarget))
                    {
                        if (hiddenInTray) dialog.StartPosition = FormStartPosition.CenterScreen;
                        DialogResult choice = hiddenInTray ? dialog.ShowDialog() : dialog.ShowDialog(this);
                        if (choice == DialogResult.Yes || choice == DialogResult.No)
                        {
                            RememberGameClassification(question, choice == DialogResult.Yes);
                        }
                        else automation.Defer(question);
                    }
                }
            }
            catch (Exception error)
            {
                if (automation == null && retainedAssignments.Active)
                {
                    retainedAssignments.Pause();
                    Append("保留的分配已暂停 · " + Native.Explain(error), Color.Firebrick);
                }
                else { StopAutomation("自动方案遇到错误，已暂停。"); Append("自动方案：" + Native.Explain(error), Color.Firebrick); }
            }
            finally { automationTicking = false; if (!hiddenInTray) UpdateState(); }
        }
        private void RememberGameClassification(ApplicationGroup question, bool isGame)
        {
            schemeStore.Classify(question, isGame); // Persist before any affinity write.
            Append("已记住：" + question.Title + (isGame ? "属于游戏。" : "不属于游戏。"), UiTheme.Blue);
            if (automation == null || automation.SchemeId != "game") return;
            // Only fresh identities that still match the saved application are eligible.
            processes = service.Processes(out unavailable);
            foreach (AutomaticTarget target in automation.Plan(ApplicationGroup.Build(processes, visuals.Get), schemeStore.Read(), service.Topology))
                ExecuteAutomaticTarget(target);
            if (!isGame && NonGameCpuTarget == null) Append(NonGameCaption, UiTheme.Muted);
            RefreshProcesses();
        }
        private void ExecuteAutomaticTarget(AutomaticTarget target)
        {
            OperationResult result = !target.UseCpuSets ? service.Apply(target.Process, target.Mask) : cpuSetService != null ? cpuSetService.Apply(target.Process, target.Mask) :
                new OperationResult { Pid = target.Process.Pid, Name = target.Process.Name, Message = "CPU 集不可用，请检查 CPU 集恢复记录。" };
            automation.MarkAttempted(target, result);
            Record(result, target.Process.Key);
        }

        private string DescribeApplications(IEnumerable<ApplicationGroup> apps)
        {
            AutomationSession session = automation;
            SchemeData data = session == null || schemeStore == null ? null : schemeStore.Read();
            return string.Join("\r\n\r\n", apps.Select(app => app.Title + " · " + app.Members.Count + " 个进程\r\n" +
                "硬亲和性（最近读取）：" + CpuAssignmentLabel.From(app.Members, service.Topology, cpuGroups).Text + "\r\n" + app.AllocationDetails() + "\r\n" +
                DescribeCpuSets(app) +
                DescribePriorities(app) + (session == null ? "" : "\r\n" + DescribeRule(app, session, data)) +
                "\r\n最近操作：" + app.ResultText(results) + FirstOperationFailure(app)));
        }
        private static string DescribeCpuSets(ApplicationGroup app)
        {
            ulong?[] masks = app.Members.Select(row => row.CpuSetMask).Distinct().ToArray();
            if (!masks.Any(mask => mask.GetValueOrDefault() != 0)) return "";
            return "默认 CPU 集：" + string.Join(" / ", masks.Select(mask => !mask.HasValue ? "无法读取" :
                mask.Value == 0 ? "未设置" : "CPU " + Masks.Format(mask.Value))) + "\r\n";
        }
        private string FirstOperationFailure(ApplicationGroup app)
        {
            foreach (ProcessRow row in app.Members)
            {
                OperationResult result;
                if (results.TryGetValue(row.Key, out result) && !result.Success && !result.Skipped)
                    return "\r\n" + result.Name + " · " + result.Message;
            }
            return "";
        }

        private string DescribeRule(ApplicationGroup app, AutomationSession session, SchemeData data)
        {
            if (session == null || data == null) return "方案目标：未选择";
            List<AutomaticTarget> targets;
            try { targets = session.Plan(new[] { app }, data, service.Topology, includeAttempted: true); }
            catch (InvalidOperationException error) { return "规则状态：" + error.Message; }
            catch (ArgumentException error) { return "规则状态：" + error.Message; }
            if (targets.Count == 0) return "方案目标：无匹配规则";
            string description = "方案目标：" + string.Join(" / ", targets.Select(target => target.UseCpuSets ? "CPU 集 · " + Masks.Format(target.Mask) :
                CpuSelectionGroup.Describe(target.Mask, cpuGroups) + " · CPU " + Masks.Format(target.Mask)).Distinct());
            description += "\r\n规则状态：" + string.Join("，", targets.GroupBy(session.ExecutionStatus).Select(group => group.Key + " " + group.Count() + " 个"));
            return description;
        }
        private static bool HasWindow(ProcessRow row)
        {
            try { using (Process process = Process.GetProcessById(row.Pid)) return process.MainWindowHandle != IntPtr.Zero; }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }
        private void ManageSchemes()
        {
            if (schemeStore == null) return;
            StopAutomation("编辑方案前已暂停自动分配；保存后可选择启用。");
            RefreshProcesses();
            using (SchemeManagerDialog dialog = new SchemeManagerDialog(schemeStore, service.Topology, ApplicationGroup.Build(processes, visuals.Get), selectedMask))
            {
                dialog.ShowDialog(this);
                if (dialog.ActivateId != null) ActivateScheme(dialog.ActivateId);
                else RenderProcesses();
            }
        }
    }
}
