using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;

namespace Pulse
{
    /// <summary>
    /// Owns LibreHardwareMonitor on a dedicated thread and produces one snapshot per interval.
    /// Everything the UI shows comes from these snapshots; nothing else touches the hardware.
    /// </summary>
    sealed class SensorService : IDisposable
    {
        public static readonly string[] SeriesKeys = { "cpu", "cpuT", "cpuP", "gpu", "gpuT", "gpuP", "vram", "ram", "ramT", "disk", "diskT", "diskR", "diskW", "netD", "netU", "llm", "ping", "fan", "pwr", "bat", "batW" };

        public readonly HistoryBuffer History = new HistoryBuffer(SeriesKeys, 3600);
        public volatile int IntervalMs = 1000;
        public volatile bool WantProcs, WantProcsSlow;
        public event Action<Dictionary<string, object>> Sampled;   // raised on the sensor thread
        public event Action<Dictionary<string, object>> InfoReady; // raised on the sensor thread
        public Dictionary<string, object> Info { get; private set; }
        public LlmService Llm;
        /// <summary>Power supply as the user described it (rated watts, 80 PLUS level); used for efficiency.</summary>
        public volatile int PsuWatt = 850;
        public volatile string PsuRating = "gold";
        public NetQualityService NetQ;

        volatile string gpuPref = "auto";
        volatile bool remapGpu;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly ProcScanner procs = new ProcScanner();
        Thread thread;
        volatile bool running;
        long tick;

        Computer pc;
        IHardware cpu, gpu, ram, vmem;
        List<IHardware> gpus = new List<IHardware>(), dimms = new List<IHardware>(), nics = new List<IHardware>(), disks = new List<IHardware>(), boards = new List<IHardware>();
        ISensor cpuLoad, cpuTemp, cpuPower, cpuClock;
        List<ISensor> cpuThreads = new List<ISensor>(), cpuCoreClocks = new List<ISensor>(), ccdTemps = new List<ISensor>();
        ISensor gLoad, gTemp, gHot, gMemT, gClock, gMemClock, gPower, gVramUsed, gVramTotal, gShUsed, gShTotal, gVideo, gMemCtl;
        List<ISensor> gFans = new List<ISensor>(), gFanCtl = new List<ISensor>();
        ISensor rLoad, rUsed, rAvail, vLoad, vUsed, vAvail;
        IHardware nic;
        readonly Dictionary<IHardware, double> nicTraffic = new Dictionary<IHardware, double>();

        PerformanceCounter pcCpuPerf, pcDiskRead, pcDiskWrite, pcDiskIdle;
        double baseMhz;
        object drivesCache = new object[0];
        long lastDrives = -1, lastProcs = -1;
        bool cpuTempSeen, ramTempSeen, diskTempSeen;
        Dictionary<string, object> inventory = new Dictionary<string, object>();
        List<object> gpuProcs = new List<object>();
        readonly Dictionary<string, double> fanSeen = new Dictionary<string, double>();
        readonly Dictionary<int, string> procNames = new Dictionary<int, string>();

        /// <summary>
        /// Dedicated video memory per process from the "GPU Process Memory" counters, for the
        /// adapter that holds the most. DWM mirrors some allocations of other processes, so it is
        /// dropped when it claims an implausible share.
        /// </summary>
        List<object> GpuProcesses(double usedMb)
        {
            var result = new List<object>();
            try
            {
                var data = new PerformanceCounterCategory("GPU Process Memory").ReadCategory();
                InstanceDataCollection ded = null;
                foreach (string k in data.Keys) if (k.Equals("dedicated usage", StringComparison.OrdinalIgnoreCase)) ded = data[k];
                if (ded == null) return result;
                var rows = new List<Tuple<string, int, double>>();
                foreach (InstanceData d in ded.Values)
                {
                    var m = Regex.Match(d.InstanceName, @"pid_(\d+)_luid_(0x[0-9a-f]+_0x[0-9a-f]+)", RegexOptions.IgnoreCase);
                    if (m.Success && d.RawValue > 0) rows.Add(Tuple.Create(m.Groups[2].Value, int.Parse(m.Groups[1].Value), d.RawValue / 1048576.0));
                }
                if (rows.Count == 0) return result;
                var luid = rows.GroupBy(r => r.Item1).OrderByDescending(g => g.Sum(r => r.Item3)).First().Key;
                var perPid = rows.Where(r => r.Item1 == luid).GroupBy(r => r.Item2).Select(g => new { pid = g.Key, mb = g.Sum(r => r.Item3) }).ToList();
                var named = perPid.Select(p => new { name = ProcName(p.pid), p.mb }).Where(p => p.name != null).ToList();
                if (usedMb > 0) named = named.Where(p => !(p.name == "dwm" && p.mb > usedMb * 0.5)).ToList();
                foreach (var g in named.GroupBy(p => p.name).Select(g => new { name = g.Key, mb = g.Sum(p => p.mb) }).OrderByDescending(g => g.mb).Take(7))
                    if (g.mb >= 16) result.Add(new Dictionary<string, object> { ["n"] = g.name, ["v"] = Math.Round(g.mb) });
            }
            catch (Exception ex) { if (tick < 5) Log.Write("gpu procs: " + ex.Message); }
            return result;
        }

        string ProcName(int pid)
        {
            string n;
            if (procNames.TryGetValue(pid, out n)) return n;
            try { using (var p = Process.GetProcessById(pid)) n = p.ProcessName; } catch { n = null; }
            if (procNames.Count > 500) procNames.Clear();
            return procNames[pid] = n;
        }
        Dictionary<string, string> nicDescriptions = new Dictionary<string, string>();

