using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Pulse
{
    /// <summary>
    /// Local LLM monitor for llama.cpp running in Docker. One long-lived "docker exec" streams the
    /// server's /models, /metrics and /slots once a second; the API key never leaves the container
    /// (the script reads it from the server's own command line).
    /// </summary>
    sealed class LlmService : IDisposable
    {
        const string Script = @"K=$(tr '\0' '\n' < /proc/1/cmdline | grep -A1 -- '--api-key' | tail -1)
P0=$(tr '\0' '\n' < /proc/1/cmdline | grep -A1 -- '--port' | tail -1); [ -z ""$P0"" ] && P0=8080
while :; do
  MJ=$(curl -s -m 2 -H ""Authorization: Bearer $K"" http://127.0.0.1:$P0/models)
  echo '@@BEGIN'
  echo '@@MODELS'; printf '%s\n' ""$MJ""
  P=$(printf '%s' ""$MJ"" | grep -oE '""value"":""loaded"",""args"":\[[^]]*\]' | grep -oE '""--port"",""[0-9]+""' | grep -oE '[0-9]+' | head -1)
  if [ -n ""$P"" ]; then
    echo '@@METRICS'; curl -s -m 2 http://127.0.0.1:$P/metrics
    echo '@@SLOTS'; curl -s -m 2 http://127.0.0.1:$P/slots; echo
  else
    echo '@@METRICS'; curl -s -m 2 -H ""Authorization: Bearer $K"" http://127.0.0.1:$P0/metrics
    echo '@@SLOTS'; curl -s -m 2 -H ""Authorization: Bearer $K"" http://127.0.0.1:$P0/slots; echo
  fi
  echo '@@END'
  sleep 1
done
";

        public volatile bool Enabled = true;
        public volatile string ContainerPref = "auto";
        readonly object gate = new object();
        Dictionary<string, object> snap;
        Process proc;
        string container;
        DateTime nextAttempt = DateTime.MinValue;
        int failures;

        // rate state
        readonly Dictionary<int, long[]> slotPrev = new Dictionary<int, long[]>(); // id → {task, decoded}
        DateTime lastFrame;
        double tpsLive, peak;
        double lastPred = -1, lastPredSec = -1, tpsLast;

        public Dictionary<string, object> Snapshot { get { lock (gate) return snap; } }
        volatile bool running;

        public void Start()
        {
            running = true;
            new Thread(() =>
            {
                while (running) { try { Pump(); } catch (Exception ex) { Log.Write("llm pump: " + ex.Message); } Thread.Sleep(2000); }
                Stop();
            }) { IsBackground = true, Name = "Pulse llm watch" }.Start();
        }

        /// <summary>Keeps the sidecar alive (or retries with back-off).</summary>
        void Pump()
        {
            if (!Enabled) { Stop(); lock (gate) snap = null; return; }
            if (proc != null && !proc.HasExited) return;
            if (DateTime.UtcNow < nextAttempt) return;
            nextAttempt = DateTime.UtcNow.AddSeconds(Math.Min(60, 5 * (1 << Math.Min(failures, 4))));
            failures++;
            try
            {
                container = FindContainer();
                if (container == null) { Offline("none"); return; }
                StartSidecar();
            }
            catch (Exception ex) { Log.Write("llm: " + ex.Message); Offline("error"); }
        }

        void Offline(string why)
        {
            lock (gate) snap = new Dictionary<string, object> { ["state"] = "offline", ["why"] = why, ["container"] = container };
        }

        static string Docker()
        {
            var p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Docker\Docker\resources\bin\docker.exe");
            return File.Exists(p) ? p : "docker";
        }

        string FindContainer()
        {
            var psi = new ProcessStartInfo(Docker(), "ps --format \"{{.Names}}\t{{.Image}}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
            string output;
            using (var p = Process.Start(psi))
            {
                output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(8000)) { try { p.Kill(); } catch { } return null; }
            }
            var rows = output.Split('\n').Select(l => l.Trim().Split('\t')).Where(a => a.Length == 2).ToList();
            if (ContainerPref != "auto") return rows.Any(r => r[0] == ContainerPref) ? ContainerPref : null;
            // A GPU build of llama.cpp server wins; then anything named like an LLM.
            var best = rows.Where(r => Regex.IsMatch(r[1], "llama\\.?cpp|llama-server", RegexOptions.IgnoreCase))
                           .OrderByDescending(r => Regex.IsMatch(r[1], "cuda|vulkan|rocm|gpu", RegexOptions.IgnoreCase) ? 2 : 0)
                           .ThenByDescending(r => Regex.IsMatch(r[0], "llm|chat", RegexOptions.IgnoreCase) ? 1 : 0)
                           .FirstOrDefault();
            return best != null ? best[0] : null;
        }

        void StartSidecar()
        {
            var psi = new ProcessStartInfo(Docker(), "exec -i " + container + " sh -s")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8,
            };
            proc = Process.Start(psi);
            proc.StandardInput.NewLine = "\n";
            proc.StandardInput.Write(Script.Replace("\r\n", "\n"));
            proc.StandardInput.Close();
            proc.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) Log.Write("llm sidecar: " + e.Data); };
            proc.BeginErrorReadLine();
            var p = proc;
            new Thread(() => ReadLoop(p)) { IsBackground = true, Name = "Pulse llm" }.Start();
            Log.Write("llm: monitoring container " + container);
        }

        void ReadLoop(Process p)
        {
            var section = "";
            var buf = new Dictionary<string, StringBuilder>();
            try
            {
                string line;
                while ((line = p.StandardOutput.ReadLine()) != null)
                {
                    if (line == "@@BEGIN") { buf.Clear(); section = ""; continue; }
                    if (line == "@@END") { Frame(buf); failures = 0; continue; }
                    if (line.StartsWith("@@")) { section = line.Substring(2); buf[section] = new StringBuilder(); continue; }
                    if (section.Length > 0) buf[section].AppendLine(line);
                }
            }
            catch (Exception ex) { Log.Write("llm read: " + ex.Message); }
            Offline("stopped");
        }

        static double Metric(string text, string name)
        {
            var m = Regex.Match(text, "^llamacpp:" + Regex.Escape(name) + @"\s+([0-9.eE+-]+)", RegexOptions.Multiline);
            double v;
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : double.NaN;
        }

        static string ArgAfter(List<object> args, string flag)
        {
            if (args == null) return null;
            int i = args.FindIndex(a => Convert.ToString(a) == flag);
            return i >= 0 && i + 1 < args.Count ? Convert.ToString(args[i + 1]) : null;
        }

        void Frame(Dictionary<string, StringBuilder> buf)
        {
            var now = DateTime.UtcNow;
            double dt = lastFrame == default(DateTime) ? 0 : (now - lastFrame).TotalSeconds;
            lastFrame = now;
            StringBuilder sb;

            // ---- models (router mode lists every preset with its state)
            var models = new List<object>();
            string loadedId = null, file = null, state = "unloaded";
            int ctx = 0;
            if (buf.TryGetValue("MODELS", out sb))
            {
                try
                {
                    var root = Json.D(Json.Parse(sb.ToString()));
                    foreach (var m in Json.L(Json.Get(root, "data")) ?? new List<object>())
                    {
                        var d = Json.D(m);
                        var st = Json.D(Json.Get(d, "status"));
                        var value = Json.Str(st, "value", "loaded");
                        var args = Json.L(Json.Get(st, "args"));
                        var id = Json.Str(d, "id");
                        var path = ArgAfter(args, "--model") ?? id;
                        models.Add(new Dictionary<string, object> { ["id"] = id, ["state"] = value, ["file"] = Path.GetFileName(path) });
                        if (value == "loaded" || (value == "loading" && loadedId == null))
                        {
                            loadedId = id; state = value; file = Path.GetFileName(path);
                            int.TryParse(ArgAfter(args, "--ctx-size") ?? "0", out ctx);
                        }
                    }
                }
                catch { }
            }

            // ---- metrics (Prometheus text)
            double pred = double.NaN, predSec = double.NaN, prompt = double.NaN, promptSec = double.NaN, processing = double.NaN, deferred = double.NaN;
            if (buf.TryGetValue("METRICS", out sb))
            {
                var t = sb.ToString();
                pred = Metric(t, "tokens_predicted_total"); predSec = Metric(t, "tokens_predicted_seconds_total");
                prompt = Metric(t, "prompt_tokens_total"); promptSec = Metric(t, "prompt_seconds_total");
                processing = Metric(t, "requests_processing"); deferred = Metric(t, "requests_deferred");
            }
            // Speed of the request that just finished (counters only move when a request completes).
            if (!double.IsNaN(pred) && !double.IsNaN(predSec))
            {
                if (lastPred >= 0 && pred > lastPred && predSec > lastPredSec) tpsLast = (pred - lastPred) / (predSec - lastPredSec);
                lastPred = pred; lastPredSec = predSec;
            }

            // ---- slots: live speed from tokens decoded since the previous frame
            int busy = 0, slots = 0;
            long decodedNow = 0;
            if (buf.TryGetValue("SLOTS", out sb))
            {
                try
                {
                    var arr = Json.L(Json.Parse(sb.ToString())) ?? new List<object>();
                    var seen = new HashSet<int>();
                    foreach (var s in arr.Select(Json.D).Where(x => x != null))
                    {
                        slots++;
                        int id = (int)Json.Num(s, "id");
                        bool on = Json.Bool(s, "is_processing");
                        long task = (long)Json.Num(s, "id_task", -1);
                        var nt = Json.L(Json.Get(s, "next_token"));
                        long dec = nt != null && nt.Count > 0 ? (long)Json.Num(Json.D(nt[0]), "n_decoded") : 0;
                        if (on) busy++;
                        long[] prev;
                        if (slotPrev.TryGetValue(id, out prev) && prev[0] == task && dec >= prev[1]) decodedNow += dec - prev[1];
                        else if (on) decodedNow += dec; // a request that started since the last frame
                        slotPrev[id] = new[] { task, dec };
                        seen.Add(id);
                    }
                }
                catch { }
            }
            double inst = dt > 0.2 ? decodedNow / dt : 0;
            tpsLive = busy > 0 ? (tpsLive <= 0 ? inst : tpsLive * 0.5 + inst * 0.5) : 0;
            if (tpsLive > peak) peak = tpsLive;
            if (busy > 0 && state == "loaded") state = "busy";

            string quant = null, family = null;
            if (file != null)
            {
                var q = Regex.Match(file, @"(?:UD-)?(I?Q\d[\w]*?|F16|BF16|F32)(?=\.gguf)", RegexOptions.IgnoreCase);
                if (q.Success) quant = q.Groups[1].Value.ToUpperInvariant();
                family = Regex.Replace(Path.GetFileNameWithoutExtension(file), @"[-_](UD-)?(I?Q\d.*|F16|BF16|F32)$", "", RegexOptions.IgnoreCase).Replace('-', ' ').Replace('_', ' ');
            }

            var result = new Dictionary<string, object>
            {
                ["state"] = loadedId == null ? "unloaded" : state, ["container"] = container, ["model"] = loadedId, ["file"] = file,
                ["title"] = family, ["quant"] = quant, ["ctx"] = ctx, ["models"] = models,
                ["tps"] = Json.R(tpsLive), ["tpsLast"] = Json.R(tpsLast > 0 ? tpsLast : (double?)null),
                ["tpsAvg"] = Json.R(!double.IsNaN(pred) && predSec > 0 ? pred / predSec : (double?)null),
                ["ppAvg"] = Json.R(!double.IsNaN(prompt) && promptSec > 0 ? prompt / promptSec : (double?)null, 0),
                ["busy"] = busy, ["slots"] = slots, ["queued"] = double.IsNaN(deferred) ? 0 : (int)deferred,
                ["predicted"] = double.IsNaN(pred) ? (object)null : (long)pred, ["prompted"] = double.IsNaN(prompt) ? (object)null : (long)prompt,
                ["peak"] = Json.R(peak),
            };
            lock (gate) snap = result;
        }

        void Stop()
        {
            var p = proc;
            proc = null;
            if (p == null) return;
            try { if (!p.HasExited) p.Kill(); } catch { }
            try { p.Dispose(); } catch { }
        }

        public void Dispose() { running = false; Enabled = false; Stop(); }
    }
}
