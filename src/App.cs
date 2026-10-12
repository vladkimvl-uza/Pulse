using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace Pulse
{
    sealed class PulseApp : ApplicationContext
    {
        public const string Version = BuildInfo.Version;

        public readonly SettingsStore Store = new SettingsStore();
        public readonly SensorService Sensors = new SensorService();
        public readonly WallpaperService Wall = new WallpaperService();
        public readonly IspService Isp = new IspService();
        public readonly LlmService Llm = new LlmService();
        public readonly NetQualityService NetQ = new NetQualityService();
        public readonly FpsService Fps = new FpsService();
        public PerfGovernor Perf { get; private set; }
        public readonly AnomalyService Anom = new AnomalyService(Paths.Local);
        OverlayWindow overlay;
        readonly System.Windows.Forms.Timer overlayTimer = new System.Windows.Forms.Timer { Interval = 250 };
        DateTime lastGame = DateTime.MinValue, overlaySeen = DateTime.MinValue, lastFpsLog = DateTime.MinValue;
        public readonly bool Dev;
        public Icon AppIcon;
        public string LastTickJson;
        public bool AllHidden { get; private set; }

        readonly bool elevated;
        bool pawn, taskExists, taskAutostart, runKey;
        string setupState = "";
        readonly SynchronizationContext ui;
        readonly List<WidgetWindow> widgets = new List<WidgetWindow>();
        SettingsWindow settingsWin;
        string pendingSection, pendingWidget, balloonSection;
        Task<CoreWebView2Environment> envTask;

        NotifyIcon tray;
        ContextMenuStrip trayMenu, lastMenu;
        IntPtr trayHicon;
        long lastTrayDraw;
        string lastTrayKey = "";

        HotkeyWindow hotkeys;
        bool hotkeyOk, peekAll;
        readonly System.Windows.Forms.Timer fsTimer = new System.Windows.Forms.Timer { Interval = 1500 };
        readonly System.Windows.Forms.Timer wallTimer = new System.Windows.Forms.Timer { Interval = 60000 };
        readonly HashSet<object> procDemand = new HashSet<object>();
        readonly Dictionary<string, DateTime> alertLast = new Dictionary<string, DateTime>();
        readonly HashSet<string> alertActive = new HashSet<string>();
        Dictionary<string, object> lastTick;
        bool quitting;
        int lastDump;

        public readonly bool Sandbox;

        public PulseApp(bool dev, bool elevated, bool sandbox)
        {
            Dev = dev;
            Sandbox = sandbox;
            this.elevated = elevated;
            ui = SynchronizationContext.Current;

            Store.Load();
            Strings.Lang = Json.Str(Store.Global, "lang", "ru");
            try { AppIcon = Icon.ExtractAssociatedIcon(Paths.Exe); } catch { AppIcon = IconFactory.AppIconHandle(64); }

            pawn = Elevation.PawnIOInstalled();
            runKey = Elevation.RunKeyEnabled();

            Sensors.DeviceKind = DeviceKind();
            Sensors.DeviceModel = Device.Model();
            BatteryService.Fake = Dev && Environment.GetCommandLineArgs().Any(a => a.Equals("--fake-battery", StringComparison.OrdinalIgnoreCase));
            if (BatteryService.Fake)
            {
                // design aid only: a made-up pack on this desktop, so do not show this PC's board as the "laptop"
                Sensors.DeviceKind = "laptop";
                Sensors.DeviceModel = new Dictionary<string, object> { ["vendor"] = null, ["model"] = Strings.Lang == "en" ? "Demo laptop" : Strings.Lang == "uz" ? "Demo noutbuk" : "Демо-ноутбук" };
            }
            Store.ApplyDevice(Sensors.DeviceKind);
            Anom.Config = () => Json.D(Json.Get(Store.Global, "anomaly")) ?? new Dictionary<string, object>();
            Anom.IdleSeconds = Native.IdleSeconds;
            Anom.Fullscreen = () => { int st; return Native.SHQueryUserNotificationState(out st) == 0 && (st == 2 || st == 3 || st == 4); };
            Anom.Changed += () => Post(BroadcastAnom);
            Anom.Raised += a => Post(() => NotifyAnomaly(a));
            Sensors.WantProcsSlow = AnomOn;
            ApplyInterval();
            Sensors.SetGpu(Json.Str(Store.Global, "gpu", "auto"));
            Sensors.Sampled += snap => { var json = Json.Write(snap); Post(() => OnTick(snap, json)); };
            Sensors.InfoReady += info => Post(BroadcastInfo);
            Isp.Enabled = Json.Bool(Store.Global, "isp", true);
            Isp.Changed += info => Post(BroadcastInfo);
            Isp.Kick();
            Sensors.Llm = Llm;
            ApplyPsu();
            Sensors.NetQ = NetQ;
            Llm.Enabled = Json.Bool(Store.Global, "llm", true);
            Llm.ContainerPref = Json.Str(Store.Global, "llmContainer", "auto");
            NetQ.Enabled = Json.Bool(Store.Global, "netQuality", true);
            Llm.Start();
            NetQ.Start();
            Sensors.Start();

            Wall.Changed += () => { foreach (var w in Windows()) w.SendWall(); };
            Wall.Refresh();

            foreach (var cfg in Store.Widgets.Select(Json.D).ToList()) CreateWidget(Json.Str(cfg, "id"));
            if (widgets.Count > 0 && widgets.All(w => w.UserClosed)) AllHidden = true;

            BuildTray();
            hotkeys = new HotkeyWindow();
            RegisterHotkey();

            overlayTimer.Tick += (s, e) => OverlayTick();
            overlayTimer.Start();
            fsTimer.Tick += (s, e) => FullscreenTick();
            UpdateFsTimer();
            wallTimer.Tick += (s, e) => Wall.Refresh();
            wallTimer.Start();
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

            // Slow shell-outs (schtasks) happen off the UI thread.
            Task.Run(() =>
            {
                bool exists = Elevation.TaskExists();
                bool auto = exists && Elevation.TaskAutostart();
                Post(() => { taskExists = exists; taskAutostart = auto; ApplyAutostart(); BroadcastSys(); });
            });

            Perf = new PerfGovernor(this);
            Perf.Changed += () => { ApplyInterval(); BroadcastPerf(); };
            ApplyInterval();

            if (Dev) StartDevChannel();

            if (Store.FirstRun && !Sandbox)
            {
                Store.SaveNow();
                CreateStartMenuShortcut();
                tray.ShowBalloonTip(6000, Strings.Get("running"), Strings.Get("runningHint"), ToolTipIcon.Info);
            }
        }

        public void Post(Action a) { ui.Post(_ => { try { a(); } catch (Exception ex) { Log.Error("ui", ex); } }, null); }

        /// <summary>
        /// --dev only: drop %LOCALAPPDATA%\Pulse\dev\cmd.json ({"action":..} or {"js":..,"target":"widget|settings"})
        /// to drive the app without touching the mouse. Script results go to dev\out.txt.
        /// </summary>
        void StartDevChannel()
        {
            var dir = Path.Combine(Paths.Local, "dev");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "cmd.json");
            var timer = new System.Windows.Forms.Timer { Interval = 400 };
            timer.Tick += async (s, e) =>
            {
                if (!File.Exists(file)) return;
                string text;
                try { text = File.ReadAllText(file); File.Delete(file); } catch { return; }
                try
                {
                    var cmd = Json.D(Json.Parse(text));
                    var js = Json.Str(cmd, "js");
                    if (js == null) { Action(Json.Str(cmd, "action"), cmd, widgets.FirstOrDefault()); return; }
                    var target = Json.Str(cmd, "target", "widget");
                    var sb = new StringBuilder();
                    foreach (var w in Windows().Where(x => target == "all" || (target == "settings") == (x is SettingsWindow)).ToList())
                        sb.AppendLine(await w.Exec(js));
                    File.WriteAllText(Path.Combine(dir, "out.txt"), sb.ToString());
                }
                catch (Exception ex) { File.WriteAllText(Path.Combine(dir, "out.txt"), "ERR " + ex.Message); }
            };
            timer.Start();
        }

        public Task<CoreWebView2Environment> WebEnvironment()
        {
            if (envTask == null)
            {
                var opts = new CoreWebView2EnvironmentOptions("--disable-background-networking --disable-features=msSmartScreenProtection,msWebOOUI");
                // Elevated and normal runs must not share a profile: WebView2 refuses mixed integrity.
                envTask = CoreWebView2Environment.CreateAsync(null, Paths.WebData + (elevated ? "-admin" : ""), opts);
            }
            return envTask;
        }

        IEnumerable<WebWindow> Windows()
        {
            foreach (var w in widgets.ToList()) if (!w.IsDisposed) yield return w;
            if (settingsWin != null && !settingsWin.IsDisposed) yield return settingsWin;
        }

        public IEnumerable<Rectangle> OtherCards(WebWindow self)
        {
            return widgets.Where(w => w != self && !w.IsDisposed && w.Visible).Select(w => w.CardBounds).ToList();
        }

        void Broadcast(object msg)
        {
            var json = Json.Write(msg);
            foreach (var w in Windows()) w.PostRaw(json);
        }

        void ApplyPsu()
        {
            var psu = Json.D(Json.Get(Store.Global, "psu"));
            Sensors.PsuWatt = (int)MathX.Clamp(Json.Num(psu, "watt", 850), 200, 3000);
            Sensors.PsuRating = Json.Str(psu, "rating", "gold");
        }

        void BroadcastInfo() { Broadcast(new Dictionary<string, object> { ["type"] = "info", ["info"] = FullInfo() }); }

        /// <summary>Hardware info plus the internet provider (when enabled) and downloaded vendor icons.</summary>
        public Dictionary<string, object> FullInfo()
        {
            var info = new Dictionary<string, object>(Sensors.Info);
            if (Isp.Enabled && Isp.Info != null) info["isp"] = Isp.Info;
            lock (brandLogos) info["brandLogos"] = new Dictionary<string, object>(brandLogos);
            return info;
        }

        readonly Dictionary<string, object> brandLogos = LoadBrandLogos();
        readonly HashSet<string> brandFetching = new HashSet<string>();
        static string BrandFile { get { return Path.Combine(Paths.Cache, "brand-logos.json"); } }

        static Dictionary<string, object> LoadBrandLogos()
        {
            try { if (File.Exists(BrandFile)) return Json.D(Json.Parse(File.ReadAllText(BrandFile))) ?? new Dictionary<string, object>(); } catch { }
            return new Dictionary<string, object>();
        }

        /// <summary>Fetches a vendor's official site icon once; "" marks a vendor whose site gave nothing.</summary>
        void FetchBrandLogo(string key, string domain)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(domain) || !System.Text.RegularExpressions.Regex.IsMatch(domain, @"^[a-z0-9.-]+\.[a-z]{2,}$")) return;
            lock (brandLogos) { if (brandLogos.ContainsKey(key) || !brandFetching.Add(key)) return; }
            Task.Run(() =>
            {
                string url = null;
                try { url = SiteIcon.Fetch(domain, "brand-"); } catch (Exception ex) { Log.Write("brand " + key + ": " + ex.Message); }
                lock (brandLogos)
                {
                    brandLogos[key] = url ?? "";
                    brandFetching.Remove(key);
                    try { File.WriteAllText(BrandFile, Json.Write(brandLogos)); } catch { }
                }
                Post(BroadcastInfo);
            });
        }

        public void BroadcastSettings() { Broadcast(new Dictionary<string, object> { ["type"] = "settings", ["settings"] = Store.Root }); }
        void BroadcastSys() { Broadcast(new Dictionary<string, object> { ["type"] = "sys", ["sys"] = SysState() }); }

        public Dictionary<string, object> PerfState()
        {
            return new Dictionary<string, object>
            {
                ["type"] = "perf", ["mode"] = Perf != null ? Perf.Mode : PerfGovernor.Balanced, ["reason"] = Perf != null ? Perf.Reason : "",
                ["weak"] = PerfGovernor.WeakHardware,
            };
        }
        void BroadcastPerf() { Broadcast(PerfState()); }

        // ------------------------------------------------------------ anomalies

        bool AnomOn { get { return Json.Bool(Json.D(Json.Get(Store.Global, "anomaly")), "enabled", true); } }

        /// <summary>What is unusual right now, and how far the learning has come (every page).</summary>
        public Dictionary<string, object> AnomState()
        {
            return new Dictionary<string, object>
            {
                ["type"] = "anom", ["active"] = Anom.Active.Select(a => (object)a.Wire()).ToList(),
                ["learned"] = Math.Round(Anom.Learned, 3), ["learnedSec"] = Math.Round(Anom.LearnedSeconds), ["learnNeed"] = AnomalyService.LearnSeconds,
            };
        }
        void BroadcastAnom() { Broadcast(AnomState()); }

        /// <summary>The journal for the settings page, newest first.</summary>
        void SendAnomJournal(WebWindow to)
        {
            if (to == null) return;
            var s = AnomState();
            s["type"] = "anomJournal";
            s["events"] = Anom.Journal.OrderByDescending(a => a.T0).Select(a => (object)a.Wire()).ToList();
            to.Post(s);
        }

        void NotifyAnomaly(AnomalyService.Anomaly a)
        {
            var cfg = Json.D(Json.Get(Store.Global, "anomaly"));
            if (!Json.Bool(cfg, "notify", true) || tray == null) return;
            DateTime last;
            if (alertLast.TryGetValue("anom:" + a.Key, out last) && DateTime.Now - last < TimeSpan.FromMinutes(30)) return;
            alertLast["anom:" + a.Key] = DateTime.Now;
            Func<string, string> p = k => Json.Str(a.P, k, "");
            string unit = Json.Str(Store.Global, "tempUnit", "C");
            Func<string, string> deg = k => { double c = Json.Num(a.P, k); return Math.Round(unit == "F" ? c * 9 / 5 + 32 : c) + "°"; };
            string text;
            switch (a.Kind)
            {
                case "hot": text = string.Format(Strings.Get("an.hot." + a.Mod), deg("temp"), deg("norm")); break;
                case "fanStop": text = string.Format(Strings.Get("an.fanStop"), p("fan"), p("norm")); break;
                case "idleCpu": text = string.Format(Strings.Get("an.idleCpu"), ExeTitle(p("exe")), p("proc")); break;
                case "idleGpu": text = string.Format(Strings.Get("an.idleGpu"), ExeTitle(p("exe")), p("proc")); break;
                case "leak": text = string.Format(Strings.Get("an.leak"), ExeTitle(p("exe")), p("gb"), p("rate")); break;
                case "rail": text = string.Format(Strings.Get("an.rail"), p("rail"), p("v"), p("dev")); break;
                case "batDrain": text = string.Format(Strings.Get("an.batDrain"), p("rate"), p("norm")); break;
                default: return;
            }
            balloonSection = "anomaly";
            tray.ShowBalloonTip(8000, Strings.Get("an.title"), text, ToolTipIcon.Warning);
        }

        static string ExeTitle(string exe) { return System.Text.RegularExpressions.Regex.Replace(exe ?? "", @"\.exe$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase); }

        void AnomIgnore(string key, bool on)
        {
            var cfg = Json.D(Json.Get(Store.Global, "anomaly"));
            var list = (Json.L(Json.Get(cfg, "ignore")) ?? new List<object>()).Select(Convert.ToString).Where(k => k != key).Select(k => (object)k).ToList();
            if (on) list.Add(key);
            SetSetting("global.anomaly.ignore", list);
        }

        /// <summary>Sampling interval from settings; eco never samples faster than every 2 s.</summary>
        void ApplyInterval()
        {
            int ms = (int)MathX.Clamp(Json.Num(Store.Global, "interval", 1000), 250, 10000);
            if (Perf != null && Perf.IsEco) ms = Math.Max(ms, 2000);
            Sensors.IntervalMs = ms;
        }

        public IEnumerable<WidgetWindow> Widgets { get { return widgets.Where(w => !w.IsDisposed).ToList(); } }

        /// <summary>What the installer decided (install.json next to the exe), else detected here.</summary>
        static string DeviceKind()
        {
            try
            {
                var f = Path.Combine(Paths.AppDir, "install.json");
                if (File.Exists(f))
                {
                    var kind = Json.Str(Json.D(Json.Parse(File.ReadAllText(f))), "device");
                    if (kind == "laptop" || kind == "desktop") return kind;
                }
            }
            catch { }
            return Device.Kind();
        }

        public Dictionary<string, object> SysState()
        {
            return new Dictionary<string, object>
            {
                ["elevated"] = elevated, ["pawnio"] = pawn, ["task"] = taskExists,
                ["autostart"] = taskExists ? taskAutostart : runKey, ["setup"] = setupState,
                ["version"] = Version, ["hotkeyOk"] = hotkeyOk, ["fps"] = Fps.Error ?? (Fps.Running ? "running" : "idle"), ["widgets"] = widgets.Count, ["hidden"] = AllHidden,
            };
        }

        // ------------------------------------------------------------ data flow

        void OnTick(Dictionary<string, object> snap, string json)
        {
            if (quitting) return;
            lastTick = snap;
            if (Perf != null) Perf.FeedCpu(Json.Num(Json.D(Json.Get(snap, "cpu")), "load", 0));
            Anom.Feed(snap);
            LastTickJson = json;
            if (Dev && Environment.TickCount - lastDump > 5000)
            {
                lastDump = Environment.TickCount;
                try { File.WriteAllText(Path.Combine(Paths.Local, "last-tick.json"), json); } catch { }
            }
            foreach (var w in Windows()) w.PostRaw(json);
            UpdateTray(false);
            CheckAlerts();
        }

        // ------------------------------------------------------------ widgets

        WidgetWindow CreateWidget(string id)
        {
            var w = new WidgetWindow(this, id);
            widgets.Add(w);
            w.FormClosed += (s, e) => { widgets.Remove(w); procDemand.Remove(w); Sensors.WantProcs = procDemand.Count > 0; };
            if (!AllHidden && !w.UserClosed) { w.Show(); w.ApplyFlags(); }
            return w;
        }

        public void ShowAll()
        {
            AllHidden = false;
            // "show widgets" also brings back the ones closed with their own button
            foreach (var w in widgets) if (w.UserClosed) Store.Set("widgets." + w.Id + ".hidden", false);
            foreach (var w in widgets) w.ShowAnimated();
            BroadcastSys();
            BroadcastSettings();
        }

        bool closeHintShown;

        /// <summary>
        /// The widget's own close button: hides just this one (nothing is deleted). When it was the last
        /// one on screen, the tray icon brings them back — said once in a balloon.
        /// </summary>
        void CloseWidget(WidgetWindow w)
        {
            if (w == null) return;
            SetSetting("widgets." + w.Id + ".hidden", true);
            w.HideAnimated();
            if (widgets.All(x => x.UserClosed))
            {
                AllHidden = true;
                BroadcastSys();
                if (!closeHintShown && tray != null) { closeHintShown = true; tray.ShowBalloonTip(6000, Strings.Get("closedTitle"), Strings.Get("closedHint"), ToolTipIcon.Info); }
            }
        }

        public void HideAll()
        {
            AllHidden = true;
            peekAll = false;
            foreach (var w in widgets) { w.SetPeek(false); w.HideAnimated(); }
            BroadcastSys();
        }

        void ToggleAll() { if (AllHidden) ShowAll(); else HideAll(); }

        void TogglePeek()
        {
            if (AllHidden) { ShowAll(); peekAll = false; }
            peekAll = !peekAll;
            foreach (var w in widgets) w.SetPeek(peekAll);
        }

        public void ShowFromOtherInstance()
        {
            if (AllHidden) ShowAll();
            OpenSettings(null, null);
        }

        // ------------------------------------------------------------ settings & commands

        /// <summary>Widget properties that follow each other while "change all widgets together" is on.</summary>
        static readonly HashSet<string> LinkedKeys = new HashSet<string> { "w", "h", "zoom", "opacity", "bgOpacity", "mode", "clickThrough", "ghost", "locked" };
        bool Linked { get { return Json.Bool(Store.Global, "linkWidgets", true); } }

        public void SetSetting(string path, object value)
        {
            if (string.IsNullOrEmpty(path)) return;
            Store.Set(path, value);
            var parts = path.Split('.');
            if (parts[0] == "widgets" && parts.Length > 2)
            {
                var targets = new List<WidgetWindow>();
                var w = widgets.FirstOrDefault(x => x.Id == parts[1]);
                if (w != null) targets.Add(w);
                string key = parts.Length == 3 ? parts[2] : null;
                if (Linked && key != null)
                {
                    foreach (var o in widgets.Where(x => x.Id != parts[1]))
                    {
                        if (LinkedKeys.Contains(key)) { Store.Set("widgets." + o.Id + "." + key, value); targets.Add(o); }
                        else if (key == "modules" && CopyStyles(Json.L(value), o.Id)) targets.Add(o);
                    }
                }
                foreach (var t in targets) t.ApplyConfig();
                if (targets.Count > 1 && (key == "w" || key == "h" || key == "zoom")) Reflow();
            }
            else if (parts[0] == "global" && parts.Length > 1) ApplyGlobal(parts[1]);
            BroadcastSettings();
        }

        /// <summary>Gives another widget the same visual style per module (which modules are shown stays its own).</summary>
        bool CopyStyles(List<object> from, string toId)
        {
            var dst = Json.L(Json.Get(Store.Widget(toId), "modules"));
            if (from == null || dst == null) return false;
            bool changed = false;
            foreach (var src in from.Select(Json.D).Where(x => x != null))
            {
                var d = dst.Select(Json.D).FirstOrDefault(x => Json.Str(x, "id") == Json.Str(src, "id"));
                if (d != null && Json.Str(d, "style") != Json.Str(src, "style")) { d["style"] = Json.Str(src, "style", "auto"); changed = true; }
            }
            if (changed) Store.SaveSoon();
            return changed;
        }

        /// <summary>A widget was resized by its edge: the others follow when linked.</summary>
        public void WidgetResized(WidgetWindow w)
        {
            if (!Linked || widgets.Count < 2) return;
            var cfg = Store.Widget(w.Id);
            foreach (var o in widgets.Where(x => x != w))
            {
                Store.Set("widgets." + o.Id + ".w", Json.Get(cfg, "w"));
                Store.Set("widgets." + o.Id + ".h", Json.Get(cfg, "h"));
                o.ApplyConfig();
            }
            Reflow();
            BroadcastSettings();
        }

        /// <summary>After sizes change, widgets that now overlap move apart (down, or to the next column).</summary>
        void Reflow()
        {
            var list = widgets.Where(x => x.Visible).OrderBy(x => x.CardBounds.Top).ThenBy(x => x.CardBounds.Left).ToList();
            for (int i = 1; i < list.Count; i++)
            {
                for (int pass = 0; pass < list.Count; pass++)
                {
                    var b = list[i].CardBounds;
                    var hit = list.Take(i).Select(x => x.CardBounds).FirstOrDefault(a => a.IntersectsWith(b));
                    if (hit.IsEmpty) break;
                    int gap = (int)Math.Round(14 * list[i].DpiScale);
                    var wa = Screen.FromRectangle(hit).WorkingArea;
                    if (hit.Bottom + gap + b.Height <= wa.Bottom) list[i].MoveCardTo(b.Left, hit.Bottom + gap);
                    else list[i].MoveCardTo(hit.Left - gap - b.Width, Math.Max(wa.Top + gap, hit.Top));
                }
            }
        }

        void ApplyGlobal(string key)
        {
            var g = Store.Global;
            switch (key)
            {
                case "interval": ApplyInterval(); Sensors.Poke(); break;
                case "anomaly": Sensors.WantProcsSlow = AnomOn; BroadcastAnom(); break; // disabling ends active ones on the next tick
                case "perf": Perf.Check(); break;
                case "gpu": Sensors.SetGpu(Json.Str(g, "gpu", "auto")); break;
                case "hotkey": RegisterHotkey(); BroadcastSys(); break;
                case "hideFullscreen": UpdateFsTimer(); break;
                case "lang": Strings.Lang = Json.Str(g, "lang", "ru"); break;
                case "autostart": ApplyAutostart(); BroadcastSys(); break;
                case "trayMetric": case "colors": lastTrayDraw = 0; UpdateTray(true); break;
                case "isp": Isp.Enabled = Json.Bool(g, "isp", true); if (Isp.Enabled) Isp.Kick(200); BroadcastInfo(); break;
                case "llm": Llm.Enabled = Json.Bool(g, "llm", true); break;
                case "llmContainer": Llm.ContainerPref = Json.Str(g, "llmContainer", "auto"); break;
                case "netQuality": NetQ.Enabled = Json.Bool(g, "netQuality", true); break;
                case "fps": RegisterHotkey(); BroadcastSys(); break;
                case "psu": ApplyPsu(); break;
            }
        }

        public void HandleCommand(string cmd, Dictionary<string, object> msg, WebWindow sender)
        {
            switch (cmd)
            {
                case "set": SetSetting(Json.Str(msg, "path"), Json.Get(msg, "value")); break;
                case "menu": var ww = sender as WidgetWindow; if (ww != null) ShowWidgetMenu(ww); break;
                case "need":
                    if (Json.Bool(msg, "procs")) procDemand.Add(sender); else procDemand.Remove(sender);
                    bool want = procDemand.Count > 0;
                    if (want && !Sensors.WantProcs) { Sensors.WantProcs = true; Sensors.Poke(); }
                    Sensors.WantProcs = want;
                    break;
                case "action": Action(Json.Str(msg, "name"), msg, sender); break;
            }
        }

        void Action(string name, Dictionary<string, object> msg, WebWindow sender)
        {
            string id = Json.Str(msg, "id");
            var w = widgets.FirstOrDefault(x => x.Id == id);
            switch (name)
            {
                case "widgetMin": SetSetting("widgets." + id + ".collapsed", Json.Bool(msg, "on", true)); break;
                case "widgetClose": CloseWidget(w); break;
                case "widgetShow": if (w != null) { SetSetting("widgets." + id + ".hidden", false); if (!AllHidden) w.ShowAnimated(); else ShowAll(); } break;
                case "anomJournal": SendAnomJournal(sender); break;
                case "anomIgnore": AnomIgnore(Json.Str(msg, "key"), true); SendAnomJournal(sender); break;
                case "anomUnignore": AnomIgnore(Json.Str(msg, "key"), false); SendAnomJournal(sender); break;
                case "anomReset": Anom.Reset(); SendAnomJournal(sender); break;
                case "anomClear": Anom.ClearJournal(); SendAnomJournal(sender); break;
                case "anomTest": if (Dev) { Anom.Inject(Json.Str(msg, "kind") ?? "all"); BroadcastAnom(); } break;
                case "openSettings": OpenSettings(Json.Str(msg, "section"), id ?? (sender is WidgetWindow ? ((WidgetWindow)sender).Id : null)); break;
                case "addWidget": AddWidget(null); break;
                case "duplicateWidget": AddWidget(id); break;
                case "removeWidget": RemoveWidget(id); break;
                case "resetPosition": if (w != null) w.ResetPosition(); break;
                case "sizePreset": SizePreset(id, Json.Str(msg, "preset")); break;
                case "toggleAll": ToggleAll(); break;
                case "showAll": ShowAll(); break;
                case "hideAll": HideAll(); break;
                case "peek": TogglePeek(); break;
                case "enableSensors": EnableSensors(); break;
                case "exportCsv": ExportCsv(); break;
                case "openDataFolder": Process.Start("explorer.exe", "\"" + Paths.Roaming + "\""); break;
                case "openLog": if (File.Exists(Log.Path)) Process.Start("notepad.exe", "\"" + Log.Path + "\""); break;
                case "resetSettings": ResetSettings(); break;
                case "testAlert": tray.ShowBalloonTip(5000, "Pulse", Strings.Get("alert.test"), ToolTipIcon.Info); break;
                case "quit": Quit(); break;
                case "settingsTop": if (Dev && settingsWin != null) settingsWin.TopMost = true; break;
                case "brandLogo": FetchBrandLogo(Json.Str(msg, "key"), Json.Str(msg, "domain")); break;
                case "demoOverlay": if (Dev) DemoOverlay(); break;
            }
        }

        static readonly Dictionary<string, int[]> Presets = new Dictionary<string, int[]>
        {
            ["s"] = new[] { 220, 220 }, ["m"] = new[] { 560, 220 }, ["l"] = new[] { 620, 440 },
            ["wide"] = new[] { 900, 210 }, ["tall"] = new[] { 250, 640 }, ["strip"] = new[] { 640, 60 },
        };

        void SizePreset(string id, string preset)
        {
            int[] wh;
            if (id == null || preset == null || !Presets.TryGetValue(preset, out wh)) return;
            SetSetting("widgets." + id + ".w", wh[0]);
            SetSetting("widgets." + id + ".h", wh[1]);
        }

        void AddWidget(string copyFrom)
        {
            var cfg = Store.NewWidget();
            var src = copyFrom != null ? Store.Widget(copyFrom) : null;
            if (src != null)
            {
                var id = cfg["id"];
                cfg = Json.D(Json.Clone(src));
                cfg["id"] = id;
            }
            else
            {
                // A new widget starts with a single module so it is obviously different from the first.
                var mods = Json.L(cfg["modules"]).Select(Json.D).ToList();
                var used = new HashSet<string>(Store.Widgets.Select(Json.D).SelectMany(x => Json.L(x["modules"]).Select(Json.D)).Where(m => Json.Bool(m, "on")).Select(m => Json.Str(m, "id")));
                var pick = mods.FirstOrDefault(m => !used.Contains(Json.Str(m, "id"))) ?? mods[0];
                foreach (var m in mods) m["on"] = m == pick;
                cfg["w"] = 260; cfg["h"] = 260;
            }
            cfg["x"] = null; cfg["y"] = null;
            Store.Widgets.Add(cfg);
            Store.SaveSoon();
            var win = CreateWidget(Json.Str(cfg, "id"));
            if (AllHidden) ShowAll();
            BroadcastSettings();
            BroadcastSys();
            if (settingsWin != null && !settingsWin.IsDisposed) settingsWin.Navigate("widgets", win.Id);
        }

        void RemoveWidget(string id)
        {
            var w = widgets.FirstOrDefault(x => x.Id == id);
            if (w == null) return;
            if (widgets.Count <= 1) { HideAll(); return; }
            Store.Widgets.RemoveAll(x => Json.Str(Json.D(x), "id") == id);
            Store.SaveSoon();
            w.Close();
            BroadcastSettings();
            BroadcastSys();
        }

        void ResetSettings()
        {
            Store.ResetToDefaults();
            foreach (var w in widgets.ToList()) w.Close();
            widgets.Clear();
            foreach (var cfg in Store.Widgets.Select(Json.D).ToList()) CreateWidget(Json.Str(cfg, "id"));
            Strings.Lang = Json.Str(Store.Global, "lang", "ru");
            ApplyInterval();
            Perf.Check();
            RegisterHotkey();
            UpdateFsTimer();
            ApplyAutostart();
            BroadcastSettings();
            BroadcastSys();
        }

        public void OpenSettings(string section, string widgetId)
        {
            if (settingsWin == null || settingsWin.IsDisposed)
            {
                var near = widgets.Count > 0 ? widgets[0].Bounds : Screen.PrimaryScreen.Bounds;
                settingsWin = new SettingsWindow(this, near);
                pendingSection = section; pendingWidget = widgetId;
                settingsWin.Show();
            }
            else
            {
                if (settingsWin.WindowState == FormWindowState.Minimized) settingsWin.WindowState = FormWindowState.Normal;
                if (section != null || widgetId != null) settingsWin.Navigate(section, widgetId);
            }
            settingsWin.Activate();
            Native.SetForegroundWindow(settingsWin.Handle);
        }

        /// <summary>Navigation requested before the settings page finished loading.</summary>
        public void TakePendingNav(out string section, out string widget)
        {
            section = pendingSection; widget = pendingWidget;
            pendingSection = pendingWidget = null;
        }

        public void SettingsClosed(SettingsWindow w)
        {
            if (settingsWin == w) settingsWin = null;
            procDemand.Remove(w);
            Sensors.WantProcs = procDemand.Count > 0;
        }

        void ExportCsv()
        {
            using (var dlg = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = "pulse-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            })
            {
                IWin32Window owner = settingsWin != null && !settingsWin.IsDisposed ? settingsWin : null;
                if (dlg.ShowDialog(owner) != DialogResult.OK) return;
                File.WriteAllText(dlg.FileName, Sensors.History.Csv(), new UTF8Encoding(true));
            }
        }

        // ------------------------------------------------------------ elevation / sensors

        void EnableSensors()
        {
            bool auto = Json.Bool(Store.Global, "autostart", true);
            if (elevated)
            {
                if (pawn && taskExists) return;
                setupState = "running"; BroadcastSys();
                Task.Run(() =>
                {
                    int rc = Elevation.RunSetup(auto);
                    Post(() => { setupState = rc == 0 ? "done" : "failed"; pawn = Elevation.PawnIOInstalled(); BroadcastSys(); });
                });
                return;
            }
            var p = Elevation.LaunchSetup(auto);
            if (p == null) { setupState = "cancelled"; BroadcastSys(); return; }
            setupState = "running"; BroadcastSys();
            Task.Run(() =>
            {
                p.WaitForExit();
                int rc = p.ExitCode;
                // On success the elevated instance replaces this one; reaching here means it did not.
                Post(() =>
                {
                    setupState = rc == 0 ? "done" : "failed";
                    pawn = Elevation.PawnIOInstalled();
                    BroadcastSys();
                    if (rc != 0) tray.ShowBalloonTip(5000, "Pulse", Strings.Get("setup.fail"), ToolTipIcon.Warning);
                });
            });
        }

        void ApplyAutostart()
        {
            if (Sandbox) return;
            bool want = Json.Bool(Store.Global, "autostart", true);
            if (taskExists)
            {
                if (elevated && want != taskAutostart)
                {
                    try { Elevation.RegisterTask(want); taskAutostart = want; } catch (Exception ex) { Log.Error("task autostart", ex); }
                }
                if (runKey) { Elevation.SetRunKey(false); runKey = false; }
            }
            else if (want != runKey) { Elevation.SetRunKey(want); runKey = Elevation.RunKeyEnabled(); }
        }

        static void CreateStartMenuShortcut()
        {
            try
            {
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                var lnk = Path.Combine(dir, "Pulse.lnk");
                var t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic sh = Activator.CreateInstance(t);
                dynamic s = sh.CreateShortcut(lnk);
                s.TargetPath = Paths.Exe;
                s.WorkingDirectory = Paths.AppDir;
                s.Description = "Pulse — CPU, GPU, RAM monitor";
                s.IconLocation = Paths.Exe + ",0";
                s.Save();
            }
            catch (Exception ex) { Log.Error("shortcut", ex); }
        }

        // ------------------------------------------------------------ tray & menus

        static bool IsDark(Dictionary<string, object> g)
        {
            var theme = Json.Str(g, "theme", "auto");
            if (theme == "dark") return true;
            if (theme == "light") return false;
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k == null || Convert.ToInt32(k.GetValue("AppsUseLightTheme", 0)) == 0;
            }
            catch { return true; }
        }

        static bool LightTaskbar()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k != null && Convert.ToInt32(k.GetValue("SystemUsesLightTheme", 0)) == 1;
            }
            catch { return false; }
        }

        void BuildTray()
        {
            tray = new NotifyIcon { Icon = AppIcon, Text = "Pulse", Visible = true };
            trayMenu = MenuKit.Create(IsDark(Store.Global));
            trayMenu.Opening += (s, e) => PopulateTrayMenu();
            tray.ContextMenuStrip = trayMenu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ToggleAll(); };
            // an anomaly balloon opens the anomaly page; other alerts just open settings
            tray.BalloonTipClicked += (s, e) => { var sec = balloonSection; balloonSection = null; OpenSettings(sec, null); };
            tray.BalloonTipClosed += (s, e) => balloonSection = null;
            UpdateTray(true);
        }

        void PopulateTrayMenu()
        {
            var g = Store.Global;
            trayMenu.Renderer = new ModernRenderer(IsDark(g));
            trayMenu.Items.Clear();
            trayMenu.Items.Add(new ToolStripMenuItem("Pulse") { Enabled = false, Tag = "header" });
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(MenuKit.Item(Strings.Get(AllHidden ? "show" : "hide"), ToggleAll));
            int closed = widgets.Count(w => w.UserClosed);
            if (!AllHidden && closed > 0) trayMenu.Items.Add(MenuKit.Item(string.Format(Strings.Get("showClosed"), closed), ShowAll));
            bool allTop = widgets.Count > 0 && widgets.All(w => w.Mode == "top");
            trayMenu.Items.Add(MenuKit.Item(Strings.Get("onTop"), () => { foreach (var w in widgets.ToList()) SetSetting("widgets." + w.Id + ".mode", allTop ? "desktop" : "top"); }, allTop));
            bool allCt = widgets.Count > 0 && widgets.All(w => Json.Bool(Store.Widget(w.Id), "clickThrough"));
            trayMenu.Items.Add(MenuKit.Item(Strings.Get("clickThrough"), () => { foreach (var w in widgets.ToList()) SetSetting("widgets." + w.Id + ".clickThrough", !allCt); }, allCt));
            trayMenu.Items.Add(MenuKit.Item(Strings.Get("snap"), () => SetSetting("global.snap", !Json.Bool(g, "snap", true)), Json.Bool(g, "snap", true)));
            var hk = Json.Str(g, "hotkey", "");
            if (!string.IsNullOrEmpty(hk) && hk != "none")
                trayMenu.Items.Add(MenuKit.Item(Strings.Get("peekHint") + "    " + hk, TogglePeek, peekAll));
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(MenuKit.Item(Strings.Get("add"), () => AddWidget(null)));
            trayMenu.Items.Add(MenuKit.Item(Strings.Get("settings"), () => OpenSettings(null, null)));
            bool auto = taskExists ? taskAutostart : runKey;
            trayMenu.Items.Add(MenuKit.Item(Strings.Get("autostart"), () => SetSetting("global.autostart", !auto), auto));
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(MenuKit.Item(Strings.Get("quit"), Quit));
        }

        /// <summary>Metrics a module can headline (the values live in ui/widget.js METRICS; same keys in ui/core.js).</summary>
        static readonly Dictionary<string, string[]> MetricKeys = new Dictionary<string, string[]>
        {
            ["cpu"] = new[] { "load", "temp", "clock", "power" }, ["gpu"] = new[] { "load", "temp", "vram", "hot", "memT", "clock", "power" },
            ["ram"] = new[] { "load", "used", "avail", "temp" }, ["disk"] = new[] { "act", "read", "write", "temp" }, ["net"] = new[] { "down", "up", "ping" },
            ["llm"] = new[] { "tps", "vram" }, ["fans"] = new[] { "max", "rpm" }, ["power"] = new[] { "wall", "pcpu", "pgpu", "today" },
            ["battery"] = new[] { "pct", "rate", "time", "health" },
        };
        static readonly HashSet<string> MetricBoth = new HashSet<string> { "cpu", "gpu", "ram", "disk" };

        void ShowWidgetMenu(WidgetWindow w)
        {
            var cfg = Store.Widget(w.Id);
            if (cfg == null) return;
            if (lastMenu != null) { lastMenu.Dispose(); lastMenu = null; }
            var m = lastMenu = MenuKit.Create(IsDark(Store.Global));
            MenuKit.AutoClose(m);
            string p = "widgets." + w.Id + ".";
            var mods = Json.L(cfg["modules"]).Select(Json.D).ToList();

            var modMenu = MenuKit.Sub(Strings.Get("modules"), m);
            foreach (var mod in mods)
            {
                var id = Json.Str(mod, "id");
                if (id == "battery" && Sensors.DeviceKind != "laptop") continue; // no battery on this PC
                bool on = Json.Bool(mod, "on");
                modMenu.DropDownItems.Add(MenuKit.Item(Strings.Get("m." + id), () =>
                {
                    if (on && mods.Count(x => Json.Bool(x, "on")) <= 1) return; // keep at least one
                    mod["on"] = !on;
                    SetSetting(p + "modules", mods.Cast<object>().ToList());
                }, on));
            }

            var styles = mods.Where(x => Json.Bool(x, "on")).Select(x => Json.Str(x, "style", "auto")).Distinct().ToList();
            string current = styles.Count == 1 ? styles[0] : null;
            var viewMenu = MenuKit.Sub(Strings.Get("view"), m);
            foreach (var s in new[] { "auto", "ring", "gauge", "chart", "pie", "bars", "numbers" })
            {
                var style = s;
                viewMenu.DropDownItems.Add(MenuKit.Item(Strings.Get("s." + s), () =>
                {
                    foreach (var mod in mods) mod["style"] = style;
                    SetSetting(p + "modules", mods.Cast<object>().ToList());
                }, current == s));
            }

            var sizeMenu = MenuKit.Sub(Strings.Get("size"), m);
            foreach (var z in new[] { "s", "m", "l", "wide", "tall", "strip" })
            {
                var preset = z;
                sizeMenu.DropDownItems.Add(MenuKit.Item(Strings.Get("z." + z), () => SizePreset(w.Id, preset)));
            }

            var bgMenu = MenuKit.Sub(Strings.Get("bg"), m);
            double bgNow = Json.Num(cfg, "bgOpacity", 1);
            foreach (var pct in new[] { 100, 80, 60, 40, 20, 0 })
            {
                double v = pct / 100.0;
                // The slider is "transparency", the setting is opacity of the glass.
                string label = pct == 0 ? Strings.Get("bgNone") : (100 - pct) + "%";
                bgMenu.DropDownItems.Add(MenuKit.Item(label, () => SetSetting(p + "bgOpacity", v), Math.Abs(bgNow - v) < 0.051));
            }

            m.Items.Add(modMenu);
            // per module: what it headlines in this widget, and what it shows in the capsule
            var showMenu = MenuKit.Sub(Strings.Get("mtShow"), m);
            var capMenu = MenuKit.Sub(Strings.Get("mtCap"), m);
            foreach (var mod in mods.Where(x => Json.Bool(x, "on")))
            {
                var mid = Json.Str(mod, "id");
                string[] keys;
                if (mid == null || !MetricKeys.TryGetValue(mid, out keys)) continue;
                foreach (var target in new[] { Tuple.Create(showMenu, "main"), Tuple.Create(capMenu, "cap") })
                {
                    string field = target.Item2, cur = Json.Str(mod, field, "");
                    var sub = MenuKit.Sub(Strings.Get("m." + mid), m);
                    var opts = new List<string> { "" };
                    opts.AddRange(keys);
                    if (field == "cap" && MetricBoth.Contains(mid)) opts.Add("both");
                    foreach (var k in opts)
                    {
                        var key = k;
                        sub.DropDownItems.Add(MenuKit.Item(Strings.Get(key == "" ? "mt.auto" : "mt." + key), () => SetSetting(p + "modules." + mid + "." + field, key), cur == key));
                    }
                    target.Item1.DropDownItems.Add(sub);
                }
            }
            if (showMenu.DropDownItems.Count > 0) { m.Items.Add(showMenu); m.Items.Add(capMenu); }
            m.Items.Add(viewMenu);
            m.Items.Add(sizeMenu);
            m.Items.Add(bgMenu);
            m.Items.Add(new ToolStripSeparator());
            bool top = w.Mode == "top";
            m.Items.Add(MenuKit.Item(Strings.Get("onTop"), () => SetSetting(p + "mode", top ? "desktop" : "top"), top));
            bool ct = Json.Bool(cfg, "clickThrough");
            m.Items.Add(MenuKit.Item(Strings.Get("clickThrough"), () => SetSetting(p + "clickThrough", !ct), ct));
            bool locked = Json.Bool(cfg, "locked");
            m.Items.Add(MenuKit.Item(Strings.Get("lock"), () => SetSetting(p + "locked", !locked), locked));
            m.Items.Add(new ToolStripSeparator());
            bool linked = Linked;
            m.Items.Add(MenuKit.Item(Strings.Get("link"), () => SetSetting("global.linkWidgets", !linked), linked));
            m.Items.Add(MenuKit.Item(Strings.Get("customize"), () => OpenSettings("widgets", w.Id)));
            m.Items.Add(MenuKit.Item(Strings.Get("add"), () => AddWidget(null)));
            m.Items.Add(MenuKit.Item(Strings.Get("duplicate"), () => AddWidget(w.Id)));
            if (widgets.Count > 1) m.Items.Add(MenuKit.Item(Strings.Get("remove"), () => RemoveWidget(w.Id)));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(MenuKit.Item(Strings.Get("resetPos"), () => w.ResetPosition()));
            m.Items.Add(MenuKit.Item(Strings.Get(w.Collapsed ? "wExpand" : "wCollapse"), () => SetSetting(p + "collapsed", !w.Collapsed)));
            m.Items.Add(MenuKit.Item(Strings.Get("wClose"), () => CloseWidget(w)));
            m.Items.Add(MenuKit.Item(Strings.Get("hide"), HideAll));
            m.Show(Cursor.Position);
        }

        void UpdateTray(bool force)
        {
            if (tray == null || lastTick == null) return;
            long now = Environment.TickCount;
            if (!force && now - lastTrayDraw < 1900) return;
            lastTrayDraw = now;
            var g = Store.Global;
            var metric = Json.Str(g, "trayMetric", "cpu");
            var cpu = Json.D(lastTick["cpu"]); var gpu = Json.D(lastTick["gpu"]); var ram = Json.D(lastTick["ram"]);

            string unit = Json.Str(g, "tempUnit", "C");
            Func<Dictionary<string, object>, string> tempOf = d =>
            {
                var t = Json.Get(d, "temp");
                if (t == null) return "";
                double c = Json.Num(d, "temp");
                return " · " + Math.Round(unit == "F" ? c * 9 / 5 + 32 : c) + "°";
            };
            var text = "Pulse\nCPU " + Math.Round(Json.Num(cpu, "load")) + "%" + tempOf(cpu) +
                       "\nGPU " + Math.Round(Json.Num(gpu, "load")) + "%" + tempOf(gpu) +
                       "\nRAM " + Math.Round(Json.Num(ram, "load")) + "%";
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;

            if (metric == "none")
            {
                if (lastTrayKey != "none") { tray.Icon = AppIcon; FreeTrayIcon(); lastTrayKey = "none"; }
                return;
            }
            var d = metric == "gpu" ? gpu : metric == "ram" ? ram : cpu;
            double frac = Json.Num(d, "load") / 100.0;
            var colors = Json.D(g["colors"]);
            Color color;
            try { color = ColorTranslator.FromHtml(Json.Str(colors, metric, "#0A84FF")); } catch { color = Color.DodgerBlue; }
            bool alert = alertActive.Contains(metric == "gpu" ? "gpuTemp" : metric == "ram" ? "ramLoad" : "cpuTemp");
            bool light = LightTaskbar();
            var key = metric + Math.Round(frac * 40) + alert + light + color.ToArgb();
            if (key == lastTrayKey) return;
            lastTrayKey = key;
            int size = SystemInformation.SmallIconSize.Width;
            var h = IconFactory.TrayIcon(frac, color, light, size, alert);
            tray.Icon = Icon.FromHandle(h);
            FreeTrayIcon();
            trayHicon = h;
        }

        void FreeTrayIcon()
        {
            if (trayHicon != IntPtr.Zero) { Native.DestroyIcon(trayHicon); trayHicon = IntPtr.Zero; }
        }

        // ------------------------------------------------------------ alerts

        void CheckAlerts()
        {
            var g = Store.Global;
            var a = Json.D(g["alerts"]);
            if (lastTick == null || !Json.Bool(a, "enabled", true)) return;
            string unit = Json.Str(g, "tempUnit", "C");
            Func<double, string> fmtT = c => Math.Round(unit == "F" ? c * 9 / 5 + 32 : c) + (unit == "F" ? "°F" : "°C");
            CheckOne("cpuTemp", Json.D(lastTick["cpu"]), "temp", Json.Num(a, "cpuTemp", 90), 4, Strings.Get("alert.title"), v => string.Format(Strings.Get("alert.cpu"), fmtT(v)));
            CheckOne("gpuTemp", Json.D(lastTick["gpu"]), "temp", Json.Num(a, "gpuTemp", 85), 4, Strings.Get("alert.title"), v => string.Format(Strings.Get("alert.gpu"), fmtT(v)));
            CheckOne("ramLoad", Json.D(lastTick["ram"]), "load", Json.Num(a, "ramLoad", 95), 3, Strings.Get("alert.ram"), v => string.Format(Strings.Get("alert.ramT"), Math.Round(v) + "%"));
        }

        void CheckOne(string key, Dictionary<string, object> d, string field, double threshold, double hysteresis, string title, Func<double, string> text)
        {
            var raw = Json.Get(d, field);
            if (raw == null || threshold <= 0) return;
            double v = Json.Num(d, field);
            if (v >= threshold)
            {
                if (alertActive.Add(key))
                {
                    lastTrayDraw = 0;
                    DateTime last;
                    if (!alertLast.TryGetValue(key, out last) || DateTime.Now - last > TimeSpan.FromMinutes(10))
                    {
                        alertLast[key] = DateTime.Now;
                        tray.ShowBalloonTip(6000, title, text(v), ToolTipIcon.Warning);
                    }
                }
            }
            else if (v < threshold - hysteresis && alertActive.Remove(key)) lastTrayDraw = 0;
        }

        // ------------------------------------------------------------ hotkey, fullscreen, system events

        void RegisterHotkey()
        {
            if (Sandbox) return;
            hotkeyOk = hotkeys.Register(1, Json.Str(Store.Global, "hotkey", "Ctrl+Alt+P"), TogglePeek);
            var fps = Json.D(Store.Global["fps"]);
            hotkeys.Register(2, Json.Str(fps, "hotkey", "Ctrl+Alt+F"), () => SetSetting("global.fps.enabled", !Json.Bool(Json.D(Store.Global["fps"]), "enabled", true)));
        }

        /// <summary>--dev only: shows the HUD with synthetic frames so its look can be checked without a game.</summary>
        void DemoOverlay()
        {
            var cfg = Json.D(Store.Global["fps"]);
            if (overlay == null || overlay.IsDisposed) overlay = new OverlayWindow(this);
            var rnd = new Random();
            var t = new System.Windows.Forms.Timer { Interval = 250 };
            int n = 0;
            t.Tick += (s, e) =>
            {
                if (++n > 60) { t.Stop(); t.Dispose(); overlay.Disappear(); return; }
                overlaySeen = DateTime.UtcNow;
                overlay.Place(Screen.PrimaryScreen.Bounds, Json.Str(cfg, "corner", "tl"), Json.Str(cfg, "content", "sensors"), Json.Num(cfg, "zoom", 1));
                overlay.Appear();
                var series = Enumerable.Range(0, 90).Select(i => Math.Round(6.9 + rnd.NextDouble() * 1.2 + (i % 37 == 0 ? 9 : 0), 1)).ToArray();
                var info = new Dictionary<string, object> { ["type"] = "fps", ["fps"] = 140 + rnd.Next(8), ["low"] = 97, ["ft"] = 7.0, ["series"] = series, ["content"] = Json.Str(cfg, "content", "sensors"), ["app"] = "demo" };
                if (lastTick != null) { info["cpu"] = lastTick["cpu"]; info["gpu"] = lastTick["gpu"]; }
                overlay.Post(info);
            };
            t.Start();
        }

        // ------------------------------------------------------------ in-game FPS overlay

        static readonly HashSet<string> ShellClasses = new HashSet<string> { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Core.CoreWindow" };

        static bool IsFullscreen(IntPtr hwnd)
        {
            var cls = new StringBuilder(64);
            Native.GetClassName(hwnd, cls, 64);
            if (ShellClasses.Contains(cls.ToString())) return false;
            // A maximized window (VS Code, a browser) covers the whole monitor when the taskbar auto-hides;
            // games run borderless or exclusive, never as a maximized window.
            if (Native.IsZoomed(hwnd)) return false;
            Native.RECT r;
            if (!Native.GetWindowRect(hwnd, out r)) return false;
            var mon = Screen.FromHandle(hwnd).Bounds;
            return r.Left <= mon.Left && r.Top <= mon.Top && r.Right >= mon.Right && r.Bottom >= mon.Bottom;
        }

        /// <summary>
        /// Four times a second: is the foreground app a game that is presenting frames? PresentMon
        /// only runs while there is (or recently was) one, so it costs nothing on the desktop.
        /// </summary>
        void OverlayTick()
        {
            var cfg = Json.D(Store.Global["fps"]);
            if (quitting) return;
            if (!Json.Bool(cfg, "enabled", true))
            {
                if (overlay != null) overlay.Disappear();
                if (Fps.Running) Fps.Stop();
                return;
            }
            var fg = Native.GetForegroundWindow();
            int pid;
            Native.GetWindowThreadProcessId(fg, out pid);
            if (pid == Process.GetCurrentProcess().Id) return;
            bool all = Json.Str(cfg, "scope", "fullscreen") == "all";
            bool candidate = fg != IntPtr.Zero && (all || IsFullscreen(fg));
            if (candidate) { lastGame = DateTime.UtcNow; if (!Fps.Running) Fps.Start(); }
            else if (Fps.Running && DateTime.UtcNow - lastGame > TimeSpan.FromSeconds(30)) Fps.Stop();

            var info = candidate && Fps.Running ? Fps.For(pid) : null;
            if (Fps.Running && DateTime.UtcNow - lastFpsLog > TimeSpan.FromSeconds(20))
            {
                lastFpsLog = DateTime.UtcNow;
                Log.Write("fps diag: fg pid " + pid + " fullscreen=" + IsFullscreen(fg) + " | " + Fps.Summary());
            }
            if (info != null)
            {
                if (DateTime.UtcNow - overlaySeen > TimeSpan.FromSeconds(10)) Log.Write("fps overlay: " + Json.Str(info, "app") + " pid " + pid + " " + Json.Num(info, "fps") + " fps");
                overlaySeen = DateTime.UtcNow;
                if (overlay == null || overlay.IsDisposed) overlay = new OverlayWindow(this);
                var content = Json.Str(cfg, "content", "sensors");
                overlay.Place(Screen.FromHandle(fg).Bounds, Json.Str(cfg, "corner", "tl"), content, Json.Num(cfg, "zoom", 1));
                overlay.Appear();
                info["content"] = content;
                info["type"] = "fps";
                if (lastTick != null) { info["cpu"] = lastTick["cpu"]; info["gpu"] = lastTick["gpu"]; }
                overlay.Post(info);
            }
            else if (overlay != null && overlay.Visible && DateTime.UtcNow - overlaySeen > TimeSpan.FromSeconds(1.2)) overlay.Disappear();
        }

        void UpdateFsTimer()
        {
            if (Json.Bool(Store.Global, "hideFullscreen")) fsTimer.Start();
            else { fsTimer.Stop(); foreach (var w in widgets) w.SetFullscreenHidden(false); }
        }

        void FullscreenTick()
        {
            int state;
            bool fs = Native.SHQueryUserNotificationState(out state) == 0 && (state == 2 || state == 3 || state == 4);
            foreach (var w in widgets) w.SetFullscreenHidden(fs);
        }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            Post(() =>
            {
                if (e.Category == UserPreferenceCategory.Desktop || e.Category == UserPreferenceCategory.General) Wall.Refresh();
                lastTrayKey = ""; UpdateTray(true);
            });
        }

        void OnDisplayChanged(object sender, EventArgs e)
        {
            Post(() =>
            {
                Wall.Refresh();
                foreach (var w in widgets)
                {
                    var c = w.CardBounds;
                    if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(c))) w.ResetPosition();
                    else w.SendWall();
                }
            });
        }

        public void Quit()
        {
            if (quitting) return;
            quitting = true;
            try { Store.SaveNow(); } catch { }
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            fsTimer.Stop(); wallTimer.Stop();
            if (Perf != null) Perf.Dispose();
            Anom.Save();
            if (tray != null) { tray.Visible = false; tray.Dispose(); }
            FreeTrayIcon();
            hotkeys.Dispose();
            overlayTimer.Stop();
            if (overlay != null) { try { overlay.Close(); } catch { } }
            Fps.Dispose(); Llm.Dispose(); NetQ.Dispose();
            foreach (var w in Windows().ToList()) { try { w.Close(); } catch { } }
            Sensors.Dispose();
            ExitThread();
        }
    }

    /// <summary>Message-only window that owns the global hotkeys.</summary>
    sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        readonly Dictionary<int, Action> actions = new Dictionary<int, Action>();

        public HotkeyWindow() { CreateHandle(new CreateParams { Parent = Native.HWND_MESSAGE }); }

        public bool Register(int id, string spec, Action action)
        {
            if (actions.ContainsKey(id)) { Native.UnregisterHotKey(Handle, id); actions.Remove(id); }
            if (string.IsNullOrEmpty(spec) || spec == "none") return true;
            uint mods = 0x4000; // MOD_NOREPEAT
            uint vk = 0;
            foreach (var raw in spec.Split('+'))
            {
                var t = raw.Trim().ToLowerInvariant();
                if (t == "ctrl") mods |= 2;
                else if (t == "alt") mods |= 1;
                else if (t == "shift") mods |= 4;
                else if (t == "win") mods |= 8;
                else if (t == "space") vk = 0x20;
                else if (t.Length > 1 && t[0] == 'f' && int.TryParse(t.Substring(1), out int fn) && fn >= 1 && fn <= 24) vk = (uint)(0x6F + fn);
                else if (t.Length == 1) vk = char.ToUpperInvariant(t[0]);
            }
            if (vk == 0) return false;
            if (!Native.RegisterHotKey(Handle, id, mods, vk)) return false;
            actions[id] = action;
            return true;
        }

        protected override void WndProc(ref Message m)
        {
            Action a;
            if (m.Msg == Native.WM_HOTKEY && actions.TryGetValue(m.WParam.ToInt32(), out a)) { a(); return; }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            foreach (var id in actions.Keys.ToList()) Native.UnregisterHotKey(Handle, id);
            DestroyHandle();
        }
    }
}