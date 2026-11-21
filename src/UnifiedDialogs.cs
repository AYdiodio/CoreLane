using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal sealed class TestTargetWindow : Form
    {
        internal TestTargetWindow()
        {
            DialogUi.Setup(this, Program.DisplayName + " · 游戏分类测试进程", new Size(540, 320));
            TableLayoutPanel root = DialogUi.Rows(44, -1, 48); Controls.Add(root);
            root.Controls.Add(DialogUi.Label("独立测试窗口", 18, UiTheme.Ink), 0, 0);
            CardPanel card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(18), Margin = new Padding(0, 8, 0, 12) };
            card.Controls.Add(DialogUi.Label("这是工具创建的测试进程。\r\n可验证游戏分类询问、自动分配与恢复。\r\n3 分钟后自动退出，也可随时关闭。", 10, UiTheme.Muted)); root.Controls.Add(card, 0, 1);
            FlowLayoutPanel actions = DialogUi.Actions(); SoftButton close = GameQuestionDialog.MakeButton("关闭测试窗口", 140, delegate { Close(); }, false);
            actions.Controls.Add(close); root.Controls.Add(actions, 0, 2); CancelButton = close;
        }
    }
    internal sealed class AppMessageDialog : Form
    {
        internal AppMessageDialog(string text, string caption, MessageBoxButtons buttons, string confirmLabel = null)
        {
            DialogUi.Setup(this, caption, new Size(660, 420)); MinimumSize = new Size(540, 360);
            TableLayoutPanel root = DialogUi.Rows(48, -1, 56); Controls.Add(root);
            root.Controls.Add(DialogUi.Label(caption, 18, UiTheme.Ink), 0, 0);
            CardPanel content = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(18), Margin = new Padding(0, 8, 0, 12) };
            CapsuleScrollPanel viewport = new CapsuleScrollPanel { Dock = DockStyle.Fill, BackColor = Color.White };
            TextBox message = new TextBox { Text = text, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, ForeColor = UiTheme.Ink, ScrollBars = ScrollBars.None, TabStop = false };
            viewport.Content.Controls.Add(message); content.Controls.Add(viewport); root.Controls.Add(content, 0, 1);
            Action arrangeText = delegate
            {
                message.Width = viewport.ContentWidth;
                Size measured = TextRenderer.MeasureText(text, message.Font, new Size(Math.Max(1, message.Width - 2), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                message.Height = measured.Height + (int)Math.Ceiling(8 * UiTheme.Scale(this)); viewport.SetContentHeight(message.Height);
            };
            viewport.ViewportChanged += delegate { arrangeText(); }; Shown += delegate { arrangeText(); };
            message.MouseWheel += delegate(object sender, MouseEventArgs e) { viewport.ScrollWheel(e.Delta); HandledMouseEventArgs handled = e as HandledMouseEventArgs; if (handled != null) handled.Handled = true; };
            FlowLayoutPanel actions = DialogUi.Actions(); root.Controls.Add(actions, 0, 2);
            DialogResult[] results = buttons == MessageBoxButtons.YesNoCancel ? new[] { DialogResult.Yes, DialogResult.No, DialogResult.Cancel } :
                buttons == MessageBoxButtons.YesNo ? new[] { DialogResult.Yes, DialogResult.No } : buttons == MessageBoxButtons.OKCancel ? new[] { DialogResult.OK, DialogResult.Cancel } : new[] { DialogResult.OK };
            foreach (DialogResult result in results)
            {
                DialogResult value = result;
                string label = result == DialogResult.Yes ? "是" : result == DialogResult.No ? "否" : result == DialogResult.Cancel ? "取消" : confirmLabel ?? "知道了";
                SoftButton button = GameQuestionDialog.MakeButton(label, 100, delegate { DialogResult = value; }, result == results[0]);
                actions.Controls.Add(button);
                if (result == results[0]) { AcceptButton = button; ActiveControl = button; }
                if (result == DialogResult.Cancel || result == DialogResult.No || results.Length == 1) CancelButton = button;
            }
            if (confirmLabel != null && buttons == MessageBoxButtons.OKCancel) ActiveControl = (Control)CancelButton;
        }
    }
    internal static class AppMessageBox
    {
        internal static DialogResult Show(string text, string caption, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None)
        { return Show(null, text, caption, buttons, icon); }
        internal static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None)
        { using (AppMessageDialog dialog = new AppMessageDialog(text, caption, buttons)) return owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner); }
    }
    internal static class DialogStyle
    {
        internal static void Grid(DataGridView grid)
        {
            grid.EnableHeadersVisualStyles = false; grid.GridColor = UiTheme.Line;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal; grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.White; grid.ColumnHeadersDefaultCellStyle.ForeColor = UiTheme.Muted;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.White; grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8);
            grid.DefaultCellStyle.Padding = new Padding(8); grid.DefaultCellStyle.ForeColor = UiTheme.Ink;
            grid.DefaultCellStyle.SelectionBackColor = UiTheme.BlueWash; grid.DefaultCellStyle.SelectionForeColor = UiTheme.Ink;
            grid.RowTemplate.Height = 54; grid.ColumnHeadersHeight = 42;
            grid.CellPainting += delegate(object sender, DataGridViewCellPaintingEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                if (grid.Columns[e.ColumnIndex] is DataGridViewCheckBoxColumn)
                {
                    e.PaintBackground(e.CellBounds, true); bool selected = e.Value != null && Convert.ToBoolean(e.Value);
                    float size = 20 * UiTheme.Scale(grid); RectangleF box = new RectangleF(e.CellBounds.X + (e.CellBounds.Width - size) / 2, e.CellBounds.Y + (e.CellBounds.Height - size) / 2, size, size);
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    UiTheme.FillRound(e.Graphics, selected ? UiTheme.Blue : UiTheme.Line, box, size / 3);
                    if (selected) UiGlyphs.Draw(e.Graphics, UiGlyph.Check, RectangleF.Inflate(box, -3, -3), Color.White);
                    e.Handled = true; return;
                }
                if (!(grid.Columns[e.ColumnIndex] is DataGridViewButtonColumn)) return;
                e.PaintBackground(e.CellBounds, true);
                Rectangle frame = Rectangle.Inflate(e.CellBounds, -8, -9);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                UiTheme.FillRound(e.Graphics, UiTheme.Page, frame, frame.Height / 2F);
                TextRenderer.DrawText(e.Graphics, Convert.ToString(e.FormattedValue) + "  ⌄", grid.Font, Rectangle.Inflate(frame, -10, 0), UiTheme.Blue,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix); e.Handled = true;
            };
        }
        internal static void Choices(Control owner, string[] choices, Action<string> choose, Point point)
        {
            CapsuleContextMenu menu = new CapsuleContextMenu { Font = owner.Font };
            foreach (string item in choices) { string value = item; menu.Items.Add(value, null, delegate { choose(value); }); }
            menu.Closed += delegate { menu.Dispose(); }; menu.Show(owner, point);
        }
    }
}
