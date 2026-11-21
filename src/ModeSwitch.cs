using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CoreAllocator
{
    // Real radio buttons preserve keyboard navigation and accessible selected state.
    internal sealed class ModeSwitch : Panel
    {
        private readonly ModeSegment manual;
        private readonly ModeSegment automatic;
        private readonly Timer animation = new Timer { Interval = 15 };
        private float position;
        private bool syncing;
        internal event EventHandler ModeChanged;
        internal event EventHandler AutomaticRequested;
        internal bool Automatic { get { return automatic.Checked; } }

        internal ModeSwitch()
        {
            DoubleBuffered = true; BackColor = UiTheme.Page;
            AccessibleName = "分配模式"; Size = new Size(292, 44);
            manual = new ModeSegment(this, false) { Text = "手动分配", AccessibleName = "手动分配", Checked = true };
            automatic = new ModeSegment(this, true) { Text = "自动方案", AccessibleName = "自动方案" };
            Controls.Add(manual); Controls.Add(automatic);
            manual.CheckedChanged += Changed; automatic.CheckedChanged += Changed;
            automatic.Click += delegate { if (!syncing && AutomaticRequested != null) AutomaticRequested(this, EventArgs.Empty); };
            animation.Tick += delegate
            {
                float target = Automatic ? 1 : 0;
                position += (target - position) * .24F;
                if (Math.Abs(target - position) < .004F) { position = target; animation.Stop(); }
                Repaint();
            };
        }
        private void Changed(object sender, EventArgs e)
        {
            if (syncing || !((RadioButton)sender).Checked) return;
            animation.Start(); Repaint();
            if (ModeChanged != null) ModeChanged(this, EventArgs.Empty);
        }
        internal void SetAutomatic(bool value)
        {
            if (Automatic == value) return;
            syncing = true;
            try { automatic.Checked = value; manual.Checked = !value; }
            finally { syncing = false; }
            if (Visible && IsHandleCreated) animation.Start(); else position = value ? 1 : 0;
            Repaint();
        }
        private void Repaint() { Invalidate(); if (manual != null) manual.Invalidate(); if (automatic != null) automatic.Invalidate(); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e); if (manual == null || automatic == null) return;
            int half = ClientSize.Width / 2;
            manual.Bounds = new Rectangle(0, 0, half, ClientSize.Height);
            automatic.Bounds = new Rectangle(half, 0, ClientSize.Width - half, ClientSize.Height);
        }
        internal void PaintTrack(Graphics graphics, Point offset)
        {
            graphics.Clear(BackColor); GraphicsState saved = graphics.Save();
            try
            {
                graphics.TranslateTransform(-offset.X, -offset.Y); graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float scale = UiTheme.Scale(this), gap = 4 * scale;
                RectangleF track = new RectangleF(0, 0, Width - 1, Height - 1);
                UiTheme.FillRound(graphics, Color.FromArgb(234, 236, 242), track, Height / 2F);
                float width = (Width - gap * 2) / 2;
                RectangleF thumb = new RectangleF(gap + position * width, gap, width, Height - gap * 2);
                Color accent = Enabled ? UiTheme.Mix(UiTheme.Blue, UiTheme.Purple, position) : UiTheme.Muted;
                UiTheme.FillRound(graphics, Color.FromArgb(15, accent), new RectangleF(thumb.X, thumb.Y + scale * 2, thumb.Width, thumb.Height), thumb.Height / 2);
                UiTheme.FillRound(graphics, accent, thumb, thumb.Height / 2);
            }
            finally { graphics.Restore(saved); }
        }
        protected override void OnPaint(PaintEventArgs e) { PaintTrack(e.Graphics, Point.Empty); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Repaint(); }
        protected override void Dispose(bool disposing) { if (disposing) animation.Dispose(); base.Dispose(disposing); }

        private sealed class ModeSegment : RadioButton
        {
            private readonly ModeSwitch owner;
            private readonly bool automatic;
            private bool hover;
            internal ModeSegment(ModeSwitch owner, bool automatic)
            {
                this.owner = owner; this.automatic = automatic; Appearance = Appearance.Button;
                FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; AutoSize = false;
                Cursor = Cursors.Hand; Margin = new Padding(0);
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }
            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
            protected override void OnPaint(PaintEventArgs e)
            {
                owner.PaintTrack(e.Graphics, Location); float scale = UiTheme.Scale(this);
                // Blend labels with the moving thumb, rather than switching color before it arrives.
                float selected = automatic ? owner.position : 1 - owner.position;
                Color ink = UiTheme.Mix(hover ? UiTheme.Ink : UiTheme.Muted, Color.White, selected);
                int icon = (int)(16 * scale), gap = (int)(7 * scale);
                Size text = TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
                int x = Math.Max((int)(9 * scale), (Width - text.Width - icon - gap) / 2);
                UiGlyphs.Draw(e.Graphics, automatic ? UiGlyph.Sparkles : UiGlyph.Pointer, new RectangleF(x, (Height - icon) / 2F, icon, icon), ink);
                TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(x + icon + gap, 0, Math.Max(1, Width - x - icon - gap - (int)(8 * scale)), Height), ink,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -(int)(9 * scale), -(int)(8 * scale)), ink, owner.BackColor);
            }
            protected override bool ProcessCmdKey(ref Message message, Keys keyData)
            {
                if (keyData == Keys.Left || keyData == Keys.Home || keyData == Keys.Right || keyData == Keys.End)
                {
                    bool next = keyData == Keys.Right || keyData == Keys.End;
                    ModeSegment target = next ? owner.automatic : owner.manual; target.Focus(); target.Checked = true;
                    return true;
                }
                return base.ProcessCmdKey(ref message, keyData);
            }
        }
    }
}