        /// <summary>Static hardware facts for vendor logos and the About page (WMI, read once).</summary>
        Dictionary<string, object> Inventory()
        {
            var inv = new Dictionary<string, object>();
            try
            {
                var mods = new List<object>();
                using (var q = new ManagementObjectSearcher("SELECT Manufacturer, PartNumber, Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory"))
                    foreach (ManagementObject o in q.Get())
                    {
                        int type = Convert.ToInt32(o["SMBIOSMemoryType"] ?? 0);
                        mods.Add(new Dictionary<string, object>
                        {
                            ["mfr"] = ((o["Manufacturer"] as string) ?? "").Trim(), ["part"] = ((o["PartNumber"] as string) ?? "").Trim(),
                            ["gb"] = Math.Round(Convert.ToDouble(o["Capacity"] ?? 0) / 1073741824.0),
                            ["speed"] = Convert.ToInt32(o["ConfiguredClockSpeed"] ?? o["Speed"] ?? 0),
                            ["type"] = type == 34 ? "DDR5" : type == 26 ? "DDR4" : type == 24 ? "DDR3" : type == 35 ? "LPDDR5" : null,
                        });
                    }
                inv["ramMods"] = mods;
            }
            catch (Exception ex) { Log.Write("wmi memory: " + ex.Message); }
            try
            {
                var disks = new List<object>();
                var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
                using (var q = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT FriendlyName, MediaType, BusType, Size FROM MSFT_PhysicalDisk")))
                    foreach (ManagementObject o in q.Get())
                    {
                        int bus = Convert.ToInt32(o["BusType"] ?? 0), media = Convert.ToInt32(o["MediaType"] ?? 0);
                        disks.Add(new Dictionary<string, object>
                        {
                            ["name"] = ((o["FriendlyName"] as string) ?? "").Trim(),
                            ["kind"] = bus == 17 ? "NVMe" : media == 4 ? "SSD" : media == 3 ? "HDD" : bus == 7 ? "USB" : "",
                            ["gb"] = Math.Round(Convert.ToDouble(o["Size"] ?? 0) / 1e9),
                        });
                    }
                inv["disks"] = disks;
            }
            catch (Exception ex) { Log.Write("wmi disks: " + ex.Message); }
            try
            {
                // Board partner of the graphics card = brand of its fans (PCI subsystem vendor).
                var vendors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["1462"] = "MSI", ["1043"] = "ASUS", ["1458"] = "GIGABYTE", ["1569"] = "Palit", ["10B0"] = "Gainward", ["19DA"] = "ZOTAC",
                    ["3842"] = "EVGA", ["10DE"] = "NVIDIA", ["196E"] = "PNY", ["1B4C"] = "GALAX", ["7377"] = "Colorful", ["1682"] = "XFX",
                    ["148C"] = "PowerColor", ["1DA2"] = "Sapphire", ["1849"] = "ASRock", ["1D05"] = "Inno3D",
                };
                using (var q = new ManagementObjectSearcher("SELECT Name, PNPDeviceID FROM Win32_VideoController"))
                    foreach (ManagementObject o in q.Get())
                    {
                        var name = (o["Name"] as string) ?? "";
                        var m = Regex.Match((o["PNPDeviceID"] as string) ?? "", @"SUBSYS_[0-9A-F]{4}([0-9A-F]{4})", RegexOptions.IgnoreCase);
                        string v;
                        if (m.Success && gpu != null && name.Contains(gpu.Name.Replace("NVIDIA ", "").Replace("AMD ", "")) && vendors.TryGetValue(m.Groups[1].Value, out v)) inv["gpuVendor"] = v;
                    }
            }
            catch (Exception ex) { Log.Write("wmi gpu vendor: " + ex.Message); }
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                    foreach (ManagementObject o in q.Get())
                    {
                        inv["board"] = ((o["Product"] as string) ?? "").Trim();
                        inv["boardMfr"] = ((o["Manufacturer"] as string) ?? "").Trim();
                        break;
                    }
            }
            catch (Exception ex) { Log.Write("wmi board: " + ex.Message); }
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) nicDescriptions[ni.Name] = ni.Description;
            }
            catch { }
            return inv;
        }

        public SensorService() { Info = new Dictionary<string, object>(); }

        public readonly BatteryService Battery = new BatteryService();
        /// <summary>"laptop" or "desktop" (decided by the installer, else detected) and the maker / model.</summary>
        public string DeviceKind = "desktop";
        public Dictionary<string, object> DeviceModel = new Dictionary<string, object>();

        public void Start()
        {
            running = true;
            thread = new Thread(Run) { IsBackground = true, Name = "Pulse sensors", Priority = ThreadPriority.AboveNormal };
            thread.SetApartmentState(ApartmentState.STA); // icon extraction uses the shell
            thread.Start();
        }

        public void SetGpu(string pref) { gpuPref = string.IsNullOrEmpty(pref) ? "auto" : pref; remapGpu = true; wake.Set(); }
        public Dictionary<string, object> Icons() { return procs.AllIcons(); }
        public void Poke() { wake.Set(); }

        void Run()
        {
            try { Open(); }
            catch (Exception ex) { Log.Error("sensors open", ex); }
            var sw = Stopwatch.StartNew();
            int failures = 0;
            while (running)
            {
                long t0 = sw.ElapsedMilliseconds;
                try
                {
                    if (remapGpu) { remapGpu = false; MapGpu(); PublishInfo(); }
                    var snap = Sample(sw.ElapsedMilliseconds);
                    var h = Sampled;
                    if (h != null) h(snap);
                    failures = 0;
                }
                catch (Exception ex) { if (failures++ < 5) Log.Error("sensors sample", ex); }
                long wait = IntervalMs - (sw.ElapsedMilliseconds - t0);
                wake.WaitOne((int)Math.Max(50, wait));
            }
        }

        // ---------------------------------------------------------------- discovery

        void Open()
        {
            try
            {
                pcCpuPerf = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total", true); pcCpuPerf.NextValue();
            }
            catch (Exception ex) { Log.Write("perf cpu: " + ex.Message); pcCpuPerf = null; }
            try
            {
                pcDiskRead = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", true); pcDiskRead.NextValue();
                pcDiskWrite = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", true); pcDiskWrite.NextValue();
                pcDiskIdle = new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total", true); pcDiskIdle.NextValue();
            }
            catch (Exception ex) { Log.Write("perf disk: " + ex.Message); pcDiskRead = pcDiskWrite = pcDiskIdle = null; }
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    baseMhz = Convert.ToDouble(k.GetValue("~MHz"));
            }
            catch { baseMhz = 0; }

            pc = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true, IsNetworkEnabled = true, IsStorageEnabled = true, IsMotherboardEnabled = true, IsControllerEnabled = true };
            pc.Open();
            foreach (var hw in pc.Hardware) UpdateDeep(hw);
            Map();
            inventory = Inventory();
            PublishInfo();
            DumpSensors();
        }

        static void UpdateDeep(IHardware h)
        {
            try { h.Update(); foreach (var s in h.SubHardware) s.Update(); } catch { }
        }

        static IEnumerable<ISensor> All(IHardware h) { return h.Sensors.Concat(h.SubHardware.SelectMany(All)); }
        static readonly Regex LimitName = new Regex(@"Limit|Critical|Warning|Resolution|Threshold", RegexOptions.IgnoreCase);

        static ISensor First(IEnumerable<ISensor> s, SensorType t, params string[] names)
        {
            var list = s.Where(x => x.SensorType == t).ToList();
            foreach (var n in names)
            {
                var hit = list.FirstOrDefault(x => x.Name == n);
                if (hit != null) return hit;
            }
            return null;
        }

        void Map()
        {
            cpu = pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
            if (cpu != null)
            {
                var s = All(cpu).ToList();
                cpuLoad = First(s, SensorType.Load, "CPU Total");
                cpuThreads = s.Where(x => x.SensorType == SensorType.Load && x.Name.StartsWith("CPU Core #")).OrderBy(x => x.Index).ToList();
                cpuTemp = First(s, SensorType.Temperature, "Core (Tctl/Tdie)", "CPU Package", "Package", "Core (Tctl)", "Core (Tdie)", "Core Average", "Core Max")
                          ?? s.FirstOrDefault(x => x.SensorType == SensorType.Temperature);
                ccdTemps = s.Where(x => x.SensorType == SensorType.Temperature && Regex.IsMatch(x.Name, @"^CCD\s?\d+\b") && !x.Name.Contains("Max") && !x.Name.Contains("Average")).OrderBy(x => x.Name).ToList();
                cpuPower = First(s, SensorType.Power, "Package", "CPU Package");
                cpuClock = First(s, SensorType.Clock, "Cores (Average)");
                cpuCoreClocks = s.Where(x => x.SensorType == SensorType.Clock && Regex.IsMatch(x.Name, @"^(CPU )?Core #\d+$")).ToList();
            }

            gpus = pc.Hardware.Where(h => h.HardwareType == HardwareType.GpuNvidia || h.HardwareType == HardwareType.GpuAmd || h.HardwareType == HardwareType.GpuIntel).ToList();
            MapGpu();

            var mems = pc.Hardware.Where(h => h.HardwareType == HardwareType.Memory).ToList();
            ram = mems.FirstOrDefault(h => h.Identifier.ToString() == "/ram") ?? mems.FirstOrDefault(h => h.Name.Contains("Total") || h.Name.Contains("Generic"));
            vmem = mems.FirstOrDefault(h => h.Identifier.ToString() == "/vram");
            dimms = mems.Where(h => h != ram && h != vmem).ToList();
            if (ram != null)
            {
                rLoad = First(ram.Sensors, SensorType.Load, "Memory");
                rUsed = First(ram.Sensors, SensorType.Data, "Memory Used");
                rAvail = First(ram.Sensors, SensorType.Data, "Memory Available");
            }
            if (vmem != null)
            {
                vLoad = First(vmem.Sensors, SensorType.Load, "Memory", "Virtual Memory");
                vUsed = First(vmem.Sensors, SensorType.Data, "Memory Used", "Virtual Memory Used");
                vAvail = First(vmem.Sensors, SensorType.Data, "Memory Available", "Virtual Memory Available");
            }

            nics = pc.Hardware.Where(h => h.HardwareType == HardwareType.Network).ToList();
            nic = nics.OrderByDescending(h =>
            {
                var s = All(h).Where(x => x.SensorType == SensorType.Data).Sum(x => (double)(x.Value ?? 0));
                return s;
            }).FirstOrDefault();
            disks = pc.Hardware.Where(h => h.HardwareType == HardwareType.Storage).ToList();
            boards = pc.Hardware.Where(h => h.HardwareType == HardwareType.Motherboard || h.HardwareType == HardwareType.Cooler || h.HardwareType == HardwareType.EmbeddedController).ToList();
        }

        static double GpuMemTotal(IHardware h)
        {
            var s = All(h).ToList();
            var t = First(s, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total");
            return t != null && t.Value.HasValue ? t.Value.Value : 0;
        }

        void MapGpu()
        {
            gpu = null;
            if (gpuPref != "auto") gpu = gpus.FirstOrDefault(g => g.Identifier.ToString() == gpuPref);
            if (gpu == null)
                gpu = gpus.OrderByDescending(g => g.HardwareType == HardwareType.GpuNvidia ? 1 : 0)
                          .ThenByDescending(GpuMemTotal).FirstOrDefault();
            if (gpu == null) return;
            var s = All(gpu).ToList();
            gLoad = First(s, SensorType.Load, "GPU Core", "D3D 3D");
            gTemp = First(s, SensorType.Temperature, "GPU Core");
            gHot = First(s, SensorType.Temperature, "GPU Hot Spot");
            gMemT = First(s, SensorType.Temperature, "GPU Memory Junction", "GPU Memory");
            gClock = First(s, SensorType.Clock, "GPU Core");
            gMemClock = First(s, SensorType.Clock, "GPU Memory");
            gPower = First(s, SensorType.Power, "GPU Package", "GPU Power", "GPU Core");
            gVramUsed = First(s, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used");
            gVramTotal = First(s, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total");
            gShUsed = First(s, SensorType.SmallData, "D3D Shared Memory Used");
            gShTotal = First(s, SensorType.SmallData, "D3D Shared Memory Total");
            gVideo = First(s, SensorType.Load, "GPU Video Engine", "D3D Video Decode");
            gMemCtl = First(s, SensorType.Load, "GPU Memory Controller");
            gFans = s.Where(x => x.SensorType == SensorType.Fan).ToList();
            gFanCtl = s.Where(x => x.SensorType == SensorType.Control).ToList();
        }

        void PublishInfo()
        {
            var info = new Dictionary<string, object>
            {
                ["cpu"] = cpu != null ? CleanName(cpu.Name) : "CPU",
                ["threads"] = cpuThreads.Count > 0 ? cpuThreads.Count : Environment.ProcessorCount,
                ["cores"] = Math.Max(1, cpuCoreClocks.Count),
                ["gpu"] = gpu != null ? CleanName(gpu.Name) : null,
                ["gpuId"] = gpu != null ? gpu.Identifier.ToString() : null,
                ["gpus"] = gpus.Select(g => new Dictionary<string, object> { ["id"] = g.Identifier.ToString(), ["name"] = CleanName(g.Name) }).ToList(),
                ["ramTotal"] = rUsed != null && rAvail != null ? Json.R((double)(rUsed.Value ?? 0) + (rAvail.Value ?? 0), 1) : null,
                ["vramTotal"] = gVramTotal != null ? Json.R(gVramTotal.Value, 0) : null,
                ["cpuTemp"] = cpuTempSeen, ["ramTemp"] = ramTempSeen, ["diskTemp"] = diskTempSeen,
                ["os"] = OsName(), ["machine"] = Environment.MachineName,
                ["nic"] = nic != null ? nic.Name : null,
                ["nicDesc"] = NicDescription(),
                ["device"] = DeviceKind, ["model"] = DeviceModel,
            };
            foreach (var kv in inventory) info[kv.Key] = kv.Value;
            Info = info;
            var h = InfoReady;
            if (h != null) h(info);
        }

        /// <summary>Writes every sensor LibreHardwareMonitor sees to sensors.txt (diagnostics).</summary>
        void DumpSensors()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Pulse sensor dump " + DateTime.Now + (Elevation.IsElevated() ? " [elevated]" : ""));
                Action<IHardware, string> dump = null;
                dump = (hw, ind) =>
                {
                    sb.AppendLine(ind + "[" + hw.HardwareType + "] " + hw.Name + "  " + hw.Identifier);
                    foreach (var x in hw.Sensors)
                        sb.AppendLine(ind + "   " + x.SensorType.ToString().PadRight(12) + x.Name.PadRight(40) + (x.Value.HasValue ? x.Value.Value.ToString("0.##") : "null") + "   " + x.Identifier);
                    foreach (var sub in hw.SubHardware) dump(sub, ind + "    ");
                };
                foreach (var hw in pc.Hardware) dump(hw, "");
                File.WriteAllText(Path.Combine(Paths.Local, "sensors.txt"), sb.ToString());
            }
            catch (Exception ex) { Log.Error("dump", ex); }
        }

        // ---------------------------------------------------------------- power

        readonly Dictionary<string, Queue<double>> railWindow = new Dictionary<string, Queue<double>>();
        double kwhToday, kwhSession, energyTotal;
        Dictionary<string, double> energyDays;
        string energySince;

        void LoadEnergy()
        {
            energyDays = new Dictionary<string, double>();
            try
            {
                var e = Json.D(Json.Parse(File.ReadAllText(EnergyFile)));
                var days = Json.D(Json.Get(e, "days"));
                if (days != null) foreach (var kv in days) energyDays[kv.Key] = Convert.ToDouble(kv.Value, System.Globalization.CultureInfo.InvariantCulture);
                else if (Json.Str(e, "day") != null) energyDays[Json.Str(e, "day")] = Json.Num(e, "kwh"); // first version kept only today
                energyTotal = Json.Get(e, "total") != null ? Json.Num(e, "total") : energyDays.Values.Sum();
                energySince = Json.Str(e, "since");
            }
            catch { }
            if (energySince == null) energySince = energyDays.Keys.OrderBy(k => k).FirstOrDefault() ?? DateTime.Now.ToString("yyyy-MM-dd");
        }

        public void SaveEnergy()
        {
            if (energyDays == null) return;
            try
            {
                var cutoff = DateTime.Now.AddDays(-400).ToString("yyyy-MM-dd");
                var keep = energyDays.Where(kv => string.CompareOrdinal(kv.Key, cutoff) >= 0).OrderBy(kv => kv.Key)
                                     .ToDictionary(kv => kv.Key, kv => (object)Math.Round(kv.Value, 4));
                File.WriteAllText(EnergyFile, Json.Write(new Dictionary<string, object> { ["days"] = keep, ["total"] = Math.Round(energyTotal, 4), ["since"] = energySince }));
            }
            catch (Exception ex) { Log.Error("energy save", ex); }
        }
        long lastPowerMs = -1, lastEnergySave;
        static readonly string EnergyFile = Path.Combine(Paths.Cache, "energy.json");

        static readonly Dictionary<string, double[]> Efficiency = new Dictionary<string, double[]>
        {
            // efficiency at 10 %, 20 %, 50 %, 100 % load (230 V mains)
            ["white"] = new[] { .77, .82, .85, .82 }, ["bronze"] = new[] { .80, .85, .88, .85 }, ["gold"] = new[] { .84, .90, .92, .89 },
            ["platinum"] = new[] { .87, .92, .94, .91 }, ["titanium"] = new[] { .90, .94, .96, .93 },
        };

        static double Eff(string rating, double load)
        {
            double[] e;
            if (!Efficiency.TryGetValue(rating ?? "gold", out e)) e = Efficiency["gold"];
            double[] x = { .1, .2, .5, 1.0 };
            if (load <= x[0]) return e[0] - (x[0] - load) * .6;
            for (int i = 1; i < x.Length; i++) if (load <= x[i]) return e[i - 1] + (e[i] - e[i - 1]) * (load - x[i - 1]) / (x[i] - x[i - 1]);
            return e[3] - (load - 1) * .1;
        }

        /// <summary>
        /// System power: CPU package and graphics board are measured; the rest (board, chipset, VRM
        /// losses, memory, drives, fans) is estimated from the hardware actually present. Wall power
        /// adds the PSU's 80 PLUS efficiency at the current load. Rails come from the Super I/O.
        /// </summary>
        Dictionary<string, object> Power(double? cpuW, double? gpuW, double? diskAct, List<object> fanList, long nowMs)
        {
            double cpu = cpuW ?? 0, gpuPw = gpuW ?? 0;
            int dimmCount = Math.Max(1, (Json.L(Json.Get(inventory, "ramMods")) ?? new List<object>()).Count);
            int diskCount = Math.Max(1, (Json.L(Json.Get(inventory, "disks")) ?? new List<object>()).Count);
            double fansW = 0;
            foreach (var f in fanList.Select(Json.D))
            {
                double rpm = Json.Num(f, "rpm"), pct = Json.Num(f, "pct", 50);
                if (rpm <= 0 || Json.Str(f, "src") == "gpu") continue; // GPU fans are inside its board power
                fansW += Json.Str(f, "n", "").StartsWith("Pump") ? 4 : 0.4 + 2.2 * Math.Pow(pct / 100.0, 2);
            }
            double rest = 18 + 0.1 * cpu + dimmCount * 4 + diskCount * (2 + 4 * (diskAct ?? 0) / 100.0) + fansW + 6;
            double dc = cpu + gpuPw + rest;
            int watt = Math.Max(200, PsuWatt);
            double load = dc / watt, eff = Eff(PsuRating, load), wall = dc / eff;

            // energy ledger: kWh per calendar day, kept for 400 days
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            if (energyDays == null) LoadEnergy();
            if (lastPowerMs >= 0)
            {
                double kwh = wall * ((nowMs - lastPowerMs) / 3600000.0) / 1000;
                double cur;
                energyDays.TryGetValue(today, out cur);
                energyDays[today] = cur + kwh;
                energyTotal += kwh;
                kwhSession += kwh;
            }
            lastPowerMs = nowMs;
            if (nowMs - lastEnergySave > 60000) { lastEnergySave = nowMs; SaveEnergy(); }
            kwhToday = energyDays.ContainsKey(today) ? energyDays[today] : 0;
            var now = DateTime.Now.Date;
            Func<DateTime, double> day = d => { double v; return energyDays.TryGetValue(d.ToString("yyyy-MM-dd"), out v) ? v : 0; };
            var last30 = Enumerable.Range(0, 30).Select(i => now.AddDays(i - 29)).ToList();
            var monthKey = now.ToString("yyyy-MM");

            // rails: deviation from nominal and how much they move over the last minute
            var rails = new List<object>();
            int score = 4;
            double? battery = null;
            foreach (var b in boards)
                foreach (var v in All(b).Where(x => x.SensorType == SensorType.Voltage))
                {
                    double nominal = v.Name == "+12V" ? 12 : v.Name == "+5V" ? 5 : v.Name == "+3.3V" ? 3.3 : 0;
                    var val = V(v);
                    if (v.Name == "CMOS Battery" && val.HasValue) battery = val;
                    if (nominal == 0 || !val.HasValue || val.Value <= 0) continue;
                    Queue<double> q;
                    if (!railWindow.TryGetValue(v.Name, out q)) railWindow[v.Name] = q = new Queue<double>();
                    q.Enqueue(val.Value);
                    while (q.Count > 60) q.Dequeue();
                    double avg = q.Average(), dev = (avg - nominal) / nominal * 100, ripple = (q.Max() - q.Min()) / nominal * 100;
                    int s = Math.Abs(dev) < 1.5 && ripple < 1 ? 4 : Math.Abs(dev) < 3 && ripple < 2 ? 3 : Math.Abs(dev) < 5 && ripple < 3.5 ? 2 : 1;
                    score = Math.Min(score, s);
                    rails.Add(new Dictionary<string, object> { ["n"] = v.Name, ["v"] = Json.R(val, 3), ["nom"] = nominal, ["dev"] = Json.R(dev, 2), ["ripple"] = Json.R(ripple, 2), ["s"] = s });
                }

            return new Dictionary<string, object>
            {
                ["cpu"] = Json.R(cpuW, 0), ["gpu"] = Json.R(gpuW, 0), ["rest"] = Json.R(rest, 0), ["dc"] = Json.R(dc, 0), ["wall"] = Json.R(wall, 0),
                ["eff"] = Json.R(eff * 100, 1), ["load"] = Json.R(load, 3), ["watt"] = watt, ["rails"] = rails, ["score"] = rails.Count > 0 ? score : 0,
                ["battery"] = Json.R(battery, 2), ["today"] = Json.R(kwhToday, 3), ["session"] = Json.R(kwhSession, 3),
                ["yesterday"] = Json.R(day(now.AddDays(-1)), 3), ["week"] = Json.R(Enumerable.Range(0, 7).Sum(i => day(now.AddDays(-i))), 3),
                ["month"] = Json.R(energyDays.Where(kv => kv.Key.StartsWith(monthKey)).Sum(kv => kv.Value), 3),
                ["total"] = Json.R(energyTotal, 3), ["since"] = energySince,
                ["days"] = last30.Select(d => Json.R(day(d), 3)).ToList(), ["daysFrom"] = last30[0].ToString("yyyy-MM-dd"),
            };
        }

        string NicDescription()
        {
            if (nic == null) return null;
            string d;
            return nicDescriptions.TryGetValue(nic.Name, out d) ? d : null;
        }

        static string CleanName(string n)
        {
            n = Regex.Replace(n ?? "", @"\((R|TM)\)|\s+\d+-Core Processor|\s+Processor", "", RegexOptions.IgnoreCase);
            n = n.Replace("NVIDIA GeForce ", "GeForce ");
            return Regex.Replace(n, @"\s{2,}", " ").Trim();
        }

        static string OsName()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    var name = (string)k.GetValue("ProductName") ?? "Windows";
                    var build = Convert.ToInt32(k.GetValue("CurrentBuildNumber") ?? "0");
                    if (build >= 22000) name = name.Replace("Windows 10", "Windows 11");
                    var disp = k.GetValue("DisplayVersion") as string;
                    return disp != null ? name + " " + disp : name;
                }
            }
            catch { return "Windows"; }
        }

        // ---------------------------------------------------------------- sampling

        static double? V(ISensor s)
        {
            if (s == null || !s.Value.HasValue) return null;
            var v = (double)s.Value.Value;
            return double.IsNaN(v) || double.IsInfinity(v) ? (double?)null : v;
        }
        static double? Pos(ISensor s) { var v = V(s); return v.HasValue && v.Value > 0.01 ? v : null; }
        static double? Max(IEnumerable<ISensor> s)
        {
            var vals = s.Select(Pos).Where(x => x.HasValue).Select(x => x.Value).ToList();
            return vals.Count > 0 ? vals.Max() : (double?)null;
        }

        Dictionary<string, object> Sample(long nowMs)
        {
            tick++;
            if (tick == 8) DumpSensors();
            if (cpu != null) UpdateDeep(cpu);
            if (gpu != null) UpdateDeep(gpu);
            if (ram != null) ram.Update();
            if (vmem != null) vmem.Update();
            if (tick % 5 == 1) foreach (var d in dimms) UpdateDeep(d);
            foreach (var n in nics) UpdateDeep(n);
            if (tick % 3 == 1) foreach (var d in disks) UpdateDeep(d);
            foreach (var b in boards) UpdateDeep(b);

            // ---- CPU
            double? cLoad = V(cpuLoad), cTemp = Pos(cpuTemp), cPow = Pos(cpuPower);
            double? cClk = Pos(cpuClock);
            if (!cClk.HasValue && cpuCoreClocks.Count > 0)
            {
                var c = cpuCoreClocks.Select(Pos).Where(x => x.HasValue).Select(x => x.Value).ToList();
                if (c.Count > 0) cClk = c.Average();
            }
            if (!cClk.HasValue && pcCpuPerf != null && baseMhz > 0)
            {
                try { cClk = baseMhz * pcCpuPerf.NextValue() / 100.0; } catch { }
            }
            if (cTemp.HasValue && !cpuTempSeen) { cpuTempSeen = true; PublishInfo(); }

            // ---- GPU
            double? gL = V(gLoad), gT = Pos(gTemp), gP = Pos(gPower);
            double? vu = V(gVramUsed), vt = Pos(gVramTotal);
            // Integrated graphics (Radeon in a Ryzen, Intel UHD / Iris Xe) own only a small carve-out and live in
            // system RAM: memory is both parts, the temperature is the processor die they share, and their
            // power is already part of the processor package (so it is not counted twice).
            bool igpu = (vt ?? 0) < 1100 && Pos(gShTotal).HasValue;
            if (igpu)
            {
                vu = (vu ?? 0) + (V(gShUsed) ?? 0); vt = (vt ?? 0) + Pos(gShTotal);
                if (!gT.HasValue) gT = cTemp;
                gP = null;
            }
            double? vramPct = vu.HasValue && vt.HasValue ? 100.0 * vu.Value / vt.Value : (double?)null;
            double? fanRpm = gFans.Count > 0 ? (gFans.Select(V).Where(x => x.HasValue).Select(x => x.Value).DefaultIfEmpty(0).Max()) : (double?)null;
            double? fanPct = gFanCtl.Count > 0 ? (gFanCtl.Select(V).Where(x => x.HasValue).Select(x => x.Value).DefaultIfEmpty(0).Max()) : (double?)null;

            // ---- RAM
            double? used = V(rUsed), avail = V(rAvail);
            double? total = used.HasValue && avail.HasValue ? used + avail : null;
            // Per-stick SPD hub temperature; the DIMM also reports limits, which must not be mistaken for readings.
            var dimmList = new List<object>();
            double? rT = null, rWarn = null;
            foreach (var d in dimms.OrderBy(x => x.Identifier.ToString()))
            {
                var ts = All(d).Where(x => x.SensorType == SensorType.Temperature).ToList();
                var reading = ts.FirstOrDefault(x => x.Name.StartsWith("DIMM")) ?? ts.FirstOrDefault(x => !LimitName.IsMatch(x.Name));
                var limit = ts.FirstOrDefault(x => x.Name.Contains("High Limit") && !x.Name.Contains("Critical"));
                var v = Pos(reading);
                if (!v.HasValue) continue;
                rT = rT.HasValue ? Math.Max(rT.Value, v.Value) : v;
                var lim = Pos(limit);
                if (lim.HasValue) rWarn = rWarn.HasValue ? Math.Min(rWarn.Value, lim.Value) : lim;
                var slot = Regex.Match(reading.Name + " " + d.Name, @"#(\d+)");
                dimmList.Add(new Dictionary<string, object> { ["n"] = slot.Success ? slot.Groups[1].Value : (dimmList.Count + 1).ToString(), ["t"] = Json.R(v) });
            }
            if (rT.HasValue && !ramTempSeen) { ramTempSeen = true; PublishInfo(); }
            double? vU = V(vUsed), vA = V(vAvail);

            // ---- Disk (LHM storage needs admin; perf counters always work)
            double? dRead = null, dWrite = null, dAct = null, dT = null;
            if (disks.Count > 0)
            {
                var ds = disks.SelectMany(All).ToList();
                dT = Max(ds.Where(x => x.SensorType == SensorType.Temperature && (x.Name.StartsWith("Composite") || x.Name == "Temperature" || x.Name == "Temperature #1") && !LimitName.IsMatch(x.Name)));
                var r = ds.Where(x => x.SensorType == SensorType.Throughput && x.Name.StartsWith("Read")).Select(V).Where(x => x.HasValue).ToList();
                var w = ds.Where(x => x.SensorType == SensorType.Throughput && x.Name.StartsWith("Write")).Select(V).Where(x => x.HasValue).ToList();
                if (r.Count > 0) dRead = r.Sum(x => x.Value);
                if (w.Count > 0) dWrite = w.Sum(x => x.Value);
                dAct = Max(ds.Where(x => x.SensorType == SensorType.Load && x.Name.Contains("Activity")));
            }
            try
            {
                if (pcDiskRead != null && !dRead.HasValue) dRead = pcDiskRead.NextValue();
                if (pcDiskWrite != null && !dWrite.HasValue) dWrite = pcDiskWrite.NextValue();
                if (pcDiskIdle != null && !dAct.HasValue) dAct = MathX.Clamp(100 - pcDiskIdle.NextValue(), 0, 100);
            }
            catch { }
            if (dT.HasValue && !diskTempSeen) { diskTempSeen = true; PublishInfo(); }
            if (lastDrives < 0 || nowMs - lastDrives > 10000) { lastDrives = nowMs; drivesCache = ReadDrives(); }

            // ---- Network: follow whichever adapter actually carries traffic
            double? down = null, up = null;
            foreach (var n in nics)
            {
                var s = All(n).ToList();
                double dn = V(First(s, SensorType.Throughput, "Download Speed")) ?? 0, upv = V(First(s, SensorType.Throughput, "Upload Speed")) ?? 0;
                double acc; nicTraffic.TryGetValue(n, out acc);
                nicTraffic[n] = acc * 0.9 + dn + upv;
                if (n == nic) { down = dn; up = upv; }
            }
            if (tick % 5 == 0 && nicTraffic.Count > 0)
            {
                var best = nicTraffic.OrderByDescending(kv => kv.Value).First();
                if (best.Value > 1024 && best.Key != nic) { nic = best.Key; PublishInfo(); }
            }

            // ---- Fans: motherboard headers / AIO controllers, plus the graphics card's own fans
            var fans = new List<object>();
            double? fanMax = null;
            foreach (var hw in boards.Concat(gpu != null ? new[] { gpu } : new IHardware[0]))
            {
                var ss = All(hw).ToList();
                bool isGpu = hw == gpu;
                foreach (var f in ss.Where(x => x.SensorType == SensorType.Fan))
                {
                    var rpm = V(f);
                    var key = f.Identifier.ToString();
                    double seen;
                    fanSeen.TryGetValue(key, out seen);
                    if (rpm.HasValue && rpm.Value > seen) fanSeen[key] = seen = rpm.Value;
                    var ctl = ss.FirstOrDefault(x => x.SensorType == SensorType.Control && x.Name == f.Name)
                              ?? ss.FirstOrDefault(x => x.SensorType == SensorType.Control && x.Index == f.Index && !isGpu);
                    var pct = V(ctl);
                    // Unplugged headers read 0 rpm forever; GPU fans legitimately stop at idle.
                    if (seen < 1 && !isGpu) continue;
                    if (pct.HasValue) fanMax = Math.Max(fanMax ?? 0, pct.Value);
                    fans.Add(new Dictionary<string, object> { ["n"] = f.Name, ["src"] = isGpu ? "gpu" : "mb", ["rpm"] = Json.R(rpm, 0), ["pct"] = Json.R(pct, 0), ["id"] = key });
                }
            }

            var power = Power(cPow, gP, dAct, fans, nowMs);

            var unix = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
            // ---- video memory per process (WSL/Docker shows up as the VM worker process)
            if (tick % 3 == 1) gpuProcs = GpuProcesses(vu ?? 0);
            var llmSnap = Llm != null ? Llm.Snapshot : null;
            if (llmSnap != null)
            {
                llmSnap = new Dictionary<string, object>(llmSnap);
                var vm = gpuProcs.Select(Json.D).FirstOrDefault(x => Json.Str(x, "n") == "vmwp");
                if (vm != null) llmSnap["vram"] = vm["v"];
            }
            var netQ = NetQ != null ? NetQ.Snapshot : null;
            double? ping = null;
            if (netQ != null)
            {
                var target = Json.D(Json.Get(netQ, Json.Str(netQ, "target") == "isp" ? "isp" : "inet"));
                if (target != null && Json.Get(target, "rtt") != null) ping = Json.Num(target, "rtt");
            }
            var bat = Battery.Sample(nowMs);
            History.Push(unix, new double?[] { cLoad, cTemp, cPow, gL, gT, gP, vramPct, V(rLoad), rT, dAct, dT, dRead, dWrite, down, up,
                llmSnap != null && Json.Get(llmSnap, "tps") != null ? Json.Num(llmSnap, "tps") : (double?)null, ping, fanMax, Json.Num(power, "wall"),
                bat != null && Json.Get(bat, "pct") != null ? Json.Num(bat, "pct") : (double?)null, bat != null && Json.Get(bat, "rate") != null ? Json.Num(bat, "rate") : (double?)null });

            var snap = new Dictionary<string, object>
            {
                ["type"] = "tick",
                ["t"] = unix,
                ["iv"] = IntervalMs,
                ["up"] = (long)(Native.GetTickCount64() / 1000),
                ["cpu"] = new Dictionary<string, object>
                {
                    ["load"] = Json.R(cLoad), ["temp"] = Json.R(cTemp), ["clock"] = Json.R(cClk, 0), ["power"] = Json.R(cPow),
                    ["threads"] = cpuThreads.Select(x => (int)Math.Round(V(x) ?? 0)).ToArray(),
                    ["ccd"] = ccdTemps.Select(x => Json.R(Pos(x))).ToArray(),
                },
                ["gpu"] = new Dictionary<string, object>
                {
                    ["load"] = Json.R(gL), ["temp"] = Json.R(gT), ["hot"] = Json.R(Pos(gHot)), ["memT"] = Json.R(Pos(gMemT)),
                    ["clock"] = Json.R(V(gClock), 0), ["memClock"] = Json.R(V(gMemClock), 0), ["power"] = Json.R(gP),
                    ["fan"] = Json.R(fanRpm, 0), ["fanPct"] = Json.R(fanPct, 0),
                    ["vu"] = Json.R(vu, 0), ["vt"] = Json.R(vt, 0), ["vram"] = Json.R(vramPct), ["igpu"] = igpu,
                    ["video"] = Json.R(V(gVideo)), ["mc"] = Json.R(V(gMemCtl)), ["procs"] = gpuProcs,
                },
                ["ram"] = new Dictionary<string, object>
                {
                    ["load"] = Json.R(V(rLoad)), ["used"] = Json.R(used, 2), ["avail"] = Json.R(avail, 2), ["total"] = Json.R(total, 1),
                    ["temp"] = Json.R(rT), ["warn"] = Json.R(rWarn, 0), ["dimms"] = dimmList, ["vload"] = Json.R(V(vLoad)), ["vused"] = Json.R(vU, 1), ["vtotal"] = Json.R(vU.HasValue && vA.HasValue ? vU + vA : null, 1),
                },
                ["disk"] = new Dictionary<string, object>
                {
                    ["act"] = Json.R(dAct), ["read"] = Json.R(dRead, 0), ["write"] = Json.R(dWrite, 0), ["temp"] = Json.R(dT), ["drives"] = drivesCache,
                },
                ["net"] = new Dictionary<string, object> { ["down"] = Json.R(down, 0), ["up"] = Json.R(up, 0), ["name"] = nic != null ? nic.Name : null, ["q"] = netQ },
                ["llm"] = llmSnap,
                ["fans"] = new Dictionary<string, object> { ["max"] = Json.R(fanMax, 0), ["list"] = fans },
                ["power"] = power,
                ["battery"] = bat,
            };

            // widgets that list processes want them every 2 s; the anomaly checks are fine with every 30 s
            if ((WantProcs || WantProcsSlow) && (lastProcs < 0 || nowMs - lastProcs >= (WantProcs ? 2000 : 30000)))
            {
                lastProcs = nowMs;
                try
                {
                    Dictionary<string, object> icons;
                    snap["procs"] = procs.Scan(out icons);
                    if (icons.Count > 0) snap["icons"] = icons;
                }
                catch (Exception ex) { Log.Error("procs", ex); }
            }
            return snap;
        }

        static object ReadDrives()
        {
            var list = new List<object>();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                {
                    try
                    {
                        if (!d.IsReady || d.DriveType != DriveType.Fixed) continue;
                        list.Add(new Dictionary<string, object>
                        {
                            ["n"] = d.Name.TrimEnd('\\'), ["label"] = d.VolumeLabel,
                            ["total"] = Math.Round(d.TotalSize / 1073741824.0, 1), ["free"] = Math.Round(d.TotalFreeSpace / 1073741824.0, 1),
                        });
                    }
                    catch { }
                }
            }
            catch { }
            return list;
        }

        public void Dispose()
        {
            running = false;
            wake.Set();
            try { if (thread != null) thread.Join(1500); } catch { }
            SaveEnergy();
            try { if (pc != null) pc.Close(); } catch { }
        }
    }

    /// <summary>Fixed-size ring buffer of samples shared by every chart, so a widget opened later still has history.</summary>
    sealed class HistoryBuffer
    {
        readonly string[] keys;
        readonly int cap;
        readonly float[][] data;
        readonly long[] times;
        int count, head;
        readonly object gate = new object();

        public HistoryBuffer(string[] keys, int cap)
        {
            this.keys = keys; this.cap = cap;
            data = keys.Select(k => new float[cap]).ToArray();
            times = new long[cap];
        }

        public void Push(long t, double?[] vals)
        {
            lock (gate)
            {
                times[head] = t;
                for (int i = 0; i < keys.Length; i++) data[i][head] = vals[i].HasValue ? (float)vals[i].Value : float.NaN;
                head = (head + 1) % cap;
                if (count < cap) count++;
            }
        }

        IEnumerable<int> Indices(int max)
        {
            int n = Math.Min(count, max);
            for (int i = n; i > 0; i--) yield return ((head - i) % cap + cap) % cap;
        }

        public Dictionary<string, object> Export(int max)
        {
            lock (gate)
            {
                var idx = Indices(max).ToList();
                var series = new Dictionary<string, object>();
                for (int k = 0; k < keys.Length; k++)
                {
                    var arr = data[k];
                    series[keys[k]] = idx.Select(i => float.IsNaN(arr[i]) ? null : (object)Math.Round(arr[i], 1)).ToList();
                }
                return new Dictionary<string, object> { ["type"] = "history", ["t"] = idx.Select(i => times[i]).ToList(), ["data"] = series };
            }
        }

        public string Csv()
        {
            lock (gate)
            {
                var sb = new StringBuilder();
                sb.Append("time;").AppendLine(string.Join(";", keys));
                var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                foreach (var i in Indices(cap))
                {
                    sb.Append(epoch.AddMilliseconds(times[i]).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                    for (int k = 0; k < keys.Length; k++)
                    {
                        sb.Append(';');
                        if (!float.IsNaN(data[k][i])) sb.Append(Math.Round(data[k][i], 2).ToString(System.Globalization.CultureInfo.CurrentCulture));
                    }
                    sb.AppendLine();
                }
                return sb.ToString();
            }
        }
    }
}
