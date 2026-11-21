using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CoreAllocator
{
    // A draft only: changing these boxes never calls the process/affinity service.
    internal sealed class CpuMaskDialog : Form
    {
        private readonly Dictionary<CheckBox, int> boxes = new Dictionary<CheckBox, int>();
        private readonly Label summary = new Label { Dock = DockStyle.Fill, ForeColor = UiTheme.Blue, AutoEllipsis = true };
        private readonly SoftButton save;
        private readonly Topology topology;
        private readonly bool editable;
        private readonly bool direct;
        internal ulong SelectedMask;
        private readonly ApplicationPriorityEditor priorities;
        private readonly CapsuleSchemePicker allocationMode;
        internal bool UsesCpuSets { get { return allocationMode != null && allocationMode.SelectedIndex == 1; } }
        internal Dictionary<string, uint> PriorityChanges { get { return priorities == null ? new Dictionary<string, uint>() : priorities.Changes; } }

        internal CpuMaskDialog(Topology topology, ulong initial, bool readOnly) : this(topology, initial, readOnly, null, null, true) { }
        internal CpuMaskDialog(Topology topology, ulong initial, bool readOnly, string target, ProcessVisual visual, bool unified, string targetDetails = null, ProcessRow[] targets = null, bool canSetPriority = false, bool canSetCpuSets = false)
        {
            DoubleBuffered = ResizeRedraw = true; SuspendLayout(); this.topology = topology; editable = topology.CanAssign && !readOnly; SelectedMask = initial; direct = target != null;
            IntelHybridInfo hybrid = IntelHybridInfo.Detect(topology);
            Text = readOnly ? "CPU 与设备信息" : direct ? "为应用选择 CPU" : "自定义 CPU"; Font = new Font("Microsoft YaHei UI", 9.5F); BackColor = UiTheme.Page; ForeColor = UiTheme.Ink;
            bool combined = targets != null && direct && editable;
            if (combined) Text = "应用设置";
            StartPosition = FormStartPosition.CenterParent; ClientSize = new Size(800, 620); MinimumSize = new Size(650, 580); ShowIcon = false;
            MinimizeBox = false; MaximizeBox = false;
            if (combined) ClientSize = new Size(800, 740);
            TableLayoutPanel root = new RepaintTable { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 6 };
            int[] heights = combined ? new[] { 64, 70, -1, 46, 46 } : new[] { 64, 78, -1, 64, 46 };
            root.RowCount = heights.Length;
            foreach (int height in heights) root.RowStyles.Add(new RowStyle(height < 0 ? SizeType.Percent : SizeType.Absolute, height < 0 ? 100 : height)); Controls.Add(root);
            TableLayoutPanel heading = new RepaintTable { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, direct ? 62 : 0)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            if (direct) heading.Controls.Add(new ApplicationIconBadge(visual, unified) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 8) }, 0, 0);
            heading.Controls.Add(DialogUi.Label(combined ? target : direct ? "为 " + target + " 选择 CPU" : Text, 17, UiTheme.Ink), 1, 0); root.Controls.Add(heading, 0, 0);
            RoundedSurface info = new RoundedSurface { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(14, 5, 14, 5), Margin = new Padding(0, 0, 0, 8) };
            Label infoText = DialogUi.Label(direct ? (initial == 0 ? "范围混合或无法读取，请重新选择 CPU。" : "最近读取的允许范围：CPU " + Masks.Format(initial)) + "\r\n包含：" + (targetDetails ?? target) : topology.CpuName + "\r\n" + topology.Cores.Select(c => c.Number).Distinct().Count() + " 物理核心 · " + topology.Groups.Sum(g => Masks.Bits(g.Mask).Count()) + " 逻辑 CPU\r\n" + hybrid.Reason, 9, UiTheme.Muted);
            info.Controls.Add(infoText); root.Controls.Add(info, 0, 1);
            FlowLayoutPanel presets = new FlowLayoutPanel { WrapContents = false };
            SoftButton onlyPerformance = GameQuestionDialog.MakeButton("仅大核（P）", 130, delegate { SelectMask(hybrid.MaskFor(0, true)); }, false); onlyPerformance.Enabled = editable && hybrid.Recognized;
            onlyPerformance.Visible = hybrid.Recognized; presets.Controls.Add(onlyPerformance);
            if (combined && canSetCpuSets)
            {
                allocationMode = new CapsuleSchemePicker { Width = 206, Height = 36, AccessibleName = "CPU 分配方式" };
                allocationMode.Items.AddRange(new object[] { "硬亲和性", "CPU 集（调度范围）" });
                ulong? previous = targets[0].CpuSetMask;
                bool useExisting = previous.GetValueOrDefault() != 0 && targets.All(p => p.CpuSetMask == previous);
                allocationMode.SelectedIndex = useExisting ? 1 : 0;
                if (useExisting) initial = previous.Value & topology.Available;
                allocationMode.SelectedIndexChanged += delegate { UpdateSelection(); };
                presets.Controls.Add(allocationMode);
                infoText.Text = (useExisting ? "当前默认 CPU 集：CPU " + Masks.Format(previous.Value) + "\r\n" : "") + infoText.Text;
            }
            FlowLayoutPanel groups = new FlowLayoutPanel { WrapContents = true, Margin = Padding.Empty };
            CpuCoreSweep sweep = new CpuCoreSweep();
            ToolTip tips = new ToolTip();
            if (direct) tips.SetToolTip(infoText, targetDetails ?? target);
            foreach (CpuSelectionGroup group in CpuSelectionGroup.FromTopology(topology))
            {
                int[] cpus = Masks.Bits(group.Mask).ToArray(); int rows = (cpus.Length + 3) / 4;
                TableLayoutPanel section = new CoreGroupPanel { BackColor = Color.White, ColumnCount = 4, RowCount = rows + 1, Width = 350, Height = 60 + rows * 42, Padding = new Padding(10), Margin = new Padding(0, 0, 12, 12) };
                for (int i = 0; i < 4; i++) section.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
                section.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); for (int i = 0; i < rows; i++) section.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                Label groupTitle = new Label { Text = group.Name + (group.ClusterLabel == null ? "" : " · " + group.ClusterLabel) + " · G" + group.WindowsGroup, AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiTheme.Muted };
                tips.SetToolTip(groupTitle, "Windows G" + group.WindowsGroup + (group.CacheBytes == 0 ? "" : " · L3 " + (group.CacheBytes / 1048576.0).ToString("0.#") + " MB"));
                section.Controls.Add(groupTitle, 0, 0); section.SetColumnSpan(groupTitle, 2);
                List<CheckBox> groupBoxes = new List<CheckBox>();
                SoftButton all = GameQuestionDialog.MakeButton(UsesCpuSets ? "仅此组" : "全选本组", 110, delegate
                {
                    if (UsesCpuSets) SelectMask(group.Mask);
                    else foreach (CheckBox box in groupBoxes) box.Checked = true;
                }, false);
                if (allocationMode != null)
                    allocationMode.SelectedIndexChanged += delegate { all.Text = UsesCpuSets ? "仅此组" : "全选本组"; all.Invalidate(); };
                all.Dock = DockStyle.Fill; all.Enabled = editable; section.Controls.Add(all, 2, 0); section.SetColumnSpan(all, 2);
                for (int i = 0; i < cpus.Length; i++)
                {
                    int cpu = cpus[i];
                    CheckBox box = new CoreCheckBox { Accent = group.Index % 2 == 0 ? UiTheme.Blue : UiTheme.Purple, Text = "CPU " + cpu, Dock = DockStyle.Fill, Margin = new Padding(3), Enabled = editable, Checked = group.WindowsGroup == 0 && (initial & (1UL << cpu)) != 0, AccessibleName = "Windows G" + group.WindowsGroup + " 逻辑 CPU " + cpu };
                    box.FlatAppearance.BorderSize = 0; box.BackColor = box.Checked ? UiTheme.BlueWash : Color.White; box.ForeColor = box.Checked ? UiTheme.Blue : UiTheme.Ink;
                    box.CheckedChanged += delegate { box.BackColor = box.Checked ? UiTheme.BlueWash : Color.White; box.ForeColor = box.Checked ? UiTheme.Blue : UiTheme.Ink; UpdateSelection(); };
                    CpuCore core = topology.Cores.FirstOrDefault(c => c.Group == group.WindowsGroup && (c.Mask & (1UL << cpu)) != 0);
                    string kind = hybrid.CoreLabel(core);
                    if (kind.Length > 0) { box.Text += kind == "大核 P" ? " · P" : " · E"; box.AccessibleName += " " + kind; }
                    tips.SetToolTip(box, core == null ? "Windows G" + group.WindowsGroup + " · 逻辑 CPU " + cpu : "Windows G" + group.WindowsGroup + " · 物理核心 " + core.Number + " · 同核逻辑 CPU " + Masks.Format(core.Mask) + (kind.Length == 0 ? "" : " · " + kind + " · Windows 等级 " + core.EfficiencyClass));
                    boxes.Add(box, cpu); groupBoxes.Add(box); section.Controls.Add(box, i % 4, 1 + i / 4); sweep.Attach((CoreCheckBox)box);
                }
                groups.Controls.Add(section);
            }
            CapsuleScrollPanel body = new CapsuleScrollPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, AccessibleName = "应用设置内容" };
            Panel stack = body.Content; root.Controls.Add(body, 0, 2);
            SettingsSectionHeader coreHeader = new SettingsSectionHeader("核心分配", UiGlyph.Cpu) { Expanded = !combined };
            SettingsSectionHeader priorityHeader = new SettingsSectionHeader("进程优先级", UiGlyph.Adjust);
            Panel coreContent = new RepaintPanel { Margin = Padding.Empty }; coreContent.Controls.Add(groups); coreContent.Controls.Add(presets);
            stack.Controls.Add(coreHeader); stack.Controls.Add(coreContent);
            if (combined) { priorities = new ApplicationPriorityEditor(targets, canSetPriority); stack.Controls.Add(priorityHeader); stack.Controls.Add(priorities); }
            bool layingOut = false;
            Action arrange = delegate
            {
                if (layingOut) return; layingOut = true;
                try
                {
                    float scale = UiTheme.Scale(this); int gap = (int)(10 * scale), head = (int)(48 * scale);
                    int width = body.ContentWidth;
                    info.Margin = new Padding(0, 0, body.RailWidth, (int)Math.Round(8 * scale));
                    stack.Width = width; int y = 0;
                    coreHeader.SetBounds(0, y, width, head); y += head + gap;
                    coreContent.Visible = coreHeader.Expanded;
                    if (coreHeader.Expanded)
                    {
                        int columns = width >= 650 * scale ? 2 : 1, groupHeight = 0, rowHeight = 0, index = 0;
                        groups.Width = width;
                        groups.SuspendLayout();
                        foreach (Control section in groups.Controls)
                        {
                            int columnGap = (int)Math.Round(12 * scale);
                            section.Margin = new Padding(0, 0, columns == 2 && index % 2 == 0 ? columnGap : 0, columnGap);
                            section.Width = columns == 1 ? width : index % 2 == 0 ? (width - columnGap) / 2 : width - columnGap - (width - columnGap) / 2;
                            rowHeight = Math.Max(rowHeight, section.Height + section.Margin.Vertical);
                            if (++index % columns == 0) { groupHeight += rowHeight; rowHeight = 0; }
                        }
                        groupHeight += rowHeight; groups.Height = groupHeight; groups.ResumeLayout(true);
                        bool showPresets = hybrid.Recognized || allocationMode != null;
                        int presetHeight = showPresets ? (int)(44 * scale) : 0;
                        presets.Visible = showPresets; presets.SetBounds(0, 0, width, presetHeight);
                        groups.Location = new Point(0, presetHeight);
                        coreContent.SetBounds(0, y, width, groupHeight + presetHeight); y += coreContent.Height + gap;
                    }
                    if (combined)
                    {
                        priorityHeader.SetBounds(0, y, width, head); y += head + gap;
                        priorities.Visible = priorityHeader.Expanded;
                        if (priorityHeader.Expanded) { priorities.SetBounds(0, y, width, priorities.MeasureHeight(scale)); y += priorities.Height + gap; }
                    }
                    body.SetContentHeight(y); body.Invalidate(true);
                }
                finally { layingOut = false; }
            };
            coreHeader.Click += delegate { sweep.End(); coreHeader.Expanded = !coreHeader.Expanded; arrange(); };
            priorityHeader.Click += delegate { priorityHeader.Expanded = !priorityHeader.Expanded; arrange(); };
            body.ViewportChanged += delegate { arrange(); }; body.ViewScrolled += delegate { sweep.End(); };
            Shown += delegate { arrange(); };
            root.Controls.Add(summary, 0, 3);
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            save = GameQuestionDialog.MakeButton(combined ? "应用设置" : direct ? "分配给此应用" : "确认选择", 150, delegate { Masks.Validate(SelectedMask, topology.Available); DialogResult = DialogResult.OK; }, true); save.Visible = editable;
            SoftButton cancel = GameQuestionDialog.MakeButton(readOnly ? "关闭" : "取消", 100, delegate { DialogResult = DialogResult.Cancel; }, false); CancelButton = cancel;
            actions.Controls.Add(save); actions.Controls.Add(cancel); root.Controls.Add(actions, 0, 4); ActiveControl = cancel;
            FormClosed += delegate { sweep.End(); tips.Dispose(); }; UpdateSelection();
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi; ResumeLayout(true);
        }
        private void SelectMask(ulong mask)
        {
            if (!editable || mask == 0) return;
            foreach (KeyValuePair<CheckBox, int> pair in boxes) pair.Key.Checked = (mask & (1UL << pair.Value)) != 0;
        }
        private void UpdateSelection()
        {
            if (editable)
            {
                SelectedMask = 0; foreach (KeyValuePair<CheckBox, int> pair in boxes) if (pair.Key.Checked) SelectedMask |= 1UL << pair.Value;
                string description = UsesCpuSets ? "CPU 集适用于未单独指定 CPU 集的线程，仍受硬亲和性限制。" :
                    direct ? "点击分配后，只修改打开窗口时选中的进程。" : "确认后将所选范围填入规则。";
                summary.Text = "已选 " + Masks.Bits(SelectedMask).Count() + " 个逻辑 CPU · CPU " + Masks.Format(SelectedMask) + "\r\n" + description;
            }
            else summary.Text = topology.CanAssign ? "仅查看 CPU 拓扑。CPU 编号表示逻辑处理器。" : "当前多处理器组仅供查看，分配与恢复已禁用。";
            if (save != null) save.Enabled = editable && SelectedMask != 0;
        }
    }
}
