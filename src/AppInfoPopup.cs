using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CoreAllocator
{
    // Read-only, non-activating hover information. It never captures mouse or keyboard input.
    internal sealed class AppInfoPopup : Form
    {
        internal readonly AppIconTile AnchorTile;
        private readonly string heading;
        private readonly string text;
        private readonly float scale;
        private readonly Font headingFont;
        internal AppInfoPopup(AppIconTile tile, string heading, string text)
        {
            AnchorTile = tile; this.heading = heading; this.text = text; scale = UiTheme.Scale(tile);
            Font = tile.Font; headingFont = new Font(Font.FontFamily, Font.Size + 1, FontStyle.Regular);
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; BackColor = Color.White; DoubleBuffered = true;
            int width = Math.Min((int)(430 * scale), Screen.FromControl(tile).WorkingArea.Width - 24);
            Size measured = TextRenderer.MeasureText(text, Font, new Size(width - (int)(32 * scale), 1000), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            ClientSize = new Size(width, Math.Min((int)(400 * scale), measured.Height + (int)(152 * scale)));
            AccessibleName = heading; AccessibleDescription = text;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnShown(EventArgs e)
        {
            using (GraphicsPath path = UiTheme.Round(new RectangleF(0, 0, Width, Height), 22 * scale)) Region = new Region(path);
            base.OnShown(e);
        }
        protected override CreateParams CreateParams
        {
            get { CreateParams parameters = base.CreateParams; parameters.ExStyle |= 0x08000000 | 0x00000080; parameters.ClassStyle |= 0x00020000; return parameters; }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; } // HTTRANSPARENT: keep hover on the original application.
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            base.WndProc(ref message);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF border = new RectangleF(.5F, .5F, Width - 1, Height - 1);
            using (Pen pen = new Pen(UiTheme.Line)) using (GraphicsPath path = UiTheme.Round(border, 22 * scale)) g.DrawPath(pen, path);
            int pad = (int)(18 * scale);
            UiTheme.FillRound(g, UiTheme.IconBase, new RectangleF(10 * scale, 10 * scale, Width - 20 * scale, 76 * scale), 17 * scale);
            UiTheme.DrawAppIcon(g, AnchorTile.Group.Visual, new RectangleF(24 * scale, 26 * scale, 40 * scale, 40 * scale), AnchorTile.UnifiedIcons);
            int titleX = (int)(78 * scale);
            TextRenderer.DrawText(g, heading, headingFont, new Rectangle(titleX, (int)(23 * scale), Width - titleX - pad, (int)(26 * scale)), UiTheme.Ink,
                TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, AnchorTile.Group.Applications.Count + " 个应用 · " + AnchorTile.Group.Members.Count + " 个进程", Font,
                new Rectangle(titleX, (int)(52 * scale), Width - titleX - pad, (int)(22 * scale)), UiTheme.Muted, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, "分配信息", Font, new Rectangle(pad, (int)(98 * scale), Width - pad * 2, (int)(24 * scale)), UiTheme.Blue,
                TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, text, Font, new Rectangle(pad, (int)(128 * scale), Width - pad * 2, Height - pad - (int)(128 * scale)), UiTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
        protected override void Dispose(bool disposing) { if (disposing) headingFont.Dispose(); base.Dispose(disposing); }
    }
}
