using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Pulse
{
    /// <summary>
    /// Per-application CPU and memory, grouped by executable like Task Manager's "Apps" view.
    /// One NtQuerySystemInformation call returns every process with CPU times and private working
    /// set, so no per-process handles are needed (and it works without elevation).
    /// </summary>
    sealed class ProcScanner
    {
        const int SystemProcessInformation = 5;
        const int STATUS_INFO_LENGTH_MISMATCH = unchecked((int)0xC0000004);

        sealed class Meta { public string Desc; public string Icon; }

        IntPtr buf = IntPtr.Zero;
        int bufLen;
        readonly Dictionary<long, long> prevTimes = new Dictionary<long, long>();
        long prevStamp;
        readonly Dictionary<string, Meta> meta = new Dictionary<string, Meta>(StringComparer.OrdinalIgnoreCase);
        readonly int logical = Environment.ProcessorCount;

        struct Row { public string Name; public int Pid; public double Cpu; public long Mem; }

        public Dictionary<string, object> Scan(out Dictionary<string, object> newIcons)
        {
            var fresh = newIcons = new Dictionary<string, object>();
            ReadAll();
            long stamp = Stopwatch.GetTimestamp();
            double elapsed100ns = prevStamp == 0 ? 0 : (stamp - prevStamp) * 1e7 / Stopwatch.Frequency;
            prevStamp = stamp;

            var groups = new Dictionary<string, Row>(StringComparer.OrdinalIgnoreCase);
            var seen = new Dictionary<long, long>();
            int count = 0;
            int offset = 0;
            while (true)
            {
                IntPtr p = buf + offset;
                int next = Marshal.ReadInt32(p, 0);
                long privateWs = Marshal.ReadInt64(p, 8);
                long create = Marshal.ReadInt64(p, 32);
                long user = Marshal.ReadInt64(p, 40), kernel = Marshal.ReadInt64(p, 48);
                int nameLen = Marshal.ReadInt16(p, 56);
                IntPtr namePtr = Marshal.ReadIntPtr(p, 64);
                int pid = (int)(long)Marshal.ReadIntPtr(p, 80);

                if (pid != 0)
                {
                    count++;
                    string name = namePtr != IntPtr.Zero && nameLen > 0 ? Marshal.PtrToStringUni(namePtr, nameLen / 2) : "System";
                    long key = ((long)pid << 40) ^ create;
                    long t = user + kernel;
                    long prev;
                    double cpu = 0;
                    if (elapsed100ns > 0 && prevTimes.TryGetValue(key, out prev) && t >= prev)
                        cpu = (t - prev) / (elapsed100ns * logical) * 100.0;
                    seen[key] = t;

                    Row r;
                    if (!groups.TryGetValue(name, out r)) r = new Row { Name = name, Pid = pid };
                    r.Cpu += cpu;
                    r.Mem += privateWs;
                    groups[name] = r;
                }
                if (next == 0) break;
                offset += next;
            }
            prevTimes.Clear();
            foreach (var kv in seen) prevTimes[kv.Key] = kv.Value;

            Func<Row, Dictionary<string, object>> wire = r =>
            {
                var m = Describe(r.Name, r.Pid, fresh);
                return new Dictionary<string, object> { ["n"] = r.Name, ["d"] = m.Desc };
            };
            var cpuTop = groups.Values.Where(r => r.Cpu >= 0.05).OrderByDescending(r => r.Cpu).Take(6)
                .Select(r => { var d = wire(r); d["v"] = Math.Round(r.Cpu, 1); return (object)d; }).ToList();
            var memTop = groups.Values.OrderByDescending(r => r.Mem).Take(6)
                .Select(r => { var d = wire(r); d["v"] = Math.Round(r.Mem / 1073741824.0, 3); return (object)d; }).ToList();

            return new Dictionary<string, object> { ["cpu"] = cpuTop, ["mem"] = memTop, ["count"] = count, ["ready"] = elapsed100ns > 0 };
        }

        void ReadAll()
        {
            if (buf == IntPtr.Zero) { bufLen = 1 << 20; buf = Marshal.AllocHGlobal(bufLen); }
            for (int attempt = 0; attempt < 6; attempt++)
            {
                int ret;
                int st = Native.NtQuerySystemInformation(SystemProcessInformation, buf, bufLen, out ret);
                if (st == 0) return;
                if (st != STATUS_INFO_LENGTH_MISMATCH) throw new InvalidOperationException("NtQuerySystemInformation 0x" + st.ToString("X"));
                Marshal.FreeHGlobal(buf);
                bufLen = Math.Max(bufLen * 2, ret + 65536);
                buf = Marshal.AllocHGlobal(bufLen);
            }
            throw new InvalidOperationException("process list keeps growing");
        }

        /// <summary>Every icon extracted so far, for windows that open after the first scan.</summary>
        public Dictionary<string, object> AllIcons()
        {
            lock (meta) return meta.Where(kv => kv.Value.Icon != null).ToDictionary(kv => kv.Key, kv => (object)kv.Value.Icon);
        }

        Meta Describe(string name, int pid, Dictionary<string, object> newIcons)
        {
            Meta m;
            lock (meta)
            {
                if (meta.TryGetValue(name, out m)) return m;
                m = new Meta { Desc = Path.GetFileNameWithoutExtension(name) };
                meta[name] = m;
            }
            string path = ImagePath(pid);
            if (path == null) return m;
            try
            {
                var vi = FileVersionInfo.GetVersionInfo(path);
                var desc = (vi.FileDescription ?? "").Trim();
                if (desc.Length > 1 && desc.Length < 48) m.Desc = desc;
            }
            catch { }
            try
            {
                using (var icon = Icon.ExtractAssociatedIcon(path))
                using (var bmp = icon.ToBitmap())
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    m.Icon = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                    newIcons[name] = m.Icon;
                }
            }
            catch { }
            return m;
        }

        static string ImagePath(int pid)
        {
            IntPtr h = Native.OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return Native.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { Native.CloseHandle(h); }
        }
    }
}
