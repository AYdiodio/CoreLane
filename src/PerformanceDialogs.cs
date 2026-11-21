using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal sealed class ApplicationPriorityEditor : Panel
    {
        private readonly Dictionary<ProcessRow, CapsuleSchemePicker> pickers = new Dictionary<ProcessRow, CapsuleSchemePicker>();
        private readonly List<Panel> rows = new List<Panel>();
        private readonly List<Label> labels = new List<Label>();
        private readonly RoundedSurface surface = new RoundedSurface { BackColor = Color.White, Margin = Padding.Empty };
        private readonly Label bulkLabel = DialogUi.Label("统一设置", 10, UiTheme.Muted);
        private readonly CapsuleSchemePicker bulk;
        private static readonly uint[] values = { 0, PriorityLevels.BelowNormal, PriorityLevels.Normal, PriorityLevels.AboveNormal };
        internal Dictionary<string, uint> Changes
        {
            get { return pickers.Where(pair => pair.Value.Enabled && values[pair.Value.SelectedIndex] != 0 &&
                values[pair.Value.SelectedIndex] != pair.Key.Priority).ToDictionary(pair => pair.Key.Key, pair => values[pair.Value.SelectedIndex]); }
        }
        internal ApplicationPriorityEditor(ProcessRow[] targets, bool canSet)
        {
            DoubleBuffered = ResizeRedraw = true; BackColor = UiTheme.Page; Margin = Padding.Empty;
            bulk = Picker("统一设置进程优先级", 0); bulk.Enabled = canSet;
            bulkLabel.Dock = DockStyle.None;
            Controls.Add(bulkLabel); Controls.Add(bulk); Controls.Add(surface);
            foreach (ProcessRow target in targets)
            {
                Panel row = new RepaintPanel { BackColor = Color.White, Margin = Padding.Empty };
                Label label = DialogUi.Label(target.Name + " · PID " + target.Pid + "\r\n当前 " + PriorityLevels.Describe(target.Priority), 9, UiTheme.Ink); label.Dock = DockStyle.None;
                row.Controls.Add(label); labels.Add(label); rows.Add(row);
                CapsuleSchemePicker picker = Picker(target.Key + " 进程优先级", Array.IndexOf(values, target.Priority));
                picker.Enabled = canSet && PerformanceProfile.EligiblePath(target.ExecutablePath);
                pickers.Add(target, picker); row.Controls.Add(picker); surface.Controls.Add(row);
            }
            bulk.SelectedIndexChanged += delegate
            {
                foreach (CapsuleSchemePicker picker in pickers.Values.Where(picker => picker.Enabled)) picker.SelectedIndex = bulk.SelectedIndex;
            };
            PerformLayout();
        }
        internal int MeasureHeight(float scale) { return (int)Math.Round((48 + 24 + rows.Count * 76) * scale); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (bulk == null) return;
            float s = UiTheme.Scale(this); int inset = (int)Math.Round(12 * s), rowHeight = (int)Math.Round(76 * s), pickerHeight = (int)Math.Round(36 * s);
            int pickerWidth = Math.Min((int)Math.Round(180 * s), Math.Max(1, Width / 2)), pickerX = Math.Max(inset, Width - inset - pickerWidth);
            bulkLabel.SetBounds(inset, 0, Math.Max(1, pickerX - 2 * inset), (int)(40 * s));
            bulk.SetBounds(pickerX, (int)(2 * s), pickerWidth, pickerHeight);
            surface.SetBounds(0, (int)Math.Round(48 * s), Width, 2 * inset + rows.Count * rowHeight);
            int index = 0;
            foreach (CapsuleSchemePicker picker in pickers.Values)
            {
                rows[index].SetBounds(inset, inset + index * rowHeight, Math.Max(1, Width - 2 * inset), rowHeight);
                labels[index].SetBounds(0, 0, Math.Max(1, pickerX - 2 * inset), rowHeight);
                picker.SetBounds(pickerX - inset, (rowHeight - pickerHeight) / 2, pickerWidth, pickerHeight); index++;
            }
            surface.Invalidate(true); Invalidate(true);
        }
        private static CapsuleSchemePicker Picker(string label, int initial)
        {
            CapsuleSchemePicker picker = new CapsuleSchemePicker { AccessibleName = label };
            picker.Items.AddRange(new object[] { "保持现状", "低于正常", "正常", "高于正常" });
            picker.SelectedIndex = Math.Max(0, initial);
            return picker;
        }
    }

    internal sealed class PriorityDialog : Form
    {
        internal uint SelectedPriority { get; private set; }

        internal PriorityDialog(ProcessRow[] targets)
        {
            DialogUi.Setup(this, "设置进程优先级", new Size(535, 275));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            var layout = DialogUi.Rows(42, 66, 52, -1, 48);
            Controls.Add(layout);
            layout.Controls.Add(DialogUi.Label("设置进程优先级", 18, UiTheme.Ink), 0, 0);
            layout.Controls.Add(DialogUi.Label("影响 " + targets.Length + " 个当前进程。可通过「恢复原设置」交还 Windows 调度。", 9.5F, UiTheme.Muted), 0, 1);
            var picker = new CapsuleSchemePicker { Dock = DockStyle.Left, Width = 205, AccessibleName = "选择进程优先级" };
            picker.Items.AddRange(PerformanceDialog.Levels.Select(value => (object)PriorityLevels.Describe(value)));
            int existing = Array.IndexOf(PerformanceDialog.Levels, targets[0].Priority);
            picker.SelectedIndex = existing >= 0 && targets.All(row => row.Priority == targets[0].Priority) ? existing : 1;
            layout.Controls.Add(picker, 0, 2);
            var actions = DialogUi.Actions();
            var apply = GameQuestionDialog.MakeButton("应用优先级", 126, delegate
            {
                SelectedPriority = PerformanceDialog.Levels[picker.SelectedIndex];
                DialogResult = DialogResult.OK;
            }, true);
            var cancel = GameQuestionDialog.MakeButton("取消", 86, delegate { DialogResult = DialogResult.Cancel; }, false);
            actions.Controls.Add(apply);
            actions.Controls.Add(cancel);
            layout.Controls.Add(actions, 0, 4);
            CancelButton = cancel;
        }
    }

    internal sealed class PerformanceDialog : Form
    {
        internal static readonly uint[] Levels = { PriorityLevels.BelowNormal, PriorityLevels.Normal, PriorityLevels.AboveNormal };
        private readonly SchemeStore store;
        private readonly string schemeId;
        private readonly DataGridView grid = new DataGridView();
        private readonly CoreCheckBox guard = new CoreCheckBox { Width = 146, Height = 36 };
        private readonly CapsuleSchemePicker threshold = new CapsuleSchemePicker { Width = 108, Height = 36, AccessibleName = "后台负载阈值" };
        private readonly CapsuleSchemePicker duration = new CapsuleSchemePicker { Width = 108, Height = 36, AccessibleName = "持续秒数" };
        private readonly uint[] values = new[] { 0U }.Concat(Levels).ToArray();
        private readonly string[] levelNames = new[] { "保持现状" }.Concat(Levels.Select(PriorityLevels.Describe)).ToArray();
        internal readonly string SavedName;

        internal PerformanceDialog(SchemeStore store, string schemeId, string name, IEnumerable<ProcessRow> processes)
        {
            this.store = store;
            this.schemeId = schemeId;
            SavedName = name;
            PerformanceProfile saved = store.Read().PerformanceFor(schemeId) ?? new PerformanceProfile { SchemeId = schemeId };
            DialogUi.Setup(this, "优先级与后台保护", new Size(810, 625));
            MinimumSize = new Size(740, 540);
            var layout = DialogUi.Rows(42, 32, 47, 50, 45, -1, 48);
            Controls.Add(layout);
            layout.Controls.Add(DialogUi.Label("优先级与后台保护", 18, UiTheme.Ink), 0, 0);
            layout.Controls.Add(DialogUi.Label("方案 · " + name, 11F, UiTheme.Purple), 0, 1);
            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            guard.Accent = UiTheme.Purple;
            guard.Checked = saved.GuardEnabled;
            guard.Text = guard.Checked ? "后台保护已开启" : "开启后台保护";
            guard.CheckedChanged += delegate { guard.Text = guard.Checked ? "后台保护已开启" : "开启后台保护"; };
            options.Controls.Add(guard);
            AddOption(options, "负载阈值", threshold, new[] { 40, 50, 65, 80, 90, 100 }, saved.Threshold, "%");
            AddOption(options, "持续时间", duration, new[] { 5, 8, 10, 15, 20, 30, 60 }, saved.Duration, " 秒");
            layout.Controls.Add(options, 0, 2);
            layout.Controls.Add(DialogUi.Label("触发应用运行时，勾选的后台目标持续超载才会临时降为「低于正常」。100% 指占满一个逻辑 CPU。\r\n持续低于一半阈值、触发应用退出或方案暂停时恢复。保存后由你启用方案。", 9F, UiTheme.Muted), 0, 3);
            var search = new SearchTextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 8) };
            search.AccessibleName = "搜索程序名称或路径";
            search.TextChanged += delegate
            {
                grid.EndEdit();
                grid.CurrentCell = null;
                string query = search.Text.Trim();
                foreach (DataGridViewRow row in grid.Rows)
                    row.Visible = ((string)row.Tag).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
            };
            layout.Controls.Add(search, 0, 4);
            var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(10), Margin = new Padding(0, 0, 0, 8) };
            ConfigureGrid(saved, processes);
            card.Controls.Add(grid);
            layout.Controls.Add(card, 0, 5);
            var actions = DialogUi.Actions();
            var cancel = GameQuestionDialog.MakeButton("取消", 86, delegate { DialogResult = DialogResult.Cancel; }, false);
            actions.Controls.Add(GameQuestionDialog.MakeButton("保存设置", 120, delegate { Save(); }, true));
            actions.Controls.Add(cancel);
            layout.Controls.Add(actions, 0, 6);
            CancelButton = cancel;
        }

        private sealed class Option
        {
            internal int Value;
            internal string Suffix;
            public override string ToString() { return Value + Suffix; }
        }

        private static void AddOption(FlowLayoutPanel options, string caption, CapsuleSchemePicker picker, int[] choices, int value, string suffix)
        {
            options.Controls.Add(new Label { Text = caption, AutoSize = false, Width = 70, Height = 36, TextAlign = ContentAlignment.MiddleRight, ForeColor = UiTheme.Muted });
            int[] numbers = choices.Concat(new[] { value }).Distinct().OrderBy(number => number).ToArray();
            picker.Items.AddRange(numbers.Select(number => (object)new Option { Value = number, Suffix = suffix }));
            picker.SelectedIndex = Array.IndexOf(numbers, value);
            options.Controls.Add(picker);
        }

        private void ConfigureGrid(PerformanceProfile saved, IEnumerable<ProcessRow> processes)
        {
            grid.Dock = DockStyle.Fill;
            grid.BackgroundColor = Color.White;
            grid.BorderStyle = BorderStyle.None;
            grid.RowHeadersVisible = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.MultiSelect = false;
            DialogStyle.Grid(grid);
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "程序 · 悬停查看路径", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
            grid.Columns.Add(new DataGridViewButtonColumn { HeaderText = "方案优先级", Width = 152 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "触发应用", Width = 106 });
            grid.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "后台目标", Width = 106 });
            foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
            var byPath = saved.Rules.ToDictionary(rule => rule.Path, StringComparer.OrdinalIgnoreCase);
            var paths = processes.Select(row => row.ExecutablePath).Where(PerformanceProfile.EligiblePath).Concat(byPath.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(path => !byPath.ContainsKey(path)).ThenBy(Path.GetFileName, StringComparer.CurrentCultureIgnoreCase);
            foreach (string path in paths)
            {
                PerformanceRule rule;
                if (!byPath.TryGetValue(path, out rule)) rule = new PerformanceRule { Path = path };
                DataGridViewRow row = grid.Rows[grid.Rows.Add(Path.GetFileName(path), levelNames[Array.IndexOf(values, rule.Priority)], rule.Trigger, rule.Background)];
                row.Tag = path;
                row.Cells[0].ToolTipText = path;
            }
            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            grid.CellValueChanged += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || (e.ColumnIndex != 2 && e.ColumnIndex != 3)) return;
                DataGridViewRow row = grid.Rows[e.RowIndex];
                if (Convert.ToBoolean(row.Cells[e.ColumnIndex].Value)) row.Cells[e.ColumnIndex == 2 ? 3 : 2].Value = false;
            };
            grid.CellContentClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 1) return;
                DataGridViewCell cell = grid.Rows[e.RowIndex].Cells[1];
                Rectangle bounds = grid.GetCellDisplayRectangle(1, e.RowIndex, true);
                DialogStyle.Choices(grid, levelNames, choice => cell.Value = choice, new Point(bounds.Left, bounds.Bottom));
            };
        }

        private void Save()
        {
            grid.EndEdit();
            var profile = new PerformanceProfile { SchemeId = schemeId, GuardEnabled = guard.Checked,
                Threshold = ((Option)threshold.SelectedItem).Value, Duration = ((Option)duration.SelectedItem).Value };
            foreach (DataGridViewRow row in grid.Rows)
            {
                uint priority = values[Array.IndexOf(levelNames, (string)row.Cells[1].Value)];
                bool trigger = Convert.ToBoolean(row.Cells[2].Value);
                bool background = Convert.ToBoolean(row.Cells[3].Value);
                if (priority != 0 || trigger || background)
                    profile.Rules.Add(new PerformanceRule { Path = (string)row.Tag, Priority = priority, Trigger = trigger, Background = background });
            }
            try
            {
                store.Update(data =>
                {
                    if (data.Performance == null) data.Performance = new List<PerformanceProfile>();
                    data.Performance.RemoveAll(item => item.SchemeId == schemeId);
                    data.Performance.Add(profile);
                });
                DialogResult = DialogResult.OK;
            }
            catch (Exception error) { AppMessageBox.Show(this, Native.Explain(error), "无法保存设置"); }
        }
    }
}
