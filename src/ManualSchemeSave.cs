using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal static class ManualSchemeRules
    {
        internal static List<SchemeRule> CaptureCurrent(IList<ProcessRow> rows, Topology topology,
            Func<string, ProcessVisual> lookup, out int incompletePaths)
        {
            var rules = new List<SchemeRule>();
            incompletePaths = rows.Count(row => !SavedApplication.ValidPath(row.ExecutablePath));
            foreach (ApplicationGroup app in ApplicationGroup.Build(rows.Where(row => SavedApplication.ValidPath(row.ExecutablePath)), lookup))
            {
                foreach (var path in app.Members.GroupBy(row => row.ExecutablePath, StringComparer.OrdinalIgnoreCase))
                {
                    ulong mask = Common(path.Select(row => row.ReadError == null && (row.Mask & ~topology.Available) == 0 ? row.Mask : 0UL));
                    bool eligible = PerformanceProfile.EligiblePath(path.Key);
                    ulong? defaults = eligible ? Common(path.Select(row => row.CpuSetMask)) : null;
                    if (defaults.HasValue && (defaults.Value & ~(mask == 0 ? topology.Available : mask)) != 0) defaults = null;
                    uint priority = eligible ? Common(path.Select(row => PriorityLevels.Selectable(row.Priority) ? row.Priority : 0U)) : 0;
                    if (mask == 0 || !defaults.HasValue || priority == 0) incompletePaths++;
                    if (mask != 0 || defaults.HasValue || priority != 0)
                        rules.Add(new SchemeRule { Application = new SavedApplication { Name = app.Title, Paths = new List<string> { path.Key } },
                            MaskHex = mask.ToString("X16"), CpuSetsHex = defaults.HasValue ? defaults.Value.ToString("X16") : null,
                            CapturedPriority = priority, ExactPaths = true });
                }
            }
            return rules;
        }

        private static T Common<T>(IEnumerable<T> values)
        {
            T[] unique = values.Distinct().Take(2).ToArray();
            return unique.Length == 1 ? unique[0] : default(T);
        }

        internal static string DescribeCpu(ulong mask, bool useCpuSets, ulong? defaults, IList<CpuSelectionGroup> groups)
        {
            if (useCpuSets) return "CPU 集 · CPU " + Masks.Format(mask);
            string hard = mask == 0 ? "" : (groups == null ? "硬亲和性" : CpuSelectionGroup.Describe(mask, groups)) + " · CPU " + Masks.Format(mask);
            if (!defaults.HasValue) return hard;
            string cpuSets = defaults.Value == 0 ? "默认 CPU 集未设置" : "CPU 集 · CPU " + Masks.Format(defaults.Value);
            return hard.Length == 0 ? cpuSets : hard + " / " + cpuSets;
        }

        internal static List<SchemeRule> Capture(IEnumerable<ProcessRow> successful, ulong mask, Func<string, ProcessVisual> lookup,
            bool useCpuSets = false, bool exactPaths = false)
        {
            return ApplicationGroup.Build(successful.Where(p => SavedApplication.ValidPath(p.ExecutablePath)), lookup)
                .Select(app => new SchemeRule { Application = SavedApplication.From(app), MaskHex = mask.ToString("X16"),
                    UseCpuSets = useCpuSets, ExactPaths = exactPaths || useCpuSets }).ToList();
        }

        internal static void Merge(List<SchemeRule> destination, IList<SchemeRule> rules)
        {
            RemovePaths(destination, rules.SelectMany(rule => rule.Application.Paths));
            foreach (SchemeRule rule in rules)
                destination.Add(new SchemeRule { MaskHex = rule.MaskHex, UseCpuSets = rule.UseCpuSets, ExactPaths = rule.ExactPaths, CpuSetsHex = rule.CpuSetsHex,
                    Application = new SavedApplication { Name = rule.Application.Name, Paths = new List<string>(rule.Application.Paths) } });
        }

        internal static void RemovePaths(List<SchemeRule> rules, IEnumerable<string> paths)
        {
            var replaced = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            foreach (SchemeRule old in rules) old.Application.Paths.RemoveAll(replaced.Contains);
            rules.RemoveAll(rule => rule.Application.Paths.Count == 0);
        }

        internal static string Save(SchemeStore store, Topology topology, IList<SchemeRule> rules, string id, string name)
        {
            if (!topology.CanAssign || rules.Count == 0) throw new InvalidOperationException("没有可保存的应用设置。");
            foreach (SchemeRule rule in rules)
            {
                if (rule.Mask != 0 || !rule.CpuSets.HasValue && rule.CapturedPriority == 0) Masks.Validate(rule.Mask, topology.Available);
                if (rule.CpuSets.GetValueOrDefault() != 0) Masks.Validate(rule.CpuSets.Value, topology.Available);
            }
            string target = id ?? Guid.NewGuid().ToString("N");
            store.Update(delegate(SchemeData data)
            {
                SavedScheme scheme = target == "game" ? data.GameOverrides : data.Schemes.FirstOrDefault(s => s.Id == target);
                if (id != null && id != "game" && scheme == null) throw new InvalidOperationException("目标方案已不存在，请重新选择。");
                if (scheme != null && scheme.TopologyKey != SchemeStore.Fingerprint(topology)) throw new InvalidOperationException("目标方案的 CPU 拓扑已变化，请先在管理方案中核对。");
                if (scheme == null) { scheme = new SavedScheme { Id = target, Name = target == "game" ? "游戏" : name.Trim(), TopologyKey = SchemeStore.Fingerprint(topology) }; if (target == "game") data.GameOverrides = scheme; else data.Schemes.Add(scheme); }
                Merge(scheme.Rules, rules.Where(rule => rule.Mask != 0 || rule.CpuSets.HasValue).ToArray());
                SavePriorities(data, target, rules);
            });
            return target;
        }

        private static void SavePriorities(SchemeData data, string id, IList<SchemeRule> rules)
        {
            var priorities = rules.Where(rule => rule.CapturedPriority != 0).ToArray();
            if (priorities.Length == 0) return;
            if (data.Performance == null) data.Performance = new List<PerformanceProfile>();
            PerformanceProfile profile = data.PerformanceFor(id);
            if (profile == null)
            {
                profile = new PerformanceProfile { SchemeId = id };
                data.Performance.Add(profile);
            }
            var byPath = profile.Rules.ToDictionary(rule => rule.Path, StringComparer.OrdinalIgnoreCase);
            foreach (SchemeRule captured in priorities)
                foreach (string path in captured.Application.Paths)
                {
                    PerformanceRule rule;
                    if (!byPath.TryGetValue(path, out rule))
                    {
                        rule = new PerformanceRule { Path = path };
                        byPath.Add(path, rule);
                        profile.Rules.Add(rule);
                    }
                    rule.Priority = captured.CapturedPriority;
                }
        }
    }
    internal sealed class ManualRuleList : CapsuleScrollPanel
    {
        private bool arranging;
        internal int RowHeight = 76;
        internal ControlCollection Items { get { return Content.Controls; } }
        internal ManualRuleList()
        {
            BackColor = Color.White;
            Content.ControlAdded += delegate { PerformLayout(); };
            Content.ControlRemoved += delegate { PerformLayout(); };
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (arranging || Content == null) return; arranging = true;
            try
            {
                float scale = UiTheme.Scale(this); int height = (int)Math.Round(RowHeight * scale), gap = (int)Math.Round(4 * scale);
                for (int i = 0; i < Items.Count; i++) Items[i].Bounds = new Rectangle(0, i * (height + gap), ContentWidth, height);
                SetContentHeight(Math.Max(0, Items.Count * (height + gap) - gap)); Content.Invalidate(true);
            }
            finally { arranging = false; }
        }
    }
    internal sealed class ManualSchemeSaveDialog : Form
    {
        private const int RuleHeight = 58;
        private sealed class Choice { internal string Id, Name; public override string ToString() { return Name; } }
        private readonly CapsuleSchemePicker destination = new CapsuleSchemePicker { AccessibleName = "保存到", AlignOptionsLeft = true };
        private readonly TextBox name = new TextBox { MaxLength = 40, Text = "当前设置", BorderStyle = BorderStyle.None,
            BackColor = UiTheme.Page, AccessibleName = "方案名称" };
        private readonly SchemeStore store;
        private readonly Topology topology;
        private readonly IList<SchemeRule> rules;
        internal string SavedId;

        internal ManualSchemeSaveDialog(SchemeStore store, Topology topology, IList<SchemeRule> rules, Func<string, ProcessVisual> lookup = null,
            bool unified = true, bool saveSession = false, int incompletePaths = 0)
        {
            this.store = store;
            this.topology = topology;
            this.rules = rules;
            string title = saveSession ? "保存方案" : "保存本次分配";
            int previewHeight = Math.Min(Math.Max(rules.Count, 1), 5) * (RuleHeight + 4) + 16;
            DialogUi.Setup(this, title, new Size(664, 262 + previewHeight));
            MinimumSize = new Size(560, 310);
            TableLayoutPanel root = DialogUi.Rows(38, 92, 32, -1, 42);
            root.Padding = new Padding(18);
            Controls.Add(root);
            root.Controls.Add(DialogUi.Label(title, 17, UiTheme.Ink), 0, 0);

            var fieldCard = new CardPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, AccessibleName = "方案目标" };
            Label targetLabel = DialogUi.Label("保存到", 10, UiTheme.Ink);
            Label nameLabel = DialogUi.Label("方案名称", 10, UiTheme.Muted);
            targetLabel.Dock = nameLabel.Dock = destination.Dock = DockStyle.None;
            var nameBase = new CapsulePanel { BackColor = UiTheme.Page };
            nameBase.Controls.Add(name);
            fieldCard.Controls.AddRange(new Control[] { targetLabel, destination, nameLabel, nameBase });
            fieldCard.Layout += delegate
            {
                float scale = UiTheme.Scale(fieldCard);
                int horizontal = (int)Math.Round(14 * scale), vertical = (int)Math.Round(8 * scale);
                int gap = (int)Math.Round(6 * scale), rowHeight = (int)Math.Round(35 * scale);
                int labelWidth = Math.Max(TextRenderer.MeasureText(nameLabel.Text, nameLabel.Font).Width, (int)Math.Round(68 * scale));
                int start = horizontal + labelWidth + gap;
                int width = Math.Max(0, fieldCard.ClientSize.Width - start - horizontal);
                targetLabel.SetBounds(horizontal, vertical, labelWidth, rowHeight);
                destination.SetBounds(start, vertical, width, rowHeight);
                nameLabel.SetBounds(horizontal, vertical + rowHeight + gap, labelWidth, rowHeight);
                nameBase.SetBounds(start, nameLabel.Top, width, rowHeight);
                name.SetBounds(horizontal, Math.Max(0, (rowHeight - name.Height) / 2), Math.Max(0, width - 2 * horizontal), name.Height);
            };
            root.Controls.Add(fieldCard, 0, 1);

            List<SavedScheme> existing = store.Read().Schemes;
            destination.Items.Add(new Choice { Name = "保存为新方案" });
            destination.Items.Add(new Choice { Id = "game", Name = "游戏" });
            foreach (SavedScheme scheme in existing) destination.Items.Add(new Choice { Id = scheme.Id, Name = scheme.Name });

            string scope = saveSession ? "当前运行的应用" : "最近一次分配";
            string summary = rules.Count == 0 ? "暂无可保存的设置" : scope + " · " + rules.Count + " 项";
            if (incompletePaths > 0) summary += " · " + incompletePaths + " 项的部分设置未纳入";
            root.Controls.Add(DialogUi.Label(summary, 10, UiTheme.Muted), 0, 2);
            var viewport = new RepaintPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            var preview = new CardPanel { Dock = DockStyle.Top, Padding = new Padding(12, 8, 8, 8), Margin = Padding.Empty, Visible = rules.Count > 0 };
            var list = new ManualRuleList { Dock = DockStyle.Fill, RowHeight = RuleHeight, AccessibleName = "待保存的分配" };
            IList<CpuSelectionGroup> groups = CpuSelectionGroup.FromTopology(topology);
            list.SuspendLayout();
            foreach (SchemeRule rule in rules)
            {
                ProcessVisual visual = lookup == null ? null : lookup(rule.Application.Paths[0]);
                list.Items.Add(CreateRuleRow(rule, visual, unified, groups));
            }
            list.ResumeLayout(true);
            RepaintTable header = RuleColumns();
            header.Dock = DockStyle.Top;
            header.Height = 22;
            Label applicationLabel = DialogUi.Label("应用", 9, UiTheme.Muted);
            header.Controls.Add(applicationLabel, 0, 0);
            header.SetColumnSpan(applicationLabel, 2);
            Label cpuLabel = DialogUi.Label("CPU 分配", 9, UiTheme.Muted);
            header.Controls.Add(cpuLabel, 2, 0);
            header.Controls.Add(DialogUi.Label("进程优先级", 9, UiTheme.Muted), 3, 0);
            header.Layout += delegate
            {
                header.Padding = new Padding(0, 0, list.RailWidth, 0);
                cpuLabel.Padding = new Padding((int)Math.Round(12 * UiTheme.Scale(header)), 0, 0, 0);
            };
            preview.Controls.Add(list);
            preview.Controls.Add(header);
            viewport.Controls.Add(preview);
            root.Controls.Add(viewport, 0, 3);
            Action fitPreview = delegate { preview.Height = Math.Min(viewport.ClientSize.Height, list.ContentHeight + preview.Padding.Vertical + header.Height); };
            viewport.SizeChanged += delegate { fitPreview(); };
            list.Content.SizeChanged += delegate { fitPreview(); };
            fitPreview();

            Label note = DialogUi.Label("", 9, UiTheme.Muted);
            var footer = new RepaintTable { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, RowCount = 1 };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 246));
            footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            footer.Controls.Add(note, 0, 0);
            FlowLayoutPanel actions = DialogUi.Actions();
            SoftButton save = GameQuestionDialog.MakeButton(saveSession ? "保存方案" : "保存到方案", 140, delegate { Save(); }, true);
            SoftButton cancel = GameQuestionDialog.MakeButton("取消", 90, delegate { DialogResult = DialogResult.Cancel; }, false);
            actions.Controls.Add(save);
            actions.Controls.Add(cancel);
            footer.Controls.Add(actions, 1, 0);
            root.Controls.Add(footer, 0, 4);
            CancelButton = cancel;
            AcceptButton = save;
            EventHandler updateSave = delegate
            {
                string id = ((Choice)destination.SelectedItem).Id;
                bool isNew = id == null;
                nameBase.Visible = nameLabel.Visible = isNew;
                root.RowStyles[1].Height = (float)Math.Round((isNew ? 92 : 52) * UiTheme.Scale(this));
                fieldCard.PerformLayout();
                string value = name.Text.Trim();
                bool duplicate = isNew && (value == "游戏" || existing.Any(s => string.Equals(s.Name, value, StringComparison.CurrentCultureIgnoreCase)));
                note.ForeColor = duplicate ? Color.Firebrick : UiTheme.Muted;
                note.Text = duplicate ? "这个方案名称已存在。" : isNew ? "启用新方案后，才会自动执行分配。" :
                    id == "game" ? "这些分配优先于游戏分类。启用游戏方案后执行。" : "同一路径的规则将更新。启用方案后执行。";
                save.Enabled = rules.Count > 0 && !duplicate && (!isNew || value.Length > 0);
            };
            destination.SelectedIndexChanged += updateSave;
            name.TextChanged += updateSave;
            destination.SelectedIndex = 0;
            Shown += updateSave;
            ActiveControl = destination;
        }

        private static RepaintTable RuleColumns()
        {
            var row = new RepaintTable { ColumnCount = 5, RowCount = 1, BackColor = Color.White, Margin = Padding.Empty };
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65));
            return row;
        }

        private Control CreateRuleRow(SchemeRule rule, ProcessVisual visual, bool unified, IList<CpuSelectionGroup> groups)
        {
            RepaintTable row = RuleColumns();
            row.Controls.Add(new ApplicationIconBadge(visual, unified) { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 8, 8) }, 0, 0);
            ulong shownMask = rule.CpuSets.GetValueOrDefault() != 0 ? rule.CpuSets.Value : rule.Mask;
            string range = shownMask == 0 ? "未保存 CPU 范围" : (rule.UseCpuSets || rule.CpuSets.GetValueOrDefault() != 0 ? "CPU 集" : "硬亲和性") + " · CPU " + Masks.Format(shownMask);
            TableLayoutPanel text = DialogUi.Rows(-1, -1);
            text.Padding = new Padding(0, 6, 8, 6);
            text.Controls.Add(DialogUi.Label(rule.Application.Name, 10.5F, UiTheme.Ink), 0, 0);
            text.Controls.Add(DialogUi.Label(range, 9, UiTheme.Muted), 0, 1);
            row.Controls.Add(text, 1, 0);
            CpuAssignmentLabel label = CpuAssignmentLabel.From(new[] { new ProcessRow { Mask = shownMask } }, topology, groups);
            row.Controls.Add(new CapsuleLabel { Text = shownMask == 0 ? "保持现状" : label.Text, ForeColor = label.Ink, BackColor = label.Fill, Dock = DockStyle.Fill,
                Margin = new Padding(0, 15, 8, 15) }, 2, 0);
            string priority = rule.CapturedPriority == 0 ? "保持现状" : PriorityLevels.Describe(rule.CapturedPriority);
            row.Controls.Add(DialogUi.Label(priority, 9, UiTheme.Muted), 3, 0);
            SoftButton detail = GameQuestionDialog.MakeButton("详情", 60, delegate
            {
                string cpu = ManualSchemeRules.DescribeCpu(rule.Mask, rule.UseCpuSets, rule.CpuSets, null);
                AppMessageBox.Show(this, rule.Application.Name + "\r\n" + (cpu.Length == 0 ? "CPU 分配 · 保持现状" : cpu) +
                    "\r\n进程优先级 · " + priority + "\r\n\r\n" + string.Join("\r\n", rule.Application.Paths), "应用详情");
            }, false);
            detail.AccessibleName = "查看 " + rule.Application.Name + " 的详情";
            detail.Dock = DockStyle.Fill;
            detail.Margin = new Padding(0, 14, 0, 14);
            row.Controls.Add(detail, 4, 0);
            return row;
        }

        private void Save()
        {
            try
            {
                Choice choice = (Choice)destination.SelectedItem;
                SavedId = ManualSchemeRules.Save(store, topology, rules, choice.Id, name.Text);
                DialogResult = DialogResult.OK;
            }
            catch (Exception error) { AppMessageBox.Show(this, Native.Explain(error), "未保存", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
    }
    internal sealed partial class MainForm
    {
        private void OpenSaveSessionScheme()
        {
            if (busy || settingsDialogOpen || schemeStore == null || !service.Topology.CanAssign) return;
            settingsDialogOpen = true; CloseProcessDetails();
            try
            {
                int unreadable, incomplete;
                List<ProcessRow> current = service.Processes(out unreadable);
                List<SchemeRule> rules = ManualSchemeRules.CaptureCurrent(current, service.Topology, visuals.Get, out incomplete);
                using (ManualSchemeSaveDialog dialog = new ManualSchemeSaveDialog(schemeStore, service.Topology, rules, visuals.Get, unifiedIcons, true, incomplete + unreadable))
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        if (automation != null && automation.SchemeId == dialog.SavedId) StopAutomation("已保存更新，请重新启用方案后执行。");
                        Append("已保存到所选方案，下次启用时执行。", UiTheme.Blue);
                    }
            }
            finally { settingsDialogOpen = false; FlushDeferredRender(); UpdateState(); }
        }
    }
}
