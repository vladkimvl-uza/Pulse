using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Pulse
{
    /// <summary>Draws the app icon (three activity rings on a dark squircle) and the live tray ring.</summary>
    static class IconFactory
    {
        static readonly Color[][] RingColors =
        {
            new[] { Color.FromArgb(10, 132, 255), Color.FromArgb(100, 210, 255) },
            new[] { Color.FromArgb(48, 209, 88), Color.FromArgb(160, 245, 120) },
            new[] { Color.FromArgb(191, 90, 242), Color.FromArgb(255, 110, 200) },
        };
        static readonly float[] RingFill = { 0.78f, 0.58f, 0.68f };

        static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Bitmap AppIcon(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float pad = size * 0.04f;
                var box = new RectangleF(pad, pad, size - 2 * pad, size - 2 * pad);
                using (var path = RoundRect(box, box.Width * 0.235f))
                using (var bg = new LinearGradientBrush(box, Color.FromArgb(48, 48, 54), Color.FromArgb(12, 12, 16), 90f))
                {
                    g.FillPath(bg, path);
                    using (var edge = new Pen(Color.FromArgb(46, 255, 255, 255), Math.Max(1f, size / 128f))) g.DrawPath(edge, path);
                }
                float c = size / 2f;
                float w = size * (size <= 24 ? 0.115f : 0.095f);
                float gap = size <= 24 ? size * 0.02f : size * 0.03f;
                float r0 = box.Width * 0.36f;
                for (int i = 0; i < 3; i++)
                {
                    float r = r0 - i * (w + gap);
                    if (r <= w) break;
                    var rect = new RectangleF(c - r, c - r, 2 * r, 2 * r);
                    using (var track = new Pen(Color.FromArgb(40, RingColors[i][0]), w)) g.DrawEllipse(track, rect);
                    using (var br = new LinearGradientBrush(new RectangleF(c - r - w, c - r - w, 2 * r + 2 * w, 2 * r + 2 * w), RingColors[i][0], RingColors[i][1], 45f))
                    using (var pen = new Pen(br, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(pen, rect, -90, 360 * RingFill[i]);
                }
            }
            return bmp;
        }

        public static Icon AppIconHandle(int size)
        {
            using (var b = AppIcon(size)) return Icon.FromHandle(b.GetHicon());
        }

        /// <summary>A ring showing <paramref name="frac"/> in <paramref name="color"/>. Caller must DestroyIcon the handle.</summary>
        public static IntPtr TrayIcon(double frac, Color color, bool lightTaskbar, int size, bool alert)
        {
            using (var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    float w = Math.Max(2f, size * 0.17f);
                    float r = size / 2f - w / 2f - 0.5f;
                    var rect = new RectangleF(size / 2f - r, size / 2f - r, 2 * r, 2 * r);
                    var trackColor = lightTaskbar ? Color.FromArgb(55, 0, 0, 0) : Color.FromArgb(70, 255, 255, 255);
                    using (var track = new Pen(trackColor, w)) g.DrawEllipse(track, rect);
                    var c = alert ? Color.FromArgb(255, 69, 58) : color;
                    float sweep = (float)(360 * Math.Max(0.02, Math.Min(1, frac)));
                    using (var pen = new Pen(c, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawArc(pen, rect, -90, sweep);
                    float dot = size * 0.2f;
                    using (var b = new SolidBrush(lightTaskbar ? Color.FromArgb(200, 30, 30, 34) : Color.FromArgb(230, 255, 255, 255)))
                        g.FillEllipse(b, size / 2f - dot / 2, size / 2f - dot / 2, dot, dot);
                }
                return bmp.GetHicon();
            }
        }

        /// <summary>Multi-resolution .ico with PNG-compressed frames (used at build time).</summary>
        public static void WriteIco(string path, int[] sizes)
        {
            var frames = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
                using (var b = AppIcon(sizes[i]))
                using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); frames[i] = ms.ToArray(); }
            using (var fs = new FileStream(path, FileMode.Create))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(frames[i].Length); w.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (var f in frames) w.Write(f);
            }
        }
    }
}
