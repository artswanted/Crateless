using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace GHelper.UI.Nebula
{
    /// <summary>
    /// Reads the public ROG wallpaper gallery (rog.asus.com/wallpapers). The page is a Nuxt app and
    /// ships its data inside the HTML as a minified state object, so this parses that state: the
    /// short variable names are resolved from the trailing argument list. Nothing is bundled with
    /// the app; thumbnails and files come straight from the ASUS CDN when the page is opened.
    /// </summary>
    public static class RogWallpapers
    {
        public const string GalleryUrl = "https://rog.asus.com/wallpapers/";

        public sealed class Variant
        {
            public int Width, Height;
            public string Ratio = "";
            public string Url = "";
            public string Preview = "";
        }

        public sealed class Item
        {
            public int Id;
            public string Name = "", Category = "", Thumb = "";
            public int Downloads;
            public List<Variant> Desktop = new();
            public List<Variant> Phone = new();
            public Image? ThumbImage;
            public bool ThumbLoading, ThumbFailed;
            public string LocalFile = "";
            public bool LocalExists;
            public Variant? BestVariant;
            public bool Downloading;
            public string Error = "";
        }

        private static readonly HttpClient http = MakeClient();
        public static readonly List<Item> Items = new();
        public static bool Loading { get; private set; }
        public static bool Loaded { get; private set; }
        public static string Error { get; private set; } = "";

        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ROG Wallpapers");

        private static HttpClient MakeClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Crateless");
            c.DefaultRequestHeaders.Accept.ParseAdd("text/html,image/*,*/*");
            return c;
        }

        /// <summary>Loads the gallery once (or again when <paramref name="force"/>), then calls <paramref name="repaint"/>.</summary>
        public static void Load(Action repaint, bool force = false)
        {
            if (Loading || (Loaded && !force)) return;
            Loading = true; Error = "";
            Task.Run(async () =>
            {
                try
                {
                    List<Item> items;
                    try { items = await FetchApi(); }
                    catch (Exception ex)
                    {
                        // the JSON API is what the site itself uses; fall back to the page's embedded state
                        Logger.WriteLine("ROG wallpapers API: " + ex.Message + ", falling back to the page");
                        items = Parse(await http.GetStringAsync(GalleryUrl));
                    }
                    lock (Items)
                    {
                        // keep already downloaded thumbnails when reloading
                        var old = Items.ToDictionary(i => i.Id);
                        Items.Clear();
                        foreach (var it in items)
                        {
                            if (old.TryGetValue(it.Id, out var o)) { it.ThumbImage = o.ThumbImage; it.LocalFile = o.LocalFile; }
                            Items.Add(it);
                        }
                    }
                    Loaded = true;
                    Logger.WriteLine($"ROG wallpapers: {items.Count} items");
                }
                catch (Exception ex)
                {
                    Error = ex.Message;
                    Logger.WriteLine("ROG wallpapers: " + ex.Message);
                }
                Loading = false;
                repaint();
            });
        }

        private const string ApiUrl = "https://api-rog.asus.com/recent-data/api/v4/wallpaper/Filter?WebsiteCode=global&Sort=newest:asc&PageSize=100&Page=";

        /// <summary>The same JSON endpoint the gallery page calls for its "load more"; all pages are read.</summary>
        private static async Task<List<Item>> FetchApi()
        {
            var list = new List<Item>();
            for (int page = 1; page <= 20; page++)
            {
                using var doc = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync(ApiUrl + page));
                var result = doc.RootElement.GetProperty("result");
                int total = result.TryGetProperty("wallpaperTotal", out var t) ? t.GetInt32() : 0;
                int before = list.Count;
                foreach (var w in result.GetProperty("wallpapers").EnumerateArray())
                {
                    var it = new Item
                    {
                        Id = w.GetProperty("wId").GetInt32(),
                        Name = w.GetProperty("name").GetString() ?? "",
                        Category = w.TryGetProperty("category", out var c) ? c.GetString() ?? "" : "",
                        Thumb = w.GetProperty("thumbnail").GetString() ?? "",
                        Downloads = w.TryGetProperty("download", out var d) ? d.GetInt32() : 0,
                    };
                    if (w.TryGetProperty("devices", out var devices))
                        foreach (var dev in devices.EnumerateArray())
                        {
                            string devName = (dev.TryGetProperty("name", out var dn) ? dn.GetString() ?? "" : "").ToLowerInvariant();
                            var target = devName.Contains("phone") || devName.Contains("mobile") ? it.Phone : it.Desktop;
                            if (!dev.TryGetProperty("images", out var images)) continue;
                            foreach (var im in images.EnumerateArray())
                            {
                                var v = new Variant
                                {
                                    Width = im.TryGetProperty("width", out var iw) ? iw.GetInt32() : 0,
                                    Height = im.TryGetProperty("high", out var ih) ? ih.GetInt32() : 0,
                                    Ratio = im.TryGetProperty("ratio", out var ir) ? ir.GetString() ?? "" : "",
                                    Preview = im.TryGetProperty("image", out var ii) ? ii.GetString() ?? "" : "",
                                    Url = im.TryGetProperty("dlUrl", out var iu) ? iu.GetString() ?? "" : "",
                                };
                                if (v.Url.StartsWith("http")) target.Add(v);
                            }
                        }
                    if (it.Name.Length > 0 && it.Thumb.Length > 0 && it.Desktop.Count > 0 && !list.Any(x => x.Id == it.Id)) list.Add(it);
                }
                if (list.Count == before || list.Count >= total) break;
            }
            if (list.Count == 0) throw new Exception("empty result");
            return list;
        }

        private static readonly SemaphoreSlim thumbGate = new(4);

        public static void LoadThumb(Item it, Action repaint)
        {
            if (it.ThumbImage is not null || it.ThumbLoading || it.ThumbFailed || it.Thumb.Length == 0) return;
            it.ThumbLoading = true;
            Task.Run(async () =>
            {
                await thumbGate.WaitAsync();
                try
                {
                    var bytes = await http.GetByteArrayAsync(it.Thumb);
                    using var ms = new MemoryStream(bytes);
                    using var img = Image.FromStream(ms);
                    // keep a small copy so a dozen thumbnails do not hold full-size bitmaps
                    it.ThumbImage = Fit(img, 480, 300);
                }
                catch (Exception ex) { it.ThumbFailed = true; Logger.WriteLine("ROG wallpaper thumb: " + ex.Message); }
                finally { thumbGate.Release(); }
                it.ThumbLoading = false;
                repaint();
            });
        }

        private static Bitmap Fit(Image src, int maxW, int maxH)
        {
            float k = Math.Min(1f, Math.Min(maxW / (float)src.Width, maxH / (float)src.Height));
            int w = Math.Max(1, (int)(src.Width * k)), h = Math.Max(1, (int)(src.Height * k));
            var bmp = new Bitmap(w, h);
            using var g = Graphics.FromImage(bmp);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, w, h);
            return bmp;
        }

        /// <summary>Picks the desktop file closest to the primary screen's aspect ratio, largest first.</summary>
        public static Variant? Best(Item it)
        {
            if (it.BestVariant is not null) return it.BestVariant;
            if (it.Desktop.Count == 0) return null;
            var b = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1200);
            float want = b.Width / (float)b.Height;
            // closest aspect ratio, then the smallest file that still covers the screen (8K+ originals are huge)
            return it.BestVariant = it.Desktop
                .OrderBy(v => v.Width > 0 && v.Height > 0 ? MathF.Round(Math.Abs(v.Width / (float)v.Height - want), 2) : 9f)
                .ThenBy(v => v.Width >= b.Width ? 0 : 1)
                .ThenBy(v => v.Width >= b.Width ? v.Width : -v.Width)
                .First();
        }

        /// <summary>Downloads the best desktop variant into the Pictures folder; optionally sets it as the wallpaper.</summary>
        public static void Download(Item it, bool apply, Action repaint)
        {
            var v = Best(it);
            if (v is null || it.Downloading) return;
            it.Downloading = true; it.Error = "";
            Task.Run(async () =>
            {
                try
                {
                    Directory.CreateDirectory(Folder);
                    using var resp = await http.GetAsync(v.Url, HttpCompletionOption.ResponseHeadersRead);
                    resp.EnsureSuccessStatusCode();
                    string ext = ".jpg";
                    string? fn = resp.Content.Headers.ContentDisposition?.FileNameStar ?? resp.Content.Headers.ContentDisposition?.FileName;
                    if (!string.IsNullOrEmpty(fn)) { var e = Path.GetExtension(fn.Trim('"')); if (e.Length is > 1 and <= 5) ext = e.ToLowerInvariant(); }
                    else if (resp.Content.Headers.ContentType?.MediaType is string mt && mt.Contains("png")) ext = ".png";
                    string safe = string.Concat(it.Name.Split(Path.GetInvalidFileNameChars())).Trim();
                    if (safe.Length == 0) safe = "rog-" + it.Id;
                    string path = Path.Combine(Folder, $"{safe} {v.Width}x{v.Height}{ext}");
                    await using (var fs = File.Create(path)) await resp.Content.CopyToAsync(fs);
                    it.LocalFile = path;
                    it.LocalExists = true;
                    Logger.WriteLine("ROG wallpaper saved: " + path);
                    if (apply) AuraWallpaper.SetCustom(path);
                }
                catch (Exception ex) { it.Error = ex.Message; Logger.WriteLine("ROG wallpaper download: " + ex.Message); }
                it.Downloading = false;
                repaint();
            });
        }

        public static void OpenFolder()
        {
            try { Directory.CreateDirectory(Folder); Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Folder}\"") { UseShellExecute = true }); }
            catch (Exception ex) { Logger.WriteLine(ex.Message); }
        }

        // ---- parsing ----------------------------------------------------------------------------

        private static readonly Regex ItemHead = new(@"\{wId:(\d+),name:([^,]+),category:([^,]+),description:(""(?:[^""\\]|\\.)*""|[^,]+),thumbnail:([^,]+),download:([^,]+)", RegexOptions.Compiled);
        private static readonly Regex Device = new(@"\{name:([^,]+),type:([^,]+),images:\[", RegexOptions.Compiled);
        private static readonly Regex Img = new(@"\{width:([^,]+),high:([^,]+),ratio:([^,]+),image:([^,]+),dlUrl:([^,]+)", RegexOptions.Compiled);

        public static List<Item> Parse(string html)
        {
            var list = new List<Item>();
            int start = html.IndexOf("window.__NUXT__=(function(", StringComparison.Ordinal);
            if (start < 0) throw new Exception("gallery data not found");
            var vars = Variables(html, start);
            string Val(string tok)
            {
                tok = tok.Trim();
                if (tok.Length >= 2 && tok[0] == '"') return Unescape(tok[1..^1]);
                return vars.TryGetValue(tok, out var v) ? v : tok;
            }
            int Num(string tok) => int.TryParse(Val(tok), NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : 0;

            var heads = ItemHead.Matches(html);
            for (int i = 0; i < heads.Count; i++)
            {
                var m = heads[i];
                int from = m.Index, to = i + 1 < heads.Count ? heads[i + 1].Index : html.IndexOf("));</script>", from, StringComparison.Ordinal);
                if (to < 0) to = html.Length;
                string chunk = html[from..to];
                var it = new Item
                {
                    Id = int.Parse(m.Groups[1].Value),
                    Name = Val(m.Groups[2].Value),
                    Category = Val(m.Groups[3].Value),
                    Thumb = Val(m.Groups[5].Value),
                    Downloads = Num(m.Groups[6].Value),
                };
                if (it.Name.Length == 0 || it.Thumb.Length == 0) continue;

                var devs = Device.Matches(chunk);
                for (int d = 0; d < devs.Count; d++)
                {
                    int dFrom = devs[d].Index, dTo = d + 1 < devs.Count ? devs[d + 1].Index : chunk.Length;
                    string devName = Val(devs[d].Groups[1].Value).ToLowerInvariant();
                    var target = devName.Contains("phone") || devName.Contains("mobile") ? it.Phone : it.Desktop;
                    foreach (Match im in Img.Matches(chunk[dFrom..dTo]))
                    {
                        var v = new Variant
                        {
                            Width = Num(im.Groups[1].Value), Height = Num(im.Groups[2].Value),
                            Ratio = Val(im.Groups[3].Value), Preview = Val(im.Groups[4].Value), Url = Val(im.Groups[5].Value),
                        };
                        if (v.Url.StartsWith("http")) target.Add(v);
                    }
                }
                if (it.Desktop.Count > 0) list.Add(it);
            }
            return list;
        }

        /// <summary>Maps the minified function parameters (a, b, c, …) to the literal values passed at the end of the state.</summary>
        private static Dictionary<string, string> Variables(string html, int start)
        {
            int pOpen = html.IndexOf('(', start + "window.__NUXT__=".Length);
            int pClose = html.IndexOf(')', pOpen);
            var names = html[(pOpen + 1)..pClose].Split(',');
            int end = html.IndexOf("));</script>", start, StringComparison.Ordinal);
            if (end < 0) end = html.Length;
            int argsAt = html.LastIndexOf("}(", end, StringComparison.Ordinal);
            string args = html[(argsAt + 2)..end];

            var values = new List<string>();
            int p = 0;
            while (p < args.Length)
            {
                char ch = args[p];
                if (ch == ',') { p++; continue; }
                if (ch == '"')
                {
                    int q = p + 1;
                    while (q < args.Length && args[q] != '"') q += args[q] == '\\' ? 2 : 1;
                    values.Add(Unescape(args[(p + 1)..Math.Min(q, args.Length)]));
                    p = q + 1;
                }
                else
                {
                    int q = args.IndexOf(',', p);
                    if (q < 0) q = args.Length;
                    string t = args[p..q].Trim();
                    values.Add(t is "null" or "void 0" ? "" : t);
                    p = q;
                }
            }
            var map = new Dictionary<string, string>();
            for (int i = 0; i < names.Length && i < values.Count; i++) map[names[i]] = values[i];
            return map;
        }

        private static string Unescape(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            return Regex.Replace(s, @"\\u([0-9a-fA-F]{4})|\\(.)", m =>
                m.Groups[1].Success ? ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString()
                : m.Groups[2].Value switch { "n" => "\n", "t" => "\t", var o => o });
        }
    }
}
