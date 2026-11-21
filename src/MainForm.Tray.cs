using System;
using System.Drawing;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal sealed partial class MainForm
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private bool hiddenInTray;
        private bool exitFromTray;
        private bool trayHintShown;

        private bool TrayCommandsAvailable
        {
            get { return !busy && !automationTicking && !Interacting && !IsDisposed; }
        }

        private void InitializeTray()
        {
            trayMenu = new CapsuleContextMenu { CompactCapsules = true };
            trayMenu.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs e) { BuildTrayMenu(); e.Cancel = false; };
            trayIcon = new NotifyIcon { Icon = Icon ?? SystemIcons.Application, Text = Program.DisplayName, ContextMenuStrip = trayMenu, Visible = true };
            trayIcon.DoubleClick += delegate { if (TrayCommandsAvailable) OpenFromTray(); };
            BuildTrayMenu();
        }

        private void BuildTrayMenu()
        {
            while (trayMenu.Items.Count > 0)
            {
                ToolStripItem previous = trayMenu.Items[0];
                trayMenu.Items.RemoveAt(0);
                previous.Dispose();
            }
            trayMenu.Items.Add(new GlyphMenuItem(AutomationCaption, UiGlyph.Sparkles, null) { Enabled = false });
            AddTrayAction("打开主窗口", UiGlyph.Arrow, OpenFromTray);
            ToolStripMenuItem schemes = CreateTraySubmenu("切换并启用方案", UiGlyph.Sparkles);
            schemes.Enabled = TrayCommandsAvailable && schemeStore != null && service.Topology.CanAssign;
            ToolStripMenuItem protection = CreateTraySubmenu("方案优先级与后台保护", UiGlyph.Adjust);
            protection.Enabled = TrayCommandsAvailable && priorityService != null;
            foreach (SchemePick choice in SchemeChoices())
            {
                schemes.DropDownItems.Add(new GlyphMenuItem(choice.Name, UiGlyph.Sparkles, delegate
                {
                    if (TrayCommandsAvailable && (automation == null || automation.SchemeId != choice.Id)) ActivateScheme(choice.Id);
                }) { Checked = automation != null && automation.SchemeId == choice.Id });
                protection.DropDownItems.Add(new GlyphMenuItem(choice.Name, UiGlyph.Adjust, delegate
                {
                    if (TrayCommandsAvailable) { OpenFromTray(); ManagePerformance(choice); }
                }));
            }
            trayMenu.Items.Add(schemes);
            if (automation != null)
                AddTrayAction("暂停自动方案", UiGlyph.Pause, delegate { StopAutomation("已暂停方案，当前 CPU 分配保留。"); }).Tag = ButtonTone.Pause;
            else if (retainedAssignments.Active)
                AddTrayAction("暂停保留的分配", UiGlyph.Pause, PauseRetainedAssignments).Tag = ButtonTone.Pause;
            AddTrayAction("管理方案与游戏分类…", UiGlyph.Adjust, delegate { OpenFromTray(); ManageSchemes(); }).Enabled &= schemeStore != null;
            if (protection.DropDownItems.Count > 0) trayMenu.Items.Add(protection);
            else protection.Dispose();
            AddTrayAction("保存本次分配到方案…", UiGlyph.Plus, delegate { OpenFromTray(); OpenSaveManualScheme(); }).Enabled &= schemeStore != null && lastManualRules.Count > 0;
            AddTrayAction("恢复原设置…", UiGlyph.Undo, delegate { OpenFromTray(); OpenRestoreSettings(); }).Enabled &= CanRestoreScheduling;
            trayMenu.Items.Add(new CapsuleMenuSeparator());
            AddTrayAction("退出 " + Program.DisplayName, UiGlyph.Arrow, delegate
            {
                OpenFromTray();
                exitFromTray = true;
                try { Close(); }
                finally { exitFromTray = false; }
            });
            SizeMenu((CapsuleContextMenu)trayMenu, 36, 196);
        }

        private ToolStripMenuItem CreateTraySubmenu(string label, UiGlyph glyph)
        {
            CapsuleContextMenu menu = new CapsuleContextMenu { CompactCapsules = true };
            menu.Opening += delegate { SizeMenu(menu, 36, 168); };
            return new GlyphMenuItem(label, glyph, null, true) { DropDown = menu };
        }

        private ToolStripItem AddTrayAction(string label, UiGlyph glyph, Action action)
        {
            ToolStripItem item = new GlyphMenuItem(label, glyph, delegate { if (TrayCommandsAvailable) action(); });
            trayMenu.Items.Add(item);
            item.Enabled = TrayCommandsAvailable;
            return item;
        }

        private void HideInTray()
        {
            CloseProcessDetails();
            hiddenInTray = true;
            Hide();
            if (trayHintShown) return;
            trayHintShown = true;
            trayIcon.ShowBalloonTip(2500, Program.DisplayName, "已收起到系统托盘。双击打开，右键可管理方案或退出。", ToolTipIcon.Info);
        }

        private void OpenFromTray()
        {
            hiddenInTray = false;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            QueueProcessRefresh(false);
        }

        private void DisposeTray()
        {
            if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); trayIcon = null; }
            if (trayMenu != null) { trayMenu.Dispose(); trayMenu = null; }
        }
    }
}
