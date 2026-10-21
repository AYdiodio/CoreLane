using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Security.Principal;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal sealed class MainForm : Form
    {
        private readonly AffinityService service;
        private readonly Color ink = Color.FromArgb(25, 39, 60);
        private readonly Color muted = Color.FromArgb(89, 107, 128);
        private readonly Color accent = Color.FromArgb(28, 99, 211);
        private readonly DataGridView grid = new DataGridView();
        private readonly TextBox search = new TextBox();
        private readonly RichTextBox log = new RichTextBox();
        private readonly Label selection = new Label();
        private readonly Label status = new Label();
        private readonly Label processInfo = new Label();
        private readonly FlowLayoutPanel cpuPanel = new FlowLayoutPanel();
        private readonly List<CheckBox> cpuBoxes = new List<CheckBox>();
        private readonly ComboBox clusters = new ComboBox();
        private readonly Dictionary<string, string> results = new Dictionary<string, string>();
        private readonly ToolTip tips = new ToolTip();
        private readonly Button apply;
        private readonly Button restoreSelected;
        private readonly Button restoreAll;
        private List<ProcessRow> processes = new List<ProcessRow>();
        private bool busy;
        private int unavailable;

        internal MainForm(AffinityService service)
        {
            SuspendLayout();
            this.service = service;
            Text = "核心分配工具 · v0.1.1";
            Font = new Font("Microsoft YaHei UI", 9F);
            ForeColor = ink;
            BackColor = Color.FromArgb(243, 246, 250);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1240, 850);
            MinimumSize = new Size(1060, 740);

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(18), BackColor = BackColor };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            Controls.Add(root);

            Panel header = new Panel { Dock = DockStyle.Fill, BackColor = ink, Margin = new Padding(0, 0, 0, 10) };
            Label title = Label("核心分配", 19, Color.White);
            title.Dock = DockStyle.None;
            title.SetBounds(18, 7, 220, 33);
            Label subtitle = Label("选择进程 → 选择逻辑 CPU → 手动分配", 9, Color.FromArgb(196, 211, 233));
            subtitle.Dock = DockStyle.None;
            subtitle.SetBounds(20, 40, 650, 20);
            header.Controls.Add(title); header.Controls.Add(subtitle);
            Label version = Label("WINDOWS 11  /  x64  /  v0.1.1", 9, Color.FromArgb(196, 211, 233));
            version.Dock = DockStyle.None;
            version.AutoSize = true; version.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            header.Controls.Add(version);
            header.Resize += delegate { version.Location = new Point(header.Width - version.PreferredWidth - 20, 22); };
            root.Controls.Add(header, 0, 0);

            TableLayoutPanel hardware = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.White, Padding = new Padding(12, 6, 12, 6), Margin = new Padding(0, 0, 0, 10) };
            hardware.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            hardware.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            hardware.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            hardware.Controls.Add(Label(service.Topology.CpuName, 11, ink), 0, 0);
            int logical = service.Topology.Groups.Sum(g => Masks.Bits(g.Mask).Count());
            int physical = service.Topology.Cores.Select(c => c.Number).Distinct().Count();
            bool admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
            hardware.Controls.Add(Label(physical + " 物理核心  ·  " + logical + " 逻辑 CPU  ·  " + service.Topology.Groups.Count + " 个 Windows 处理器组  ·  " + service.Topology.Clusters.Count + " 个 L3 共享组  ·  " + (admin ? "管理员权限" : "普通用户权限"), 9, muted), 0, 1);
            hardware.Controls.Add(Label(service.Topology.CanAssign ? "本机拓扑已读取。L3 共享组用于辅助选择；CPU 编号指逻辑处理器。" : "此机器有多个处理器组：可查看拓扑，第一版已禁用亲和性修改。", 9, service.Topology.CanAssign ? muted : Color.DarkRed), 0, 2);
            root.Controls.Add(hardware, 0, 1);

            TableLayoutPanel body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 10) };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 316));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.Controls.Add(body, 0, 2);

            TableLayoutPanel cpu = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, BackColor = Color.White, Padding = new Padding(12), Margin = new Padding(0, 0, 12, 0) };
            cpu.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            cpu.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            cpu.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            cpu.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            cpu.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            cpu.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            cpu.Controls.Add(Label("01  选择目标 CPU", 11, ink), 0, 0);
            clusters.Dock = DockStyle.Fill; clusters.DropDownStyle = ComboBoxStyle.DropDownList;
            clusters.Items.Add("按实际 L3 共享组选择…");
            foreach (CpuCluster cluster in service.Topology.Clusters) clusters.Items.Add(cluster);
            clusters.SelectedIndex = 0;
            clusters.DropDownWidth = 520;
            clusters.SelectedIndexChanged += delegate
            {
                CpuCluster cluster = clusters.SelectedItem as CpuCluster;
                if (cluster != null) SetCpuSelection(cluster.Group, cluster.Mask);
            };
            cpu.Controls.Add(clusters, 0, 1);
            FlowLayoutPanel cpuActions = Flow();
            cpuActions.Controls.Add(Button("全选", delegate { SetCpuSelection(0, service.Topology.Available); }, false, 72));
            cpuActions.Controls.Add(Button("清空", delegate { SetCpuSelection(0, 0); }, false, 72));
            cpu.Controls.Add(cpuActions, 0, 2);
            cpuPanel.Dock = DockStyle.Fill; cpuPanel.AutoScroll = true; cpuPanel.WrapContents = true; cpuPanel.Margin = new Padding(0);
            foreach (CpuGroup group in service.Topology.Groups)
            {
                foreach (int bit in Masks.Bits(group.Mask))
                {
                    CheckBox box = new CheckBox { Text = service.Topology.Groups.Count == 1 ? "CPU " + bit : "G" + group.Number + ":" + bit,
                        Appearance = Appearance.Button, TextAlign = ContentAlignment.MiddleCenter, Size = new Size(60, 27), Margin = new Padding(2),
                        FlatStyle = FlatStyle.Flat, Tag = new Tuple<int, int>(group.Number, bit), Enabled = service.Topology.CanAssign };
                    box.FlatAppearance.BorderColor = Color.FromArgb(216, 225, 236);
                    box.CheckedChanged += delegate { box.BackColor = box.Checked ? Color.FromArgb(222, 235, 255) : Color.White; UpdateState(); };
                    CpuCore core = service.Topology.Cores.FirstOrDefault(c => c.Group == group.Number && (c.Mask & (1UL << bit)) != 0);
                    tips.SetToolTip(box, "Windows 组 " + group.Number + " / 逻辑 CPU " + bit + (core == null ? "" : "\r\n物理核心 " + core.Number + " · 同核逻辑 CPU " + Masks.Format(core.Mask)));
                    cpuBoxes.Add(box); cpuPanel.Controls.Add(box);
                }
            }
            cpu.Controls.Add(cpuPanel, 0, 3);
            selection.Dock = DockStyle.Fill; selection.ForeColor = accent;
            cpu.Controls.Add(selection, 0, 4);
            Label cpuHint = Label("选择只改变目标范围。\r\n点击“分配”才会修改进程。", 8, muted);
            cpu.Controls.Add(cpuHint, 0, 5);
            body.Controls.Add(cpu, 0, 0);

            TableLayoutPanel processPane = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Color.White, Padding = new Padding(12), Margin = new Padding(0) };
            processPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            processPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            processPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            processPane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            processPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            FlowLayoutPanel processTitle = Flow();
            Label processHeading = Label("02  勾选运行中的进程", 11, ink);
            processHeading.Dock = DockStyle.None; processHeading.Size = new Size(268, 28);
            processTitle.Controls.Add(processHeading);
            Button testTarget = Button("启动测试进程", delegate { StartTestTarget(); }, false, 126);
            testTarget.Height = 25;
            tips.SetToolTip(testTarget, "启动一个独立的等待进程，3 分钟后自动退出。可用它先试用分配和恢复。");
            processTitle.Controls.Add(testTarget);
            Button inspect = Button("检查勾选进程权限", delegate
            {
                grid.EndEdit();
                List<ProcessRow> selected = SelectedProcesses();
                if (selected.Count == 0) { Append("请先勾选要检查的进程。权限检查不会修改亲和性。", muted); return; }
                RunBatch(delegate { foreach (ProcessRow row in selected) Record(service.InspectPermissions(row), row.Key); });
            }, false, 162);
            inspect.Height = 25;
            processTitle.Controls.Add(inspect);
            processPane.Controls.Add(processTitle, 0, 0);
            FlowLayoutPanel filters = Flow();
            search.Width = 146; search.Margin = new Padding(0, 3, 7, 0);
            tips.SetToolTip(search, "搜索进程名或 PID；空格分隔多个关键词，匹配任意一个。筛选按钮不会自动勾选进程。");
            search.TextChanged += delegate { RenderProcesses(); };
            filters.Controls.Add(search);
            filters.Controls.Add(Button("刷新", delegate { RefreshProcesses(); }, false, 62));
            Button apexFilter = Button("Apex / Steam", delegate
            {
                search.Text = "r5apex steam.exe";
                Append("Steam 继承：先自行退出 Apex，勾选 steam.exe 分配目标 CPU，再从 Steam 启动 Apex；刷新核对游戏实际 CPU 范围。", accent);
                Append("筛选不会勾选或修改进程。若其他调度工具仍有规则，请核对其范围，避免设置随后被改回。", muted);
            }, false, 110);
            tips.SetToolTip(apexFilter, "同时查找 Apex 与 Steam。已受保护的游戏无法直接修改时，可先手动分配 Steam，再启动游戏以继承亲和性。");
            filters.Controls.Add(apexFilter);
            filters.Controls.Add(Button("直播伴侣", delegate { search.Text = "直播 douyin live companion"; }, false, 86));
            filters.Controls.Add(Button("NVIDIA", delegate { search.Text = "nvidia nvcontainer nvsphelper shadowplay"; }, false, 78));
            filters.Controls.Add(Button("全部", delegate { search.Clear(); }, false, 58));
            processPane.Controls.Add(filters, 0, 1);
            processInfo.Dock = DockStyle.Fill; processInfo.ForeColor = muted;
            processPane.Controls.Add(processInfo, 0, 2);
            SetupGrid();
            processPane.Controls.Add(grid, 0, 3);
            FlowLayoutPanel actions = Flow(); actions.Padding = new Padding(0, 10, 0, 0); actions.WrapContents = false;
            apply = Button("分配给勾选进程", delegate { ApplySelected(); }, true, 146);
            restoreSelected = Button("恢复勾选进程", delegate { Restore(false); }, false, 136);
            restoreAll = Button("一键恢复全部 (0)", delegate { Restore(true); }, false, 166);
            actions.Controls.Add(apply); actions.Controls.Add(restoreSelected); actions.Controls.Add(restoreAll);
            processPane.Controls.Add(actions, 0, 4);
            body.Controls.Add(processPane, 1, 0);

            TableLayoutPanel logPane = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.White, Padding = new Padding(12, 6, 12, 8), Margin = new Padding(0) };
            logPane.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); logPane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            logPane.Controls.Add(Label("操作结果  /  每个进程独立显示", 9, ink), 0, 0);
            log.Dock = DockStyle.Fill; log.ReadOnly = true; log.BorderStyle = BorderStyle.None; log.BackColor = Color.FromArgb(247, 249, 252); log.Font = new Font("Microsoft YaHei UI", 9F);
            logPane.Controls.Add(log, 0, 1); root.Controls.Add(logPane, 0, 3);
            status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.ForeColor = muted;
            root.Controls.Add(status, 0, 4);
            FormClosing += OnClosing;
            Shown += delegate
            {
                SetCpuSelection(0, service.Topology.Available);
                RefreshProcesses();
                Append("就绪。仅修改你勾选的当前用户进程；首次分配前保存原亲和性。", muted);
                Append("工具运行权限：" + (Native.IsAdministrator ? "管理员" : "普通用户") + "。可勾选进程后点击“检查勾选进程权限”进行只读诊断。", muted);
                if (service.Store.Items.Length > 0) Append("发现 " + service.Store.Items.Length + " 条上次保留的恢复记录，可点击“一键恢复全部”。", accent);
            };
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(true);
        }

        private Label Label(string text, float size, Color color)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, AutoEllipsis = true, Font = new Font("Microsoft YaHei UI", size), ForeColor = color, Margin = new Padding(0) };
        }
        private FlowLayoutPanel Flow() { return new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false }; }
        private Button Button(string text, EventHandler click, bool primary, int width)
        {
            Button button = new Button { Text = text, Width = width, Height = 30, FlatStyle = FlatStyle.Flat,
                BackColor = primary ? accent : Color.White, ForeColor = primary ? Color.White : ink, Margin = new Padding(0, 0, 7, 0), Cursor = Cursors.Hand };
            button.FlatAppearance.BorderColor = primary ? accent : Color.FromArgb(208, 218, 231);
            button.Click += click;
            return button;
        }

        private void SetupGrid()
        {
            grid.Dock = DockStyle.Fill; grid.BackgroundColor = Color.White; grid.BorderStyle = BorderStyle.None;
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false; grid.MultiSelect = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(235, 241, 249);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = ink; grid.ColumnHeadersHeight = 34;
            grid.RowTemplate.Height = 29; grid.GridColor = Color.FromArgb(232, 238, 245);
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(226, 237, 253);
            grid.DefaultCellStyle.SelectionForeColor = ink;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 253);
            grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Pick", HeaderText = "选", Width = 34, AutoSizeMode = DataGridViewAutoSizeColumnMode.None });
            AddColumn("Name", "进程", 160); AddColumn("Pid", "PID", 52);
            AddColumn("Current", "当前 CPU", 85); AddColumn("Original", "原 CPU", 85); AddColumn("Result", "最近结果", 180);
            grid.CurrentCellDirtyStateChanged += delegate { if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            grid.CellValueChanged += delegate { UpdateState(); };
        }
        private void AddColumn(string name, string title, float weight)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = title, ReadOnly = true, FillWeight = weight, MinimumWidth = 45, SortMode = DataGridViewColumnSortMode.NotSortable });
        }
        private List<ProcessRow> SelectedProcesses()
        {
            return grid.Rows.Cast<DataGridViewRow>().Where(r => Convert.ToBoolean(r.Cells[0].Value)).Select(r => (ProcessRow)r.Tag).Where(p => p != null).ToList();
        }
        private ulong SelectedMask()
        {
            ulong mask = 0;
            foreach (CheckBox box in cpuBoxes)
            {
                Tuple<int, int> cpu = (Tuple<int, int>)box.Tag;
                if (box.Checked && cpu.Item1 == 0) mask |= 1UL << cpu.Item2;
            }
            return mask;
        }
        private void SetCpuSelection(int group, ulong mask)
        {
            foreach (CheckBox box in cpuBoxes)
            {
                Tuple<int, int> cpu = (Tuple<int, int>)box.Tag;
                box.Checked = cpu.Item1 == group && (mask & (1UL << cpu.Item2)) != 0;
            }
            UpdateState();
        }
        private void RefreshProcesses()
        {
            try { processes = service.Processes(out unavailable); RenderProcesses(); }
            catch (Exception error) { Append("刷新失败：" + Native.Explain(error), Color.Firebrick); }
        }
        private void StartTestTarget()
        {
            try
            {
                using (Process worker = Process.Start(new ProcessStartInfo { FileName = typeof(MainForm).Assembly.Location,
                    Arguments = "--test-worker", UseShellExecute = false, CreateNoWindow = true }))
                {
                    search.Text = worker.Id.ToString();
                    RefreshProcesses();
                    Append("已启动测试进程 " + System.IO.Path.GetFileName(typeof(MainForm).Assembly.Location) + " [" + worker.Id + "]，3 分钟后自动退出。请勾选此 PID 后测试分配与恢复。", accent);
                }
            }
            catch (Exception error) { Append("启动测试进程失败：" + Native.Explain(error), Color.Firebrick); }
        }
        private void RenderProcesses()
        {
            HashSet<string> selected = new HashSet<string>(SelectedProcesses().Select(p => p.Key));
            grid.Rows.Clear();
            string[] tokens = search.Text.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (ProcessRow process in processes)
            {
                if (tokens.Length > 0 && !tokens.Any(t => process.Name.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0 || process.Pid.ToString().Contains(t))) continue;
                OriginalAffinity original = service.Store.Find(process.Pid, process.Created);
                string result;
                results.TryGetValue(process.Key, out result);
                int index = grid.Rows.Add(selected.Contains(process.Key), process.Name, process.Pid,
                    process.ReadError == null ? Masks.Format(process.Mask) : "无法读取",
                    original == null ? "—" : Masks.Format(original.Mask), result ?? process.ReadError ?? "待操作");
                grid.Rows[index].Tag = process;
                grid.Rows[index].Cells[5].ToolTipText = result ?? process.ReadError ?? "勾选此进程后才能分配。";
            }
            processInfo.Text = "搜索进程名 / PID  ·  显示 " + grid.Rows.Count + " / " + processes.Count + " 个当前用户进程  ·  不可读取 " + unavailable + " 个";
            UpdateState();
        }
        private void UpdateState()
        {
            if (apply == null) return;
            ulong mask = SelectedMask();
            selection.Text = "已选 " + Masks.Bits(mask).Count() + " 个逻辑 CPU\r\nCPU " + Masks.Format(mask);
            int count = SelectedProcesses().Count;
            bool enabled = service.Topology.CanAssign && !busy;
            apply.Enabled = enabled && count > 0 && mask != 0;
            restoreSelected.Enabled = enabled && SelectedProcesses().Any(p => service.Store.Find(p.Pid, p.Created) != null);
            restoreAll.Enabled = enabled && service.Store.Items.Length > 0;
            restoreAll.Text = "一键恢复全部 (" + service.Store.Items.Length + ")";
            status.Text = "已勾选 " + count + " 个进程  ·  待恢复 " + service.Store.Items.Length + " 条  ·  分配仅对当前进程实例生效；重新启动进程后需重新分配";
        }
        private void Record(OperationResult result, string key)
        {
            results[key] = (result.Skipped ? "跳过：" : result.Success ? "成功：" : "失败：") + result.Message;
            Append(result.ToString(), result.Skipped ? muted : result.Success ? Color.FromArgb(23, 120, 80) : Color.Firebrick);
        }
        private void ApplySelected()
        {
            grid.EndEdit();
            List<ProcessRow> selected = SelectedProcesses();
            ulong mask = SelectedMask();
            RunBatch(delegate
            {
                foreach (ProcessRow row in selected) Record(service.Apply(row, mask), row.Key);
            });
        }
        private void Restore(bool all)
        {
            grid.EndEdit();
            HashSet<string> selected = new HashSet<string>(SelectedProcesses().Select(p => p.Key));
            OriginalAffinity[] originals = service.Store.Items.Where(x => all || selected.Contains(x.Key)).ToArray();
            RunBatch(delegate { foreach (OriginalAffinity original in originals) Record(service.Restore(original), original.Key); });
        }
        private void RunBatch(Action action)
        {
            if (busy) return;
            busy = true; UseWaitCursor = true; UpdateState();
            try { action(); }
            finally { busy = false; UseWaitCursor = false; RefreshProcesses(); UpdateState(); }
        }
        private void Append(string text, Color color)
        {
            log.SelectionStart = log.TextLength; log.SelectionLength = 0; log.SelectionColor = color;
            log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine);
            log.ScrollToCaret();
        }
        private void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (service.Store.Items.Length == 0) return;
            DialogResult choice = MessageBox.Show(this, "还有 " + service.Store.Items.Length + " 条恢复记录。\r\n\r\n是：恢复全部后退出\r\n否：保留当前分配并退出（下次打开仍可恢复）\r\n取消：返回工具\r\n\r\n请保留程序旁的 data 目录。", "退出与恢复", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (choice == DialogResult.Cancel) { e.Cancel = true; return; }
            if (choice == DialogResult.Yes)
            {
                Restore(true);
                if (service.Store.Items.Length > 0)
                {
                    e.Cancel = true;
                    Append("部分恢复未完成，窗口已保留。可查看原因并重试，或再次关闭选择“否”以保存记录退出。", Color.Firebrick);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tips.Dispose();
            base.Dispose(disposing);
        }
    }
}
