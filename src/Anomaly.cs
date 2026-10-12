using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Pulse
{
    /// <summary>
    /// Learns what is normal for this computer and reports what is not: a chip running hotter than
    /// usual at the same power, a fan that stalls or slows down, load while nobody uses the PC, a
    /// process whose memory only ever grows, a supply rail drifting away from its norm, a connection
    /// much worse than usual, a laptop draining fast while idle.
    /// Norms are per-situation running statistics (temperature per power band, fan speed per duty
    /// band…) that keep adapting slowly; they live in baseline.json, the journal in events.json.
    /// </summary>
    sealed class AnomalyService
    {
        // ---------------------------------------------------------------- model

        public sealed class Anomaly
        {
            public string Key, Kind, Mod, Sev;       // sev: warn | info
            public long T0, T1;                      // unix ms; T1 = 0 while active
            public Dictionary<string, object> P = new Dictionary<string, object>();
            public bool Active { get { return T1 == 0; } }
            public Dictionary<string, object> Wire()
            {
                return new Dictionary<string, object> { ["key"] = Key, ["kind"] = Kind, ["mod"] = Mod, ["sev"] = Sev, ["t0"] = T0, ["t1"] = T1 == 0 ? null : (object)T1, ["p"] = P };
            }
        }

        /// <summary>Running mean / variance; after <c>cap</c> samples it becomes an exponential average that keeps adapting.</summary>
        sealed class Stat
        {
            public double N, Mean, Var;
            public void Add(double x, double cap)
            {
                N = Math.Min(N + 1, cap);
                double a = 1 / N, d = x - Mean;
                Mean += a * d;
                Var = (1 - a) * (Var + a * d * d);
            }
            public double Sd { get { return Math.Sqrt(Math.Max(0, Var)); } }
        }

        /// <summary>Counts how long a condition has held; forgets it after it has been false for a while.</summary>
        sealed class Hold
        {
            public double On, Off;
            public bool Step(bool cond, double dt, double need, double release)
            {
                if (cond) { On += dt; Off = 0; } else { Off += dt; if (Off >= release) On = 0; }
                return On >= need;
            }
        }

        // ---------------------------------------------------------------- state

        readonly string dir;
        readonly Dictionary<string, Stat> norms = new Dictionary<string, Stat>();
        readonly Dictionary<string, Hold> holds = new Dictionary<string, Hold>();
        readonly Dictionary<string, Anomaly> active = new Dictionary<string, Anomaly>();
        readonly List<Anomaly> journal = new List<Anomaly>();
        readonly Dictionary<string, double> ema = new Dictionary<string, double>();
        readonly Dictionary<string, List<double[]>> memTrail = new Dictionary<string, List<double[]>>();
        double learnedSec;
        long lastT, lastSave, lastMemSample;
        int bucketCpu = -1, bucketGpu = -1;
        double bucketCpuSec, bucketGpuSec;
        Dictionary<string, object> lastProcs;
        List<KeyValuePair<string, double>> gpuTop = new List<KeyValuePair<string, double>>();
        Task gpuTask;
        long lastGpuScan;

        /// <summary>Seconds since the last keyboard / mouse input, and whether a fullscreen app or presentation is in front.</summary>
        public Func<double> IdleSeconds = () => 0;
        public Func<bool> Fullscreen = () => false;
        /// <summary>Settings: enabled, sensitivity (low | normal | high), ignore (keys).</summary>
        public Func<Dictionary<string, object>> Config = () => new Dictionary<string, object>();
        /// <summary>New anomaly worth a notification (warn severity, not ignored).</summary>
        public event Action<Anomaly> Raised;
        /// <summary>The set of active anomalies changed.</summary>
        public event Action Changed;

        public const double LearnSeconds = 2 * 3600;

        public AnomalyService(string dir)
        {
            this.dir = dir;
            Load();
        }

        public IList<Anomaly> Active { get { return active.Values.OrderBy(a => a.T0).ToList(); } }
        public IList<Anomaly> Journal { get { lock (journal) return journal.ToList(); } }
        public double Learned { get { return Math.Min(1, learnedSec / LearnSeconds); } }
        public double LearnedSeconds { get { return learnedSec; } }

        double Sens { get { var s = Json.Str(Config(), "sensitivity", "normal"); return s == "low" ? 1.5 : s == "high" ? 0.7 : 1; } }
        bool Ignored(string key)
        {
            var list = Json.L(Json.Get(Config(), "ignore"));
            return list != null && list.Any(x => { var k = Convert.ToString(x); return key == k || key.StartsWith(k + ":"); });
        }

        /// <summary>Each kind of check can be switched off on its own (settings: anomaly.detectors.&lt;name&gt;).</summary>
        bool On(string detector) { return Json.Bool(Json.D(Json.Get(Config(), "detectors")), detector, true); }

        Stat Norm(string key) { Stat s; if (!norms.TryGetValue(key, out s)) norms[key] = s = new Stat(); return s; }
        Hold H(string key) { Hold h; if (!holds.TryGetValue(key, out h)) holds[key] = h = new Hold(); return h; }
        double Ema(string key, double x, double dt, double tau)
        {
            double v;
            if (!ema.TryGetValue(key, out v)) v = x;
            else v += (x - v) * (1 - Math.Exp(-dt / tau));
            ema[key] = v;
            return v;
        }

        // ---------------------------------------------------------------- per tick

        public void Feed(Dictionary<string, object> snap)
        {
            if (snap == null) return;
            long t = (long)Json.Num(snap, "t");
            double dt = lastT == 0 ? 1 : MathX.Clamp((t - lastT) / 1000.0, 0.1, 15);
            lastT = t;
            var procs = Json.D(Json.Get(snap, "procs"));
            if (procs != null && Json.Bool(procs, "ready")) lastProcs = procs;
            if (!Json.Bool(Config(), "enabled", true)) { if (active.Count > 0) { foreach (var k in active.Keys.ToList()) End(k, t); Changed?.Invoke(); } return; }

            learnedSec += dt;
            bool changed = false;
            var seen = new HashSet<string>();
            try
            {
                if (On("hot")) { Thermal(snap, "cpu", dt, t, seen, ref bucketCpu, ref bucketCpuSec, 10, ref changed); Thermal(snap, "gpu", dt, t, seen, ref bucketGpu, ref bucketGpuSec, 15, ref changed); }
                if (On("fans")) Fans(snap, dt, t, seen, ref changed);
                if (On("idle")) IdleLoad(snap, dt, t, seen, ref changed);
                if (On("leak")) Memory(t, seen, ref changed);
                if (On("rails")) Rails(snap, dt, t, seen, ref changed);
                if (On("net")) Network(snap, dt, t, seen, ref changed);
                if (On("battery")) Battery(snap, dt, t, seen, ref changed);
            }
            catch (Exception ex) { Log.Error("anomaly", ex); }

            foreach (var k in pinned) seen.Add(k);
            foreach (var k in active.Keys.ToList()) if (!seen.Contains(k)) { End(k, t); changed = true; }
            if (changed) Changed?.Invoke();
            if (t - lastSave > 10 * 60 * 1000) { lastSave = t; Save(); }
        }

        /// <summary>Keeps an anomaly alive this tick (raising it when new). Returns true when it is new.</summary>
        bool Keep(HashSet<string> seen, string key, string kind, string mod, string sev, long t, Dictionary<string, object> p, ref bool changed)
        {
            if (Ignored(key)) return false;
            seen.Add(key);
            Anomaly a;
            if (active.TryGetValue(key, out a))
            {
                foreach (var kv in p) a.P[kv.Key] = kv.Value; // latest numbers (peaks are kept by the detectors)
                return false;
            }
            a = new Anomaly { Key = key, Kind = kind, Mod = mod, Sev = sev, T0 = t, P = p };
            active[key] = a;
            lock (journal)
            {
                journal.Add(a);
                long cutoff = t - 14L * 86400000;
                journal.RemoveAll(x => !x.Active && x.T1 < cutoff);
                if (journal.Count > 300) journal.RemoveRange(0, journal.Count - 300);
            }
            changed = true;
            Log.Write("anomaly: " + key + " " + Json.Write(p));
            if (sev == "warn") Raised?.Invoke(a);
            SaveJournalSoon();
            return true;
        }

        void End(string key, long t)
        {
            Anomaly a;
            if (!active.TryGetValue(key, out a)) return;
            a.T1 = t;
            active.Remove(key);
            SaveJournalSoon();
        }

        bool IsActive(string key) { return active.ContainsKey(key); }

        // ---------------------------------------------------------------- detectors

        /// <summary>
        /// Temperature against what this chip usually reaches at the same power (or load). Learns
        /// only in steady state (power not ramping) and never while running hot.
        /// </summary>
        void Thermal(Dictionary<string, object> snap, string id, double dt, long t, HashSet<string> seen, ref int bucket, ref double bucketSec, double band, ref bool changed)
        {
            var d = Json.D(Json.Get(snap, id));
            if (d == null || Json.Get(d, "temp") == null) return;
            double temp = Ema(id + "T", Json.Num(d, "temp"), dt, 6);
            bool byPower = Json.Get(d, "power") != null && Json.Num(d, "power") > 0;
            double raw = byPower ? Json.Num(d, "power") : Json.Num(d, "load"), w = byPower ? band : 10;
            double x = Ema(id + (byPower ? "P" : "L"), raw, dt, 15), xFast = Ema(id + (byPower ? "Pf" : "Lf"), raw, dt, 3);
            // the band only changes once the power is clearly inside another one (no flapping at the edges)
            int b = (int)Math.Floor(x / w);
            if (bucket < 0 || x < (bucket - 0.25) * w || x > (bucket + 1.25) * w) bucket = b;
            var key = "hot:" + id;
            var n = Norm(id + "T:" + (byPower ? "p" : "l") + bucket);
            double margin = Math.Max(8 * Sens, 3 * n.Sd);
            double excess = temp - n.Mean;
            bool hot = n.N >= 180 && excess > margin;
            if (H(key).Step(hot, dt, 90 * Sens, IsActive(key) ? 45 : 20))
            {
                var p = new Dictionary<string, object> { ["temp"] = Math.Round(temp), ["norm"] = Math.Round(n.Mean), ["excess"] = Math.Round(excess), ["by"] = byPower ? "power" : "load", ["x"] = Math.Round(x) };
                Anomaly a;
                if (active.TryGetValue(key, out a)) p["peak"] = Math.Max(Json.Num(a.P, "peak"), Math.Round(temp)); else p["peak"] = Math.Round(temp);
                Keep(seen, key, "hot", id, "warn", t, p, ref changed);
            }
            // learn only in steady state (temperature lags power) and never while running hot
            else if (Math.Abs(xFast - x) < w * 0.5 && !hot && (n.N < 30 || excess < margin * 0.5)) n.Add(temp, 3000);
        }

        /// <summary>A fan that stops while it should spin, or turns much slower than it usually does in the same situation.</summary>
        void Fans(Dictionary<string, object> snap, double dt, long t, HashSet<string> seen, ref bool changed)
        {
            var list = Json.L(Json.Get(Json.D(Json.Get(snap, "fans")), "list"));
            if (list == null) return;
            double cpuT = Json.Num(Json.D(Json.Get(snap, "cpu")), "temp", 0);
            foreach (var o in list.Select(Json.D).Where(f => f != null))
            {
                string id = Json.Str(o, "id"), name = Json.Str(o, "n");
                if (id == null || Json.Get(o, "rpm") == null) continue;
                double rpm = Json.Num(o, "rpm");
                bool hasPct = Json.Get(o, "pct") != null;
                double pct = Json.Num(o, "pct");
                bool gpu = Json.Str(o, "src") == "gpu";
                var n = Norm("fan:" + id + ":" + (hasPct ? "d" + (int)(pct / 10) : "t" + (int)(cpuT / 5)));
                bool learned = n.N >= 60;
                // stalled: no rotation although it normally spins here, or a board header driving it hard
                bool stall = rpm < 80 && ((learned && n.Mean >= 400) || (!gpu && hasPct && pct >= 40));
                bool slow = !stall && learned && n.N >= 120 && n.Mean >= 500 && rpm < n.Mean * (1 - 0.45 / Sens);
                var keyStop = "fanStop:" + id; var keySlow = "fanSlow:" + id;
                var p = new Dictionary<string, object> { ["fan"] = name, ["id"] = id, ["rpm"] = Math.Round(rpm), ["norm"] = Math.Round(n.Mean), ["pct"] = hasPct ? (object)Math.Round(pct) : null };
                if (H(keyStop).Step(stall, dt, 15, 10)) Keep(seen, keyStop, "fanStop", "fans", "warn", t, p, ref changed);
                else if (H(keySlow).Step(slow, dt, 60 * Sens, 30)) Keep(seen, keySlow, "fanSlow", "fans", "info", t, p, ref changed);
                else if (!stall && !slow && rpm >= 80) n.Add(rpm, 2000);
            }
        }

        static readonly HashSet<string> Maintenance = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "MsMpEng.exe", "MpDefenderCoreService.exe", "NisSrv.exe", "MsSense.exe", "TiWorker.exe", "TrustedInstaller.exe", "MoUsoCoreWorker.exe",
            "usocoreworker.exe", "SearchIndexer.exe", "SearchProtocolHost.exe", "SearchFilterHost.exe", "CompatTelRunner.exe", "mscorsvw.exe",
            "ngen.exe", "ngentask.exe", "defrag.exe", "dfrgui.exe", "sppsvc.exe", "WmiPrvSE.exe", "msiexec.exe", "OneDrive.exe", "DismHost.exe",
            "wuauclt.exe", "UsoClient.exe", "MRT.exe", "SgrmBroker.exe", "taskhostw.exe", "svchost.exe", "System", "Registry", "Memory Compression",
        };
        static readonly HashSet<string> VmHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "vmwp.exe", "vmmem", "vmmemWSL", "vmmem.exe", "vmmemWSL.exe" };

        /// <summary>
        /// Load while nobody is at the computer (no input for a few minutes, nothing fullscreen):
        /// names the process. Windows upkeep is noted quietly; the local AI model serving requests is expected.
        /// </summary>
        void IdleLoad(Dictionary<string, object> snap, double dt, long t, HashSet<string> seen, ref bool changed)
        {
            double idle = IdleSeconds();
            bool away = idle >= 180 * Sens && !Fullscreen();
            double cpu = Ema("idleCpu", Json.Num(Json.D(Json.Get(snap, "cpu")), "load"), dt, 60);
            double gpu = Ema("idleGpu", Json.Num(Json.D(Json.Get(snap, "gpu")), "load"), dt, 60);
            double limit = 25 * Sens;
            var llm = Json.D(Json.Get(snap, "llm"));
            bool llmBusy = llm != null && (Json.Num(llm, "busy") > 0 || Json.Num(llm, "tps") > 0);

            // CPU: the heaviest process from the slow process scan
            if (H("idle:cpu").Step(away && cpu >= limit, dt, 180 * Sens, 60))
            {
                var top = Json.L(Json.Get(lastProcs, "cpu"))?.Select(Json.D).FirstOrDefault(x => x != null && Json.Num(x, "v") >= 3);
                string exe = top != null ? Json.Str(top, "n") : null;
                if (exe != null && !(VmHosts.Contains(exe) && llmBusy))
                {
                    // Windows upkeep, and the WSL / Docker VM (its load is the containers the user runs), are noted quietly
                    bool upkeep = Maintenance.Contains(exe) || VmHosts.Contains(exe);
                    Keep(seen, "idle:cpu:" + exe, "idleCpu", "cpu", upkeep ? "info" : "warn", t, new Dictionary<string, object>
                    {
                        ["exe"] = exe, ["desc"] = Json.Str(top, "d"), ["proc"] = Math.Round(Json.Num(top, "v")), ["load"] = Math.Round(cpu), ["upkeep"] = upkeep,
                        ["idle"] = Math.Round(idle / 60),
                    }, ref changed);
                }
            }
            // GPU: per-process engine utilisation, scanned only while it looks suspicious
            bool gpuSusp = away && gpu >= limit;
            if (gpuSusp && t - lastGpuScan > 30000 && (gpuTask == null || gpuTask.IsCompleted))
            {
                lastGpuScan = t;
                gpuTask = Task.Run(() => { try { gpuTop = GpuEngineTop(); } catch (Exception ex) { Log.Write("gpu engines: " + ex.Message); } });
            }
            if (H("idle:gpu").Step(gpuSusp, dt, 180 * Sens, 60) && gpuTop.Count > 0)
            {
                var top = gpuTop[0];
                if (!(VmHosts.Contains(top.Key) && llmBusy) && top.Value >= 5)
                    Keep(seen, "idle:gpu:" + top.Key, "idleGpu", "gpu", Maintenance.Contains(top.Key) || VmHosts.Contains(top.Key) ? "info" : "warn", t, new Dictionary<string, object>
                    {
                        ["exe"] = top.Key, ["proc"] = Math.Round(top.Value), ["load"] = Math.Round(gpu), ["idle"] = Math.Round(idle / 60),
                    }, ref changed);
            }
        }

        static Dictionary<string, CounterSample> gpuPrev = new Dictionary<string, CounterSample>();

        /// <summary>Busiest processes on the GPU (highest engine utilisation per process), from the "GPU Engine" counters.</summary>
        static List<KeyValuePair<string, double>> GpuEngineTop()
        {
            var data = new PerformanceCounterCategory("GPU Engine").ReadCategory()["Utilization Percentage"];
            var next = new Dictionary<string, CounterSample>();
            var byPid = new Dictionary<int, double>();
            foreach (InstanceData d in data.Values)
            {
                next[d.InstanceName] = d.Sample;
                CounterSample prev;
                if (!gpuPrev.TryGetValue(d.InstanceName, out prev)) continue;
                var m = Regex.Match(d.InstanceName, @"pid_(\d+)_");
                if (!m.Success) continue;
                int pid = int.Parse(m.Groups[1].Value);
                double v = CounterSample.Calculate(prev, d.Sample);
                double cur;
                byPid.TryGetValue(pid, out cur);
                byPid[pid] = Math.Max(cur, v);
            }
            gpuPrev = next;
            var res = new List<KeyValuePair<string, double>>();
            foreach (var kv in byPid.OrderByDescending(x => x.Value).Take(3))
            {
                string name;
                try { using (var p = Process.GetProcessById(kv.Key)) name = p.ProcessName + ".exe"; } catch { continue; }
                res.Add(new KeyValuePair<string, double>(name, kv.Value));
            }
            return res;
        }

        static readonly HashSet<string> NoLeak = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "vmmem", "vmmemWSL", "vmwp.exe", "Memory Compression", "System", "Registry" };

        /// <summary>A process whose private memory keeps climbing in a straight line for most of an hour.</summary>
        void Memory(long t, HashSet<string> seen, ref bool changed)
        {
            if (lastProcs == null || t - lastMemSample < 60000) { KeepAlive("leak:", seen); return; }
            lastMemSample = t;
            double now = t / 3600000.0; // hours
            var mem = Json.L(Json.Get(lastProcs, "mem"));
            if (mem != null)
                foreach (var o in mem.Select(Json.D).Where(x => x != null))
                {
                    string exe = Json.Str(o, "n");
                    if (exe == null || NoLeak.Contains(exe)) continue;
                    List<double[]> trail;
                    if (!memTrail.TryGetValue(exe, out trail)) memTrail[exe] = trail = new List<double[]>();
                    trail.Add(new[] { now, Json.Num(o, "v") });
                }
            foreach (var kv in memTrail.ToList())
            {
                var trail = kv.Value;
                trail.RemoveAll(p => now - p[0] > 1.5);
                if (trail.Count == 0) { memTrail.Remove(kv.Key); continue; }
                string key = "leak:" + kv.Key;
                double span = trail[trail.Count - 1][0] - trail[0][0];
                bool was = IsActive(key);
                if (trail.Count < 25 || span < 0.66 * Sens) { if (was) seen.Add(key); continue; }
                double slope, r2;
                Fit(trail, out slope, out r2);
                int ups = 0;
                for (int i = 1; i < trail.Count; i++) if (trail[i][1] >= trail[i - 1][1] - 0.01) ups++;
                double rise = trail[trail.Count - 1][1] - trail.Min(p => p[1]), cur = trail[trail.Count - 1][1];
                // the last 20 minutes decide whether it is still growing: a process that grew an hour ago
                // and has since levelled off is not flagged, and a flagged one is released once it stops
                var recent = trail.Where(p => now - p[0] <= 0.33).ToList();
                double rs = 0, rr;
                if (recent.Count >= 8) Fit(recent, out rs, out rr);
                bool growing = recent.Count < 8 ? was : rs >= (was ? 0.1 : 0.25 * Sens);
                bool leak = growing && (was || (slope >= 0.5 * Sens && r2 >= 0.8 && ups >= 0.8 * (trail.Count - 1) && rise >= 0.4 && cur >= 0.8));
                if (leak)
                    Keep(seen, key, "leak", "ram", "warn", t, new Dictionary<string, object>
                    {
                        ["exe"] = kv.Key, ["gb"] = Math.Round(cur, 2), ["rate"] = Math.Round(slope, 2), ["rise"] = Math.Round(rise, 2), ["min"] = Math.Round(span * 60),
                    }, ref changed);
            }
        }

        void KeepAlive(string prefix, HashSet<string> seen) { foreach (var k in active.Keys) if (k.StartsWith(prefix)) seen.Add(k); }

        static void Fit(List<double[]> pts, out double slope, out double r2)
        {
            int n = pts.Count;
            double mx = pts.Average(p => p[0]), my = pts.Average(p => p[1]);
            double sxy = 0, sxx = 0, syy = 0;
            foreach (var p in pts) { double dx = p[0] - mx, dy = p[1] - my; sxy += dx * dy; sxx += dx * dx; syy += dy * dy; }
            slope = sxx > 0 ? sxy / sxx : 0;
            r2 = sxx > 0 && syy > 0 ? sxy * sxy / (sxx * syy) : 0;
        }

        /// <summary>A supply rail outside the ATX ±5 % window and away from its own usual reading (so a sensor's fixed offset is not an alarm).</summary>
        void Rails(Dictionary<string, object> snap, double dt, long t, HashSet<string> seen, ref bool changed)
        {
            var rails = Json.L(Json.Get(Json.D(Json.Get(snap, "power")), "rails"));
            if (rails == null) return;
            foreach (var o in rails.Select(Json.D).Where(x => x != null))
            {
                string name = Json.Str(o, "n");
                if (name == null || Json.Get(o, "dev") == null) continue;
                double dev = Json.Num(o, "dev");
                var n = Norm("rail:" + name);
                bool bad = Math.Abs(dev) > 5 && n.N >= 300 && Math.Abs(dev - n.Mean) > 2 * Sens;
                var key = "rail:" + name;
                if (H(key).Step(bad, dt, 20, 20))
                    Keep(seen, key, "rail", "power", "warn", t, new Dictionary<string, object>
                    {
                        ["rail"] = name, ["v"] = Json.Num(o, "v"), ["dev"] = Math.Round(dev, 1), ["norm"] = Math.Round(n.Mean, 1),
                    }, ref changed);
                else if (!bad) n.Add(dev, 5000);
            }
        }

        /// <summary>Packet loss or latency to the provider far beyond the usual for this line.</summary>
        void Network(Dictionary<string, object> snap, double dt, long t, HashSet<string> seen, ref bool changed)
        {
            var q = Json.D(Json.Get(Json.D(Json.Get(snap, "net")), "q"));
            if (q == null) return;
            if (Json.Bool(q, "offline"))
            {
                if (H("net:off").Step(true, dt, 30, 10)) Keep(seen, "net:offline", "offline", "net", "info", t, new Dictionary<string, object>(), ref changed);
                return;
            }
            H("net:off").Step(false, dt, 30, 10);
            var target = Json.D(Json.Get(q, Json.Str(q, "target") == "isp" ? "isp" : "inet"));
            if (target == null || Json.Get(target, "rtt") == null) return;
            double rtt = Json.Num(target, "rtt"), loss = Json.Num(target, "loss");
            var n = Norm("net:rtt");
            bool lossy = loss >= 5 * Sens;
            bool slow = n.N >= 300 && rtt > Math.Max(n.Mean * 3, n.Mean + 40) * Sens;
            if (H("net:q").Step(lossy || slow, dt, 60, 30))
                Keep(seen, "net:quality", "netBad", "net", "info", t, new Dictionary<string, object>
                {
                    ["rtt"] = Math.Round(rtt), ["loss"] = Math.Round(loss, 1), ["norm"] = Math.Round(n.Mean), ["who"] = Json.Str(q, "target"),
                }, ref changed);
            else if (!lossy && !slow && loss < 1) n.Add(rtt, 5000);
        }

        /// <summary>A laptop draining much faster than it usually does while nobody uses it.</summary>
        void Battery(Dictionary<string, object> snap, double dt, long t, HashSet<string> seen, ref bool changed)
        {
            var b = Json.D(Json.Get(snap, "battery"));
            if (b == null || Json.Str(b, "state") != "discharging" || Json.Get(b, "rate") == null) return;
            double rate = Ema("batRate", Json.Num(b, "rate"), dt, 30);
            bool idle = IdleSeconds() >= 180;
            if (!idle) return;
            var n = Norm("bat:idle");
            bool fast = n.N >= 120 && rate >= Math.Max(8, n.Mean * 2 * Sens);
            if (H("bat:drain").Step(fast, dt, 120, 60))
            {
                var top = Json.L(Json.Get(lastProcs, "cpu"))?.Select(Json.D).FirstOrDefault(x => x != null && Json.Num(x, "v") >= 2);
                Keep(seen, "bat:drain", "batDrain", "battery", "warn", t, new Dictionary<string, object>
                {
                    ["rate"] = Math.Round(rate, 1), ["norm"] = Math.Round(n.Mean, 1), ["exe"] = top != null ? Json.Str(top, "n") : null,
                }, ref changed);
            }
            else if (!fast) n.Add(rate, 2000);
        }

        // ---------------------------------------------------------------- persistence

        string BaseFile { get { return dir == null ? null : Path.Combine(dir, "baseline.json"); } }
        string JournalFile { get { return dir == null ? null : Path.Combine(dir, "events.json"); } }

        void Load()
        {
            if (dir == null) return;
            try
            {
                if (File.Exists(BaseFile))
                {
                    var d = Json.D(Json.Parse(File.ReadAllText(BaseFile)));
                    learnedSec = Json.Num(d, "learned");
                    foreach (var kv in Json.D(Json.Get(d, "norms")) ?? new Dictionary<string, object>())
                    {
                        var a = Json.L(kv.Value);
                        if (a != null && a.Count == 3) norms[kv.Key] = new Stat { N = Convert.ToDouble(a[0]), Mean = Convert.ToDouble(a[1]), Var = Convert.ToDouble(a[2]) };
                    }
                }
                if (File.Exists(JournalFile))
                    foreach (var o in Json.L(Json.Parse(File.ReadAllText(JournalFile))) ?? new List<object>())
                    {
                        var d = Json.D(o);
                        if (d == null) continue;
                        var a = new Anomaly
                        {
                            Key = Json.Str(d, "key"), Kind = Json.Str(d, "kind"), Mod = Json.Str(d, "mod"), Sev = Json.Str(d, "sev"),
                            T0 = (long)Json.Num(d, "t0"), T1 = (long)Json.Num(d, "t1"), P = Json.D(Json.Get(d, "p")) ?? new Dictionary<string, object>(),
                        };
                        if (a.T1 == 0) a.T1 = a.T0; // anything still open at shutdown counts as ended then
                        journal.Add(a);
                    }
            }
            catch (Exception ex) { Log.Error("anomaly load", ex); }
        }

        public void Save()
        {
            if (dir == null) return;
            try
            {
                var n = norms.ToDictionary(kv => kv.Key, kv => (object)new object[] { Math.Round(kv.Value.N), Math.Round(kv.Value.Mean, 3), Math.Round(kv.Value.Var, 3) });
                File.WriteAllText(BaseFile, Json.Write(new Dictionary<string, object> { ["learned"] = Math.Round(learnedSec), ["norms"] = n }));
                List<object> j;
                lock (journal) j = journal.Select(a => (object)a.Wire()).ToList();
                File.WriteAllText(JournalFile, Json.Write(j));
            }
            catch (Exception ex) { Log.Error("anomaly save", ex); }
        }

        void SaveJournalSoon() { lastSave = Math.Min(lastSave, lastT - 9 * 60 * 1000); } // written within a minute

        /// <summary>Forget everything learned (after a hardware change, a new cooler, a move…).</summary>
        public void Reset()
        {
            norms.Clear(); holds.Clear(); ema.Clear(); memTrail.Clear();
            learnedSec = 0;
            foreach (var k in active.Keys.ToList()) End(k, lastT);
            Save();
            Changed?.Invoke();
        }

        // ---------------------------------------------------------------- design aid (--dev)

        readonly HashSet<string> pinned = new HashSet<string>();

        /// <summary>Shows sample anomalies of one kind ("all" for every kind, "none" to clear) so the UI can be checked.</summary>
        public void Inject(string kind)
        {
            long t = lastT > 0 ? lastT : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (var k in pinned) End(k, t);
            pinned.Clear();
            if (kind == "none") { Changed?.Invoke(); return; }
            var samples = new[]
            {
                Tuple.Create("hot:cpu", "hot", "cpu", "warn", new Dictionary<string, object> { ["temp"] = 81, ["norm"] = 66, ["excess"] = 15, ["peak"] = 84, ["by"] = "power", ["x"] = 120 }),
                Tuple.Create("fanStop:demo", "fanStop", "fans", "warn", new Dictionary<string, object> { ["fan"] = "Pump Fan #1", ["id"] = "demo", ["rpm"] = 0, ["norm"] = 2650, ["pct"] = 60 }),
                Tuple.Create("idle:gpu:xmrig.exe", "idleGpu", "gpu", "warn", new Dictionary<string, object> { ["exe"] = "xmrig.exe", ["proc"] = 87, ["load"] = 91, ["idle"] = 14 }),
                Tuple.Create("idle:cpu:MsMpEng.exe", "idleCpu", "cpu", "info", new Dictionary<string, object> { ["exe"] = "MsMpEng.exe", ["desc"] = "Antimalware Service Executable", ["proc"] = 31, ["load"] = 34, ["upkeep"] = true, ["idle"] = 9 }),
                Tuple.Create("leak:chrome.exe", "leak", "ram", "warn", new Dictionary<string, object> { ["exe"] = "chrome.exe", ["gb"] = 6.4, ["rate"] = 1.3, ["rise"] = 1.1, ["min"] = 52 }),
                Tuple.Create("rail:+12V", "rail", "power", "warn", new Dictionary<string, object> { ["rail"] = "+12V", ["v"] = 11.31, ["dev"] = -5.8, ["norm"] = -0.4 }),
                Tuple.Create("net:quality", "netBad", "net", "info", new Dictionary<string, object> { ["rtt"] = 138, ["loss"] = 7.5, ["norm"] = 9, ["who"] = "isp" }),
                Tuple.Create("bat:drain", "batDrain", "battery", "warn", new Dictionary<string, object> { ["rate"] = 21.5, ["norm"] = 6.2, ["exe"] = "Teams.exe" }),
            };
            bool dummy = false;
            foreach (var s in samples)
            {
                if (kind != "all" && s.Item2 != kind) continue;
                pinned.Add(s.Item1);
                Keep(new HashSet<string>(), s.Item1, s.Item2, s.Item3, s.Item4, t - 4 * 60000, s.Item5, ref dummy);
            }
            Changed?.Invoke();
        }

        public void ClearJournal()
        {
            lock (journal) journal.RemoveAll(a => !a.Active);
            Save();
        }
    }
}
