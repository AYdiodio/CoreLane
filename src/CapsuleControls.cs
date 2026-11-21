using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CoreAllocator
{
    internal sealed class CapsulePanel : Panel
    {
        internal CapsulePanel() { DoubleBuffered = ResizeRedraw = true; BackColor = UiTheme.Page; Padding = new Padding(12, 4, 12, 4); }
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? UiTheme.Page : Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            UiTheme.FillRound(e.Graphics, BackColor, new RectangleF(0, 0, Width - 1, Height - 1), Height / 2F);
        }
    }
    internal sealed class CapsuleLabel : Label
    {
        internal CapsuleLabel() { AutoSize = false; AutoEllipsis = true; BackColor = UiTheme.Page; DoubleBuffered = ResizeRedraw = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; g.Clear(Parent == null ? UiTheme.Page : Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            UiTheme.FillRound(g, BackColor, new RectangleF(0, 0, Width - 1, Height - 1), Height / 2F);
            float scale = UiTheme.Scale(this); int left = (int)(12 * scale);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(left, 0, Math.Max(1, Width - left - (int)(12 * scale)), Height), ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }
    internal sealed class GlyphMenuItem : ToolStripMenuItem
    {
        internal readonly UiGlyph Glyph;
        internal readonly bool ShowChevron;
        private readonly Bitmap imageSlot = new Bitmap(20, 20);

        internal GlyphMenuItem(string text, UiGlyph glyph, EventHandler click, bool showChevron = false)
            : base(text, null, click)
        {
            Glyph = glyph;
            ShowChevron = showChevron;
            Image = imageSlot;
        }
        internal void Arrange(Rectangle bounds) { base.SetBounds(bounds); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { Image = null; imageSlot.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class CapsuleMenuSeparator : ToolStripSeparator
    {
        internal void Arrange(Rectangle bounds) { base.SetBounds(bounds); }
    }

    internal sealed class CapsuleMenuRenderer : ToolStripProfessionalRenderer
    {
        internal CapsuleMenuRenderer() { RoundedEdges = false; }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        { e.Graphics.Clear(Color.White); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(UiTheme.Line))
            using (GraphicsPath path = UiTheme.Round(new RectangleF(.5F, .5F, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), 18 * UiTheme.Scale(e.ToolStrip))) e.Graphics.DrawPath(pen, path);
        }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
        {
            GlyphMenuItem item = e.Item as GlyphMenuItem;
            if (item == null) { base.OnRenderItemImage(e); return; }
            // Icons and labels share an explicit row layout in OnRenderItemText.
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            if (!(e.Item is GlyphMenuItem)) UiGlyphs.Draw(e.Graphics, UiGlyph.Check, e.ImageRectangle, UiTheme.Purple);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            if (!(e.Item is GlyphMenuItem)) base.OnRenderArrow(e);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            ButtonTone tone = e.Item.Tag is ButtonTone ? (ButtonTone)e.Item.Tag : ButtonTone.Default;
            ToolStripMenuItem item = e.Item as ToolStripMenuItem;
            bool chosen = item != null && item.Checked;
            var menu = e.ToolStrip as CapsuleContextMenu;
            bool compact = menu != null && menu.CompactCapsules;
            bool neutral = !e.Item.Enabled || (!e.Item.Selected && !chosen && tone == ButtonTone.Default);
            if (neutral && !compact) return;
            Color fill = neutral ? UiTheme.Page : tone == ButtonTone.Start ? UiTheme.StartWash : tone == ButtonTone.Pause ? UiTheme.PauseWash : UiTheme.PurpleWash;
            if (e.Item.Selected && tone != ButtonTone.Default) fill = UiTheme.Mix(fill, ToneInk(tone), .08F);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = UiTheme.Scale(e.ToolStrip);
            UiTheme.FillRound(e.Graphics, fill, new RectangleF(8 * scale, 2 * scale, e.Item.Width - 16 * scale, e.Item.Height - 4 * scale),
                compact ? (e.Item.Height - 4 * scale) / 2 : 11 * scale);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            ToolStripMenuItem item = e.Item as ToolStripMenuItem;
            e.TextColor = !e.Item.Enabled ? UiTheme.Muted : e.Item.Tag is ButtonTone ? ToneInk((ButtonTone)e.Item.Tag)
                : e.Item.Selected || item != null && item.Checked ? UiTheme.Purple : UiTheme.Ink;
            GlyphMenuItem glyphItem = e.Item as GlyphMenuItem;
            if (glyphItem != null)
            {
                GraphicsState state = e.Graphics.Save();
                try
                {
                    e.Graphics.SetClip(new Rectangle(0, 0, e.Item.Width, e.Item.Height), CombineMode.Replace);
                    float s = UiTheme.Scale(e.ToolStrip), size = 18 * s;
                    UiGlyphs.Draw(e.Graphics, item.Checked ? UiGlyph.Check : glyphItem.Glyph,
                        new RectangleF(18 * s, (e.Item.Height - size) / 2, size, size), e.TextColor);
                    TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont,
                        new Rectangle((int)(50 * s), 0, Math.Max(1, e.Item.Width - (int)(82 * s)), e.Item.Height), e.TextColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                    if (glyphItem.ShowChevron) UiGlyphs.Draw(e.Graphics, UiGlyph.ChevronRight,
                        new RectangleF(e.Item.Width - 29 * s, (e.Item.Height - 14 * s) / 2, 14 * s, 14 * s), e.TextColor);
                }
                finally { e.Graphics.Restore(state); }
                return;
            }
            base.OnRenderItemText(e);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            float s = UiTheme.Scale(e.ToolStrip);
            using (Pen line = new Pen(UiTheme.Line)) e.Graphics.DrawLine(line, 16 * s, e.Item.Height / 2F, e.Item.Width - 16 * s, e.Item.Height / 2F);
        }
        private static Color ToneInk(ButtonTone tone) { return tone == ButtonTone.Start ? UiTheme.StartInk : tone == ButtonTone.Pause ? UiTheme.PauseInk : UiTheme.Ink; }
        internal static void Round(Control control)
        {
            if (control.Width < 2 || control.Height < 2) return;
            Region old = control.Region;
            using (GraphicsPath path = UiTheme.Round(new RectangleF(0, 0, control.Width, control.Height), 18 * UiTheme.Scale(control))) control.Region = new Region(path);
            if (old != null) old.Dispose();
        }
    }
    internal sealed class CapsuleContextMenu : ContextMenuStrip
    {
        private readonly Timer opening = new Timer { Interval = 15 };
        private readonly System.Diagnostics.Stopwatch elapsed = new System.Diagnostics.Stopwatch();
        private Point destination;
        private bool explicitRows, arranging;
        internal bool CompactCapsules { get; set; }
        internal void ConfigureRows(Size size)
        {
            explicitRows = true; AutoSize = false; ClientSize = size; PerformLayout();
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (!explicitRows || arranging) return;
            arranging = true;
            try
            {
                int inset = (int)Math.Round(8 * UiTheme.Scale(this)), y = inset;
                foreach (ToolStripItem item in Items)
                {
                    Rectangle bounds = new Rectangle(0, y, Math.Max(1, ClientSize.Width), item.Height);
                    GlyphMenuItem glyph = item as GlyphMenuItem; CapsuleMenuSeparator separator = item as CapsuleMenuSeparator;
                    if (glyph != null) glyph.Arrange(bounds);
                    else if (separator != null) separator.Arrange(bounds);
                    y += item.Height;
                }
            }
            finally { arranging = false; }
        }
        internal CapsuleContextMenu() { Renderer = new CapsuleMenuRenderer(); Padding = new Padding(5); BackColor = Color.White; }
        protected override void OnOpened(EventArgs e)
        {
            if (explicitRows) PerformLayout();
            base.OnOpened(e);
            if (CompactCapsules) return;
            destination = Location;
            Opacity = .15;
            Location = new Point(destination.X, destination.Y - (int)(5 * UiTheme.Scale(this)));
            opening.Tick -= AnimateOpening;
            opening.Tick += AnimateOpening;
            elapsed.Restart();
            opening.Start();
        }
        private void AnimateOpening(object sender, EventArgs args)
        {
            double progress = Math.Min(1, elapsed.Elapsed.TotalMilliseconds / 150);
            double eased = 1 - Math.Pow(1 - progress, 3);
            Opacity = .15 + .85 * eased;
            Location = new Point(destination.X, destination.Y - (int)Math.Round(5 * UiTheme.Scale(this) * (1 - eased)));
            if (progress >= 1) opening.Stop();
        }
        protected override void OnClosed(ToolStripDropDownClosedEventArgs e)
        {
            opening.Stop(); Opacity = 1;
            base.OnClosed(e);
        }
        protected override void Dispose(bool disposing) { if (disposing) opening.Dispose(); base.Dispose(disposing); }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); CapsuleMenuRenderer.Round(this); }
    }
    internal sealed class CapsuleDropDown : ToolStripDropDown
    {
        internal CapsuleDropDown() { Renderer = new CapsuleMenuRenderer(); Padding = Padding.Empty; Margin = Padding.Empty; AutoSize = false; BackColor = Color.White; }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); CapsuleMenuRenderer.Round(this); }
    }
    internal sealed class CapsuleMenuOption : SoftButton
    {
        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End || base.IsInputKey(keyData);
        }
    }
    // Only selection presentation changes. Existing callbacks still decide when to enable a scheme.
    internal sealed class CapsuleSchemePicker : Button
    {
        internal readonly List<object> Items = new List<object>();
        private int selectedIndex = -1;
        private CapsuleDropDown popup;
        private readonly List<CapsuleDropDown> retired = new List<CapsuleDropDown>();
        internal event EventHandler SelectedIndexChanged;
        internal object SelectedItem { get { return selectedIndex >= 0 && selectedIndex < Items.Count ? Items[selectedIndex] : null; } }
        internal int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                if (value < -1 || value >= Items.Count) throw new ArgumentOutOfRangeException("value");
                bool changed = value != selectedIndex; selectedIndex = value; Text = SelectedItem == null ? "选择方案" : SelectedItem.ToString();
                AccessibleDescription = Text + "；按回车、空格或上下方向键展开方案列表。"; Invalidate();
                if (changed && SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }
        internal CapsuleSchemePicker()
        {
            ResizeRedraw = true;
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = Color.White; Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.ButtonDropDown;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnClick(EventArgs e) { base.OnClick(e); ToggleDropDown(); }
        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (keyData == Keys.Down || keyData == Keys.Up) { if (popup == null) ToggleDropDown(); return true; }
            return base.ProcessCmdKey(ref message, keyData);
        }
        internal void ToggleDropDown()
        {
            if (popup != null) { popup.Close(); return; }
            if (!Enabled || Items.Count == 0) return;
            float scale = UiTheme.Scale(this); Rectangle area = Screen.FromControl(this).WorkingArea;
            int width = Math.Min(Math.Max(Width, (int)(230 * scale)), Math.Max(1, area.Width - (int)(24 * scale)));
            int rowHeight = (int)(40 * scale), gap = (int)(4 * scale), pad = (int)(7 * scale);
            int height = Math.Min(Items.Count * (rowHeight + gap) + pad * 2, Math.Min((int)(380 * scale), Math.Max(rowHeight + pad * 2, area.Height - (int)(36 * scale))));
            CapsuleScrollPanel content = new CapsuleScrollPanel { BackColor = Color.White, Size = new Size(width, height), Margin = Padding.Empty };
            CapsuleMenuOption[] options = new CapsuleMenuOption[Items.Count];
            for (int i = 0; i < Items.Count; i++)
            {
                int choice = i;
                CapsuleMenuOption option = new CapsuleMenuOption { Text = Items[i].ToString(), Font = Font, Width = Math.Max(1, content.ContentWidth - pad * 2), Height = rowHeight, Location = new Point(pad, pad + i * (rowHeight + gap)),
                    Margin = new Padding(0, 0, 0, gap), Tone = ButtonTone.Purple, SurfaceButton = true, Chosen = i == selectedIndex, TrailingCheck = i == selectedIndex,
                    Glyph = i == 0 ? UiGlyph.Sparkles : UiGlyph.Adjust, AccessibleRole = AccessibleRole.MenuItem, AccessibleDescription = i == selectedIndex ? "当前已选方案" : "选择后仍须启用方案" };
                option.Click += delegate { SelectedIndex = choice; if (popup != null) popup.Close(); Focus(); };
                option.KeyDown += delegate(object sender, KeyEventArgs args)
                {
                    int next = args.KeyCode == Keys.Home ? 0 : args.KeyCode == Keys.End ? options.Length - 1 : args.KeyCode == Keys.Up ? (choice + options.Length - 1) % options.Length : (choice + 1) % options.Length;
                    if (args.KeyCode == Keys.Up || args.KeyCode == Keys.Down || args.KeyCode == Keys.Home || args.KeyCode == Keys.End) { options[next].Focus(); content.Reveal(options[next]); args.Handled = args.SuppressKeyPress = true; }
                    if (args.KeyCode == Keys.Escape) { if (popup != null) popup.Close(); Focus(); args.Handled = args.SuppressKeyPress = true; }
                };
                options[i] = option; content.Content.Controls.Add(option);
            }
            content.SetContentHeight(Items.Count * (rowHeight + gap) + pad * 2);
            content.ViewportChanged += delegate { foreach (Control option in content.Content.Controls) option.Width = Math.Max(1, content.ContentWidth - pad * 2); };
            CapsuleDropDown nextPopup = new CapsuleDropDown { Size = new Size(width, height) }; popup = nextPopup;
            ToolStripControlHost host = new ToolStripControlHost(content) { AutoSize = false, Size = content.Size, Margin = Padding.Empty, Padding = Padding.Empty }; nextPopup.Items.Add(host);
            nextPopup.Closed += delegate
            {
                if (popup == nextPopup) popup = null; Invalidate();
                // Close still reparents its native window after Closed returns. Dispose on the next UI dispatch.
                if (nextPopup.Disposing || nextPopup.IsDisposed) return;
                retired.Add(nextPopup);
                if (IsHandleCreated && !IsDisposed && !Disposing)
                {
                    try { BeginInvoke((Action)delegate { if (retired.Remove(nextPopup) && !nextPopup.IsDisposed) nextPopup.Dispose(); }); }
                    catch (InvalidOperationException) { /* Owner disposal below releases pending windows. */ }
                }
            };
            Point point = PointToScreen(new Point(Width - width, Height + (int)(6 * scale)));
            point.X = Math.Max(area.Left, Math.Min(point.X, area.Right - width));
            if (point.Y + height > area.Bottom) point.Y = Math.Max(area.Top, PointToScreen(Point.Empty).Y - height - (int)(6 * scale));
            nextPopup.Show(this, PointToClient(point)); CapsuleMenuOption selected = options[Math.Max(0, Math.Min(selectedIndex, options.Length - 1))]; selected.Focus(); content.Reveal(selected); Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics; float scale = UiTheme.Scale(this); Color ink = Enabled ? UiTheme.Purple : UiTheme.Muted;
            g.Clear(Parent == null ? UiTheme.Page : Parent.BackColor); g.SmoothingMode = SmoothingMode.AntiAlias;
            UiTheme.FillRound(g, Parent != null && Parent.BackColor == Color.White ? UiTheme.Page : Color.White, new RectangleF(0, 0, Width - 1, Height - 1), Height / 2F);
            UiGlyphs.Draw(g, UiGlyph.Sparkles, new RectangleF(12 * scale, (Height - 16 * scale) / 2, 16 * scale, 16 * scale), ink);
            TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(35 * scale), 0, Math.Max(1, Width - (int)(62 * scale)), Height), ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            float x = Width - 18 * scale, y = Height / 2F;
            using (Pen pen = new Pen(ink, 1.4F * scale)) g.DrawLines(pen, popup == null ? new[] { new PointF(x - 4 * scale, y - 2 * scale), new PointF(x, y + 2 * scale), new PointF(x + 4 * scale, y - 2 * scale) } : new[] { new PointF(x - 4 * scale, y + 2 * scale), new PointF(x, y - 2 * scale), new PointF(x + 4 * scale, y + 2 * scale) });
            if (Focused && ShowFocusCues)
                using (Pen pen = new Pen(ink) { DashStyle = DashStyle.Dot })
                using (GraphicsPath path = UiTheme.Round(new RectangleF(3 * scale, 3 * scale, Width - 6 * scale, Height - 6 * scale), Height / 2F - 3 * scale)) g.DrawPath(pen, path);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (popup != null) { CapsuleDropDown old = popup; popup = null; old.Dispose(); }
                foreach (CapsuleDropDown old in retired.ToArray()) if (!old.IsDisposed) old.Dispose();
                retired.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
