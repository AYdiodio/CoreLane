using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal sealed class GameQuestionDialog : Form
    {
        internal GameQuestionDialog(ApplicationGroup app, CpuSelectionGroup target, bool unifiedIcons = true, CpuSelectionGroup nonGameTarget = null)
        {
            Text = "是否归类为游戏？"; Font = new Font("Microsoft YaHei UI", 10F); BackColor = UiTheme.Page;
            StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog;
            TopMost = true; ShowInTaskbar = false; ShowIcon = false;
            MinimizeBox = false; MaximizeBox = false; ClientSize = new Size(580, 330);
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 4 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80)); root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80)); root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); Controls.Add(root);
            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0) };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.Controls.Add(new ApplicationIconBadge(app.Visual, unifiedIcons) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 14, 14), AccessibleName = app.Title + "图标" }, 0, 0);
            header.Controls.Add(new Label { Text = "“" + app.Title + "”是游戏吗？", Font = new Font(Font.FontFamily, 16, FontStyle.Regular), Dock = DockStyle.Fill,
                AutoEllipsis = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0, 0, 0, 14) }, 1, 0);
            root.Controls.Add(header, 0, 0);
            CardPanel appCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(14), Margin = new Padding(0, 0, 0, 10) };
            Label appInfo = DialogUi.Label("新启动、尚未分类的应用 · 包含 " + app.Members.Count + " 个进程", 10, UiTheme.Muted);
            appCard.Controls.Add(appInfo); root.Controls.Add(appCard, 0, 1);
            root.Controls.Add(new Label { Dock = DockStyle.Fill, ForeColor = UiTheme.Muted,
                Text = "是：记住为游戏，分配到 " + target.Name + "（CPU " + Masks.Format(target.Mask) + "）。\r\n否：" + (nonGameTarget == null ? "记住为非游戏；本机无可用组 2，不修改分配。" : "记住为非游戏，分配到 " + nonGameTarget.Name + "（CPU " + Masks.Format(nonGameTarget.Mask) + "）。") + "\r\n仅在游戏方案启用时自动处理；稍后：本次不再询问。" }, 0, 2);
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            SoftButton yes = MakeButton("是，归类游戏", 150, delegate { DialogResult = DialogResult.Yes; }, true);
            SoftButton no = MakeButton("否，不是游戏", 150, delegate { DialogResult = DialogResult.No; }, false);
            SoftButton later = MakeButton("稍后", 90, delegate { DialogResult = DialogResult.Cancel; }, false);
            buttons.Controls.Add(yes); buttons.Controls.Add(no); buttons.Controls.Add(later); root.Controls.Add(buttons, 0, 3);
            CancelButton = later; ActiveControl = later;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        }
        protected override void OnShown(EventArgs e)
        {
            // A minimized owner can be positioned outside every monitor. Keep the question reachable.
            Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
            if (!area.IntersectsWith(Bounds)) area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
            base.OnShown(e);
            Activate();
        }
        internal static SoftButton MakeButton(string text, int width, EventHandler action, bool primary)
        {
            SoftButton button = new SoftButton { Text = text, Width = width, Height = 36, Primary = primary, Margin = new Padding(4) };
            if (text.StartsWith("启用", StringComparison.Ordinal)) button.Tone = ButtonTone.Start;
            if (text.StartsWith("暂停", StringComparison.Ordinal)) button.Tone = ButtonTone.Pause;
            if (text.StartsWith("删除", StringComparison.Ordinal)) button.Destructive = true;
            button.Click += action; return button;
        }
    }

    // Borrow the cached visual; this control does not own or dispose the source bitmap.
    internal sealed class ApplicationIconBadge : Control
    {
        internal readonly ProcessVisual Visual;
        internal readonly bool UnifiedIcons;
        internal ApplicationIconBadge(ProcessVisual visual, bool unifiedIcons)
        { Visual = visual; UnifiedIcons = unifiedIcons; DoubleBuffered = ResizeRedraw = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float size = Math.Min(Width, Height), inset = size * .18F;
            RectangleF face = new RectangleF((Width - size) / 2, (Height - size) / 2, size, size);
            UiTheme.FillRound(e.Graphics, UiTheme.IconBase, face, size * .27F);
            UiTheme.DrawAppIcon(e.Graphics, Visual, RectangleF.Inflate(face, -inset, -inset), UnifiedIcons);
        }
    }

    internal sealed class SchemeManagerDialog : Form
    {
        private sealed class SchemeChoice
        {
            internal string Id, Name;
            public override string ToString() { return Name; }
        }
        private sealed class MaskChoice
        {
            internal string Text;
            internal ulong Mask;
            internal bool UseCpuSets;
            public override string ToString() { return Text; }
        }
        private readonly SchemeStore store;
        private readonly Topology topology;
        private readonly IList<ApplicationGroup> apps;
        private readonly ulong selectedMask;
        private readonly CapsuleSchemePicker schemes = new CapsuleSchemePicker { Width = 240, Height = 36 };
        private readonly TextBox name = new TextBox { Width = 220, MaxLength = 40 };
        private readonly DataGridView rules = CreateGrid();
        private readonly DataGridView games = CreateGrid();
        private readonly Label note = new Label { Dock = DockStyle.Fill, ForeColor = UiTheme.Muted };
        private readonly List<MaskChoice> choices = new List<MaskChoice>();
        private bool loading;
        internal string ActivateId;

        internal SchemeManagerDialog(SchemeStore store, Topology topology, IList<ApplicationGroup> apps, ulong selectedMask)
        {
            this.store = store; this.topology = topology; this.apps = apps; this.selectedMask = selectedMask;
            DialogUi.Setup(this, "管理方案", new Size(980, 720)); MinimumSize = new Size(860, 620);
            TableLayoutPanel root = DialogUi.Rows(48, 48, -1); Controls.Add(root);
            root.Controls.Add(DialogUi.Label("管理方案", 18, UiTheme.Ink), 0, 0);
            CapsulePanel nav = new CapsulePanel { Dock = DockStyle.Left, Width = 300 }; root.Controls.Add(nav, 0, 1);
            CardPanel content = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(18), Margin = new Padding(0, 12, 0, 0) }; root.Controls.Add(content, 0, 2);
            Panel custom = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            Panel classification = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Visible = false }; content.Controls.Add(custom); content.Controls.Add(classification);
            SoftButton customTab = GameQuestionDialog.MakeButton("方案规则", 132, delegate { }, true);
            SoftButton gameTab = GameQuestionDialog.MakeButton("游戏识别", 132, delegate { }, false);
            customTab.Dock = DockStyle.Left; gameTab.Dock = DockStyle.Right; nav.Controls.Add(customTab); nav.Controls.Add(gameTab);
            customTab.Click += delegate { custom.Visible = true; classification.Visible = false; customTab.Primary = true; gameTab.Primary = false; customTab.Invalidate(); gameTab.Invalidate(); };
            gameTab.Click += delegate { custom.Visible = false; classification.Visible = true; customTab.Primary = false; gameTab.Primary = true; customTab.Invalidate(); gameTab.Invalidate(); };
            TableLayoutPanel pane = Pane(5); custom.Controls.Add(pane);
            pane.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); pane.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); pane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            pane.RowStyles.Add(new RowStyle(SizeType.Absolute, 52)); pane.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            FlowLayoutPanel selector = Flow(); selector.Controls.Add(schemes); selector.Controls.Add(new Label { Text = "方案名称", Width = 80, Height = 36, ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleCenter });
            CapsulePanel nameBase = new CapsulePanel { Width = 260, Height = 36, Padding = new Padding(14, 7, 14, 4) }; name.BorderStyle = BorderStyle.None; name.BackColor = UiTheme.Page; name.Dock = DockStyle.Fill; nameBase.Controls.Add(name); selector.Controls.Add(nameBase);
            pane.Controls.Add(selector, 0, 0); pane.Controls.Add(new Label { Text = "勾选应用，为每行选择 CPU 范围，再保存。保存只记录规则；点击“启用方案”才执行。", Dock = DockStyle.Fill, ForeColor = UiTheme.Muted }, 0, 1);
            rules.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Use", HeaderText = "使用", Width = 60, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
            rules.Columns.Add(new DataGridViewTextBoxColumn { Name = "App", HeaderText = "应用", ReadOnly = true, FillWeight = 55 });
            rules.Columns.Add(new DataGridViewButtonColumn { Name = "Cpu", HeaderText = "分配到", FillWeight = 45 }); pane.Controls.Add(rules, 0, 2);
            rules.CellContentClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != rules.Columns["Cpu"].Index || !topology.CanAssign) return;
                DataGridViewCell cell = rules.Rows[e.RowIndex].Cells["Cpu"]; Rectangle bounds = rules.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
                DialogStyle.Choices(rules, choices.Select(c => c.Text).Concat(new[] { "自定义 CPU…", "自定义 CPU 集…" }).ToArray(), delegate(string value)
                {
                    bool useCpuSets = value == "自定义 CPU 集…";
                    if (value != "自定义 CPU…" && !useCpuSets) { cell.Value = value; return; }
                    MaskChoice old = choices.FirstOrDefault(c => c.Text == Convert.ToString(cell.Value));
                    using (CpuMaskDialog picker = new CpuMaskDialog(topology, old == null ? 0 : old.Mask, false))
                        if (picker.ShowDialog(this) == DialogResult.OK) cell.Value = Choice(picker.SelectedMask, useCpuSets);
                }, new Point(bounds.Left, bounds.Bottom));
            };
            note.Text = "未勾选的应用保持原样；可保存多套规则，运行时只启用一套。"; pane.Controls.Add(note, 0, 3);
            FlowLayoutPanel actions = Flow();
            actions.Controls.Add(GameQuestionDialog.MakeButton("保存方案", 120, delegate { SaveScheme(); }, true));
            actions.Controls.Add(GameQuestionDialog.MakeButton("启用方案", 120, delegate { EnableScheme(); }, false));
            actions.Controls.Add(GameQuestionDialog.MakeButton("删除方案", 120, delegate { DeleteScheme(); }, false));
            actions.Controls.Add(GameQuestionDialog.MakeButton("关闭", 90, delegate { Close(); }, false)); pane.Controls.Add(actions, 0, 4);
            schemes.SelectedIndexChanged += delegate { if (!loading) LoadRules(); };

            TableLayoutPanel gamePane = Pane(3); classification.Controls.Add(gamePane);
            gamePane.RowStyles.Add(new RowStyle(SizeType.Absolute, 62)); gamePane.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); gamePane.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            gamePane.Controls.Add(new Label { Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, Text = "游戏方案：已归类的游戏自动使用本机 " + CpuSelectionGroup.GameTarget(topology).Name + "。\r\n这里可归类当前已运行的应用，或更正以前的判断。“未分类”表示忘记；保存后不会立即分配。" }, 0, 0);
            games.Columns.Add(new DataGridViewTextBoxColumn { Name = "App", HeaderText = "应用", ReadOnly = true, FillWeight = 65 });
            games.Columns.Add(new DataGridViewButtonColumn { Name = "Class", HeaderText = "分类", FillWeight = 35 }); gamePane.Controls.Add(games, 0, 1);
            games.CellContentClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != games.Columns["Class"].Index) return;
                DataGridViewCell cell = games.Rows[e.RowIndex].Cells["Class"]; Rectangle bounds = games.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
                DialogStyle.Choices(games, new[] { "未分类", "游戏", "非游戏" }, delegate(string value) { cell.Value = value; }, new Point(bounds.Left, bounds.Bottom));
            };
            FlowLayoutPanel gameActions = Flow();
            gameActions.Controls.Add(GameQuestionDialog.MakeButton("保存分类", 120, delegate { SaveClassifications(); }, true));
            gameActions.Controls.Add(GameQuestionDialog.MakeButton("启用游戏方案", 150, delegate { if (SaveClassifications()) { ActivateId = "game"; Close(); } }, false));
            gameActions.Controls.Add(GameQuestionDialog.MakeButton("关闭", 90, delegate { Close(); }, false)); gamePane.Controls.Add(gameActions, 0, 2);
            ReloadSchemes(null); LoadClassifications();
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        }
        private static DataGridView CreateGrid()
        {
            DataGridView grid = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                RowTemplate = { Height = 38 }, ColumnHeadersHeight = 38, SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false };
            grid.CurrentCellDirtyStateChanged += delegate { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            DialogStyle.Grid(grid); return grid;
        }
        private static TableLayoutPanel Pane(int rows) { return new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = rows, ColumnCount = 1 }; }
        private static FlowLayoutPanel Flow() { return new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false }; }
        private void ReloadSchemes(string id)
        {
            loading = true; schemes.Items.Clear(); schemes.Items.Add(new SchemeChoice { Name = "＋ 新建方案" });
            schemes.Items.Add(new SchemeChoice { Id = "game", Name = "游戏" });
            foreach (SavedScheme saved in store.Read().Schemes) schemes.Items.Add(new SchemeChoice { Id = saved.Id, Name = saved.Name });
            schemes.SelectedIndex = 0;
            for (int i = 1; i < schemes.Items.Count; i++) if (((SchemeChoice)schemes.Items[i]).Id == id) schemes.SelectedIndex = i;
            loading = false; LoadRules();
        }
        private string Choice(ulong mask, bool useCpuSets = false)
        {
            MaskChoice choice = choices.FirstOrDefault(c => c.Mask == mask && c.UseCpuSets == useCpuSets);
            if (choice == null)
            {
                IntelHybridInfo hybrid = IntelHybridInfo.Detect(topology);
                string label = useCpuSets ? "CPU 集" : hybrid.Recognized && mask == hybrid.MaskFor(0, true) ? "仅大核（P 核）" :
                    CpuSelectionGroup.Describe(mask, CpuSelectionGroup.FromTopology(topology));
                choice = new MaskChoice { Mask = mask, UseCpuSets = useCpuSets, Text = mask == 0 ? "保持硬亲和性" : label + " · CPU " + Masks.Format(mask) };
                choices.Add(choice);
            }
            return choice.Text;
        }
        private void LoadRules()
        {
            rules.CancelEdit(); rules.CurrentCell = null;
            rules.Rows.Clear(); choices.Clear();
            if (topology.CanAssign)
            {
                if (IntelHybridInfo.Detect(topology).Recognized) Choice(IntelHybridInfo.Detect(topology).MaskFor(0, true));
                foreach (CpuSelectionGroup group in CpuSelectionGroup.FromTopology(topology)) Choice(group.Mask);
                Choice(topology.Available); if (selectedMask != 0) Choice(selectedMask);
            }
            string id = ((SchemeChoice)schemes.SelectedItem).Id;
            SavedScheme saved = id == "game" ? store.Read().GameOverrides : store.Read().Schemes.FirstOrDefault(s => s.Id == id); name.Text = id == "game" ? "游戏" : saved == null ? "" : saved.Name; name.ReadOnly = id == "game";
            List<SchemeRule> remaining = saved == null ? new List<SchemeRule>() : new List<SchemeRule>(saved.Rules);
            foreach (ApplicationGroup app in apps)
            {
                SavedApplication application;
                try { application = SavedApplication.From(app); } catch (InvalidOperationException) { continue; }
                List<SchemeRule> exact = remaining.Where(r => r.ExactPaths && r.Application.Matches(app)).ToList();
                foreach (SchemeRule rule in exact)
                {
                    AddRuleRow(rule.Application, true, rule.Mask, false, rule);
                    remaining.Remove(rule);
                    application.Paths.RemoveAll(path => rule.Application.Paths.Contains(path, StringComparer.OrdinalIgnoreCase));
                }
                if (application.Paths.Count == 0) continue;
                List<SchemeRule> matched = remaining.Where(r => r.Application.Matches(app)).ToList();
                foreach (SchemeRule item in matched) remaining.Remove(item);
                ulong current = app.Members.Where(p => p.ReadError == null).Select(p => p.Mask).Distinct().Count() == 1 ? app.Members.First(p => p.ReadError == null).Mask : 0;
                ulong mask = matched.Count > 0 ? matched[0].Mask : selectedMask != 0 ? selectedMask : current != 0 ? current : topology.Available;
                AddRuleRow(application, matched.Count > 0, mask, false);
            }
            foreach (SchemeRule rule in remaining) AddRuleRow(rule.Application, true, rule.Mask, true, rule);
            note.Text = saved != null && saved.TopologyKey != SchemeStore.Fingerprint(topology) ? "CPU 拓扑已变化：请逐项重新核对 CPU 范围，再保存；旧方案当前不能启用。" : "未勾选的应用保持原样；CPU 下拉框也包含主界面已选择的自定义范围。";
        }
        private void AddRuleRow(SavedApplication app, bool enabled, ulong mask, bool offline, SchemeRule saved = null)
        {
            int index = rules.Rows.Add(enabled, app.Name + (offline ? "（未运行）" : ""), Choice(mask, saved != null && saved.UseCpuSets));
            rules.Rows[index].Tag = new SchemeRule { Application = app, ExactPaths = saved != null && saved.ExactPaths,
                CpuSetsHex = saved == null ? null : saved.CpuSetsHex };
            rules.Rows[index].Cells["App"].ToolTipText = (saved != null && saved.CpuSets.HasValue ?
                ManualSchemeRules.DescribeCpu(saved.Mask, false, saved.CpuSets, null) + "\r\n" : "") + string.Join("\r\n", app.Paths);
        }
        private bool SaveScheme()
        {
            try
            {
                rules.EndEdit(); if (!topology.CanAssign) throw new InvalidOperationException("多处理器组当前不支持自动分配。");
                string id = ((SchemeChoice)schemes.SelectedItem).Id ?? Guid.NewGuid().ToString("N");
                SavedScheme saved = new SavedScheme { Id = id, Name = name.Text.Trim(), TopologyKey = SchemeStore.Fingerprint(topology) };
                foreach (DataGridViewRow row in rules.Rows)
                    if (Convert.ToBoolean(row.Cells["Use"].Value))
                    {
                        MaskChoice target = choices.FirstOrDefault(c => c.Text == Convert.ToString(row.Cells["Cpu"].Value));
                        if (target == null) throw new InvalidOperationException("请为勾选的应用选择 CPU 范围。");
                        var previous = (SchemeRule)row.Tag;
                        string cpuSets = target.UseCpuSets ? null : previous.CpuSetsHex;
                        if (target.Mask != 0 || cpuSets == null) Masks.Validate(target.Mask, topology.Available);
                        if (cpuSets != null && target.Mask != 0 && (previous.CpuSets.Value & ~target.Mask) != 0)
                            throw new InvalidOperationException("所选硬亲和性不能包含已保存的默认 CPU 集。请选择包含该 CPU 集的范围。");
                        saved.Rules.Add(new SchemeRule { Application = previous.Application, MaskHex = target.Mask.ToString("X16"),
                            ExactPaths = previous.ExactPaths || target.UseCpuSets, UseCpuSets = target.UseCpuSets, CpuSetsHex = cpuSets });
                    }
                PerformanceProfile performance = store.Read().PerformanceFor(id);
                if (saved.Rules.Count == 0 && id != "game" && (performance == null || !performance.Rules.Any(rule => rule.Priority != 0)))
                    throw new InvalidOperationException("请至少勾选一个应用并设置 CPU 范围。");
                store.Update(delegate(SchemeData data) { if (id == "game") data.GameOverrides = saved; else { data.Schemes.RemoveAll(s => s.Id == id); data.Schemes.Add(saved); } }); ReloadSchemes(id); note.Text = "已保存“" + saved.Name + "”。尚未启用，不会修改当前分配。"; return true;
            }
            catch (Exception error) { AppMessageBox.Show(this, Native.Explain(error), "无法保存方案", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
        }
        private void EnableScheme() { if (SaveScheme()) { ActivateId = ((SchemeChoice)schemes.SelectedItem).Id; Close(); } }
        private void DeleteScheme()
        {
            string id = ((SchemeChoice)schemes.SelectedItem).Id; if (id == null) return;
            if (AppMessageBox.Show(this, id == "game" ? "清空游戏方案的手动核心规则？游戏分类、当前分配和恢复记录会保留。" : "删除这个方案？当前进程的分配和原值恢复记录会保留。", id == "game" ? "清空手动规则" : "删除方案", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try { store.DeleteScheme(id); ReloadSchemes(null); }
            catch (Exception error) { AppMessageBox.Show(this, Native.Explain(error), "无法删除方案"); }
        }
        private void LoadClassifications()
        {
            games.Rows.Clear(); List<GameClassification> remaining = store.Read().Games;
            foreach (ApplicationGroup app in apps)
            {
                SavedApplication application; try { application = SavedApplication.From(app); } catch (InvalidOperationException) { continue; }
                List<GameClassification> matched = remaining.Where(g => g.Application.Matches(app)).ToList(); foreach (GameClassification item in matched) remaining.Remove(item);
                int index = games.Rows.Add(app.Title, matched.Count == 0 ? "未分类" : matched.All(g => g.IsGame) ? "游戏" : "非游戏"); games.Rows[index].Tag = application;
                games.Rows[index].Cells[0].ToolTipText = string.Join("\r\n", application.Paths);
            }
            foreach (GameClassification saved in remaining)
            { int index = games.Rows.Add(saved.Application.Name + "（未运行）", saved.IsGame ? "游戏" : "非游戏"); games.Rows[index].Tag = saved.Application; games.Rows[index].Cells[0].ToolTipText = string.Join("\r\n", saved.Application.Paths); }
        }
        private bool SaveClassifications()
        {
            try
            {
                games.EndEdit(); List<GameClassification> saved = new List<GameClassification>();
                foreach (DataGridViewRow row in games.Rows)
                { string value = Convert.ToString(row.Cells[1].Value); if (value != "未分类") saved.Add(new GameClassification { Application = (SavedApplication)row.Tag, IsGame = value == "游戏" }); }
                store.Update(delegate(SchemeData data) { data.Games = saved; }); LoadClassifications(); return true;
            }
            catch (Exception error) { AppMessageBox.Show(this, Native.Explain(error), "无法保存游戏分类", MessageBoxButtons.OK, MessageBoxIcon.Warning); return false; }
        }
    }
}
