using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Pulse
{
    /// <summary>
    /// Laptop or desktop, and the computer's maker and model. Shared by the installer and the app;
    /// reads only the firmware's power profile and the BIOS strings Windows keeps in the registry.
    /// </summary>
    static class Device
    {
        [DllImport("powrprof.dll")] static extern int PowerDeterminePlatformRoleEx(uint version);

        /// <summary>"laptop" for the mobile / slate firmware profile (or an unspecified one with a battery), else "desktop".</summary>
        public static string Kind()
        {
            int role = 0;
            try { role = PowerDeterminePlatformRoleEx(2); } catch { }
            if (role == 2 || role == 8) return "laptop";   // PlatformRoleMobile, PlatformRoleSlate
            if (role == 0 && HasBattery()) return "laptop"; // unspecified profile but a system battery
            return "desktop";                               // a desktop on a UPS still reports a desktop profile
        }

        public static bool HasBattery()
        {
            Native.SYSTEM_POWER_STATUS ps;
            return Native.GetSystemPowerStatus(out ps) && ps.BatteryFlag != 128 && ps.BatteryFlag != 255;
        }

        /// <summary>Maker and model as people know them, e.g. ("Lenovo", "ThinkPad X1 Carbon Gen 10").</summary>
        public static Dictionary<string, object> Model()
        {
            string mfr = null, product = null, version = null, family = null, boardMfr = null, board = null;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS"))
                {
                    if (k != null)
                    {
                        mfr = Real(k.GetValue("SystemManufacturer") as string);
                        product = Real(k.GetValue("SystemProductName") as string);
                        version = Real(k.GetValue("SystemVersion") as string);
                        family = Real(k.GetValue("SystemFamily") as string);
                        boardMfr = Real(k.GetValue("BaseBoardManufacturer") as string);
                        board = Real(k.GetValue("BaseBoardProduct") as string);
                    }
                }
            }
            catch { }
            var vendor = Vendor(mfr);
            string model = product;
            // Lenovo keeps the marketing name in SystemVersion / SystemFamily; the product name is a machine type code.
            if (vendor == "Lenovo") model = Pick(version, family) ?? product;
            else if (model != null && family != null && Regex.IsMatch(model, @"^[A-Z0-9]{4,12}$") && Regex.IsMatch(family, "[a-z]")) model = family;
            // Self-built PCs: the "product" is a board code (MS-7E86) or a placeholder; the board name says more.
            if (board != null && board.Contains(" ") && (model == null || !model.Contains(" ")))
            {
                model = Regex.Replace(board, @"\s*\(MS-\w+\)\s*$", "");
                vendor = Vendor(boardMfr) ?? vendor;
            }
            if (model != null)
            {
                model = Regex.Replace(model, @"_([A-Z0-9]+)$", m => model.Contains(m.Groups[1].Value + "_") || model.Contains(" " + m.Groups[1].Value) ? "" : m.Value); // "G614JV_G614JV"
                if (vendor != null && model.StartsWith(vendor + " ", StringComparison.OrdinalIgnoreCase)) model = model.Substring(vendor.Length + 1);
                model = Regex.Replace(model, @"\s{2,}", " ").Trim();
            }
            return new Dictionary<string, object> { ["vendor"] = vendor ?? mfr, ["model"] = model };
        }

        static string Pick(params string[] xs)
        {
            foreach (var x in xs) if (x != null && Regex.IsMatch(x, "[a-zA-Z]{3,}") && !x.Equals("Lenovo", StringComparison.OrdinalIgnoreCase)) return x;
            return null;
        }

        /// <summary>Drops the placeholders board makers leave in unbranded firmware.</summary>
        static string Real(string s)
        {
            s = (s ?? "").Trim();
            if (s.Length < 2) return null;
            if (Regex.IsMatch(s, @"^(To Be Filled|System (Product )?Name|System manufacturer|Default string|Not Applicable|None|O\.?E\.?M\.?|Type1ProductConfigId|Undefined|x\.x|0+|1\.0|Version|INVALID)", RegexOptions.IgnoreCase)) return null;
            return s;
        }

        static readonly string[][] Vendors =
        {
            new[] { "lenovo", "Lenovo" }, new[] { "asus", "ASUS" }, new[] { "hewlett|^hp\\b", "HP" }, new[] { "dell", "Dell" },
            new[] { "micro-star|^msi\\b", "MSI" }, new[] { "acer", "Acer" }, new[] { "apple", "Apple" }, new[] { "huawei", "Huawei" },
            new[] { "honor", "Honor" }, new[] { "xiaomi|timi", "Xiaomi" }, new[] { "microsoft", "Microsoft" }, new[] { "razer", "Razer" },
            new[] { "samsung", "Samsung" }, new[] { "gigabyte|aorus", "Gigabyte" }, new[] { "lg electronics|^lg\\b", "LG" }, new[] { "toshiba|dynabook", "Dynabook" },
            new[] { "fujitsu", "Fujitsu" }, new[] { "chuwi", "Chuwi" }, new[] { "infinix", "Infinix" }, new[] { "tecno", "Tecno" },
            new[] { "machenike", "Machenike" }, new[] { "thunderobot", "Thunderobot" }, new[] { "maibenben", "Maibenben" }, new[] { "medion", "Medion" },
            new[] { "framework", "Framework" }, new[] { "gpd", "GPD" }, new[] { "valve", "Valve" }, new[] { "panasonic", "Panasonic" },
            new[] { "vaio|sony", "VAIO" }, new[] { "clevo", "Clevo" }, new[] { "tongfang", "Tongfang" },
        };

        static string Vendor(string mfr)
        {
            if (mfr == null) return null;
            foreach (var v in Vendors) if (Regex.IsMatch(mfr, v[0], RegexOptions.IgnoreCase)) return v[1];
            return Regex.Replace(mfr, @"\s*(Co\.,?\s*)?(Ltd\.?|Inc\.?|Corporation|Corp\.?|GmbH|Limited|Computer|Technology|International)\b\.?", "", RegexOptions.IgnoreCase).Trim(' ', ',', '.');
        }
    }
}
