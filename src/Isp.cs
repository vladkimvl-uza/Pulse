using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Pulse
{
    /// <summary>
    /// Internet provider for the network module: name from the public IP (ipinfo.io) and the logo
    /// from the provider's own website (apple-touch-icon / favicon), cached locally.
    /// </summary>
    sealed class IspService
    {
        // Brand name and website for providers whose registry names differ from their brand.
        static readonly (Regex re, string name, string domain)[] Known =
        {
            (new Regex("uzbektelekom|uztelecom", RegexOptions.IgnoreCase), "Uztelecom", "uztelecom.uz"),
            (new Regex("sarkor", RegexOptions.IgnoreCase), "Sarkor Telecom", "sarkor.uz"),
            (new Regex(@"\btps\b", RegexOptions.IgnoreCase), "TPS", "tps.uz"),
            (new Regex("comnet", RegexOptions.IgnoreCase), "Comnet", "comnet.uz"),
            (new Regex("east telecom", RegexOptions.IgnoreCase), "East Telecom", "etc.uz"),
            (new Regex("turon", RegexOptions.IgnoreCase), "Turon Telecom", "turontelecom.uz"),
            (new Regex("buzton", RegexOptions.IgnoreCase), "Buzton", "buzton.uz"),
            (new Regex("uzonline", RegexOptions.IgnoreCase), "UzOnline", "uzonline.uz"),
            (new Regex("uzmobile", RegexOptions.IgnoreCase), "Uzmobile", "uzmobile.uz"),
            (new Regex("ucell|coscom", RegexOptions.IgnoreCase), "Ucell", "ucell.uz"),
            (new Regex("universal mobile systems|mobiuz", RegexOptions.IgnoreCase), "Mobiuz", "mobi.uz"),
            (new Regex("unitel|beeline.*uz", RegexOptions.IgnoreCase), "Beeline", "beeline.uz"),
            (new Regex("vimpelcom|beeline", RegexOptions.IgnoreCase), "Beeline", "beeline.ru"),
            (new Regex("rostelecom", RegexOptions.IgnoreCase), "Ростелеком", "rt.ru"),
            (new Regex("mobile telesystems|\\bmts\\b", RegexOptions.IgnoreCase), "МТС", "mts.ru"),
            (new Regex("megafon", RegexOptions.IgnoreCase), "МегаФон", "megafon.ru"),
            (new Regex("er-telecom|dom\\.ru", RegexOptions.IgnoreCase), "Дом.ру", "dom.ru"),
            (new Regex("tele2|t2 mobile", RegexOptions.IgnoreCase), "T2", "t2.ru"),
            (new Regex("kazakhtelecom", RegexOptions.IgnoreCase), "Kazakhtelecom", "telecom.kz"),
            (new Regex("kcell", RegexOptions.IgnoreCase), "Kcell", "kcell.kz"),
            (new Regex("starlink|spacex", RegexOptions.IgnoreCase), "Starlink", "starlink.com"),
            (new Regex("comcast", RegexOptions.IgnoreCase), "Xfinity", "xfinity.com"),
            (new Regex("verizon", RegexOptions.IgnoreCase), "Verizon", "verizon.com"),
            (new Regex("vodafone", RegexOptions.IgnoreCase), "Vodafone", "vodafone.com"),
            (new Regex("deutsche telekom", RegexOptions.IgnoreCase), "Telekom", "telekom.de"),
            (new Regex("orange", RegexOptions.IgnoreCase), "Orange", "orange.com"),
        };

        public Dictionary<string, object> Info { get; private set; }
        public event Action<Dictionary<string, object>> Changed;
        public volatile bool Enabled = true;
        int running;
        readonly System.Threading.Timer timer;
        readonly string cacheFile = Path.Combine(Paths.Cache, "isp.json");

        public IspService()
        {
            try { if (File.Exists(cacheFile)) Info = Json.D(Json.Parse(File.ReadAllText(cacheFile))); } catch { }
            timer = new System.Threading.Timer(_ => Refresh(), null, Timeout.Infinite, Timeout.Infinite);
            NetworkChange.NetworkAddressChanged += (s, e) => Kick(8000);
        }

        public void Kick(int delayMs = 1500)
        {
            if (Enabled) timer.Change(delayMs, 30 * 60 * 1000);
        }

        void Refresh()
        {
            if (!Enabled || Interlocked.Exchange(ref running, 1) == 1) return;
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)12288;
                string json;
                using (var wc = Client()) { wc.Headers[HttpRequestHeader.Accept] = "application/json"; json = wc.DownloadString("https://ipinfo.io/json"); }
                var d = Json.D(Json.Parse(json));
                var org = Json.Str(d, "org", "");
                var asn = Regex.Match(org, @"^AS\d+").Value;
                var raw = Regex.Replace(org, @"^AS\d+\s*", "");
                string name = CleanName(raw), domain = null;
                foreach (var k in Known)
                    if (k.re.IsMatch(raw)) { name = k.name; domain = k.domain; break; }
                if (domain == null) domain = DomainFromHost(Json.Str(d, "hostname"));
                string logo = null;
                if (domain != null)
                {
                    var prev = Info != null && Json.Str(Info, "domain") == domain ? Json.Str(Info, "logo") : null;
                    logo = prev != null && File.Exists(Path.Combine(Paths.Cache, prev.Substring(prev.LastIndexOf('/') + 1))) ? prev : FetchLogo(domain);
                }
                Info = new Dictionary<string, object>
                {
                    ["name"] = name, ["asn"] = asn, ["domain"] = domain, ["logo"] = logo,
                    ["city"] = Json.Str(d, "city"), ["country"] = Json.Str(d, "country"),
                };
                File.WriteAllText(cacheFile, Json.Write(Info));
                var h = Changed;
                if (h != null) h(Info);
            }
            catch (Exception ex) { Log.Write("isp: " + ex.Message); }
            finally { Interlocked.Exchange(ref running, 0); }
        }

        internal static WebClient Client()
        {
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) PulseWidget/1.0";
            return wc;
        }

        static string CleanName(string s)
        {
            s = Regex.Replace(s ?? "", @"\b(Joint[- ]Stock Company|Public Joint[- ]Stock Company|JSC|PJSC|OJSC|CJSC|LLC|LLP|Ltd\.?|Limited|OOO|Inc\.?|Corporation|Corp\.?|GmbH|S\.?A\.?|Company|Co\.)\b", "", RegexOptions.IgnoreCase);
            return Regex.Replace(s.Replace(",", " "), @"\s{2,}", " ").Trim();
        }

        /// <summary>"240.64.uzpak.uz" → "uzpak.uz"; keeps three labels for co.uk-style suffixes.</summary>
        static string DomainFromHost(string host)
        {
            if (string.IsNullOrEmpty(host)) return null;
            var parts = host.ToLowerInvariant().Split('.').Where(p => p.Length > 0).ToArray();
            if (parts.Length < 2) return null;
            int take = parts.Length >= 3 && Regex.IsMatch(parts[parts.Length - 2], "^(co|com|net|org|ac|gov)$") ? 3 : 2;
            return string.Join(".", parts.Skip(parts.Length - take));
        }

        /// <summary>Downloads the provider's own touch icon or favicon into the cache folder.</summary>
        static string FetchLogo(string domain) { return SiteIcon.Fetch(domain, "isp-"); }

        internal static string FetchIcon(string domain, string prefix)
        {
            var candidates = new List<string>();
            try
            {
                string html;
                using (var wc = Client()) html = wc.DownloadString("https://" + domain + "/");
                foreach (Match m in Regex.Matches(html, @"<link[^>]+>", RegexOptions.IgnoreCase))
                {
                    var tag = m.Value;
                    var rel = Regex.Match(tag, @"rel\s*=\s*[""']([^""']+)", RegexOptions.IgnoreCase).Groups[1].Value.ToLowerInvariant();
                    if (!rel.Contains("icon")) continue;
                    var href = Regex.Match(tag, @"href\s*=\s*[""']([^""']+)", RegexOptions.IgnoreCase).Groups[1].Value;
                    if (href.Length == 0) continue;
                    int size = 0;
                    var sz = Regex.Match(tag, @"sizes\s*=\s*[""'](\d+)", RegexOptions.IgnoreCase);
                    if (sz.Success) size = int.Parse(sz.Groups[1].Value);
                    if (rel.Contains("apple-touch")) size = Math.Max(size, 180);
                    if (href.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) size = Math.Max(size, 512);
                    candidates.Add(size.ToString("D4") + "|" + new Uri(new Uri("https://" + domain + "/"), href).AbsoluteUri);
                }
            }
            catch (Exception ex) { Log.Write("isp page " + domain + ": " + ex.Message); }
            var urls = candidates.OrderByDescending(c => c).Select(c => c.Substring(5)).ToList();
            urls.Add("https://" + domain + "/apple-touch-icon.png");
            urls.Add("https://" + domain + "/favicon.ico");
            foreach (var url in urls.Distinct())
            {
                try
                {
                    byte[] data;
                    string type;
                    using (var wc = Client()) { data = wc.DownloadData(url); type = (wc.ResponseHeaders[HttpResponseHeader.ContentType] ?? "").ToLowerInvariant(); }
                    if (data.Length < 200) continue;
                    string ext = type.Contains("svg") ? ".svg" : type.Contains("png") ? ".png" : type.Contains("icon") || url.EndsWith(".ico") ? ".ico"
                               : type.Contains("webp") ? ".webp" : type.Contains("jpeg") ? ".jpg" : Path.GetExtension(new Uri(url).AbsolutePath);
                    if (string.IsNullOrEmpty(ext) || type.Contains("html")) continue;
                    var file = prefix + Regex.Replace(domain, @"[^\w.-]", "_") + ext;
                    File.WriteAllBytes(Path.Combine(Paths.Cache, file), data);
                    return "https://pulse.cache/" + file;
                }
                catch { }
            }
            return null;
        }
    }
}

namespace Pulse
{
    /// <summary>Official site icon of a vendor (touch icon / favicon), downloaded once into the cache.</summary>
    static class SiteIcon
    {
        public static string Fetch(string domain, string prefix)
        {
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12 | (System.Net.SecurityProtocolType)12288;
            return IspService.FetchIcon(domain, prefix);
        }
    }
}