// Offline check of the anomaly detectors: hours of synthetic "normal" data must stay quiet, then
// each fault must be caught; also measures the cost of one Feed() call.
// Build: csc tools\AnomTest.cs src\Anomaly.cs src\Util.cs src\Native.cs -r:System.Web.Extensions.dll
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Pulse
{
    static class AnomTest
    {
        static readonly Random R = new Random(7);
        static double N(double sd) { double u = 1 - R.NextDouble(), v = R.NextDouble(); return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v) * sd; }

        static long t = 1760000000000;
        static double power = 60, idle = 0, cpuLoad = 20, gpuLoad = 5, tempOffset = 0, fanRpmOverride = -1, railDev = -0.5, loss = 0;
        static double chromeGb = 1.2, chromeRate = 0;
        static string idleProc = "Code.exe";
        static double idleProcPct = 2;

        static Dictionary<string, object> Snap(bool withProcs)
        {
            power = Math.Max(25, Math.Min(170, power + N(4)));
            double temp = 31 + 0.33 * power + N(0.8) + tempOffset;
            double pct = Math.Max(25, Math.Min(100, (temp - 30) * 1.6));
            double rpm = fanRpmOverride >= 0 ? fanRpmOverride : 300 + pct * 18 + N(25);
            var s = new Dictionary<string, object>
            {
                ["t"] = t,
                ["cpu"] = new Dictionary<string, object> { ["load"] = cpuLoad + N(2), ["temp"] = temp, ["power"] = power },
                ["gpu"] = new Dictionary<string, object> { ["load"] = gpuLoad, ["temp"] = 40 + N(0.5), ["power"] = 30 + N(2) },
                ["fans"] = new Dictionary<string, object> { ["list"] = new List<object> {
                    new Dictionary<string, object> { ["id"] = "f1", ["n"] = "Pump Fan #1", ["rpm"] = rpm, ["pct"] = pct, ["src"] = "board" } } },
                ["power"] = new Dictionary<string, object> { ["rails"] = new List<object> {
                    new Dictionary<string, object> { ["n"] = "+12V", ["v"] = 12 * (1 + railDev / 100), ["dev"] = railDev + N(0.15) } } },
                ["net"] = new Dictionary<string, object> { ["q"] = new Dictionary<string, object> { ["target"] = "isp",
                    ["isp"] = new Dictionary<string, object> { ["rtt"] = 9 + Math.Abs(N(2)), ["loss"] = loss } } },
            };
            if (withProcs)
            {
                chromeGb += chromeRate / 120; // 30 s step
                s["procs"] = new Dictionary<string, object>
                {
                    ["ready"] = true,
                    ["cpu"] = new List<object> { new Dictionary<string, object> { ["n"] = idleProc, ["d"] = idleProc, ["v"] = idleProcPct } },
                    ["mem"] = new List<object> { new Dictionary<string, object> { ["n"] = "chrome.exe", ["v"] = chromeGb + N(0.01) } },
                };
            }
            return s;
        }

        static AnomalyService A;
        static double feedMs; static long feeds;

        static void Run(double seconds)
        {
            for (double s = 0; s < seconds; s++)
            {
                t += 1000;
                var snap = Snap(t / 1000 % 30 == 0);
                var sw = Stopwatch.StartNew();
                A.Feed(snap);
                feedMs += sw.Elapsed.TotalMilliseconds; feeds++;
            }
        }

        static void DumpNorms()
        {
            var f = typeof(AnomalyService).GetField("norms", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var d = (System.Collections.IDictionary)f.GetValue(A);
            foreach (System.Collections.DictionaryEntry e in d)
            {
                if (!((string)e.Key).StartsWith("cpuT")) continue;
                var s = e.Value; var ty = s.GetType();
                Console.WriteLine("  {0}: N={1:N0} mean={2:N1} sd={3:N2}", e.Key, ty.GetField("N").GetValue(s), ty.GetField("Mean").GetValue(s), ty.GetProperty("Sd").GetValue(s));
            }
            Console.WriteLine("  power now {0:N0}", power);
        }

        static string Active() { return string.Join(", ", A.Active.Select(a => a.Key + "(" + a.Sev + ")")); }
        static int fails;
        static void Expect(string label, Func<bool> ok)
        {
            bool pass = ok();
            if (!pass) fails++;
            Console.WriteLine((pass ? "PASS " : "FAIL ") + label + "   active: [" + Active() + "]");
        }

        static int Main()
        {
            A = new AnomalyService(null);
            A.IdleSeconds = () => idle;
            A.Fullscreen = () => false;
            var cfg = new Dictionary<string, object> { ["enabled"] = true, ["sensitivity"] = "normal" };
            A.Config = () => cfg;
            int raised = 0;
            A.Raised += a => raised++;

            Run(4 * 3600);
            Expect("4 h of normal use stays quiet", () => A.Active.Count == 0 && raised == 0);
            Expect("learning finished", () => A.Learned >= 1);

            tempOffset = 14; Run(150);
            Expect("CPU 14° hotter than its norm is caught", () => A.Active.Any(a => a.Key == "hot:cpu"));
            tempOffset = 0; Run(120);
            Expect("…and clears once it is back to normal", () => !A.Active.Any(a => a.Key == "hot:cpu"));

            tempOffset = 4; Run(400);
            Expect("4° warmer (a hot day) is not an alarm", () => !A.Active.Any(a => a.Key == "hot:cpu"));
            tempOffset = 0; Run(60);

            fanRpmOverride = 0; Run(30);
            Expect("stalled pump is caught within 30 s", () => A.Active.Any(a => a.Key == "fanStop:f1"));
            fanRpmOverride = -1; Run(30);
            Expect("…and clears when it spins again", () => !A.Active.Any(a => a.Kind == "fanStop"));

            idle = 900; cpuLoad = 45; idleProc = "xmrig.exe"; idleProcPct = 40; Run(420);
            Expect("unknown process loading the CPU while away is a warning", () => A.Active.Any(a => a.Key == "idle:cpu:xmrig.exe" && a.Sev == "warn"));
            idleProc = "MsMpEng.exe"; Run(120);
            Expect("Defender scanning while away is only noted (info)", () => A.Active.Any(a => a.Key == "idle:cpu:MsMpEng.exe" && a.Sev == "info"));
            idleProc = "vmmemWSL"; Run(120);
            Expect("the WSL / Docker VM (the user's own containers) is only noted (info)", () => A.Active.Any(a => a.Key == "idle:cpu:vmmemWSL" && a.Sev == "info"));
            idle = 0; cpuLoad = 20; idleProc = "Code.exe"; idleProcPct = 2; Run(120);
            Expect("…both end when the user is back", () => !A.Active.Any(a => a.Kind == "idleCpu"));

            chromeRate = 1.2; Run(70 * 60);
            Expect("memory climbing 1.2 GB/h for an hour is flagged", () => A.Active.Any(a => a.Key == "leak:chrome.exe"));
            chromeRate = 0; Run(30 * 60);
            Expect("…and released after it stops growing", () => !A.Active.Any(a => a.Key == "leak:chrome.exe"));

            railDev = -6.5; Run(45);
            Expect("+12V dropping to -6.5 % is caught", () => A.Active.Any(a => a.Key == "rail:+12V"));
            railDev = -0.5; Run(60);

            loss = 12; Run(90);
            Expect("12 % packet loss is noted", () => A.Active.Any(a => a.Key == "net:quality"));
            loss = 0; Run(60);

            cfg["enabled"] = false; tempOffset = 14; Run(200);
            Expect("switched off: nothing is reported", () => A.Active.Count == 0);
            cfg["enabled"] = true; tempOffset = 0;

            Console.WriteLine();
            Console.WriteLine("Feed(): {0:N4} ms per tick on average over {1:N0} ticks", feedMs / feeds, feeds);
            Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
            return fails;
        }
    }
}
