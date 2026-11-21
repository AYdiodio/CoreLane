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
        private sealed class Choice { internal string Id, Name; public override string ToString() { return Name; } }
        private readonly CapsuleSchemePicker destination = new CapsuleSchemePicker { Dock = DockStyle.Fill };
        private readonly TextBox name = new TextBox { Dock = DockStyle.Fill, MaxLength = 40 };
        private readonly SchemeStore store;
        private readonly Topology topology;
        private readonly IList<SchemeRule> rules;
        internal string SavedId;
        internal ManualSchemeSaveDialog(SchemeStore store, Topology topology, IList<SchemeRule> rules, Func<string, ProcessVisual> lookup = null,
            bool unified = true, bool createOnly = false, bool saveSession = false)
        {
            this.store = store; this.topology = topology; this.rules = rules;
            string title = saveSession ? "保存方案" : createOnly ? "新建自动方案" : "加入自动方案";
            int createHeight = Math.Min(420, 210 + rules.Count * 76);
            int reviewHeight = Math.Min(650, 370 + rules.Count * 80);
            DialogUi.Setup(this, title, createOnly ? new Size(540, createHeight) : new Size(740, reviewHeight));
            MinimumSize = createOnly ? new Size(490, 240) : new Size(650, 440);
            TableLayoutPanel root = DialogUi.Rows(48, 32, createOnly ? 0 : 64, 50, -1, createOnly ? 24 : 42, 52); Controls.Add(root);
            root.Controls.Add(DialogUi.Label(title, 18, UiTheme.Ink), 0, 0);
            string scope = saveSession ? "本次使用期间成功的手动分配" : "最近一次手动分配";
            root.Controls.Add(DialogUi.Label(rules.Count == 0 ? "请先手动分配应用的 CPU。" : scope + " · " + rules.Count + " 条规则", 10, UiTheme.Muted), 0, 1);
            CapsulePanel selector = new CapsulePanel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(14, 9, 14, 9), Margin = new Padding(0, 7, 0, 7) };
            selector.Controls.Add(destination); Label destinationTitle = DialogUi.Label("保存到", 10, UiTheme.Muted); destinationTitle.Dock = DockStyle.Left; destinationTitle.Width = 74; selector.Controls.Add(destinationTitle); root.Controls.Add(selector, 0, 2);
            selector.Visible = !createOnly;
            name.Text = createOnly ? "" : "手动分配方案"; name.BorderStyle = BorderStyle.None; name.AccessibleName = "方案名称";
            CapsulePanel nameBase = new CapsulePanel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(16, 10, 16, 6), Margin = new Padding(0, 4, 0, 0) }; nameBase.Controls.Add(name); root.Controls.Add(nameBase, 0, 3);
            Label nameLabel = DialogUi.Label("方案名称", 10, UiTheme.Muted);
            nameLabel.Dock = DockStyle.Left; nameLabel.Width = 98; nameBase.Controls.Add(nameLabel);
            destination.Items.Add(new Choice { Name = "＋ 新建方案" });
            destination.Items.Add(new Choice { Id = "game", Name = "游戏" });
            foreach (SavedScheme scheme in store.Read().Schemes) destination.Items.Add(new Choice { Id = scheme.Id, Name = scheme.Name });
            Label note = DialogUi.Label("", 9, UiTheme.Muted);
            destination.SelectedIndexChanged += delegate { string id = ((Choice)destination.SelectedItem).Id; nameBase.Visible = id == null; root.RowStyles[3].Height = id == null ? 50 * UiTheme.Scale(this) : 0;
                note.Text = id == "game" ? "优先执行手动核心规则，保留原有游戏分类。保存后不会立即启用。" : "保存后不会立即启用。同一路径的规则更新，其他应用的规则保留。"; }; destination.SelectedIndex = 0;
            CardPanel card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(12), Margin = new Padding(0, 10, 0, 10) };
            ManualRuleList list = new ManualRuleList { Dock = DockStyle.Fill };
            list.SuspendLayout();
            foreach (SchemeRule rule in rules)
            {
                SchemeRule item = rule; ProcessVisual visual = lookup == null ? null : lookup(rule.Application.Paths.First());
                TableLayoutPanel row = new TableLayoutPanel { Width = 640, Height = 76, ColumnCount = 4, RowCount = 1, BackColor = Color.White, Margin = new Padding(0, 0, 0, 4) };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132)); row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66));
                row.Controls.Add(new ApplicationIconBadge(visual, unified) { Dock = DockStyle.Fill, Margin = new Padding(0, 12, 10, 12) }, 0, 0);
                TableLayoutPanel text = DialogUi.Rows(-1, -1); text.Padding = new Padding(0, 10, 8, 10);
                text.Controls.Add(DialogUi.Label(rule.Application.Name, 10.5F, UiTheme.Ink), 0, 0);
                text.Controls.Add(DialogUi.Label((rule.UseCpuSets ? "CPU 集 · " : "硬亲和性 · ") + "CPU " + Masks.Format(rule.Mask), 9, UiTheme.Muted), 0, 1); row.Controls.Add(text, 1, 0);
                CpuAssignmentLabel label = CpuAssignmentLabel.From(new[] { new ProcessRow { Mask = rule.Mask } }, topology, CpuSelectionGroup.FromTopology(topology));
                row.Controls.Add(new CapsuleLabel { Text = label.Text, ForeColor = label.Ink, BackColor = label.Fill, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0, 22, 8, 22) }, 2, 0);
                SoftButton detail = GameQuestionDialog.MakeButton("详情", 60, delegate { AppMessageBox.Show(this, item.Application.Name + "\r\nCPU " + Masks.Format(item.Mask) + "\r\n\r\n" + string.Join("\r\n", item.Application.Paths), "应用详情"); }, false);
                detail.Dock = DockStyle.Fill; detail.Margin = new Padding(0, 22, 0, 22); row.Controls.Add(detail, 3, 0); list.Items.Add(row);
            }
            list.ResumeLayout(true);
            card.Controls.Add(list); root.Controls.Add(card, 0, 4);
            card.Visible = rules.Count > 0;
            root.Controls.Add(note, 0, 5);
            if (createOnly) { note.Text = ""; note.ForeColor = Color.Firebrick; }
            FlowLayoutPanel actions = DialogUi.Actions();
            SoftButton save = GameQuestionDialog.MakeButton(createOnly || saveSession ? "保存方案" : "保存到方案", 140, delegate { Save(); }, true);
            SoftButton cancel = GameQuestionDialog.MakeButton("取消", 90, delegate { DialogResult = DialogResult.Cancel; }, false);
            actions.Controls.Add(save); actions.Controls.Add(cancel); root.Controls.Add(actions, 0, 6); CancelButton = cancel; AcceptButton = save;
            List<SavedScheme> existing = createOnly ? store.Read().Schemes : null;
            EventHandler updateSave = delegate
            {
                string value = name.Text.Trim();
                bool duplicate = createOnly && (value == "游戏" || existing.Any(s => string.Equals(s.Name, value, StringComparison.CurrentCultureIgnoreCase)));
                if (createOnly) note.Text = duplicate ? "这个方案名称已存在。" : "";
                save.Enabled = rules.Count > 0 && !duplicate && (((Choice)destination.SelectedItem).Id != null || value.Length > 0);
            };
            destination.SelectedIndexChanged += updateSave;
            name.TextChanged += updateSave;
            updateSave(this, EventArgs.Empty);
            if (createOnly) ActiveControl = name;
        }
        private void Save()
        {
            try { Choice choice = (Choice)destination.SelectedItem; SavedId = ManualSchemeRules.Save(store, topology, rules, choice.Id, name.Text); DialogResult = DialogResult.OK; }
            catch (Exception error) { AppMessageBox.Show(this, Native.Explain(error), "未保存", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        }
    }
    internal sealed partial class MainForm
    {
        private List<SchemeRule> lastManualRules = new List<SchemeRule>();
        private readonly List<SchemeRule> sessionManualRules = new List<SchemeRule>();
        private void OpenSaveManualScheme(bool createOnly)
        {
            OpenSaveScheme(lastManualRules, createOnly, false);
        }

        private void OpenSaveSessionScheme()
        {
            OpenSaveScheme(sessionManualRules, false, true);
        }

        private void OpenSaveScheme(List<SchemeRule> rules, bool createOnly, bool saveSession)
        {
            if (busy || settingsDialogOpen || schemeStore == null || (!createOnly && rules.Count == 0)) return;
            settingsDialogOpen = true; CloseProcessDetails();
            try
            {
                using (ManualSchemeSaveDialog dialog = new ManualSchemeSaveDialog(schemeStore, service.Topology, rules, visuals.Get, unifiedIcons, createOnly, saveSession))
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        if (automation != null && automation.SchemeId == dialog.SavedId) StopAutomation("已保存更新，请重新启用方案后执行。");
                        Append(createOnly ? "已保存新方案，可在自动方案列表中选择并启用。" : "已保存到所选方案，下次启用时执行。", UiTheme.Blue);
                    }
            }
            finally { settingsDialogOpen = false; FlushDeferredRender(); UpdateState(); }
        }
    }
}
