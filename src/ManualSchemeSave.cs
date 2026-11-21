using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal static class ManualSchemeRules
    {
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
                destination.Add(new SchemeRule { MaskHex = rule.MaskHex, UseCpuSets = rule.UseCpuSets, ExactPaths = rule.ExactPaths,
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
            if (!topology.CanAssign || rules.Count == 0) throw new InvalidOperationException("没有可保存的本次成功分配记录。");
            foreach (SchemeRule rule in rules) Masks.Validate(rule.Mask, topology.Available);
            string target = id ?? Guid.NewGuid().ToString("N");
            store.Update(delegate(SchemeData data)
            {
                SavedScheme scheme = target == "game" ? data.GameOverrides : data.Schemes.FirstOrDefault(s => s.Id == target);
                if (id != null && id != "game" && scheme == null) throw new InvalidOperationException("目标方案已不存在，请重新选择。");
                if (scheme != null && scheme.TopologyKey != SchemeStore.Fingerprint(topology)) throw new InvalidOperationException("目标方案的 CPU 拓扑已变化，请先在管理方案中核对。");
                if (scheme == null) { scheme = new SavedScheme { Id = target, Name = target == "game" ? "游戏" : name.Trim(), TopologyKey = SchemeStore.Fingerprint(topology) }; if (target == "game") data.GameOverrides = scheme; else data.Schemes.Add(scheme); }
                Merge(scheme.Rules, rules);
            });
            return target;
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
        private readonly CapsuleSchemePicker destination = new CapsuleSchemePicker { Dock = DockStyle.Fill, AccessibleName = "保存到" };
        private readonly TextBox name = new TextBox { Dock = DockStyle.Fill, MaxLength = 40, Text = "手动分配方案", BorderStyle = BorderStyle.None,
            BackColor = UiTheme.Page, AccessibleName = "方案名称" };
        private readonly SchemeStore store;
        private readonly Topology topology;
        private readonly IList<SchemeRule> rules;
        internal string SavedId;

        internal ManualSchemeSaveDialog(SchemeStore store, Topology topology, IList<SchemeRule> rules, Func<string, ProcessVisual> lookup = null,
            bool unified = true, bool saveSession = false)
        {
            this.store = store;
            this.topology = topology;
            this.rules = rules;
            string title = saveSession ? "保存方案" : "保存本次分配";
            int previewHeight = Math.Min(rules.Count, 4) * (RuleHeight + 4) + 16;
            DialogUi.Setup(this, title, new Size(664, 296 + previewHeight));
            MinimumSize = new Size(550, 360);
            TableLayoutPanel root = DialogUi.Rows(42, 98, 30, -1, 30, 48);
            Controls.Add(root);
            root.Controls.Add(DialogUi.Label(title, 17, UiTheme.Ink), 0, 0);

            var fieldCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(16, 10, 16, 10), Margin = Padding.Empty, AccessibleName = "方案目标" };
            var fields = new RepaintTable { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Color.White, Margin = Padding.Empty };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            fields.Controls.Add(DialogUi.Label("保存到", 10, UiTheme.Ink), 0, 0);
            destination.Margin = Padding.Empty;
            fields.Controls.Add(destination, 1, 0);
            Label nameLabel = DialogUi.Label("方案名称", 10, UiTheme.Muted);
            fields.Controls.Add(nameLabel, 0, 1);
            var nameBase = new CapsulePanel { Dock = DockStyle.Fill, BackColor = UiTheme.Page, Padding = new Padding(14, 9, 14, 0), Margin = new Padding(0, 6, 0, 0) };
            nameBase.Controls.Add(name);
            fields.Controls.Add(nameBase, 1, 1);
            fieldCard.Controls.Add(fields);
            root.Controls.Add(fieldCard, 0, 1);

            List<SavedScheme> existing = store.Read().Schemes;
            destination.Items.Add(new Choice { Name = "保存为新方案" });
            destination.Items.Add(new Choice { Id = "game", Name = "游戏" });
            foreach (SavedScheme scheme in existing) destination.Items.Add(new Choice { Id = scheme.Id, Name = scheme.Name });

            string scope = saveSession ? "本次使用期间的分配" : "最近一次分配";
            string summary = rules.Count == 0 ? "暂无可保存的分配" : scope + " · " + rules.Count + " 条规则";
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
            preview.Controls.Add(list);
            viewport.Controls.Add(preview);
            root.Controls.Add(viewport, 0, 3);
            Action fitPreview = delegate { preview.Height = Math.Min(viewport.ClientSize.Height, list.ContentHeight + preview.Padding.Vertical); };
            viewport.SizeChanged += delegate { fitPreview(); };
            list.Content.SizeChanged += delegate { fitPreview(); };
            fitPreview();

            Label note = DialogUi.Label("", 9, UiTheme.Muted);
            root.Controls.Add(note, 0, 4);
            FlowLayoutPanel actions = DialogUi.Actions();
            SoftButton save = GameQuestionDialog.MakeButton(saveSession ? "保存方案" : "保存到方案", 140, delegate { Save(); }, true);
            SoftButton cancel = GameQuestionDialog.MakeButton("取消", 90, delegate { DialogResult = DialogResult.Cancel; }, false);
            actions.Controls.Add(save);
            actions.Controls.Add(cancel);
            root.Controls.Add(actions, 0, 5);
            CancelButton = cancel;
            AcceptButton = save;
            EventHandler updateSave = delegate
            {
                string id = ((Choice)destination.SelectedItem).Id;
                bool isNew = id == null;
                nameBase.Visible = nameLabel.Visible = isNew;
                root.RowStyles[1].Height = (float)Math.Round((isNew ? 98 : 56) * UiTheme.Scale(this));
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

        private Control CreateRuleRow(SchemeRule rule, ProcessVisual visual, bool unified, IList<CpuSelectionGroup> groups)
        {
            var row = new RepaintTable { ColumnCount = 4, RowCount = 1, BackColor = Color.White, Margin = Padding.Empty };
            row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 65));
            row.Controls.Add(new ApplicationIconBadge(visual, unified) { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 8, 8) }, 0, 0);
            string range = (rule.UseCpuSets ? "CPU 集 · " : "硬亲和性 · ") + "CPU " + Masks.Format(rule.Mask);
            TableLayoutPanel text = DialogUi.Rows(-1, -1);
            text.Padding = new Padding(0, 6, 8, 6);
            text.Controls.Add(DialogUi.Label(rule.Application.Name, 10.5F, UiTheme.Ink), 0, 0);
            text.Controls.Add(DialogUi.Label(range, 9, UiTheme.Muted), 0, 1);
            row.Controls.Add(text, 1, 0);
            CpuAssignmentLabel label = CpuAssignmentLabel.From(new[] { new ProcessRow { Mask = rule.Mask } }, topology, groups);
            row.Controls.Add(new CapsuleLabel { Text = label.Text, ForeColor = label.Ink, BackColor = label.Fill, Dock = DockStyle.Fill,
                Margin = new Padding(0, 15, 8, 15) }, 2, 0);
            SoftButton detail = GameQuestionDialog.MakeButton("详情", 60, delegate
            {
                AppMessageBox.Show(this, rule.Application.Name + "\r\n" + range + "\r\n\r\n" + string.Join("\r\n", rule.Application.Paths), "应用详情");
            }, false);
            detail.AccessibleName = "查看 " + rule.Application.Name + " 的详情";
            detail.Dock = DockStyle.Fill;
            detail.Margin = new Padding(0, 14, 0, 14);
            row.Controls.Add(detail, 3, 0);
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
        private List<SchemeRule> lastManualRules = new List<SchemeRule>();
        private readonly List<SchemeRule> sessionManualRules = new List<SchemeRule>();
        private void OpenSaveManualScheme()
        {
            OpenSaveScheme(lastManualRules, false);
        }

        private void OpenSaveSessionScheme()
        {
            OpenSaveScheme(sessionManualRules, true);
        }

        private void OpenSaveScheme(List<SchemeRule> rules, bool saveSession)
        {
            if (busy || settingsDialogOpen || schemeStore == null || rules.Count == 0) return;
            settingsDialogOpen = true; CloseProcessDetails();
            try
            {
                using (ManualSchemeSaveDialog dialog = new ManualSchemeSaveDialog(schemeStore, service.Topology, rules, visuals.Get, unifiedIcons, saveSession))
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
