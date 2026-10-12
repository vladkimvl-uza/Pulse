using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Pulse
{
    /// <summary>
    /// Connection quality to the internet provider: pings the home router, the provider's first
    /// gateway (found by a short traceroute) and a public anycast address every two seconds, and
    /// condenses latency, jitter and loss into a 0–4 "bars" score.
    /// </summary>
    sealed class NetQualityService : IDisposable
    {
        sealed class Probe
        {
            public IPAddress Ip;
            readonly Queue<double> samples = new Queue<double>(); // NaN = lost
            public void Add(double v) { samples.Enqueue(v); while (samples.Count > 30) samples.Dequeue(); }
            public Dictionary<string, object> Stats()
            {
                if (Ip == null) return null;
                var all = samples.ToList();
                var ok = all.Where(x => !double.IsNaN(x)).ToList();
                var recent = all.Skip(Math.Max(0, all.Count - 10)).Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
                double? rtt = recent.Count > 0 ? recent[recent.Count / 2] : (double?)null;
                double jit = 0;
                for (int i = 1; i < all.Count; i++) if (!double.IsNaN(all[i]) && !double.IsNaN(all[i - 1])) jit += Math.Abs(all[i] - all[i - 1]);
                int pairs = Math.Max(1, Enumerable.Range(1, Math.Max(0, all.Count - 1)).Count(i => !double.IsNaN(all[i]) && !double.IsNaN(all[i - 1])));
                return new Dictionary<string, object>
                {
                    ["ip"] = Ip.ToString(), ["rtt"] = Json.R(rtt), ["jit"] = Json.R(jit / pairs), ["loss"] = Json.R(all.Count > 0 ? 100.0 * (all.Count - ok.Count) / all.Count : 0),
                    ["min"] = Json.R(ok.Count > 0 ? ok.Min() : (double?)null), ["max"] = Json.R(ok.Count > 0 ? ok.Max() : (double?)null),
                    ["down"] = all.Count >= 3 && all.Skip(all.Count - 3).All(double.IsNaN), ["n"] = all.Count,
                };
            }
            public void Reset(IPAddress ip) { if (!Equals(ip, Ip)) { Ip = ip; samples.Clear(); } }
        }

        public volatile bool Enabled = true;
        readonly Probe gw = new Probe(), isp = new Probe(), inet = new Probe { Ip = IPAddress.Parse("1.1.1.1") };
        readonly object gate = new object();
        Dictionary<string, object> snap;
        DateTime nextDiscover = DateTime.MinValue;
        Thread thread;
        volatile bool running;

        public Dictionary<string, object> Snapshot { get { lock (gate) return snap; } }

        public void Start()
        {
            running = true;
            NetworkChange.NetworkAddressChanged += (s, e) => nextDiscover = DateTime.MinValue;
            thread = new Thread(Loop) { IsBackground = true, Name = "Pulse ping", Priority = ThreadPriority.BelowNormal };
            thread.Start();
        }

        void Loop()
        {
            while (running)
            {
                try
                {
                    if (Enabled)
                    {
                        if (DateTime.UtcNow >= nextDiscover) { Discover(); nextDiscover = DateTime.UtcNow.AddMinutes(10); }
                        var probes = new[] { gw, isp, inet }.Where(p => p.Ip != null).ToList();
                        var tasks = probes.Select(p => PingOnce(p.Ip)).ToArray();
                        Task.WaitAll(tasks, 1500);
                        for (int i = 0; i < probes.Count; i++) probes[i].Add(tasks[i].IsCompleted ? tasks[i].Result : double.NaN);
                        Publish();
                    }
                    else lock (gate) snap = null;
                }
                catch (Exception ex) { Log.Write("ping: " + ex.Message); }
                Thread.Sleep(2000);
            }
        }

        static async Task<double> PingOnce(IPAddress ip)
        {
            try
            {
                using (var p = new Ping())
                {
                    var r = await p.SendPingAsync(ip, 1000, new byte[32]);
                    return r.Status == IPStatus.Success ? Math.Max(0.5, r.RoundtripTime) : double.NaN;
                }
            }
            catch { return double.NaN; }
        }

        static bool Private(IPAddress ip)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] < 32) || (b[0] == 192 && b[1] == 168) || b[0] == 127 || (b[0] == 169 && b[1] == 254);
        }

        /// <summary>Router = default gateway; provider = first hop beyond it that answers pings.</summary>
        void Discover()
        {
            IPAddress router = null;
            try
            {
                router = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                                && !n.Description.Contains("Hyper-V") && !n.Description.Contains("Tailscale") && !n.Description.Contains("Virtual"))
                    .SelectMany(n => n.GetIPProperties().GatewayAddresses.Select(g => g.Address))
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
            }
            catch { }
            gw.Reset(router);
            IPAddress provider = null;
            using (var p = new Ping())
            {
                for (int ttl = 1; ttl <= 6 && provider == null; ttl++)
                {
                    try
                    {
                        var r = p.Send(inet.Ip, 900, new byte[32], new PingOptions(ttl, true));
                        var hop = r.Address;
                        if (hop == null || hop.Equals(IPAddress.Any)) continue;
                        if (router != null && hop.Equals(router)) continue;
                        if (Private(hop) && ttl == 1) continue; // a second home router
                        if (r.Status != IPStatus.TtlExpired && r.Status != IPStatus.Success) continue;
                        // The hop must answer direct pings too, or it is useless as a probe target.
                        if (p.Send(hop, 900).Status == IPStatus.Success) provider = hop;
                    }
                    catch { }
                }
            }
            isp.Reset(provider);
            Log.Write("ping targets: router " + router + ", provider " + provider);
        }

        /// <summary>Worst of latency, jitter and loss decides — one bad metric is enough to feel it.</summary>
        static int Score(Dictionary<string, object> s)
        {
            if (s == null || Json.Bool(s, "down")) return 0;
            double rtt = Json.Num(s, "rtt", 999), jit = Json.Num(s, "jit"), loss = Json.Num(s, "loss");
            int a = rtt < 15 ? 4 : rtt < 40 ? 3 : rtt < 80 ? 2 : rtt < 150 ? 1 : 0;
            int b = jit < 3 ? 4 : jit < 8 ? 3 : jit < 20 ? 2 : jit < 40 ? 1 : 0;
            int c = loss <= 0 ? 4 : loss < 1.5 ? 3 : loss < 4 ? 2 : loss < 10 ? 1 : 0;
            return Math.Max(1, Math.Min(a, Math.Min(b, c)));
        }

        void Publish()
        {
            var sg = gw.Stats(); var si = isp.Stats(); var sn = inet.Stats();
            var main = si ?? sn;
            var res = new Dictionary<string, object>
            {
                ["score"] = Score(main), ["target"] = si != null ? "isp" : "inet",
                ["gw"] = sg, ["isp"] = si, ["inet"] = sn, ["offline"] = sn != null && Json.Bool(sn, "down") && (si == null || Json.Bool(si, "down")),
            };
            lock (gate) snap = res;
        }

        public void Dispose() { running = false; }
    }
}
