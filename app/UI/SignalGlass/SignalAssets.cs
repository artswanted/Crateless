using System.Collections.Concurrent;
using System.Reflection;

namespace GHelper.UI.SignalGlass
{
    /// <summary>
    /// Embedded Signal Glass raster and icon assets ("SignalGlass.*" resources).
    /// Cached in memory once per key; never decoded inside Paint.
    /// </summary>
    public static class SignalAssets
    {
        private static readonly Assembly Asm = typeof(SignalAssets).Assembly;
        private static readonly ConcurrentDictionary<string, Image?> ImageCache = new();
        private static readonly ConcurrentDictionary<string, Icon?> IconCache = new();

        private static Image? LoadImage(string logicalName) =>
            ImageCache.GetOrAdd(logicalName, name =>
            {
                try
                {
                    using var stream = Asm.GetManifestResourceStream(name);
                    if (stream is null) return null;
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    ms.Position = 0;
                    return new Bitmap(ms);
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"Signal asset {name}: {ex.Message}");
                    return null;
                }
            });

        private static Icon? LoadIcon(string logicalName) =>
            IconCache.GetOrAdd(logicalName, name =>
            {
                try
                {
                    using var stream = Asm.GetManifestResourceStream(name);
                    if (stream is null) return null;
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    ms.Position = 0;
                    return new Icon(ms);
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"Signal icon {name}: {ex.Message}");
                    return null;
                }
            });

        public static Image? IconRaw(string name, bool dark, int size = 24)
        {
            int src = size <= 24 ? 24 : 48;
            return LoadImage($"SignalGlass.icon-{(dark ? "dark" : "light")}-{name}-{src}.png");
        }

        public static Image? IconScaled(string name, bool dark, int pixels, Color? tint = null, bool flipHorizontal = false)
        {
            if (pixels <= 0) return null;
            string tintKey = tint.HasValue ? tint.Value.ToArgb().ToString("X8") : "raw";
            string key = $"icon:{name}:{dark}:{pixels}:{tintKey}:{flipHorizontal}";
            return ImageCache.GetOrAdd(key, _ =>
            {
                var src = IconRaw(name, dark, pixels);
                if (src is null) return null;
                Image working = src.Width == pixels ? new Bitmap(src) : ControlHelper.ResizeImage(src, (float)pixels / src.Width);
                try
                {
                    if (tint.HasValue)
                    {
                        var tinted = ControlHelper.TintImage(working, tint.Value);
                        working.Dispose();
                        working = tinted;
                    }
                    if (flipHorizontal)
                    {
                        working.RotateFlip(RotateFlipType.RotateNoneFlipX);
                    }
                    return working;
                }
                catch
                {
                    working.Dispose();
                    throw;
                }
            });
        }

        public static Image? Background(bool dark, bool highDpi)
        {
            if (dark) return LoadImage("SignalGlass.bg-dark.png");
            return highDpi
                ? (LoadImage("SignalGlass.bg-light@2x.png") ?? LoadImage("SignalGlass.bg-light.png"))
                : LoadImage("SignalGlass.bg-light.png");
        }

        public static Image? Logo(int pixels = 24)
        {
            int pick = pixels <= 16 ? 16 : pixels <= 24 ? 24 : pixels <= 32 ? 32 : pixels <= 48 ? 48 : 256;
            var img = LoadImage($"SignalGlass.logo-{pick}.png");
            if (img is null || img.Width == pixels) return img;
            return ImageCache.GetOrAdd($"logo:scaled:{pixels}", _ => ControlHelper.ResizeImage(img, (float)pixels / img.Width));
        }

        public static Icon? CloneAppIcon()
        {
            var icon = LoadIcon("SignalGlass.crateless.ico");
            return icon is null ? null : (Icon)icon.Clone();
        }

        /// <summary>Returns a fresh Icon instance for NotifyIcon (caller disposes previous icon).</summary>
        public static Icon? CloneTrayIcon(int gpuMode, bool gpuAuto, bool darkTaskbar)
        {
            string mode = gpuAuto ? "optimized" : gpuMode switch
            {
                AsusACPI.GPUModeEco => "eco",
                AsusACPI.GPUModeUltimate => "ultimate",
                _ => "standard",
            };
            string theme = darkTaskbar ? "dark" : "light";
            var icon = LoadIcon($"SignalGlass.tray-{theme}-{mode}.ico");
            return icon is null ? null : (Icon)icon.Clone();
        }

        public static Image? FamilySilhouette(string model, bool highDpi = false)
        {
            string m = (model ?? "").Trim().ToUpperInvariant();
            string family = "vivobook";
            if (m.Contains("ZEPHYRUS") || m.Contains("GA") || m.Contains("GU")) family = "zephyrus";
            else if (m.Contains("FLOW") || m.Contains("GV") || m.Contains("GZ")) family = "flow";
            else if (m.Contains("ALLY") || m.Contains("RC")) family = "ally";
            else if (m.Contains("STRIX") || m.Contains("SCAR")) family = "strix";
            else if (m.Contains("TUF") || m.Contains("FA") || m.Contains("FX")) family = "tuf";

            string suffix = highDpi ? "@2x" : "";
            return LoadImage($"SignalGlass.family-{family}{suffix}.png") ?? LoadImage($"SignalGlass.family-{family}.png");
        }
    }
}
