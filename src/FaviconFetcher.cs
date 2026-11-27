using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace DragLaunch
{
    /// <summary>
    /// Holt das Favicon einer Webseite direkt von der Seite selbst (kein fremder Dienst):
    /// 1. Startseite laden und &lt;link rel="icon" / "apple-touch-icon" …&gt; auswerten (grösstes zuerst)
    /// 2. Ersatz: /apple-touch-icon.png und /favicon.ico
    /// Das Netzwerk läuft im Hintergrund, Prüfen und Speichern auf dem UI-Thread.
    /// </summary>
    public static class FaviconFetcher
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var h = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(10) };
            // Manche Seiten liefern ohne Browser-Kennung nichts aus
            h.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36 DragLaunch");
            h.DefaultRequestHeaders.Accept.ParseAdd("text/html,image/avif,image/png,image/x-icon,image/*;q=0.8,*/*;q=0.5");
            return h;
        }

        public static bool IsWebUrl(string p)
            => p != null && (p.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || p.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        /// <summary>Lädt Favicon-Kandidaten herunter (beste zuerst). Nur Netzwerk, keine UI.</summary>
        public static async Task<List<byte[]>> DownloadCandidatesAsync(string url)
        {
            var result = new List<byte[]>();
            if (!IsWebUrl(url) || !Uri.TryCreate(url, UriKind.Absolute, out Uri page)) return result;

            var links = new List<(Uri Href, int Size)>();
            Uri baseUri = page;
            try
            {
                using (var resp = await Http.GetAsync(page, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    baseUri = resp.RequestMessage?.RequestUri ?? page;   // nach Weiterleitung
                    string type = resp.Content.Headers.ContentType?.MediaType ?? "";
                    if (resp.IsSuccessStatusCode && type.Contains("html"))
                    {
                        string html = await ReadLimitedAsync(resp, 768 * 1024).ConfigureAwait(false);
                        var b = Regex.Match(html, "<base[^>]+href\\s*=\\s*[\"']([^\"']+)[\"']", RegexOptions.IgnoreCase);
                        if (b.Success && Uri.TryCreate(baseUri, b.Groups[1].Value, out Uri bu)) baseUri = bu;
                        links = ParseIconLinks(html, baseUri);
                    }
                }
            }
            catch { /* Seite nicht erreichbar: Ersatzpfade versuchen */ }

            var root = new Uri(baseUri.GetLeftPart(UriPartial.Authority) + "/");
            var ordered = links.OrderByDescending(l => l.Size).Select(l => l.Href).ToList();
            ordered.Add(new Uri(root, "apple-touch-icon.png"));
            ordered.Add(new Uri(root, "favicon.ico"));

            foreach (Uri u in ordered.Distinct())
            {
                if (result.Count >= 4) break;
                try
                {
                    using (var resp = await Http.GetAsync(u, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) continue;
                        byte[] data = await ReadBytesLimitedAsync(resp, 2 * 1024 * 1024).ConfigureAwait(false);
                        if (data != null && data.Length > 64 && ImageExtension(data) != null) result.Add(data);
                    }
                }
                catch { }
            }
            return result;
        }

        private static List<(Uri, int)> ParseIconLinks(string html, Uri baseUri)
        {
            var list = new List<(Uri, int)>();
            foreach (Match m in Regex.Matches(html, "<link\\b[^>]*>", RegexOptions.IgnoreCase))
            {
                string tag = m.Value;
                string rel = Attr(tag, "rel").ToLowerInvariant();
                if (!rel.Contains("icon")) continue;                       // icon, shortcut icon, apple-touch-icon, mask-icon
                if (rel.Contains("mask-icon")) continue;                    // einfarbige SVG-Maske
                string href = Attr(tag, "href");
                if (href.Length == 0 || href.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                if (href.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) || Attr(tag, "type").Contains("svg")) continue;   // SVG kann WPF nicht lesen
                if (!Uri.TryCreate(baseUri, System.Net.WebUtility.HtmlDecode(href), out Uri u)) continue;

                int size = rel.Contains("apple-touch-icon") ? 180 : 32;
                var sm = Regex.Match(Attr(tag, "sizes"), "(\\d+)\\s*x\\s*(\\d+)", RegexOptions.IgnoreCase);
                if (sm.Success) size = int.Parse(sm.Groups[1].Value);
                list.Add((u, size));
            }
            return list;
        }

        private static string Attr(string tag, string name)
        {
            var m = Regex.Match(tag, "\\b" + name + "\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))", RegexOptions.IgnoreCase);
            if (!m.Success) return "";
            return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
        }

        /// <summary>Bildformat anhand der ersten Bytes (oder null, wenn kein bekanntes Format).</summary>
        public static string ImageExtension(byte[] d)
        {
            if (d.Length < 12) return null;
            if (d[0] == 0x89 && d[1] == (byte)'P' && d[2] == (byte)'N' && d[3] == (byte)'G') return ".png";
            if (d[0] == 0 && d[1] == 0 && d[2] == 1 && d[3] == 0) return ".ico";
            if (d[0] == 0xFF && d[1] == 0xD8) return ".jpg";
            if (d[0] == (byte)'G' && d[1] == (byte)'I' && d[2] == (byte)'F') return ".gif";
            if (d[0] == (byte)'B' && d[1] == (byte)'M') return ".bmp";
            return null;   // WebP/SVG/HTML-Fehlerseiten usw. werden ignoriert
        }

        /// <summary>Prüft auf dem UI-Thread, ob WPF das Bild lesen kann, und liefert die Breite (0 = unbrauchbar).</summary>
        public static int UsableWidth(byte[] data)
        {
            try
            {
                if (ImageExtension(data) == ".ico")
                {
                    // grösste Variante aus dem ICO-Verzeichnis (0 = 256)
                    int count = BitConverter.ToUInt16(data, 4), best = 0;
                    for (int i = 0; i < count && 6 + 16 * i + 16 <= data.Length; i++)
                        best = Math.Max(best, data[6 + 16 * i] == 0 ? 256 : data[6 + 16 * i]);
                    return best;
                }
                var dec = BitmapDecoder.Create(new MemoryStream(data), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                return dec.Frames.Count > 0 ? dec.Frames.Max(f => f.PixelWidth) : 0;
            }
            catch { return 0; }
        }

        private static async Task<string> ReadLimitedAsync(HttpResponseMessage resp, int max)
        {
            byte[] b = await ReadBytesLimitedAsync(resp, max).ConfigureAwait(false);
            return b == null ? "" : System.Text.Encoding.UTF8.GetString(b);
        }

        private static async Task<byte[]> ReadBytesLimitedAsync(HttpResponseMessage resp, int max)
        {
            using (Stream s = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var ms = new MemoryStream())
            {
                var buf = new byte[16384];
                int n;
                while ((n = await s.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false)) > 0)
                {
                    ms.Write(buf, 0, n);
                    if (ms.Length >= max) break;
                }
                return ms.ToArray();
            }
        }
    }
}
