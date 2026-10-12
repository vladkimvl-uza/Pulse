using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Pulse
{
    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId, [MarshalAs(UnmanagedType.LPWStr)] string wallpaper);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetMonitorDevicePathAt(uint index);
        uint GetMonitorDevicePathCount();
        Native.RECT GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)] string monitorId);
        void SetBackgroundColor(uint color);
        uint GetBackgroundColor();
        void SetPosition(int position);
        int GetPosition();
    }

    [ComImport, Guid("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD")] class DesktopWallpaperCoClass { }

    /// <summary>
    /// The widget's frosted glass is a blurred copy of the wallpaper aligned to the window's screen
    /// position (the same idea as Windows 11 Mica). Here we find each monitor's wallpaper and keep a
    /// small JPEG in the cache folder that the page loads from https://pulse.cache/.
    /// </summary>
    sealed class WallpaperService
    {
        sealed class Mon { public Rectangle Rect; public string Source; public string Url; public string Avg; public int W, H; }

        List<Mon> mons = new List<Mon>();
        string mode = "fill";
        string bg = "#1c1c1e";
        string signature = "";
        public event Action Changed;

        static readonly string[] Modes = { "center", "tile", "stretch", "fit", "fill", "span" };

        public void Refresh()
        {
            try
            {
                var found = Discover();
                var sig = string.Join("|", found.Select(m => m.Rect + "=" + m.Source + "@" + Stamp(m.Source))) + mode + bg;
                if (sig == signature) return;
                signature = sig;
                int ver = sig.GetHashCode() & 0x7fffffff;
                foreach (var m in found) Prepare(m, ver);
                mons = found;
                CleanCache();
                var h = Changed;
                if (h != null) h();
            }
            catch (Exception ex) { Log.Error("wallpaper", ex); }
        }

        static string Stamp(string path)
        {
            try { var fi = new FileInfo(path); return fi.Exists ? fi.LastWriteTimeUtc.Ticks + ":" + fi.Length : "missing"; }
            catch { return "?"; }
        }

        List<Mon> Discover()
        {
            var result = new List<Mon>();
            var themes = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes");
            try
            {
                var dw = (IDesktopWallpaper)new DesktopWallpaperCoClass();
                try
                {
                    int pos = dw.GetPosition();
                    mode = pos >= 0 && pos < Modes.Length ? Modes[pos] : "fill";
                    uint c = dw.GetBackgroundColor(); // COLORREF 0x00BBGGRR
                    bg = string.Format("#{0:x2}{1:x2}{2:x2}", c & 0xFF, (c >> 8) & 0xFF, (c >> 16) & 0xFF);
                    uint n = dw.GetMonitorDevicePathCount();
                    for (uint i = 0; i < n; i++)
                    {
                        string id = dw.GetMonitorDevicePathAt(i);
                        Native.RECT r;
                        try { r = dw.GetMonitorRECT(id); } catch { continue; } // detached monitor
                        string path = null;
                        try { path = dw.GetWallpaper(id); } catch { }
                        if (string.IsNullOrEmpty(path) || !File.Exists(path))
                        {
                            var per = Path.Combine(themes, "Transcoded_" + i.ToString("000"));
                            path = File.Exists(per) ? per : Path.Combine(themes, "TranscodedWallpaper");
                        }
                        result.Add(new Mon { Rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), Source = path });
                    }
                }
                finally { Marshal.ReleaseComObject(dw); }
            }
            catch (Exception ex) { Log.Write("IDesktopWallpaper: " + ex.Message); }

            if (result.Count == 0)
            {
                var path = Path.Combine(themes, "TranscodedWallpaper");
                foreach (var s in Screen.AllScreens) result.Add(new Mon { Rect = s.Bounds, Source = path });
            }
            return result;
        }

        static void Prepare(Mon m, int ver)
        {
            if (!File.Exists(m.Source)) return;
            try
            {
                // The page blurs it heavily anyway, so a ~1280px copy is plenty and keeps GPU memory low.
                using (var src = Image.FromStream(new MemoryStream(File.ReadAllBytes(m.Source))))
                {
                    double k = Math.Min(1.0, 1280.0 / Math.Max(src.Width, src.Height));
                    int w = Math.Max(1, (int)(src.Width * k)), h = Math.Max(1, (int)(src.Height * k));
                    using (var bmp = new Bitmap(w, h))
                    {
                        using (var g = Graphics.FromImage(bmp))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(src, new Rectangle(0, 0, w, h));
                        }
                        var name = "wall_" + ver + "_" + (m.Source.GetHashCode() & 0x7fffffff) + ".jpg";
                        var enc = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
                        var ps = new EncoderParameters(1);
                        ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 82L);
                        bmp.Save(Path.Combine(Paths.Cache, name), enc, ps);
                        m.Url = "https://pulse.cache/" + name;
                        using (var one = new Bitmap(1, 1))
                        {
                            using (var g1 = Graphics.FromImage(one)) { g1.InterpolationMode = InterpolationMode.HighQualityBilinear; g1.DrawImage(bmp, new Rectangle(0, 0, 1, 1)); }
                            var c = one.GetPixel(0, 0);
                            m.Avg = string.Format("#{0:x2}{1:x2}{2:x2}", c.R, c.G, c.B);
                        }
                        m.W = src.Width; m.H = src.Height;
                    }
                }
            }
            catch (Exception ex) { Log.Write("wallpaper prepare " + m.Source + ": " + ex.Message); }
        }

        void CleanCache()
        {
            var keep = new HashSet<string>(mons.Where(m => m.Url != null).Select(m => m.Url.Substring(m.Url.LastIndexOf('/') + 1)), StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var f in Directory.GetFiles(Paths.Cache, "wall_*.jpg"))
                    if (!keep.Contains(Path.GetFileName(f))) { try { File.Delete(f); } catch { } }
            }
            catch { }
        }

        /// <summary>Glass description for a window: which image, how Windows lays it out, and on which monitor.</summary>
        public Dictionary<string, object> InfoFor(Rectangle window)
        {
            var list = mons;
            Mon best = null;
            long bestArea = -1;
            foreach (var m in list)
            {
                var i = Rectangle.Intersect(m.Rect, window);
                long a = (long)Math.Max(0, i.Width) * Math.Max(0, i.Height);
                if (a > bestArea) { bestArea = a; best = m; }
            }
            var virt = SystemInformation.VirtualScreen;
            Func<Rectangle, object> r = x => new Dictionary<string, object> { ["x"] = x.X, ["y"] = x.Y, ["w"] = x.Width, ["h"] = x.Height };
            if (best == null) return new Dictionary<string, object> { ["type"] = "wall", ["url"] = null, ["bg"] = bg };
            return new Dictionary<string, object>
            {
                ["type"] = "wall", ["url"] = best.Url, ["iw"] = best.W, ["ih"] = best.H, ["mode"] = mode, ["bg"] = bg, ["avg"] = best.Avg ?? bg,
                ["mon"] = r(best.Rect), ["virt"] = r(virt),
            };
        }
    }
}
