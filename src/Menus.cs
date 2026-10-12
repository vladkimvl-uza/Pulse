using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Pulse
{
    /// <summary>Windows 11-style context menus: rounded corners, soft highlight, no icon gutter.</summary>
    sealed class ModernRenderer : ToolStripProfessionalRenderer
    {
        readonly Color bg, fg, fgDim, hover, sep, accent;

        public ModernRenderer(bool dark) : base(new Colors(dark))
        {
            RoundedEdges = false;
            bg = dark ? Color.FromArgb(38, 38, 42) : Color.FromArgb(249, 249, 251);
            fg = dark ? Color.FromArgb(242, 242, 247) : Color.FromArgb(29, 29, 31);
            fgDim = dark ? Color.FromArgb(120, 242, 242, 247) : Color.FromArgb(120, 29, 29, 31);
            hover = dark ? Color.FromArgb(28, 255, 255, 255) : Color.FromArgb(16, 0, 0, 0);
            sep = dark ? Color.FromArgb(22, 255, 255, 255) : Color.FromArgb(18, 0, 0, 0);
            accent = Color.FromArgb(10, 132, 255);
        }

        sealed class Colors : ProfessionalColorTable
        {
            readonly bool dark;
            public Colors(bool d) { dark = d; UseSystemColors = false; }
            public override Color MenuBorder { get { return dark ? Color.FromArgb(60, 60, 66) : Color.FromArgb(220, 220, 224); } }
            public override Color ToolStripDropDownBackground { get { return dark ? Color.FromArgb(38, 38, 42) : Color.FromArgb(249, 249, 251); } }
            public override Color ImageMarginGradientBegin { get { return ToolStripDropDownBackground; } }
            public override Color ImageMarginGradientMiddle { get { return ToolStripDropDownBackground; } }
            public override Color ImageMarginGradientEnd { get { return ToolStripDropDownBackground; } }
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) { e.Graphics.Clear(bg); }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            using (var path = Round(r, 6))
            using (var b = new SolidBrush(hover)) g.FillPath(b, path);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? fg : fgDim;
            if (e.Item.Tag as string == "header") e.TextFont = new Font(e.TextFont, FontStyle.Bold);
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(sep)) e.Graphics.DrawLine(p, 12, y, e.Item.Width - 12, y);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = e.ImageRectangle;
            float s = Math.Min(r.Width, r.Height);
            float x = r.Left + (r.Width - s) / 2f, y = r.Top + (r.Height - s) / 2f;
            using (var p = new Pen(accent, Math.Max(1.6f, s / 8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(p, new[] { new PointF(x + s * 0.2f, y + s * 0.52f), new PointF(x + s * 0.42f, y + s * 0.74f), new PointF(x + s * 0.8f, y + s * 0.3f) });
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = fgDim;
            base.OnRenderArrow(e);
        }

        static GraphicsPath Round(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    static class MenuKit
    {
        public static Font MenuFont()
        {
            using (var probe = new Font("Segoe UI Variable Text", 9.5f))
                return probe.Name == "Segoe UI Variable Text" ? new Font("Segoe UI Variable Text", 9.5f) : new Font("Segoe UI", 9.5f);
        }

        public static ContextMenuStrip Create(bool dark)
        {
            var m = new ContextMenuStrip { Renderer = new ModernRenderer(dark), Font = MenuFont(), ShowImageMargin = false, ShowCheckMargin = true, DropShadowEnabled = true };
            Style(m);
            return m;
        }

        /// <summary>
        /// Widgets never take focus, so Windows would not dismiss their menus on an outside click.
        /// Watch the mouse ourselves: a fresh click outside every open drop-down, Esc, or another
        /// window coming to the front closes the menu.
        /// </summary>
        public static void AutoClose(ContextMenuStrip menu)
        {
            var timer = new Timer { Interval = 30 };
            bool armed = false;
            IntPtr fg = IntPtr.Zero;
            Func<bool> down = () => (Native.GetAsyncKeyState(0x01) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x02) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x04) & 0x8000) != 0;
            Func<ToolStripDropDown, IEnumerable<ToolStripDropDown>> open = null;
            open = dd => new[] { dd }.Concat(dd.Items.OfType<ToolStripMenuItem>().Where(i => i.DropDown.Visible).SelectMany(i => open(i.DropDown)));
            timer.Tick += (s, e) =>
            {
                if (!menu.Visible) { timer.Stop(); return; }
                bool isDown = down();
                if (!armed) { armed = !isDown; return; } // ignore the button still held from opening the menu
                bool esc = (Native.GetAsyncKeyState(0x1B) & 0x8000) != 0;
                var nowFg = Native.GetForegroundWindow();
                bool switched = fg != IntPtr.Zero && nowFg != fg && open(menu).All(d => d.Handle != nowFg);
                if (isDown)
                {
                    var p = Cursor.Position;
                    if (open(menu).All(d => !d.Bounds.Contains(p))) { menu.Close(ToolStripDropDownCloseReason.AppClicked); timer.Stop(); return; }
                }
                if (esc || switched) { menu.Close(ToolStripDropDownCloseReason.Keyboard); timer.Stop(); }
                fg = nowFg;
            };
            menu.Opened += (s, e) => { armed = false; fg = Native.GetForegroundWindow(); timer.Start(); };
            menu.Closed += (s, e) => timer.Stop();
            menu.Disposed += (s, e) => timer.Dispose();
        }

        /// <summary>Rounded Win11 corners for a drop-down and all its sub-menus.</summary>
        public static void Style(ToolStripDropDown dd)
        {
            dd.Opened += (s, e) => { try { Native.SetDwm(dd.Handle, 33, 2); } catch { } };
            dd.Padding = new Padding(0, 4, 0, 4);
        }

        public static ToolStripMenuItem Item(string text, Action click, bool check = false, bool enabled = true)
        {
            var it = new ToolStripMenuItem(text) { Checked = check, Enabled = enabled, Padding = new Padding(0, 3, 0, 3) };
            if (click != null) it.Click += (s, e) => click();
            return it;
        }

        public static ToolStripMenuItem Sub(string text, ContextMenuStrip owner)
        {
            var it = new ToolStripMenuItem(text) { Padding = new Padding(0, 3, 0, 3) };
            var dd = (ToolStripDropDownMenu)it.DropDown;
            dd.ShowImageMargin = false;
            dd.ShowCheckMargin = true;
            dd.Renderer = owner.Renderer;
            dd.Font = owner.Font;
            Style(dd);
            return it;
        }
    }
}
