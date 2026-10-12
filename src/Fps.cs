using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Pulse
{
    /// <summary>
    /// Frame rate of any DirectX/Vulkan/OpenGL app via Intel PresentMon (ETW, no injection).
    /// PresentMon streams one CSV row per presented frame; we keep a short window per process.
    /// Needs administrator rights, which Pulse has when started through its logon task.
    /// </summary>
    sealed class FpsService : IDisposable
    {
        sealed class Track
        {
            public readonly Queue<double[]> Frames = new Queue<double[]>(); // {arrival ms, frame time ms}
            public string App;
        }

        readonly object gate = new object();
        readonly Dictionary<int, Track> tracks = new Dictionary<int, Track>();
        readonly Stopwatch clock = Stopwatch.StartNew();
        Process proc;
        public string Error { get; private set; }
        public bool Running { get { var p = proc; return p != null && !p.HasExited; } }

        static string Exe { get { return Path.Combine(Paths.AppDir, "tools", "PresentMon.exe"); } }

        public void Start()
        {
            if (Running) return;
            if (!File.Exists(Exe)) { Error = "missing"; return; }
            if (!Elevation.IsElevated()) { Error = "elevation"; return; }
            try
            {
                var args = "--output_stdout --no_console_stats --v2_metrics --session_name PulseFPS --stop_existing_session " +
                           "--no_track_gpu --no_track_display --no_track_input --exclude dwm.exe --exclude Pulse.exe --exclude msedgewebview2.exe";
                var psi = new ProcessStartInfo(Exe, args)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
                proc = Process.Start(psi);
                try { proc.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                var p = proc;
                p.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Log.Write("presentmon: " + e.Data); };
                p.BeginErrorReadLine();
                new Thread(() => Read(p)) { IsBackground = true, Name = "Pulse fps" }.Start();
                Error = null;
                Log.Write("fps: PresentMon started");
            }
            catch (Exception ex) { Error = ex.Message; Log.Error("fps start", ex); }
        }

        public void Stop()
        {
            var p = proc;
            proc = null;
            if (p == null) return;
            try { if (!p.HasExited) p.Kill(); } catch { }
            try { p.Dispose(); } catch { }
            // Make sure the ETW session does not outlive us.
            try
            {
                using (var t = Process.Start(new ProcessStartInfo(Exe, "--terminate_existing_session --session_name PulseFPS") { UseShellExecute = false, CreateNoWindow = true }))
                    t.WaitForExit(3000);
            }
            catch { }
            lock (gate) tracks.Clear();
            Log.Write("fps: PresentMon stopped");
        }

        void Read(Process p)
        {
            int iApp = -1, iPid = -1, iFt = -1;
            try
            {
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    var cols = line.Split(',');
                    if (iPid < 0)
                    {
                        // Header: v2 uses FrameTime; v1 used MsBetweenPresents.
                        iApp = Array.IndexOf(cols, "Application");
                        iPid = Array.IndexOf(cols, "ProcessID");
                        iFt = Array.IndexOf(cols, "FrameTime");
                        if (iFt < 0) iFt = Array.IndexOf(cols, "MsBetweenPresents");
                        if (iPid >= 0) Log.Write("fps: columns app=" + iApp + " pid=" + iPid + " ft=" + iFt);
                        if (iFt < 0) iPid = -1;
                        continue;
                    }
                    if (cols.Length <= Math.Max(iPid, iFt)) continue;
                    int pid;
                    double ft;
                    if (!int.TryParse(cols[iPid], out pid)) continue;
                    if (!double.TryParse(cols[iFt], NumberStyles.Float, CultureInfo.InvariantCulture, out ft) || ft <= 0 || ft > 2000) continue;
                    double now = clock.Elapsed.TotalMilliseconds;
                    lock (gate)
                    {
                        Track t;
                        if (!tracks.TryGetValue(pid, out t)) tracks[pid] = t = new Track { App = iApp >= 0 ? cols[iApp] : null };
                        t.Frames.Enqueue(new[] { now, ft });
                        while (t.Frames.Count > 0 && now - t.Frames.Peek()[0] > 12000) t.Frames.Dequeue();
                    }
                }
            }
            catch (Exception ex) { Log.Write("fps read: " + ex.Message); }
        }

        /// <summary>Frame stats for a process, or null when it has not presented recently.</summary>
        public Dictionary<string, object> For(int pid)
        {
            lock (gate)
            {
                Track t;
                double now = clock.Elapsed.TotalMilliseconds;
                if (!tracks.TryGetValue(pid, out t) || t.Frames.Count < 3)
                {
                    // Chromium/Electron apps present from a separate GPU process with the same exe.
                    string exe = null;
                    try { using (var p = Process.GetProcessById(pid)) exe = p.ProcessName + ".exe"; } catch { }
                    t = exe == null ? null : tracks.Values.Where(x => string.Equals(x.App, exe, StringComparison.OrdinalIgnoreCase) && x.Frames.Count >= 3)
                                                    .OrderByDescending(x => x.Frames.Count(fr => now - fr[0] <= 1000)).FirstOrDefault();
                    if (t == null) return null;
                }
                var all = t.Frames.ToArray();
                if (now - all[all.Length - 1][0] > 1500) return null;
                var lastSec = all.Where(f => now - f[0] <= 1000).Select(f => f[1]).ToList();
                if (lastSec.Count == 0) return null;
                double avgFt = lastSec.Average();
                var sorted = all.Select(f => f[1]).OrderByDescending(x => x).ToList();
                double p99 = sorted[Math.Min(sorted.Count - 1, Math.Max(0, (int)(sorted.Count * 0.01)))];
                return new Dictionary<string, object>
                {
                    ["app"] = t.App, ["fps"] = Math.Round(1000.0 / avgFt), ["low"] = Math.Round(1000.0 / p99),
                    ["ft"] = Math.Round(avgFt, 1),
                    ["series"] = all.Skip(Math.Max(0, all.Length - 90)).Select(f => Math.Round(f[1], 1)).ToArray(),
                };
            }
        }

        /// <summary>Diagnostics: which processes presented frames in the last second.</summary>
        public string Summary()
        {
            lock (gate)
            {
                double now = clock.Elapsed.TotalMilliseconds;
                return string.Join(", ", tracks.Where(kv => kv.Value.Frames.Count > 0 && now - kv.Value.Frames.Last()[0] < 1500)
                    .Select(kv => kv.Value.App + "#" + kv.Key + " " + kv.Value.Frames.Count(fr => now - fr[0] <= 1000) + "f/s"));
            }
        }

        public void Dispose() { Stop(); }
    }
}
