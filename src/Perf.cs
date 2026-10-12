using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Pulse
{
    /// <summary>
    /// Keeps Pulse light. Picks the effective performance mode — "auto" drops to eco on battery,
    /// with battery saver on, on weak hardware or while the CPU is busy — and pauses every widget
    /// nobody can see (covered by windows, session locked), so hidden widgets cost nothing.
    /// </summary>
    sealed class PerfGovernor : IDisposable
    {
        public const string Max = "max", Balanced = "balanced", Eco = "eco";

        readonly PulseApp app;
        readonly Timer timer = new Timer { Interval = 600 };
        readonly int pid = Process.GetCurrentProcess().Id;
        public string Mode { get; private set; }
        /// <summary>Why auto chose eco: battery, saver, weak, busy; "" otherwise, "manual" for a fixed choice.</summary>
        public string Reason { get; private set; }
        public event Action Changed;

        bool locked;
        DateTime hotSince = DateTime.MaxValue, coolSince = DateTime.MaxValue;
        bool busy;

        public static readonly bool WeakHardware = Environment.ProcessorCount <= 4 || TotalRamGb() < 7.5;

        public PerfGovernor(PulseApp app)
        {
            this.app = app;
            Mode = Balanced; Reason = "";
            Mode = Pick(out string r); Reason = r;
            timer.Tick += (s, e) => Check();
            SystemEvents.SessionSwitch += OnSession;
            timer.Start();
        }

        public bool IsEco { get { return Mode == Eco; } }

        /// <summary>Called with every sensor tick: sustained high load switches auto to eco and back.</summary>
        public void FeedCpu(double load)
        {
            var now = DateTime.UtcNow;
            if (load >= 85) { coolSince = DateTime.MaxValue; if (hotSince == DateTime.MaxValue) hotSince = now; }
            else if (load < 60) { hotSince = DateTime.MaxValue; if (coolSince == DateTime.MaxValue) coolSince = now; }
            else { hotSince = DateTime.MaxValue; coolSince = DateTime.MaxValue; }
            if (!busy && now - hotSince > TimeSpan.FromSeconds(10)) busy = true;
            if (busy && now - coolSince > TimeSpan.FromSeconds(15)) busy = false;
        }

        public void Check()
        {
            string reason;
            var mode = Pick(out reason);
            if (mode != Mode || reason != Reason)
            {
                Mode = mode; Reason = reason;
                Log.Write("perf: " + mode + (reason.Length > 0 ? " (" + reason + ")" : ""));
                Changed?.Invoke();
            }
            foreach (var w in app.Widgets) w.SetPaused(locked || !w.Visible || Occluded(w));
        }

        string Pick(out string reason)
        {
            var pref = Json.Str(app.Store.Global, "perf", "auto");
            if (pref == Max || pref == Balanced || pref == Eco) { reason = "manual"; return pref; }
            Native.SYSTEM_POWER_STATUS ps;
            if (Native.GetSystemPowerStatus(out ps))
            {
                if (ps.SystemStatusFlag == 1) { reason = "saver"; return Eco; }
                bool hasBattery = ps.BatteryFlag != 128 && ps.BatteryFlag != 255;
                if (hasBattery && ps.ACLineStatus == 0) { reason = "battery"; return Eco; }
            }
            if (WeakHardware) { reason = "weak"; return Eco; }
            if (busy) { reason = "busy"; return Eco; }
            reason = "";
            return Balanced;
        }

        /// <summary>
        /// A desktop widget counts as hidden when none of nine points across its card shows the
        /// widget itself or the desktop. Topmost widgets are always treated as visible.
        /// </summary>
        bool Occluded(WidgetWindow w)
        {
            if (w.OnTop || w.IsDisposed) return false;
            var r = w.CardBounds;
            if (r.Width < 4 || r.Height < 4) return false;
            foreach (var fx in new[] { .12, .5, .88 })
                foreach (var fy in new[] { .15, .5, .85 })
                {
                    var p = new Native.POINT { X = r.Left + (int)(r.Width * fx), Y = r.Top + (int)(r.Height * fy) };
                    var h = Native.WindowFromPoint(p);
                    if (h == IntPtr.Zero) continue; // off-screen
                    var root = Native.GetAncestor(h, 2 /* GA_ROOT */);
                    if (Shows(root)) return false;
                }
            return true;
        }

        /// <summary>True when a point hitting this window still lets the widget be seen.</summary>
        bool Shows(IntPtr root)
        {
            if (root == IntPtr.Zero) return false;
            int owner;
            Native.GetWindowThreadProcessId(root, out owner);
            if (owner == pid) return true;
            var sb = new StringBuilder(64);
            Native.GetClassName(root, sb, 64);
            var cls = sb.ToString();
            if (cls == "Progman" || cls == "WorkerW") return true;
            int cloaked;
            return Native.DwmGetWindowAttribute(root, 14 /* DWMWA_CLOAKED */, out cloaked, 4) == 0 && cloaked != 0;
        }

        void OnSession(object sender, SessionSwitchEventArgs e)
        {
            bool on = e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.ConsoleDisconnect || e.Reason == SessionSwitchReason.RemoteDisconnect;
            bool off = e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect || e.Reason == SessionSwitchReason.RemoteConnect;
            if (on || off) app.Post(() => { locked = on; Check(); });
        }

        static double TotalRamGb()
        {
            var m = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(Native.MEMORYSTATUSEX)) };
            return Native.GlobalMemoryStatusEx(ref m) ? m.ullTotalPhys / 1073741824.0 : 16;
        }

        public void Dispose()
        {
            timer.Stop(); timer.Dispose();
            SystemEvents.SessionSwitch -= OnSession;
        }
    }
}
