using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace Pulse
{
    static class Json
    {
        static readonly JavaScriptSerializer S = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };

        public static string Write(object o) { return S.Serialize(o); }

        /// <summary>Parses JSON into Dictionary/List/primitive trees (arrays become List&lt;object&gt; so they can be edited).</summary>
        public static object Parse(string s) { return Normalize(S.DeserializeObject(s)); }

        static object Normalize(object o)
        {
            var d = o as Dictionary<string, object>;
            if (d != null)
            {
                foreach (var k in d.Keys.ToList()) d[k] = Normalize(d[k]);
                return d;
            }
            var a = o as object[];
            if (a != null) return a.Select(Normalize).ToList();
            return o;
        }

        public static object Clone(object o) { return Parse(Write(o)); }

        public static Dictionary<string, object> D(object o) { return o as Dictionary<string, object>; }
        public static List<object> L(object o) { return o as List<object>; }

        public static object Get(Dictionary<string, object> d, string k) { object v; return d != null && d.TryGetValue(k, out v) ? v : null; }
        public static string Str(Dictionary<string, object> d, string k, string def = null) { var v = Get(d, k); return v == null ? def : Convert.ToString(v, CultureInfo.InvariantCulture); }
        public static double Num(Dictionary<string, object> d, string k, double def = 0)
        {
            var v = Get(d, k);
            if (v == null || v is string || v is bool) return def;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return def; }
        }
        public static bool Bool(Dictionary<string, object> d, string k, bool def = false) { var v = Get(d, k); return v is bool ? (bool)v : def; }

        /// <summary>Recursively fills keys missing in <paramref name="target"/> from <paramref name="defaults"/>.</summary>
        public static void Merge(Dictionary<string, object> target, Dictionary<string, object> defaults)
        {
            foreach (var kv in defaults)
            {
                object cur;
                if (!target.TryGetValue(kv.Key, out cur) || cur == null) target[kv.Key] = Clone(kv.Value);
                else if (cur is Dictionary<string, object> && kv.Value is Dictionary<string, object>)
                    Merge((Dictionary<string, object>)cur, (Dictionary<string, object>)kv.Value);
            }
        }

        /// <summary>Rounds for the wire; NaN/inf/null become null (JavaScriptSerializer would emit invalid "NaN").</summary>
        public static object R(double? v, int digits = 1)
        {
            if (!v.HasValue || double.IsNaN(v.Value) || double.IsInfinity(v.Value)) return null;
            return Math.Round(v.Value, digits);
        }
    }

    static class Log
    {
        static readonly object Gate = new object();
        public static string Path;

        public static void Write(string msg)
        {
            try
            {
                lock (Gate)
                {
                    if (Path == null) return;
                    var fi = new FileInfo(Path);
                    if (fi.Exists && fi.Length > 1024 * 1024) File.Delete(Path);
                    File.AppendAllText(Path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + msg + Environment.NewLine);
                }
            }
            catch { }
        }

        public static void Error(string where, Exception ex) { Write("ERROR " + where + ": " + ex); }
    }

    static class Paths
    {
        public static readonly string AppDir = AppDomain.CurrentDomain.BaseDirectory;
        public static readonly string Exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
        public static readonly string UiDir = System.IO.Path.Combine(AppDir, "ui");
        public static string Profile = "";
        public static readonly string RoamingBase = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse");
        public static string Roaming = RoamingBase;
        public static readonly string LocalBase = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pulse");
        public static string Local = LocalBase;
        public static string Cache = System.IO.Path.Combine(Local, "cache");
        public static string WebData = System.IO.Path.Combine(Local, "WebView2");
        public static string SettingsFile = System.IO.Path.Combine(Roaming, "settings.json");

        /// <summary>A named profile keeps a test instance's data apart from the real one.</summary>
        public static void UseProfile(string name)
        {
            Profile = name;
            Roaming = RoamingBase + "-" + name;
            Local = LocalBase + "-" + name;
            Cache = System.IO.Path.Combine(Local, "cache");
            WebData = System.IO.Path.Combine(Local, "WebView2");
            SettingsFile = System.IO.Path.Combine(Roaming, "settings.json");
        }

        public static void Ensure()
        {
            Directory.CreateDirectory(Roaming);
            Directory.CreateDirectory(Cache);
            Directory.CreateDirectory(WebData);
            Log.Path = System.IO.Path.Combine(Local, "pulse.log");
        }
    }

    static class MathX
    {
        public static double Clamp(double v, double a, double b) { return v < a ? a : v > b ? b : v; }
        public static int Clamp(int v, int a, int b) { return v < a ? a : v > b ? b : v; }
    }
}
