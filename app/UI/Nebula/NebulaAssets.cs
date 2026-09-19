using System.Collections.Concurrent;
using System.Reflection;

namespace GHelper.UI.Nebula
{
    /// <summary>
    /// Raster assets of the Nebula design kit, embedded as "Nebula.*" resources.
    /// Every bitmap is decoded once and cached per (name, size); nothing is decoded in Paint.
    /// </summary>
    public static class NebulaAssets
    {
        private static readonly Assembly Asm = typeof(NebulaAssets).Assembly;
        private static readonly ConcurrentDictionary<string, Image?> Cache = new();

        private static Image? Load(string logicalName)
        {
            return Cache.GetOrAdd(logicalName, name =>
            {
                try
                {
                    using var stream = Asm.GetManifestResourceStream(name);
                    if (stream is null) return null;
                    // Copy out of the stream so the Image does not keep the manifest stream alive.
                    using var ms = new MemoryStream();
                    stream.CopyTo(ms);
                    ms.Position = 0;
                    return new Bitmap(ms);
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"Nebula asset {name}: {ex.Message}");
                    return null;
                }
            });
        }

        /// <summary>Line icon from the kit. Size is the source raster (24 or 48); scale in the caller.</summary>
        public static Image? Icon(string name, bool dark, int size = 24)
        {
            int src = size <= 24 ? 24 : 48;
            return Load($"Nebula.icon-{(dark ? "dark" : "light")}-{name}-{src}.png");
        }

        /// <summary>Icon scaled to an exact pixel size, cached.</summary>
        public static Image? IconScaled(string name, bool dark, int pixels)
        {
            if (pixels <= 0) return null;
            string key = $"scaled:{name}:{dark}:{pixels}";
            return Cache.GetOrAdd(key, _ =>
            {
                var src = Icon(name, dark, pixels <= 24 ? 24 : 48);
                if (src is null) return null;
                if (src.Width == pixels) return src;
                return ControlHelper.ResizeImage(src, (float)pixels / src.Width);
            });
        }

        /// <summary>Device render: %APPDATA%\Crateless\hero.png / hero.jpg if the user dropped one there, otherwise the kit render.</summary>
        public static Image? Hero => Cache.GetOrAdd("hero:src", _ =>
        {
            try
            {
                string dir = Logger.appPath;
                foreach (var name in new[] { "hero.png", "hero.jpg", "hero.jpeg" })
                {
                    string path = Path.Combine(dir, name);
                    if (File.Exists(path))
                    {
                        using var fs = File.OpenRead(path);
                        using var ms = new MemoryStream();
                        fs.CopyTo(ms); ms.Position = 0;
                        Logger.WriteLine("Nebula hero: " + path);
                        return new Bitmap(ms);
                    }
                }
            }
            catch (Exception ex) { Logger.WriteLine("Nebula hero: " + ex.Message); }

            // Renders that ASUS software already placed on this machine (never redistributed by us).
            var local = FindLocalDeviceRender();
            if (local is not null)
            {
                try
                {
                    using var fs = File.OpenRead(local);
                    using var ms = new MemoryStream();
                    fs.CopyTo(ms); ms.Position = 0;
                    Logger.WriteLine("Nebula hero (local ASUS render): " + local);
                    // keep a private copy so the render survives an Armoury Crate uninstall
                    try
                    {
                        string keep = Path.Combine(Logger.appPath, "hero" + Path.GetExtension(local).ToLowerInvariant());
                        if (!File.Exists(keep)) File.Copy(local, keep);
                    }
                    catch (Exception ex) { Logger.WriteLine("Nebula hero copy: " + ex.Message); }
                    return new Bitmap(ms);
                }
                catch (Exception ex) { Logger.WriteLine("Nebula hero: " + ex.Message); }
            }

            return Load("Nebula.hero-laptop.jpg");
        });

        /// <summary>
        /// Looks for a product render of this laptop left behind by Armoury Crate / MyASUS in the
        /// user's profile. Laptop SKUs start with 90NR; peripherals use other prefixes.
        /// </summary>
        public static string? FindLocalDeviceRender()
        {
            try
            {
                string packages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
                if (!Directory.Exists(packages)) return null;
                string model = AppConfig.GetModelShort();

                // 1) MyASUS / PC Assistant: <lang>_<MODEL>_img.png
                foreach (var dir in Directory.GetDirectories(packages, "B9ECED6F.ASUSPCAssistant_*"))
                {
                    string state = Path.Combine(dir, "LocalState");
                    if (!Directory.Exists(state)) continue;
                    var hit = Directory.GetFiles(state, "*_img.png")
                        .FirstOrDefault(f => model.Length > 0 && Path.GetFileName(f).Contains(model, StringComparison.OrdinalIgnoreCase));
                    if (hit is not null) return hit;
                }

                // 2) Armoury Crate product renders: ProductPNG\90NR*.png (skip *_crop)
                foreach (var dir in Directory.GetDirectories(packages, "B9ECED6F.ArmouryCrate_*"))
                {
                    string png = Path.Combine(dir, "LocalState", "ProductPNG");
                    if (!Directory.Exists(png)) continue;
                    var hit = Directory.GetFiles(png, "90NR*.png")
                        .Where(f => !Path.GetFileNameWithoutExtension(f).EndsWith("_crop", StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(f => new FileInfo(f).Length)
                        .FirstOrDefault();
                    if (hit is not null) return hit;
                }
            }
            catch (Exception ex) { Logger.WriteLine("Nebula local render: " + ex.Message); }
            return null;
        }

        /// <summary>Hero render fitted into the given width, cached by width.</summary>
        public static Image? HeroScaled(int width)
        {
            if (width <= 0) return null;
            string key = $"hero:{width}";
            return Cache.GetOrAdd(key, _ =>
            {
                var src = Hero;
                if (src is null) return null;
                return ControlHelper.ResizeImage(src, (float)width / src.Width);
            });
        }
    }
}
