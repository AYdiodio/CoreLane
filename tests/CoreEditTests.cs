using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal static partial class SelfTests
    {
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr SendCoreTestMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
        private static void DispatchCoreDoubleClick(Control tile)
        {
            IntPtr location = new IntPtr(20 | (20 << 16)), handle = tile.Handle;
            SendCoreTestMessage(handle, 0x201, new IntPtr(1), location); SendCoreTestMessage(handle, 0x202, IntPtr.Zero, location);
            SendCoreTestMessage(handle, 0x203, new IntPtr(1), location); SendCoreTestMessage(handle, 0x202, IntPtr.Zero, location); Application.DoEvents();
        }
        private static void CoreEditTests(string working)
        {
            Case("direct CPU picker: starts from read range, empty disabled, exact single bit and cancel have no side effects", delegate
            {
                Topology topology = Topology.Parse(Fixture(false));
                using (CpuMaskDialog dialog = new CpuMaskDialog(topology, 0xffff0000, false, "合并图标 · 2 个应用 · 3 个进程", null, true))
                {
                    Check(dialog.SelectedMask == 0xffff0000, "actual mask is initial selection");
                    CheckBox[] boxes = ChildControls(dialog).OfType<CheckBox>().ToArray();
                    foreach (CheckBox box in boxes) box.Checked = false;
                    Button apply = ChildControls(dialog).OfType<Button>().Single(b => b.Text == "分配给此应用");
                    Check(!apply.Enabled, "empty draft cannot apply");
                    boxes.Single(b => b.Text == "CPU 3").Checked = true;
                    Check(dialog.SelectedMask == 8 && apply.Enabled, "single core remains exact");
                    typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(dialog.CancelButton, new object[] { EventArgs.Empty }); Check(dialog.DialogResult == DialogResult.Cancel, "cancel never returns a commit");
                }
            });
            Case("restore dialog: selected and all scopes are explicit and empty scope is disabled", delegate
            {
                var records = new[] { new SchedulingRestoreEntry(77, 1, "fixture.exe", "fixture", delegate { }) };
                using (RestoreSettingsDialog dialog = new RestoreSettingsDialog(records, 0))
                {
                    Check(!ChildControls(dialog).OfType<Button>().Any(b => b.Text == "恢复已选"), "empty scope offers no secondary restore action");
                    typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(ChildControls(dialog).OfType<Button>().Single(b => b.Text == "确认恢复"), new object[] { EventArgs.Empty });
                    Check(dialog.RestoreAll && dialog.DialogResult == DialogResult.OK, "all is explicit confirmation");
                }
                using (RestoreSettingsDialog dialog = new RestoreSettingsDialog(records, 1))
                {
                    Button selected = ChildControls(dialog).OfType<Button>().Single(b => b.Text == "恢复已选");
                    typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(selected, new object[] { EventArgs.Empty });
                    Check(!dialog.RestoreAll && dialog.DialogResult == DialogResult.OK, "selected scope remains available");
                }
            });
            Topology actual = Topology.Read(); if (!actual.CanAssign) return;
            Case("live double click: cancel writes nothing; apply freezes original members and restore returns exact original", delegate
            {
                using (Process first = Process.Start(new ProcessStartInfo { FileName = typeof(SelfTests).Assembly.Location, Arguments = "--test-worker", UseShellExecute = false, CreateNoWindow = true }))
                using (Process second = Process.Start(new ProcessStartInfo { FileName = typeof(SelfTests).Assembly.Location, Arguments = "--test-worker", UseShellExecute = false, CreateNoWindow = true }))
                {
                    AffinityService service = new AffinityService(actual, new SnapshotStore(Path.Combine(working, "core-edit", "original.json")));
                    try
                    {
                        int unavailable; ProcessRow a = service.Processes(out unavailable).Single(r => r.Pid == first.Id);
                        ProcessRow b = service.Processes(out unavailable).Single(r => r.Pid == second.Id);
                        ulong before = Read(first.Id), other = Read(second.Id), target = 1UL << Masks.Bits(actual.Available).First();
                        ApplicationIconGroup icon = ApplicationIconGroup.Build(ApplicationGroup.Build(new[] { a }, delegate { return null; })).Single();
                        using (MainForm form = new MainForm(service))
                        using (AppIconTile tile = new AppIconTile { Group = icon })
                        using (Timer timer = new Timer { Interval = 40 })
                        {
                            StartupInvoke(form, "InitializePriorities");
                            uint priorityBefore = ReadPriority(first.Id), priorityOther = ReadPriority(second.Id);
                            form.Handle.ToInt64(); ((AppIconPanel)StartupField(form, "applications")).Controls.Add(tile); StartupInvoke(form, "BindApplicationInteraction", tile);
                            bool apply = false, visited = false; string mouseTrace = "";
                            tile.MouseDown += delegate(object sender, MouseEventArgs e) { mouseTrace += e.Clicks + ","; };
                            timer.Tick += delegate
                            {
                                CpuMaskDialog dialog = Application.OpenForms.OfType<CpuMaskDialog>().FirstOrDefault(); if (dialog == null) return;
                                timer.Stop(); visited = true;
                                Check((bool)StartupField(form, "settingsDialogOpen"), "modal blocks automatic processing and reordering");
                                CapsuleSchemePicker priorityPicker = ChildControls(dialog).OfType<CapsuleSchemePicker>().Single(p => p.AccessibleName == a.Key + " 进程优先级");
                                priorityPicker.SelectedIndex = 3;
                                if (!apply) { dialog.DialogResult = DialogResult.Cancel; return; }
                                icon.Applications[0].Members.Add(b); // Appears after the operation snapshot.
                                foreach (CheckBox box in ChildControls(dialog).OfType<CheckBox>()) box.Checked = box.Text == "CPU " + Masks.Bits(target).Single();
                                ChildControls(dialog).OfType<Button>().Single(x => x.Text == "应用设置").PerformClick();
                            };
                            timer.Start(); DispatchCoreDoubleClick(tile);
                            Check(visited && service.Store.Items.Length == 0 && Read(first.Id) == before && ReadPriority(first.Id) == priorityBefore,
                                "double click opens draft; cancel changes neither affinity nor priority; mouse=" + mouseTrace + "; visited=" + visited);
                            apply = true; visited = false; timer.Start(); DispatchCoreDoubleClick(tile);
                            Check(visited && Read(first.Id) == target && Read(second.Id) == other, "only captured member is changed");
                            Check(service.Store.Items.All(r => r.Pid == first.Id), "late member and other processes not written");
                            Check(ReadPriority(first.Id) == PriorityLevels.AboveNormal && ReadPriority(second.Id) == priorityOther, "only original dialog member receives priority");
                            StartupInvoke(form, "Restore", true);
                            Check(Read(first.Id) == actual.Available && service.Store.Items.Length == 0 && ReadPriority(first.Id) == PriorityLevels.Normal,
                                "CPU and priority returned to Windows defaults together");
                        }
                    }
                    finally
                    {
                        foreach (OriginalAffinity original in service.Store.Items.ToArray()) service.Restore(original);
                        if (!first.HasExited) { first.Kill(); first.WaitForExit(); } if (!second.HasExited) { second.Kill(); second.WaitForExit(); }
                    }
                }
            });
            if (Masks.Bits(actual.Available).Count() < 2) return;
            Case("live compact UI: CPU group confirmation, diagnostics and CPU Sets application and restore", delegate
            {
                string folder = Path.Combine(working, "compact-restore");
                Directory.CreateDirectory(folder);
                string executable = Path.Combine(folder, "ccd-compact-fixture.exe");
                File.Copy(typeof(SelfTests).Assembly.Location, executable);
                var service = new AffinityService(actual, new SnapshotStore(Path.Combine(folder, "original.json")));
                using (Process child = Process.Start(new ProcessStartInfo { FileName = executable, Arguments = "--test-worker", UseShellExecute = false, CreateNoWindow = true }))
                using (var form = new MainForm(service) { StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000) })
                using (var reply = new Timer { Interval = 30 })
                {
                    ulong original = Read(child.Id);
                    try
                    {
                        form.Show();
                        int unavailable;
                        ProcessRow worker = service.Processes(out unavailable).Single(row => row.Pid == child.Id);
                        var search = (TextBox)StartupField(form, "search");
                        typeof(MainForm).GetField("processes", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(form, new System.Collections.Generic.List<ProcessRow> { worker });
                        search.Text = "ccd-compact-fixture";
                        var tile = ((AppIconPanel)StartupField(form, "applications")).Controls.OfType<AppIconTile>().Single();
                        Check(tile.Group.Members.All(row => row.Pid == child.Id), "fixture filter isolates only the test worker");
                        typeof(Control).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(tile, new object[] { EventArgs.Empty });
                        var more = (ContextMenuStrip)StartupField(form, "moreMenu");
                        ToolStripItem permissionCheck = more.Items.Cast<ToolStripItem>().Single(item => item.Text == "检查所选应用权限…");
                        Check(permissionCheck.Enabled, "diagnostics is enabled for a selected process");
                        string permissionDetails = null;
                        using (var inspectReply = new Timer { Interval = 30 })
                        {
                            inspectReply.Tick += delegate
                            {
                                var modal = Application.OpenForms.OfType<AppMessageDialog>().FirstOrDefault(window => window.Text == "亲和性权限检查");
                                if (modal == null) return;
                                inspectReply.Stop();
                                permissionDetails = ChildControls(modal).OfType<TextBox>().Single().Text;
                                modal.DialogResult = DialogResult.OK;
                            };
                            inspectReply.Start();
                            permissionCheck.PerformClick();
                        }
                        Check(permissionDetails != null && permissionDetails.Contains("当前 CPU") &&
                            permissionDetails.Contains("未修改亲和性或恢复记录") && permissionDetails.Contains("[" + child.Id + "]") &&
                            Read(child.Id) == original && service.Store.Items.Length == 0, "menu surfaces read-only diagnostic for only the selected child");
                        var stale = new ProcessRow { Pid = worker.Pid, Created = worker.Created + 1, Name = worker.Name };
                        StartupInvoke(form, "ApplyTargets", new[] { stale }, original, "隔离失败测试");
                        var failureText = (Label)StartupField(form, "recentOperation");
                        Check(failureText.Text.Contains(worker.Name) && failureText.Text.Contains("PID 已被其他进程复用") &&
                            Read(child.Id) == original && service.Store.Items.Length == 0, "the bottom summary preserves the failure reason");
                        CpuTargetTile[] cpu = ChildControls(form).OfType<CpuTargetTile>().ToArray();
                        CpuTargetTile target = cpu.FirstOrDefault(card => card.Group.Mask != original);
                        if (target == null)
                        {
                            ulong oneBit = 1UL << Masks.Bits(actual.Available).First();
                            CheckResult(service.Apply(worker, oneBit));
                            StartupInvoke(form, "RefreshProcesses");
                            target = cpu.First(card => card.Group.Mask != oneBit);
                        }
                        ulong beforeClick = Read(child.Id);
                        bool confirm = false;
                        reply.Tick += delegate
                        {
                            Form modal = Application.OpenForms.Cast<Form>().FirstOrDefault(window => window is AppMessageDialog || window is RestoreSettingsDialog);
                            if (modal == null) return;
                            reply.Stop();
                            if (!confirm) { modal.DialogResult = DialogResult.Cancel; return; }
                            ChildControls(modal).OfType<Button>().Single(button => button.Text == "分配到此组" || button.Text == "恢复全部" || button.Text == "确认恢复").PerformClick();
                        };
                        reply.Start();
                        target.PerformClick();
                        Check(Read(child.Id) == beforeClick, "cancel keeps exact current mask");
                        confirm = true;
                        reply.Start();
                        target.PerformClick();
                        Check(Read(child.Id) == target.Group.Mask, "confirmation assigns the exact selected group");
                        var menu = (ContextMenuStrip)StartupField(form, "moreMenu");
                        ToolStripItem restore = menu.Items.Cast<ToolStripItem>().Single(item => item.Text == "恢复原设置…");
                        Check(restore.Enabled, "actual change enables restore");
                        reply.Start();
                        restore.PerformClick();
                        Check(Read(child.Id) == actual.Available && service.Store.Items.Length == 0 && !restore.Enabled, "menu opens the full system mask and clears pending state");
                        var cpuSets = (CpuSetService)StartupField(form, "cpuSetService");
                        var selectedGroup = ((AppIconPanel)StartupField(form, "applications")).Controls.OfType<AppIconTile>().Single().Group;
                        ulong softTarget = 1UL << Masks.Bits(original).First();
                        bool visited = false;
                        using (var choose = new Timer { Interval = 30 })
                        {
                            choose.Tick += delegate
                            {
                                var dialog = Application.OpenForms.OfType<CpuMaskDialog>().FirstOrDefault();
                                if (dialog == null) return;
                                choose.Stop();
                                try
                                {
                                    var mode = ChildControls(dialog).OfType<CapsuleSchemePicker>().Single(p => p.AccessibleName == "CPU 分配方式");
                                    mode.SelectedIndex = 1;
                                    ChildControls(dialog).OfType<SettingsSectionHeader>().First().PerformClick();
                                    ChildControls(dialog).OfType<SoftButton>().First(button => button.Text == "仅此组").PerformClick();
                                    if (dialog.SelectedMask != CpuSelectionGroup.FromTopology(actual)[0].Mask) return;
                                    foreach (CheckBox box in ChildControls(dialog).OfType<CheckBox>())
                                        box.Checked = box.Text == "CPU " + Masks.Bits(softTarget).Single();
                                    visited = true;
                                    ChildControls(dialog).OfType<Button>().Single(button => button.Text == "应用设置").PerformClick();
                                }
                                finally { if (dialog.DialogResult == DialogResult.None) dialog.DialogResult = DialogResult.Cancel; }
                            };
                            choose.Start();
                            StartupInvoke(form, "OpenApplicationCpu", selectedGroup);
                        }
                        Check(visited && Read(child.Id) == actual.Available && service.Store.Items.Length == 0 &&
                            cpuSets.Records.Length == 1 && cpuSets.Records[0].Mask == 0, "CPU Sets mode saves its own record and leaves hard affinity intact");
                        using (ProcessHandle handle = Native.Open(child.Id, false))
                            Check(CpuSetNative.ReadMask(handle) == softTarget, "Windows readback confirms the selected CPU Set");
                        Check(((AppIconPanel)StartupField(form, "applications")).Controls.OfType<AppIconTile>().Single().AssignmentLabel.Text == "CPU 集", "tile identifies the CPU Sets mode");
                        Check(new CpuSetService(actual, service.Store.DirectoryPath).Records.Single().Ids.Length == 0, "reload preserves the originally empty assignment");
                        reply.Start();
                        restore.PerformClick();
                        using (ProcessHandle handle = Native.Open(child.Id, false))
                            Check(CpuSetNative.ReadMask(handle) == 0 && cpuSets.Records.Length == 0 && Read(child.Id) == actual.Available, "restore clears the CPU Sets assignment and opens the full system mask");
                        ulong different = original & ~softTarget;
                        if (different != 0)
                        {
                            using (ProcessHandle handle = Native.OpenForCpuSets(child.Id)) CpuSetNative.SetMask(handle, different);
                            uint[] priorIds;
                            using (ProcessHandle handle = Native.Open(child.Id, false)) priorIds = CpuSetNative.ReadIds(handle);
                            CheckResult(cpuSets.Apply(worker, softTarget));
                            CheckResult(cpuSets.Apply(worker, original));
                            CpuSetService reloaded = new CpuSetService(actual, service.Store.DirectoryPath);
                            Check(reloaded.Records.Single().Ids.SequenceEqual(priorIds), "repeat application keeps the exact nonempty original CPU Set IDs across reload");
                            CheckResult(reloaded.Restore(reloaded.Records.Single()));
                            using (ProcessHandle handle = Native.Open(child.Id, false))
                                Check(CpuSetNative.ReadIds(handle).SequenceEqual(priorIds) && Read(child.Id) == actual.Available, "snapshot service can recover precise previous CPU Set IDs");
                        }
                    }
                    finally
                    {
                        foreach (OriginalAffinity record in service.Store.Items) service.Restore(record);
                        if (!child.HasExited) { child.Kill(); child.WaitForExit(); }
                    }
                }
            });
        }
    }
}
