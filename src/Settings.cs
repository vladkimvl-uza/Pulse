using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Pulse
{
    /// <summary>
    /// Settings are a JSON tree shared verbatim with the UI. ui/defaults.json is the single source
    /// of defaults; the user file only has to hold what differs. Paths look like "global.theme" or
    /// "widgets.&lt;id&gt;.mode" (widgets are addressed by id, not index).
    /// </summary>
    sealed class SettingsStore
    {
        public Dictionary<string, object> Root { get; private set; }
        Dictionary<string, object> defaults;
        System.Windows.Forms.Timer saveTimer;
        public bool FirstRun { get; private set; }

        public Dictionary<string, object> Global { get { return Json.D(Root["global"]); } }
        public List<object> Widgets { get { return Json.L(Root["widgets"]); } }
        public Dictionary<string, object> WidgetDefaults { get { return Json.D(defaults["widgetDefaults"]); } }

        public void Load()
        {
            defaults = Json.D(Json.Parse(File.ReadAllText(Path.Combine(Paths.UiDir, "defaults.json"), Encoding.UTF8)));
            Dictionary<string, object> user = null;
            try
            {
                if (File.Exists(Paths.SettingsFile)) user = Json.D(Json.Parse(File.ReadAllText(Paths.SettingsFile, Encoding.UTF8)));
            }
            catch (Exception ex)
            {
                Log.Error("settings load", ex);
                try { File.Copy(Paths.SettingsFile, Paths.SettingsFile + ".broken", true); } catch { }
            }
            FirstRun = user == null;
            Root = user ?? new Dictionary<string, object>();
            if (!(Root.ContainsKey("global") && Root["global"] is Dictionary<string, object>)) Root["global"] = new Dictionary<string, object>();
            Json.Merge(Global, Json.D(defaults["global"]));
            if (!(Root.ContainsKey("widgets") && Root["widgets"] is List<object>)) Root["widgets"] = new List<object>();
            Widgets.RemoveAll(w => !(w is Dictionary<string, object>));
            foreach (var w in Widgets.Select(Json.D)) NormalizeWidget(w);
            if (Widgets.Count == 0) Widgets.Add(NewWidget());
            Root["version"] = defaults["version"];
        }

        public Dictionary<string, object> NewWidget()
        {
            var w = Json.D(Json.Clone(WidgetDefaults));
            w["id"] = Guid.NewGuid().ToString("N").Substring(0, 8);
            return w;
        }

        void NormalizeWidget(Dictionary<string, object> w)
        {
            if (Json.Str(w, "id") == null) w["id"] = Guid.NewGuid().ToString("N").Substring(0, 8);
            Json.Merge(w, WidgetDefaults);
            // Keep every known module present so the UI can toggle it; preserve the user's order.
            var mods = Json.L(w["modules"]) ?? new List<object>();
            var known = Json.L(WidgetDefaults["modules"]).Select(m => Json.Str(Json.D(m), "id")).ToList();
            mods.RemoveAll(m => Json.D(m) == null || !known.Contains(Json.Str(Json.D(m), "id")));
            foreach (var def in Json.L(WidgetDefaults["modules"]))
            {
                var id = Json.Str(Json.D(def), "id");
                var have = mods.Select(Json.D).FirstOrDefault(m => Json.Str(m, "id") == id);
                if (have == null) { var c = Json.D(Json.Clone(def)); c["on"] = false; mods.Add(c); }
                else Json.Merge(have, Json.D(def));
            }
            w["modules"] = mods;
        }

        /// <summary>Once per device type: on a laptop the first widget gets the battery module right after memory.</summary>
        public void ApplyDevice(string kind)
        {
            if (Json.Str(Global, "deviceSetup") == kind) return;
            Global["deviceSetup"] = kind;
            if (kind == "laptop" && Widgets.Count > 0)
            {
                var mods = Json.L(Json.D(Widgets[0])["modules"]);
                var bat = mods.Select(Json.D).FirstOrDefault(m => Json.Str(m, "id") == "battery");
                if (bat != null)
                {
                    bat["on"] = true;
                    mods.Remove(bat);
                    int ram = mods.FindIndex(m => Json.Str(Json.D(m), "id") == "ram");
                    mods.Insert(ram >= 0 ? ram + 1 : mods.Count, bat);
                }
            }
            SaveSoon();
        }

        public Dictionary<string, object> Widget(string id)
        {
            return Widgets.Select(Json.D).FirstOrDefault(w => Json.Str(w, "id") == id);
        }

        public object Get(string path)
        {
            object cur = Root;
            foreach (var part in path.Split('.'))
            {
                cur = Step(cur, part);
                if (cur == null) return null;
            }
            return cur;
        }

        public void Set(string path, object value)
        {
            var parts = path.Split('.');
            object cur = Root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var next = Step(cur, parts[i]);
                if (next == null)
                {
                    var d = Json.D(cur);
                    if (d == null) throw new ArgumentException("bad settings path " + path);
                    next = d[parts[i]] = new Dictionary<string, object>();
                }
                cur = next;
            }
            var target = Json.D(cur);
            if (target == null) throw new ArgumentException("bad settings path " + path);
            target[parts[parts.Length - 1]] = value;
            SaveSoon();
        }

        object Step(object cur, string part)
        {
            var d = Json.D(cur);
            if (d != null) { object v; return d.TryGetValue(part, out v) ? v : null; }
            var l = Json.L(cur);
            if (l != null) return l.Select(Json.D).FirstOrDefault(x => x != null && Json.Str(x, "id") == part);
            return null;
        }

        public void ResetToDefaults()
        {
            var first = Widgets.Select(Json.D).FirstOrDefault();
            Root["global"] = Json.Clone(defaults["global"]);
            var fresh = NewWidget();
            // Keep the first widget's id and position so a reset does not throw it across the screen.
            if (first != null) { fresh["id"] = first["id"]; fresh["x"] = Json.Get(first, "x"); fresh["y"] = Json.Get(first, "y"); }
            Root["widgets"] = new List<object> { fresh };
            SaveSoon();
        }

        public void SaveSoon()
        {
            if (saveTimer == null)
            {
                saveTimer = new System.Windows.Forms.Timer { Interval = 600 };
                saveTimer.Tick += (s, e) => { saveTimer.Stop(); SaveNow(); };
            }
            saveTimer.Stop();
            saveTimer.Start();
        }

        public void SaveNow()
        {
            try
            {
                var tmp = Paths.SettingsFile + ".tmp";
                File.WriteAllText(tmp, Json.Write(Root), new UTF8Encoding(false));
                if (File.Exists(Paths.SettingsFile)) File.Replace(tmp, Paths.SettingsFile, null);
                else File.Move(tmp, Paths.SettingsFile);
            }
            catch (Exception ex) { Log.Error("settings save", ex); }
        }
    }
}
