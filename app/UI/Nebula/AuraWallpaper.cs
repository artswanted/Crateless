using GHelper.USB;
using Microsoft.Win32;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GHelper.UI.Nebula
{
    /// <summary>
    /// Aura wallpaper, the light way: a still image generated in the current Aura colour and set as
    /// the Windows wallpaper. It is regenerated only when the colour or effect changes (slow hue
    /// rotation for the cycling effects), so nothing renders in the background and the battery is
    /// left alone. The previous wallpaper is remembered and restored when the feature is turned off.
    /// </summary>
    public static class AuraWallpaper
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SystemParametersInfo(uint action, uint param, string path, uint flags);
        private const uint SPI_SETDESKWALLPAPER = 0x0014, SPIF_UPDATEINIFILE = 0x01, SPIF_SENDCHANGE = 0x02;

        public enum Style { Gradient = 0, Wave = 1, Aurora = 2 }

        private static System.Windows.Forms.Timer? timer;
        private static (int color, int mode, int style, int hue) applied = (-1, -1, -1, -1);
        private static int flip;
        private static float hueShift;
        private static long lastCycle;

        public static bool IsEnabled => AppConfig.Is("aura_wallpaper");
        public static Style CurrentStyle => (Style)Math.Clamp(AppConfig.Get("aura_wallpaper_style", 0), 0, 2);

        public static void Init()
        {
            if (IsEnabled) SetEnabled(true);
        }

        public static void SetEnabled(bool on)
        {
            if (on)
            {
                if (!AppConfig.Is("aura_wallpaper"))
                {
                    // remember what was there before we start overwriting it
                    string prev = CurrentWallpaper();
                    if (prev.Length > 0 && !prev.Contains("aura-wallpaper")) AppConfig.Set("aura_wallpaper_prev", prev);
                }
                AppConfig.Set("aura_wallpaper", 1);
                timer ??= new System.Windows.Forms.Timer { Interval = 2000 };
                timer.Tick -= Tick; timer.Tick += Tick;
                timer.Start();
                applied = (-1, -1, -1, -1);
                Tick(null, EventArgs.Empty);
                Logger.WriteLine("Aura wallpaper: on");
            }
            else
            {
                AppConfig.Set("aura_wallpaper", 0);
                timer?.Stop();
                Restore();
                Logger.WriteLine("Aura wallpaper: off");
            }
        }

        public static void SetStyle(Style style)
        {
            AppConfig.Set("aura_wallpaper_style", (int)style);
            if (IsEnabled) { applied = (-1, -1, -1, -1); Tick(null, EventArgs.Empty); }
        }

        public static void Restore()
        {
            string prev = AppConfig.GetString("aura_wallpaper_prev") ?? "";
            if (prev.Length > 0 && File.Exists(prev)) SetWallpaper(prev);
        }

        private static string CurrentWallpaper()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                return key?.GetValue("Wallpaper")?.ToString() ?? "";
            }
            catch { return ""; }
        }

        private static bool Cycling(AuraMode m) => m is AuraMode.AuraColorCycle or AuraMode.AuraRainbow or AuraMode.Star or AuraMode.Rain or AuraMode.Comet or AuraMode.Flash or AuraMode.HEATMAP or AuraMode.GPUMODE or AuraMode.AMBIENT or AuraMode.AUDIO or AuraMode.AUDIOPULSE;

        private static void Tick(object? sender, EventArgs e)
        {
            try
            {
                var mode = (AuraMode)AppConfig.Get("aura_mode", (int)AuraMode.AuraStatic);
                Color col = Aura.Color1;
                int hue = 0;
                if (Cycling(mode))
                {
                    // slow hue rotation, one step every 20 s: a new file only 3 times a minute
                    long now = Environment.TickCount64;
                    if (now - lastCycle >= 20_000) { lastCycle = now; hueShift = (hueShift + 20f) % 360f; }
                    col = FromHsv((col.GetHue() + hueShift) % 360f, 0.55f, 1f);
                    hue = (int)hueShift;
                }
                var key = (col.ToArgb(), (int)mode, (int)CurrentStyle, hue);
                if (key == applied) return;
                applied = key;

                string path = Generate(col, CurrentStyle);
                SetWallpaper(path);
            }
            catch (Exception ex) { Logger.WriteLine("Aura wallpaper: " + ex.Message); }
        }

        private static void SetWallpaper(string path)
        {
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }

        /// <summary>Renders the wallpaper for the primary screen and returns the file path (two names alternate so Windows notices the change).</summary>
        public static string Generate(Color col, Style style)
        {
            var b = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1200);
            int w = Math.Max(1280, b.Width), h = Math.Max(800, b.Height);
            using var bmp = Render(col, style, w, h);
            flip ^= 1;
            string path = Path.Combine(Logger.appPath, $"aura-wallpaper-{flip}.png");
            bmp.Save(path, ImageFormat.Png);
            return path;
        }

        /// <summary>Draws the wallpaper at any size (also used for the preview).</summary>
        public static Bitmap Render(Color col, Style style, int w, int h)
        {
            var bg = NebulaTheme.Dark.Bg;
            var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(bmp);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(bg);

            Color soft = Mix(bg, col, 0.35f), deep = Mix(bg, col, 0.12f);
            var full = new Rectangle(0, 0, w, h);

            using (var lg = new LinearGradientBrush(full, deep, bg, 35f)) g.FillRectangle(lg, full);

            // corner glow
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(new RectangleF(w * 0.45f, h * 0.15f, w * 0.9f, h * 1.3f));
                using var pgb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(150, col), SurroundColors = new[] { Color.FromArgb(0, col) }, CenterPoint = new PointF(w * 0.95f, h * 0.85f) };
                g.FillPath(pgb, path);
            }

            if (style == Style.Wave || style == Style.Aurora)
            {
                int bands = style == Style.Wave ? 3 : 6;
                for (int i = 0; i < bands; i++)
                {
                    float t = i / (float)Math.Max(1, bands - 1);
                    float amp = h * (style == Style.Wave ? 0.10f : 0.06f);
                    float yBase = h * (style == Style.Wave ? 0.45f + t * 0.35f : 0.20f + t * 0.10f);
                    using var wave = new GraphicsPath();
                    var pts = new List<PointF>();
                    for (int x = -20; x <= w + 20; x += Math.Max(8, w / 160))
                    {
                        float phase = x / (float)w * MathF.PI * (style == Style.Wave ? 2.2f : 1.4f) + t * 2.1f;
                        pts.Add(new PointF(x, yBase + MathF.Sin(phase) * amp + MathF.Sin(phase * 2.3f + 1) * amp * 0.35f));
                    }
                    wave.AddCurve(pts.ToArray(), 0.5f);
                    if (style == Style.Wave)
                    {
                        wave.AddLine(w + 20, h + 20, -20, h + 20);
                        wave.CloseFigure();
                        using var fb = new SolidBrush(Color.FromArgb(28 + i * 10, Mix(col, NebulaTheme.Dark.Blue, t)));
                        g.FillPath(fb, wave);
                    }
                    else
                    {
                        using var pen = new Pen(Color.FromArgb(70, Mix(col, NebulaTheme.Dark.Blue, t)), h * 0.045f);
                        g.DrawPath(pen, wave);
                    }
                }
                if (style == Style.Aurora)
                {
                    using var lg2 = new LinearGradientBrush(new Rectangle(0, 0, w, (int)(h * 0.6f)), Color.FromArgb(0, bg), Color.FromArgb(200, bg), LinearGradientMode.Vertical);
                    g.FillRectangle(lg2, 0, 0, w, h * 0.6f);
                }
            }

            // a thin accent line at the bottom edge, like the Nebula cards
            using (var lb = new LinearGradientBrush(new Rectangle(0, h - 4, w, 4), col, NebulaTheme.Dark.Blue, LinearGradientMode.Horizontal))
                g.FillRectangle(lb, 0, h - 4, w, 4);
            return bmp;
        }

        private static Color Mix(Color a, Color b, float t) => Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));

        private static Color FromHsv(float h, float s, float v)
        {
            float c = v * s, x = c * (1 - Math.Abs(h / 60f % 2 - 1)), m = v - c;
            (float r, float g, float b) = h switch
            {
                < 60 => (c, x, 0f), < 120 => (x, c, 0f), < 180 => (0f, c, x), < 240 => (0f, x, c), < 300 => (x, 0f, c), _ => (c, 0f, x),
            };
            return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }

        /// <summary>Sets any image file as the wallpaper and forgets the Aura one.</summary>
        public static void SetCustom(string path)
        {
            if (IsEnabled) SetEnabled(false);
            AppConfig.Set("aura_wallpaper_prev", path);
            SetWallpaper(path);
        }
    }
}
