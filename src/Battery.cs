using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace Pulse
{
    /// <summary>
    /// Laptop battery: charge, state (charging / on battery / full / held below full), power in and
    /// out, time left or to full, wear against the design capacity and charge cycles.
    /// GetSystemPowerStatus runs every tick (free); the battery driver's WMI classes are read every
    /// few seconds, the static data once a minute. Several batteries are summed.
    /// </summary>
    sealed class BatteryService
    {
        /// <summary>"--fake-battery": synthesized data for designing the module on a desktop.</summary>
        public static bool Fake;

        long lastStatus = long.MinValue, lastStatic = long.MinValue;
        double? remWh, fullWh, designWh, rateW, volt;
        bool? charging, discharging;
        int? cycles;
        string name, maker, chem;
        bool present = true;

        public Dictionary<string, object> Sample(long nowMs)
        {
            if (Fake) return FakeSample(nowMs);
            Native.SYSTEM_POWER_STATUS ps;
            if (!Native.GetSystemPowerStatus(out ps)) return null;
            if (ps.BatteryFlag == 128 || ps.BatteryFlag == 255) return null; // no system battery / unknown
            if (nowMs - lastStatic >= 60000) { lastStatic = nowMs; ReadStatic(); }
            if (!present) return null;
            if (nowMs - lastStatus >= 3000) { lastStatus = nowMs; ReadStatus(); }

            bool ac = ps.ACLineStatus == 1;
            double pct = ps.BatteryLifePercent <= 100 ? ps.BatteryLifePercent : (remWh.HasValue && fullWh > 0 ? remWh.Value / fullWh.Value * 100 : double.NaN);
            if (remWh.HasValue && fullWh > 0) pct = Math.Min(100, remWh.Value / fullWh.Value * 100);
            bool isCharging = charging ?? ((ps.BatteryFlag & 8) != 0);
            string state = isCharging ? "charging" : !ac ? "discharging" : pct >= 97 ? "full" : "hold";

            double? time = null;
            if (state == "discharging")
            {
                if (ps.BatteryLifeTime > 0) time = ps.BatteryLifeTime;
                else if (rateW > 0.3 && remWh.HasValue) time = remWh.Value / rateW.Value * 3600;
            }
            else if (state == "charging" && rateW > 0.3 && remWh.HasValue && fullWh.HasValue)
                time = Math.Max(0, fullWh.Value - remWh.Value) / rateW.Value * 3600 * 1.15; // the last percent charges slower

            double? health = designWh > 0 && fullWh > 0 ? Math.Min(100, fullWh.Value / designWh.Value * 100) : (double?)null;
            return new Dictionary<string, object>
            {
                ["pct"] = Json.R(double.IsNaN(pct) ? (double?)null : pct, 1), ["state"] = state, ["ac"] = ac, ["saver"] = ps.SystemStatusFlag == 1,
                ["rate"] = Json.R(state == "full" || state == "hold" ? 0 : rateW, 1), ["time"] = time.HasValue ? (object)Math.Round(time.Value) : null,
                ["health"] = Json.R(health, 1), ["cycles"] = cycles > 0 ? (object)cycles.Value : null, ["volt"] = Json.R(volt, 2),
                ["rem"] = Json.R(remWh, 1), ["full"] = Json.R(fullWh, 1), ["design"] = Json.R(designWh, 1),
                ["name"] = name, ["maker"] = maker, ["chem"] = chem,
            };
        }

        void ReadStatic()
        {
            try
            {
                double design = 0, full = 0; int cyc = 0; int n = 0;
                string nm = null, mk = null, ch = null;
                foreach (ManagementObject o in Query("SELECT DesignedCapacity, DeviceName, ManufactureName, Chemistry FROM BatteryStaticData"))
                {
                    n++;
                    design += Convert.ToDouble(o["DesignedCapacity"] ?? 0) / 1000;
                    nm = nm ?? Clean(o["DeviceName"] as string);
                    mk = mk ?? Clean(o["ManufactureName"] as string);
                    if (ch == null && o["Chemistry"] != null) ch = Chemistry(Convert.ToUInt32(o["Chemistry"]));
                }
                foreach (ManagementObject o in Query("SELECT FullChargedCapacity FROM BatteryFullChargedCapacity")) full += Convert.ToDouble(o["FullChargedCapacity"] ?? 0) / 1000;
                try { foreach (ManagementObject o in Query("SELECT CycleCount FROM BatteryCycleCount")) cyc += Convert.ToInt32(o["CycleCount"] ?? 0); } catch { }
                present = n > 0 || full > 0;
                designWh = design > 0 ? design : (double?)null;
                fullWh = full > 0 ? full : (double?)null;
                cycles = cyc > 0 ? cyc : (int?)null;
                name = nm; maker = mk; chem = ch;
            }
            catch (Exception ex) { Log.Write("battery static: " + ex.Message); }
        }

        void ReadStatus()
        {
            try
            {
                double rem = 0, rateIn = 0, rateOut = 0, v = 0; int n = 0; bool chg = false, dis = false;
                foreach (ManagementObject o in Query("SELECT RemainingCapacity, ChargeRate, DischargeRate, Voltage, Charging, Discharging FROM BatteryStatus"))
                {
                    n++;
                    rem += Convert.ToDouble(o["RemainingCapacity"] ?? 0) / 1000;
                    rateIn += Math.Abs(Convert.ToDouble(o["ChargeRate"] ?? 0)) / 1000;
                    rateOut += Math.Abs(Convert.ToDouble(o["DischargeRate"] ?? 0)) / 1000;
                    v = Math.Max(v, Convert.ToDouble(o["Voltage"] ?? 0) / 1000);
                    chg |= o["Charging"] is bool && (bool)o["Charging"];
                    dis |= o["Discharging"] is bool && (bool)o["Discharging"];
                }
                if (n == 0) return;
                remWh = rem > 0 ? rem : (double?)null;
                charging = chg; discharging = dis;
                rateW = chg ? rateIn : dis ? rateOut : 0;
                volt = v > 0 ? v : (double?)null;
            }
            catch (Exception ex) { Log.Write("battery status: " + ex.Message); }
        }

        static ManagementObjectCollection Query(string q)
        {
            using (var s = new ManagementObjectSearcher(@"root\wmi", q)) return s.Get();
        }

        static string Clean(string s) { s = (s ?? "").Trim(); return s.Length == 0 ? null : s; }

        /// <summary>The driver reports chemistry as four ASCII characters packed into a number.</summary>
        static string Chemistry(uint c)
        {
            var b = BitConverter.GetBytes(c);
            var s = new string(b.Select(x => (char)x).Where(ch => ch >= 32 && ch < 127).ToArray()).Trim().ToUpperInvariant();
            if (s.Length == 0) return null;
            if (s.StartsWith("LI") && s.Contains("P")) return "Li-Po";
            if (s.StartsWith("LI")) return "Li-ion";
            if (s == "NIMH") return "NiMH";
            if (s == "NICD") return "NiCd";
            return s;
        }

        // ---------------------------------------------------------------- design aid

        /// <summary>A 45 Wh pack cycling between charging and running on battery.</summary>
        static Dictionary<string, object> FakeSample(long nowMs)
        {
            double t = nowMs / 1000.0;
            double phase = (t % 240) / 240;                    // 4-minute loop: 2 min discharge, 2 min charge
            bool chargingNow = phase >= .5;
            double pct = chargingNow ? 18 + (phase - .5) * 2 * 74 : 92 - phase * 2 * 74;
            double rate = chargingNow ? 46 - (pct - 18) * .35 + Math.Sin(t / 3) * 1.5 : 11.5 + Math.Sin(t / 4) * 4 + (Math.Sin(t / 17) > .6 ? 9 : 0);
            double full = 45.2, design = 50.0, rem = full * pct / 100;
            double time = chargingNow ? (full - rem) / rate * 3600 * 1.15 : rem / rate * 3600;
            return new Dictionary<string, object>
            {
                ["pct"] = Json.R(pct, 1), ["state"] = chargingNow ? "charging" : "discharging", ["ac"] = chargingNow, ["saver"] = false,
                ["rate"] = Json.R(rate, 1), ["time"] = Math.Round(time), ["health"] = Json.R(full / design * 100, 1), ["cycles"] = 312,
                ["volt"] = Json.R(11.4 + pct / 100 * 1.6, 2), ["rem"] = Json.R(rem, 1), ["full"] = full, ["design"] = design,
                ["name"] = "5B10W13930", ["maker"] = "SMP", ["chem"] = "Li-Po",
            };
        }
    }
}
