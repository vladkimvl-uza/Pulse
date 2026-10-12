using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Pulse
{
    /// <summary>
    /// Borderless, per-pixel transparent window hosting one WebView2 page. Geometry is kept in CSS
    /// pixels (card size) and converted with the window's DPI and zoom, so the page never has to
    /// know about physical pixels except for glass alignment.
    /// </summary>
    abstract class WebWindow : Form
    {
        /// <summary>Transparent CSS-px border around the card: room for the shadow and 3D tilt.</summary>
        public const int CardMargin = 26;

        protected readonly PulseApp App;
        protected readonly WebView2 Web;
        bool pageReady;
        protected double CssW, CssH, Zoom = 1;
        Rectangle lastMonitor;

        protected WebWindow(PulseApp app)
        {
            App = app;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black; // GDI black has alpha 0, i.e. fully transparent under DWM blur-behind
            ShowInTaskbar = false;
            Text = "Pulse";
            Web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Transparent };
            Controls.Add(Web);
        }

        protected abstract string Page { get; }
        protected virtual bool ToolWindow { get { return true; } }
        protected virtual bool NoActivate { get { return true; } }
        public bool PageReady { get { return pageReady; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= ToolWindow ? Native.WS_EX_TOOLWINDOW : Native.WS_EX_APPWINDOW;
                if (NoActivate) cp.ExStyle |= Native.WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return NoActivate; } }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.EnableAlphaComposition(Handle);
            InitWeb();
        }

        async void InitWeb()
        {
            try
            {
                var env = await App.WebEnvironment();
                await Web.EnsureCoreWebView2Async(env);
                var core = Web.CoreWebView2;
                var s = core.Settings;
                s.AreDefaultContextMenusEnabled = App.Dev;
                s.AreDevToolsEnabled = App.Dev;
                s.AreBrowserAcceleratorKeysEnabled = App.Dev;
                s.IsStatusBarEnabled = false;
                s.IsZoomControlEnabled = false;
                s.IsPinchZoomEnabled = false;
                s.IsSwipeNavigationEnabled = false;
                s.IsBuiltInErrorPageEnabled = false;
                s.IsGeneralAutofillEnabled = false;
                s.IsPasswordAutosaveEnabled = false;
                core.SetVirtualHostNameToFolderMapping("pulse.app", Paths.UiDir, CoreWebView2HostResourceAccessKind.Allow);
                core.SetVirtualHostNameToFolderMapping("pulse.cache", Paths.Cache, CoreWebView2HostResourceAccessKind.Allow);
                core.WebMessageReceived += OnWebMessage;
                core.NavigationStarting += (o, a) => { if (!a.Uri.StartsWith("https://pulse.app/")) a.Cancel = true; };
                core.NewWindowRequested += (o, a) => a.Handled = true;
                core.ProcessFailed += (o, a) =>
                {
                    Log.Write("webview process failed: " + a.ProcessFailedKind);
                    pageReady = false;
                    BeginInvoke(new Action(() => { try { Web.CoreWebView2.Reload(); } catch { } }));
                };
                Web.ZoomFactor = Zoom;
                core.Navigate("https://pulse.app/" + Page);
            }
            catch (Exception ex) { Log.Error("webview init", ex); }
        }

        void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            Dictionary<string, object> msg;
            try { msg = Json.D(Json.Parse(e.WebMessageAsJson)); } catch { return; }
            if (msg == null) return;
            var cmd = Json.Str(msg, "cmd");
            try
            {
                switch (cmd)
                {
                    case "ready": pageReady = true; SendInit(); break;
                    case "dragStart": BeginDrag(); break;
                    case "dragMove": DragMove(); break;
                    case "dragEnd": EndDrag(); break;
                    case "resizeStart": BeginResize(Json.Str(msg, "edge", "br")); break;
                    case "resizeMove": ResizeMove(); break;
                    case "resizeEnd": EndResize(); break;
                    default: OnMessage(cmd, msg); break;
                }
            }
            catch (Exception ex) { Log.Error("message " + cmd, ex); }
        }

        protected abstract void OnMessage(string cmd, Dictionary<string, object> msg);

        protected virtual void SendInit()
        {
            Post(new Dictionary<string, object>
            {
                ["type"] = "init", ["role"] = Role, ["id"] = WidgetId, ["settings"] = App.Store.Root, ["info"] = App.FullInfo(),
                ["sys"] = App.SysState(), ["icons"] = App.Sensors.Icons(), ["margin"] = CardMargin,
            });
            Post(App.PerfState());
            Post(App.AnomState());
            Post(App.Sensors.History.Export(1800));
            if (App.LastTickJson != null) PostRaw(App.LastTickJson);
            SendWall();
            SendGeo();
        }

        protected abstract string Role { get; }
        protected virtual string WidgetId { get { return null; } }

        public void Post(object o) { PostRaw(Json.Write(o)); }

        public async System.Threading.Tasks.Task<string> Exec(string js)
        {
            if (Web.CoreWebView2 == null) return "no webview";
            return await Web.CoreWebView2.ExecuteScriptAsync(js);
        }

        public void PostRaw(string json)
        {
            if (!pageReady || IsDisposed || Web.CoreWebView2 == null) return;
            try { Web.CoreWebView2.PostWebMessageAsJson(json); } catch (Exception ex) { Log.Error("post", ex); }
        }

        // ------------------------------------------------------------ geometry

        public double DpiScale
        {
            get
            {
                int dpi = IsHandleCreated ? Native.GetDpiForWindow(Handle) : 96;
                return (dpi > 0 ? dpi : 96) / 96.0 * Zoom;
            }
        }

        /// <summary>The visible card in physical pixels (window minus the transparent margin).</summary>
        public Rectangle CardBounds
        {
            get { int m = (int)Math.Round(CardMargin * DpiScale); return new Rectangle(Left + m, Top + m, Width - 2 * m, Height - 2 * m); }
        }

        protected Size PhysicalSize(double cssW, double cssH)
        {
            var s = DpiScale;
            return new Size((int)Math.Round((cssW + 2 * CardMargin) * s), (int)Math.Round((cssH + 2 * CardMargin) * s));
        }

        /// <summary>Resize from the card size, keeping whichever screen edge the card is closest to fixed.</summary>
        protected void ApplySize(bool keepTopLeft)
        {
            var size = PhysicalSize(CssW, CssH);
            if (size == Size) return;
            int x = Left, y = Top;
            if (!keepTopLeft && IsHandleCreated)
            {
                var wa = Screen.FromRectangle(Bounds).WorkingArea;
                if (Right - wa.Left > wa.Right - Left && (wa.Right - Right) < (Left - wa.Left)) x = Right - size.Width;
                if (Bottom - wa.Top > wa.Bottom - Top && (wa.Bottom - Bottom) < (Top - wa.Top)) y = Bottom - size.Height;
            }
            Native.SetWindowPos(Handle, IntPtr.Zero, x, y, size.Width, size.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            UpdateRegion();
            SendGeo();
        }

        protected void SetZoom(double z)
        {
            z = MathX.Clamp(z, 0.6, 2.0);
            if (Math.Abs(z - Zoom) < 0.001) return;
            Zoom = z;
            if (Web.CoreWebView2 != null) Web.ZoomFactor = z;
            ApplySize(false);
        }

        /// <summary>
        /// Clip hit-testing to the card plus a little shadow, so the transparent margin does not
        /// swallow clicks meant for whatever is underneath.
        /// </summary>
        protected void UpdateRegion()
        {
            if (!IsHandleCreated) return;
            double s = DpiScale;
            int side = (int)Math.Round((CardMargin - 12) * s), top = (int)Math.Round((CardMargin - 10) * s), bottom = (int)Math.Round((CardMargin - 20) * s);
            int r = (int)Math.Round(36 * s);
            var rgn = Native.CreateRoundRectRgn(side, top, Width - side + 1, Height - bottom + 1, r, r);
            Native.SetWindowRgn(Handle, rgn, true); // the system owns rgn now
        }

        protected void SendGeo()
        {
            Post(new Dictionary<string, object> { ["type"] = "geo", ["x"] = Left, ["y"] = Top, ["w"] = Width, ["h"] = Height, ["scale"] = DpiScale });
            var mon = Screen.FromRectangle(Bounds).Bounds;
            if (mon != lastMonitor) { lastMonitor = mon; SendWall(); }
        }

        public void SendWall() { Post(App.Wall.InfoFor(Bounds)); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_DPICHANGED && IsHandleCreated)
            {
                var r = (Native.RECT)Marshal.PtrToStructure(m.LParam, typeof(Native.RECT));
                var size = PhysicalSize(CssW, CssH);
                Native.SetWindowPos(Handle, IntPtr.Zero, r.Left, r.Top, size.Width, size.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
                UpdateRegion();
                SendGeo();
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        // ------------------------------------------------------------ drag & resize (driven by the page)

        Point grab;
        Size startSize;
        double startCssW, startCssH;
        protected bool Dragging, Resizing;

        protected virtual bool Locked { get { return false; } }
        protected virtual bool NoResize { get { return false; } }

        static bool IsMouseDown()
        {
            return (Native.GetAsyncKeyState(0x01) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x02) & 0x8000) != 0;
        }

        void BeginDrag()
        {
            if (Locked) return;
            Native.POINT p; Native.GetCursorPos(out p);
            grab = new Point(p.X - Left, p.Y - Top);
            Dragging = true;
            OnInteraction(true);
        }

        void DragMove()
        {
            if (!Dragging) return;
            if (!IsMouseDown()) { EndDrag(); return; }
            Native.POINT p; Native.GetCursorPos(out p);
            var target = new Point(p.X - grab.X, p.Y - grab.Y);
            target = Snap(target);
            Native.SetWindowPos(Handle, IntPtr.Zero, target.X, target.Y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            SendGeo();
        }

        void EndDrag()
        {
            if (!Dragging) return;
            Dragging = false;
            ReportSnap("");
            OnInteraction(false);
            OnBoundsCommitted();
        }

        string resizeEdge = "br";
        Rectangle startBounds;

        void BeginResize(string edge)
        {
            if (Locked || NoResize) return;
            Native.POINT p; Native.GetCursorPos(out p);
            grab = new Point(p.X, p.Y);
            startSize = Size;
            startBounds = Bounds;
            startCssW = CssW; startCssH = CssH;
            resizeEdge = string.IsNullOrEmpty(edge) ? "br" : edge;
            Resizing = true;
            OnInteraction(true);
        }

        /// <summary>
        /// Resize from any edge or corner. The opposite edge stays put; moving edges stick to the
        /// screen's work area like the drag magnet does.
        /// </summary>
        void ResizeMove()
        {
            if (!Resizing) return;
            if (!IsMouseDown()) { EndResize(); return; }
            Native.POINT p; Native.GetCursorPos(out p);
            double s = DpiScale;
            int m = (int)Math.Round(CardMargin * s);
            var wa = Screen.FromRectangle(startBounds).WorkingArea;
            var g = App.Store.Global;
            bool snap = Json.Bool(g, "snap", true);
            int gap = (int)Math.Round(MathX.Clamp(Json.Num(g, "snapGap", 14), 0, 60) * s);
            int thr = (int)Math.Round(MathX.Clamp(Json.Num(g, "snapForce", 18), 4, 60) * s);
            var card = new Rectangle(startBounds.X + m, startBounds.Y + m, startBounds.Width - 2 * m, startBounds.Height - 2 * m);
            int left = card.Left, top = card.Top, right = card.Right, bottom = card.Bottom;
            int dx = p.X - grab.X, dy = p.Y - grab.Y;
            int minW = (int)Math.Round(MinCss.Width * s), minH = (int)Math.Round(MinCss.Height * s);
            int maxW = (int)Math.Round(1600 * s), maxH = (int)Math.Round(1100 * s);
            Func<int, int, int> stick = (v, line) => snap && Math.Abs(v - line) <= thr ? line : v;
            string edges = "";
            if (resizeEdge.Contains("r")) { right = stick(card.Right + dx, wa.Right - gap); right = MathX.Clamp(right, left + minW, left + maxW); if (right == wa.Right - gap) edges += "r"; }
            if (resizeEdge.Contains("l")) { left = stick(card.Left + dx, wa.Left + gap); left = MathX.Clamp(left, right - maxW, right - minW); if (left == wa.Left + gap) edges += "l"; }
            if (resizeEdge.Contains("b")) { bottom = stick(card.Bottom + dy, wa.Bottom - gap); bottom = MathX.Clamp(bottom, top + minH, top + maxH); if (bottom == wa.Bottom - gap) edges += "b"; }
            if (resizeEdge.Contains("t")) { top = stick(card.Top + dy, wa.Top + gap); top = MathX.Clamp(top, bottom - maxH, bottom - minH); if (top == wa.Top + gap) edges += "t"; }
            ReportSnap(edges);
            CssW = Math.Round((right - left) / s);
            CssH = Math.Round((bottom - top) / s);
            var size = PhysicalSize(CssW, CssH);
            // Anchor the edges that are not being dragged.
            int x = resizeEdge.Contains("l") ? right + m - size.Width : left - m;
            int y = resizeEdge.Contains("t") ? bottom + m - size.Height : top - m;
            Native.SetWindowPos(Handle, IntPtr.Zero, x, y, size.Width, size.Height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            UpdateRegion();
            SendGeo();
        }

        void EndResize()
        {
            if (!Resizing) return;
            Resizing = false;
            ReportSnap("");
            OnInteraction(false);
            OnBoundsCommitted();
            OnResizeCommitted();
        }

        protected virtual void OnResizeCommitted() { }
        protected virtual Size MinCss { get { return new Size(120, 64); } }
        protected virtual void OnInteraction(bool active) { }
        protected virtual void OnBoundsCommitted() { }

        string snapState = "";

        /// <summary>
        /// Magnetic edges: the card sticks to the screen's work area (at a configurable gap) and to
        /// the edges of other widgets. The page is told which sides stuck so it can light them up.
        /// </summary>
        protected virtual Point Snap(Point pos)
        {
            var g = App.Store.Global;
            if (!Json.Bool(g, "snap", true)) { ReportSnap(""); return pos; }
            double s = DpiScale;
            int m = (int)Math.Round(CardMargin * s);
            int gap = (int)Math.Round(MathX.Clamp(Json.Num(g, "snapGap", 14), 0, 60) * s);
            int thr = (int)Math.Round(MathX.Clamp(Json.Num(g, "snapForce", 18), 4, 60) * s);
            var card = new Rectangle(pos.X + m, pos.Y + m, Width - 2 * m, Height - 2 * m);
            var wa = Screen.FromPoint(new Point(card.Left + card.Width / 2, card.Top + card.Height / 2)).WorkingArea;

            // Each line remembers which screen side it represents ("" for widget-to-widget lines).
            var xs = new List<KeyValuePair<int, string>> { Line(wa.Left + gap, "l"), Line(wa.Right - gap, "r") };
            var ys = new List<KeyValuePair<int, string>> { Line(wa.Top + gap, "t"), Line(wa.Bottom - gap, "b") };
            if (Json.Bool(g, "snapWidgets", true))
            {
                int wg = (int)Math.Round(12 * s);
                foreach (var o in App.OtherCards(this))
                {
                    xs.Add(Line(o.Left, "")); xs.Add(Line(o.Right, "")); xs.Add(Line(o.Left - wg, "")); xs.Add(Line(o.Right + wg, ""));
                    ys.Add(Line(o.Top, "")); ys.Add(Line(o.Bottom, "")); ys.Add(Line(o.Top - wg, "")); ys.Add(Line(o.Bottom + wg, ""));
                }
            }
            string ex, ey;
            int dx = Best(card.Left, card.Right, xs, thr, out ex), dy = Best(card.Top, card.Bottom, ys, thr, out ey);
            ReportSnap(ex + ey);
            return new Point(pos.X + dx, pos.Y + dy);
        }

        static KeyValuePair<int, string> Line(int v, string side) { return new KeyValuePair<int, string>(v, side); }

        static int Best(int a, int b, List<KeyValuePair<int, string>> lines, int thr, out string edge)
        {
            int best = 0, bestDist = thr + 1;
            edge = "";
            foreach (var l in lines)
            {
                int d1 = l.Key - a, d2 = l.Key - b;
                // A screen side only counts for the matching card side (left line в†” left edge).
                if (l.Value != "r" && l.Value != "b" && Math.Abs(d1) < bestDist) { bestDist = Math.Abs(d1); best = d1; edge = l.Value == "" ? "w" : l.Value; }
                if (l.Value != "l" && l.Value != "t" && Math.Abs(d2) < bestDist) { bestDist = Math.Abs(d2); best = d2; edge = l.Value == "" ? "w" : l.Value; }
            }
            if (bestDist > thr) { edge = ""; return 0; }
            return best;
        }

        protected void ReportSnap(string edges)
        {
            if (edges == snapState) return;
            snapState = edges;
            Post(new Dictionary<string, object> { ["type"] = "snap", ["edges"] = edges });
        }
    }

    /// <summary>A desktop widget: lives under all windows ("desktop") or above them ("top").</summary>
    sealed class WidgetWindow : WebWindow
    {
        public readonly string Id;
        bool peek, ghost, hiddenByFullscreen;
        readonly Timer ghostTimer = new Timer { Interval = 120 };
        readonly Timer hideFallback = new Timer { Interval = 700 };

        public WidgetWindow(PulseApp app, string id) : base(app)
        {
            Id = id;
            var cfg = Cfg;
            Size2(cfg, out CssW, out CssH);
            Zoom = MathX.Clamp(Json.Num(cfg, "zoom", 1), 0.6, 2.0);
            ghostTimer.Tick += (s, e) => GhostTick();
            hideFallback.Tick += (s, e) => { hideFallback.Stop(); base.Hide(); };
            Location = new Point(-32000, -32000);
            CreateHandle();
            var size = PhysicalSize(CssW, CssH);
            Size = size;
            PlaceInitially();
            UpdateRegion();
            ApplyFlags();
        }

        Dictionary<string, object> Cfg { get { return App.Store.Widget(Id) ?? App.Store.WidgetDefaults; } }
        protected override string Page { get { return "widget.html"; } }
        protected override string Role { get { return "widget"; } }
        protected override string WidgetId { get { return Id; } }
        protected override bool Locked { get { return Json.Bool(Cfg, "locked"); } }
        public string Mode { get { return Json.Str(Cfg, "mode", "desktop"); } }
        public bool OnTop { get { return Mode == "top" || peek; } }
        /// <summary>Minimized into a capsule with the headline numbers; w / h keep the full size for later.</summary>
        public bool Collapsed { get { return Json.Bool(Cfg, "collapsed"); } }
        /// <summary>Closed with its own button: hidden until "show widgets" (tray or settings).</summary>
        public bool UserClosed { get { return Json.Bool(Cfg, "hidden"); } }
        protected override bool NoResize { get { return Collapsed; } }

        /// <summary>Card size in CSS px: the configured one, or the capsule (about 76 px per number, at most four).</summary>
        static readonly HashSet<string> BothCapable = new HashSet<string> { "cpu", "gpu", "ram", "disk" };
        void Size2(Dictionary<string, object> cfg, out double w, out double h)
        {
            if (Json.Bool(cfg, "collapsed"))
            {
                // ~78 px per number, ~118 when a cell also shows the temperature ("both"); at most four cells
                var cells = (Json.L(Json.Get(cfg, "modules")) ?? new List<object>()).Select(Json.D).Where(m => m != null && Json.Bool(m, "on")).Take(4).ToList();
                w = 62 + cells.Sum(m => Json.Str(m, "cap") == "both" && BothCapable.Contains(Json.Str(m, "id")) ? 118 : 78);
                if (cells.Count == 0) w += 78;
                h = 48;
                return;
            }
            w = Json.Num(cfg, "w", 560); h = Json.Num(cfg, "h", 220);
        }
        bool paused;

        /// <summary>Freezes the page while nobody can see it: no frames, no animation, the last image stays.</summary>
        public void SetPaused(bool on)
        {
            if (on == paused) return;
            paused = on;
            Post(new Dictionary<string, object> { ["type"] = "pause", ["on"] = on });
        }

        protected override void SendInit()
        {
            base.SendInit();
            if (paused) Post(new Dictionary<string, object> { ["type"] = "pause", ["on"] = true });
        }

        void PlaceInitially()
        {
            var cfg = Cfg;
            var x = Json.Get(cfg, "x"); var y = Json.Get(cfg, "y");
            if (x != null && y != null)
            {
                var p = new Point((int)Json.Num(cfg, "x"), (int)Json.Num(cfg, "y"));
                var card = new Rectangle(p.X + Width / 4, p.Y + Height / 4, Width / 2, Height / 2);
                if (Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(card))) { Location = p; return; }
            }
            Location = DefaultLocation();
        }

        Point DefaultLocation()
        {
            var wa = Screen.PrimaryScreen.WorkingArea;
            double s = DpiScale;
            int m = (int)Math.Round(CardMargin * s), gap = (int)Math.Round(16 * s);
            int x = wa.Right - gap - Width + m, y = wa.Top + gap - m;
            // Stack below the lowest existing widget on that edge.
            foreach (var o in App.OtherCards(this)) if (Math.Abs(o.Right - (x + Width - m)) < 40) y = Math.Max(y, o.Bottom + gap - m);
            return new Point(x, y);
        }

        public void ResetPosition()
        {
            App.Store.Set("widgets." + Id + ".x", null);
            App.Store.Set("widgets." + Id + ".y", null);
            Location = DefaultLocation();
            OnBoundsCommitted();
            SendGeo();
        }

        /// <summary>Re-reads size, zoom, mode and click-through from settings.</summary>
        public void ApplyConfig()
        {
            var cfg = Cfg;
            double z = MathX.Clamp(Json.Num(cfg, "zoom", 1), 0.6, 2.0);
            double w, h;
            Size2(cfg, out w, out h);
            bool sizeChanged = Math.Abs(w - CssW) > 0.5 || Math.Abs(h - CssH) > 0.5;
            CssW = w; CssH = h;
            if (Math.Abs(z - Zoom) > 0.001) SetZoom(z);
            else if (sizeChanged) ApplySize(false);
            ApplyFlags();
        }

        public void ApplyFlags()
        {
            if (!IsHandleCreated) return;
            var progman = Native.FindWindow("Progman", null);
            // Owned by the desktop: survives Win+D / "Show desktop" and stays above the wallpaper.
            Native.SetWindowLongPtr(Handle, Native.GWLP_HWNDPARENT, OnTop ? IntPtr.Zero : progman);
            const uint f = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE;
            if (OnTop) Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, f);
            else
            {
                Native.SetWindowPos(Handle, Native.HWND_NOTOPMOST, 0, 0, 0, 0, f);
                Native.SetWindowPos(Handle, Native.HWND_BOTTOM, 0, 0, 0, 0, f);
            }
            bool clickThrough = Json.Bool(Cfg, "clickThrough") && !peek;
            int ex = Native.ExStyle(Handle);
            int want = clickThrough ? ex | Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT : ex & ~(Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT);
            if (want != ex) Native.SetExStyle(Handle, want);
            if (clickThrough && Json.Bool(Cfg, "ghost", true)) ghostTimer.Start();
            else { ghostTimer.Stop(); SetGhost(false); }
        }

        void GhostTick()
        {
            if (!Visible) return;
            Native.POINT p; Native.GetCursorPos(out p);
            SetGhost(CardBounds.Contains(p.X, p.Y));
        }

        void SetGhost(bool on)
        {
            if (on == ghost) return;
            ghost = on;
            Post(new Dictionary<string, object> { ["type"] = "ghost", ["on"] = on });
        }

        public void SetPeek(bool on)
        {
            if (peek == on) return;
            peek = on;
            if (on && !Visible) ShowAnimated();
            if (on) SetPaused(false);
            ApplyFlags();
            Post(new Dictionary<string, object> { ["type"] = "peek", ["on"] = on });
        }

        public void SetFullscreenHidden(bool hide)
        {
            if (hide == hiddenByFullscreen) return;
            hiddenByFullscreen = hide;
            if (hide && Visible && OnTop) base.Hide();
            else if (!hide && !App.AllHidden) { base.Show(); ApplyFlags(); }
        }

        public void ShowAnimated()
        {
            hideFallback.Stop();
            SetPaused(false);
            if (!Visible) base.Show();
            ApplyFlags();
            Post(new Dictionary<string, object> { ["type"] = "visible", ["on"] = true });
        }

        public void HideAnimated()
        {
            if (!Visible) return;
            if (!PageReady) { base.Hide(); return; }
            Post(new Dictionary<string, object> { ["type"] = "visible", ["on"] = false });
            hideFallback.Start();
        }

        protected override void OnInteraction(bool active)
        {
            Post(new Dictionary<string, object> { ["type"] = "interaction", ["on"] = active, ["resize"] = Resizing });
        }

        protected override void OnResizeCommitted() { App.WidgetResized(this); }

        /// <summary>Moves the window so its card's top-left lands at the given screen point.</summary>
        public void MoveCardTo(int x, int y)
        {
            int m = (int)Math.Round(CardMargin * DpiScale);
            Native.SetWindowPos(Handle, IntPtr.Zero, x - m, y - m, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
            App.Store.Set("widgets." + Id + ".x", Left);
            App.Store.Set("widgets." + Id + ".y", Top);
            SendGeo();
        }

        protected override void OnBoundsCommitted()
        {
            App.Store.Set("widgets." + Id + ".x", Left);
            App.Store.Set("widgets." + Id + ".y", Top);
            if (!Collapsed) { App.Store.Set("widgets." + Id + ".w", CssW); App.Store.Set("widgets." + Id + ".h", CssH); }
            App.BroadcastSettings();
        }

        protected override void OnMessage(string cmd, Dictionary<string, object> msg)
        {
            switch (cmd)
            {
                case "hidden": hideFallback.Stop(); base.Hide(); break;
                default: App.HandleCommand(cmd, msg, this); break;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = new IntPtr(Native.MA_NOACTIVATE); return; }
            if (m.Msg == Native.WM_WINDOWPOSCHANGING && IsHandleCreated && !OnTop)
            {
                // Pin to the bottom of the z-order unless we are the one changing it to topmost.
                var wp = (Native.WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(Native.WINDOWPOS));
                if ((wp.flags & Native.SWP_NOZORDER) == 0 && wp.hwndInsertAfter != Native.HWND_TOPMOST)
                {
                    wp.hwndInsertAfter = Native.HWND_BOTTOM;
                    Marshal.StructureToPtr(wp, m.LParam, false);
                }
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { ghostTimer.Dispose(); hideFallback.Dispose(); }
            base.Dispose(disposing);
        }
    }

    /// <summary>The settings window: a normal (activatable, taskbar) window with the same glass look.</summary>
    sealed class SettingsWindow : WebWindow
    {
        public SettingsWindow(PulseApp app, Rectangle near) : base(app)
        {
            CssW = 860; CssH = 600;
            Icon = app.AppIcon;
            Text = "Pulse";
            ShowInTaskbar = true;
            Location = new Point(-32000, -32000);
            CreateHandle();
            Size = PhysicalSize(CssW, CssH);
            var wa = Screen.FromRectangle(near).WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + Math.Max(0, (wa.Height - Height) / 2));
            UpdateRegion();
        }

        protected override string Page { get { return "settings.html"; } }
        protected override string Role { get { return "settings"; } }
        protected override bool ToolWindow { get { return false; } }
        protected override bool NoActivate { get { return false; } }
        protected override Point Snap(Point pos) { return pos; }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.Style |= 0x20000; /* WS_MINIMIZEBOX: taskbar click restores */ return cp; }
        }

        protected override void SendInit()
        {
            base.SendInit();
            string section, widget;
            App.TakePendingNav(out section, out widget);
            if (section != null || widget != null) Navigate(section, widget);
        }

        public void Navigate(string section, string widgetId)
        {
            Post(new Dictionary<string, object> { ["type"] = "nav", ["section"] = section, ["widget"] = widgetId });
        }

        protected override void OnMessage(string cmd, Dictionary<string, object> msg)
        {
            switch (cmd)
            {
                case "closed": Close(); break;
                case "minimize": WindowState = FormWindowState.Minimized; break;
                default: App.HandleCommand(cmd, msg, this); break;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            App.SettingsClosed(this);
            base.OnFormClosed(e);
        }
    }

    /// <summary>
    /// In-game FPS HUD: a small topmost, click-through glass pill pinned to a corner of the
    /// monitor the game is on. It never takes focus or input.
    /// </summary>
    sealed class OverlayWindow : WebWindow
    {
        public OverlayWindow(PulseApp app) : base(app)
        {
            Location = new Point(-32000, -32000);
            CreateHandle();
            int ex = Native.ExStyle(Handle);
            Native.SetExStyle(Handle, ex | Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT);
        }

        protected override string Page { get { return "overlay.html"; } }
        protected override string Role { get { return "overlay"; } }

        /// <summary>Size for the chosen content, then pin to the corner of the target's monitor.</summary>
        public void Place(Rectangle monitor, string corner, string content, double zoom)
        {
            CssW = content == "fps" ? 150 : 236;
            CssH = content == "sensors" ? 104 : 62;
            Zoom = MathX.Clamp(zoom, 0.6, 2.0);
            if (Web.CoreWebView2 != null && Math.Abs(Web.ZoomFactor - Zoom) > 0.001) Web.ZoomFactor = Zoom;
            var size = PhysicalSize(CssW, CssH);
            int m = (int)Math.Round(CardMargin * DpiScale), gap = (int)Math.Round(12 * DpiScale);
            int x = corner.EndsWith("r") ? monitor.Right - gap - size.Width + m : monitor.Left + gap - m;
            int y = corner.StartsWith("b") ? monitor.Bottom - gap - size.Height + m : monitor.Top + gap - m;
            if (Bounds != new Rectangle(x, y, size.Width, size.Height))
            {
                Native.SetWindowPos(Handle, Native.HWND_TOPMOST, x, y, size.Width, size.Height, Native.SWP_NOACTIVATE);
                UpdateRegion();
            }
        }

        public void Appear()
        {
            if (!Visible) { base.Show(); Post(new Dictionary<string, object> { ["type"] = "visible", ["on"] = true }); }
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        public void Disappear()
        {
            if (Visible) base.Hide();
        }

        protected override void OnMessage(string cmd, Dictionary<string, object> msg) { }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE) { m.Result = new IntPtr(Native.MA_NOACTIVATE); return; }
            base.WndProc(ref m);
        }
    }
}
