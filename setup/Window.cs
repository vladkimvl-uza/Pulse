using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Pulse;

namespace PulseSetup
{
    /// <summary>Borderless, per-pixel transparent window with the setup pages (same technique as Pulse).</summary>
    sealed class SetupWindow : Form
    {
        const int CssW = 760, CssH = 500, Pad = 30;
        readonly WebView2 web;
        readonly string uiDir;
        readonly bool uninstall;
        string dir;
        bool busy, ready;
        Point grab;
        bool dragging;
        static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

        public SetupWindow(string uiDir, bool uninstall, string dir)
        {
            this.uiDir = uiDir; this.uninstall = uninstall; this.dir = dir;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Black;
            Text = uninstall ? "Pulse — удаление" : "Pulse Setup";
            ShowInTaskbar = true;
            try { Icon = Icon.ExtractAssociatedIcon(System.Reflection.Assembly.GetExecutingAssembly().Location); } catch { }
            web = new WebView2 { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Transparent };
            Controls.Add(web);
            CreateHandle();
            double s = Scale;
            Size = new Size((int)((CssW + 2 * Pad) * s), (int)((CssH + 2 * Pad) * s));
            var wa = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
        }

        new double Scale { get { int d = IsHandleCreated ? Native.GetDpiForWindow(Handle) : 96; return (d > 0 ? d : 96) / 96.0; } }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.Style |= 0x20000; cp.ExStyle |= 0x40000; return cp; } // minimize box, app window
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.EnableAlphaComposition(Handle);
            Init();
        }

        async void Init()
        {
            try
            {
                CoreWebView2Environment.SetLoaderDllFolderPath(uiDir);
                var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Path.GetTempPath(), "PulseSetup-wv2"));
                await web.EnsureCoreWebView2Async(env);
                var c = web.CoreWebView2;
                c.Settings.AreDefaultContextMenusEnabled = false;
                c.Settings.AreDevToolsEnabled = false;
                c.Settings.IsStatusBarEnabled = false;
                c.Settings.IsZoomControlEnabled = false;
                c.Settings.AreBrowserAcceleratorKeysEnabled = false;
                c.SetVirtualHostNameToFolderMapping("setup.pulse", uiDir, CoreWebView2HostResourceAccessKind.Allow);
                c.WebMessageReceived += (o, a) => OnMessage(a.WebMessageAsJson);
                c.NavigationStarting += (o, a) => { if (!a.Uri.StartsWith("https://setup.pulse/")) a.Cancel = true; };
                c.Navigate("https://setup.pulse/setup.html");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Pulse Setup: " + ex.Message, "Pulse", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        void Post(Dictionary<string, object> m)
        {
            if (!ready || IsDisposed) return;
            try { web.CoreWebView2.PostWebMessageAsJson(Json.Serialize(m)); } catch { }
        }

        void OnMessage(string json)
        {
            var m = Json.Deserialize<Dictionary<string, object>>(json);
            object v;
            string cmd = m.TryGetValue("cmd", out v) ? v as string : null;
            switch (cmd)
            {
                case "ready":
                    ready = true;
                    var lang = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
                    Post(new Dictionary<string, object>
                    {
                        ["type"] = "init", ["auto"] = AutoRun, ["device"] = Device.Kind(), ["model"] = Device.Model(), ["mode"] = uninstall ? "uninstall" : "install", ["version"] = Program.Version, ["dir"] = dir,
                        ["installed"] = Engine.IsInstalled(), ["sameVersion"] = Engine.InstalledVersion() == Program.Version, ["lang"] = lang == "uz" ? "uz" : lang == "ru" ? "ru" : "en",
                    });
                    break;
                case "dragStart": Native.POINT p; Native.GetCursorPos(out p); grab = new Point(p.X - Left, p.Y - Top); dragging = true; break;
                case "dragMove":
                    if (!dragging) break;
                    Native.GetCursorPos(out p);
                    Location = new Point(p.X - grab.X, p.Y - grab.Y);
                    if ((Native.GetAsyncKeyState(0x01) & 0x8000) == 0) dragging = false;
                    break;
                case "dragEnd": dragging = false; break;
                case "minimize": WindowState = FormWindowState.Minimized; break;
                case "close": if (!busy) Close(); break;
                case "browse":
                    using (var dlg = new FolderBrowserDialog { SelectedPath = dir, ShowNewFolderButton = true })
                        if (dlg.ShowDialog(this) == DialogResult.OK)
                        {
                            dir = dlg.SelectedPath.TrimEnd('\\');
                            if (!dir.EndsWith("Pulse", StringComparison.OrdinalIgnoreCase)) dir = Path.Combine(dir, "Pulse");
                            Post(new Dictionary<string, object> { ["type"] = "dir", ["dir"] = dir });
                        }
                    break;
                case "install":
                    var o = new Options
                    {
                        Autostart = Flag(m, "autostart"), Sensors = Flag(m, "sensors"), Desktop = Flag(m, "desktop"), Launch = Flag(m, "launch"),
                    };
                    Start(() => { if (Demo) Simulate(false, false); else new Engine(dir, Progress).Install(o); });
                    break;
                case "uninstall":
                    bool keep = Flag(m, "keep");
                    Start(() => { if (Demo) Simulate(true, keep); else new Engine(dir, Progress).Uninstall(keep); });
                    break;
                case "launch":
                    var exe = Path.Combine(dir, Engine.Exe);
                    if (File.Exists(exe)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + exe + "\"") { UseShellExecute = true });
                    Close();
                    break;
            }
        }

        /// <summary>"--demo": the pages run through their steps without touching the system (design checks).</summary>
        public static bool Demo, AutoRun;

        void Simulate(bool removing, bool keep)
        {
            var steps = removing
                ? new[] { Tuple.Create(0.05, "closing"), Tuple.Create(0.25, "unregistering"), Tuple.Create(0.5, "removing"), Tuple.Create(keep ? 0.5 : 0.85, keep ? "removing" : "removingData"), Tuple.Create(1.0, "removed") }
                : new[] { Tuple.Create(0.02, "closing"), Tuple.Create(0.05, "copying"), Tuple.Create(0.62, "shortcuts"), Tuple.Create(0.68, "registering"), Tuple.Create(0.8, "sensors"), Tuple.Create(1.0, "done") };
            double f = 0;
            foreach (var s in steps)
                for (; f < s.Item1; f = Math.Min(s.Item1, f + 0.01)) { Progress(f, s.Item2); System.Threading.Thread.Sleep(45); }
            Progress(1, steps[steps.Length - 1].Item2);
        }

        static bool Flag(Dictionary<string, object> m, string k) { object v; return m.TryGetValue(k, out v) && v is bool && (bool)v; }

        void Progress(double f, string key)
        {
            BeginInvoke(new Action(() => Post(new Dictionary<string, object> { ["type"] = "progress", ["f"] = f, ["step"] = key })));
        }

        void Start(Action work)
        {
            busy = true;
            Task.Run(() =>
            {
                string error = null;
                try { work(); } catch (Exception ex) { error = ex.Message; }
                BeginInvoke(new Action(() =>
                {
                    busy = false;
                    Post(new Dictionary<string, object> { ["type"] = "finished", ["error"] = error });
                }));
            });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0010 && busy) return; // WM_CLOSE while installing
            base.WndProc(ref m);
        }
    }
}
