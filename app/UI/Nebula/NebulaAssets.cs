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

            // Render that ASUS software already placed on this machine (never redistributed by us).
            // Kept as asus-render.png and used only when the user opts in (config nebula_asus_render).
            string asusCopy = "";
            try { asusCopy = Directory.GetFiles(Logger.appPath, "asus-render.*").FirstOrDefault() ?? ""; } catch { }
            var local = asusCopy.Length > 0 ? asusCopy : FindLocalDeviceRender();
            if (local is not null && AppConfig.Is("nebula_asus_render"))
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
                        string keep = Path.Combine(Logger.appPath, "asus-render" + Path.GetExtension(local).ToLowerInvariant());
                        if (!File.Exists(keep) && local != keep) File.Copy(local, keep);
                    }
                    catch (Exception ex) { Logger.WriteLine("Nebula hero copy: " + ex.Message); }
                    return new Bitmap(ms);
                }
                catch (Exception ex) { Logger.WriteLine("Nebula hero: " + ex.Message); }
            }

            return Load("Nebula." + FamilyRender(AppConfig.GetModelShort()) + ".png") ?? Load("Nebula.hero-laptop-angle.png");
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

        /// <summary>True when a render from ASUS software is available on this machine.</summary>
        public static bool HasAsusRender()
        {
            try { if (Directory.GetFiles(Logger.appPath, "asus-render.*").Length > 0) return true; } catch { }
            return FindLocalDeviceRender() is not null;
        }

        /// <summary>Drops cached hero bitmaps so the next paint re-reads the source chain.</summary>
        public static void ResetHero()
        {
            foreach (var key in Cache.Keys.Where(k => k.StartsWith("hero")).ToList()) Cache.TryRemove(key, out _);
        }

        /// <summary>Own illustration per model family (see docs: family-*.png).</summary>
        public static string FamilyRender(string model)
        {
            string m = (model ?? "").Trim().ToUpperInvariant();
            // marketing name first ("ROG Zephyrus G16 GA605WI", "ROG Strix SCAR 18 G834JZ", "TUF Gaming A15 FA507NV")
            if (m.Contains("ZEPHYRUS")) return "hero-laptop-angle";
            if (m.Contains("FLOW")) return "family-flow";
            if (m.Contains("ALLY")) return "family-ally";
            if (m.Contains("STRIX") || m.Contains("SCAR")) return "family-strix";
            if (m.Contains("TUF")) return "family-tuf";
            if (m.Contains("VIVOBOOK") || m.Contains("ZENBOOK") || m.Contains("PROART")) return "family-vivobook";
            // then the model code anywhere in the string
            var code = System.Text.RegularExpressions.Regex.Match(m, @"([A-Z]{1,2})(\d{3})[A-Z0-9]*");
            if (code.Success)
            {
                string pre = code.Groups[1].Value;
                if (pre is "GA" or "GU") return "hero-laptop-angle";
                if (pre is "GV" or "GZ") return "family-flow";
                if (pre is "RC") return "family-ally";
                if (pre is "FA" or "FX") return "family-tuf";
                if (pre == "G") return "family-strix";
            }
            return "family-vivobook";
        }

        /// <summary>Top-down render for the lighting page and its keyboard glow layer.</summary>
        public static Image? HeroTop => Load("Nebula.hero-laptop-top.png");
        public static Image? HeroTopGlow => Load("Nebula.hero-laptop-top-glow.png");

        /// <summary>Any embedded Nebula image scaled to a width, cached.</summary>
        public static Image? Scaled(string name, int width)
        {
            if (width <= 0) return null;
            return Cache.GetOrAdd($"scaled:{name}:{width}", _ =>
            {
                var src = Load("Nebula." + name + ".png");
                if (src is null) return null;
                return src.Width == width ? src : ControlHelper.ResizeImage(src, (float)width / src.Width);
            });
        }

        /// <summary>Illustration for a peripheral by its type and display name (mouse-*, headset-*, device-*).</summary>
        public static string DeviceRender(Peripherals.PeripheralType type, string displayName)
        {
            string n = (displayName ?? "").ToLowerInvariant();
            switch (type)
            {
                case Peripherals.PeripheralType.Mouse:
                    if (n.Contains("keris")) return "mouse-keris";
                    if (n.Contains("gladius")) return "mouse-gladius";
                    if (n.Contains("harpe")) return "mouse-harpe";
                    if (n.Contains("chakram")) return "mouse-chakram";
                    if (n.Contains("impact")) return "mouse-impact";
                    if (n.Contains("carry")) return "mouse-carry";
                    if (n.Contains("tuf")) return "mouse-tuf";
                    return "mouse-generic";
                case Peripherals.PeripheralType.Headset:
                    if (n.Contains("cetra") && (n.Contains("true") || n.Contains("tws") || n.Contains("speednova"))) return "headset-cetra-tws";
                    if (n.Contains("cetra")) return "headset-cetra-wired";
                    if (n.Contains("delta")) return "headset-delta";
                    if (n.Contains("strix go") || n.Contains("go ")) return "headset-strix-go";
                    if (n.Contains("fusion")) return "headset-fusion";
                    if (n.Contains("pelta")) return "headset-pelta";
                    return "headset-generic";
                default:
                    return "device-keyboard";
            }
        }

        /// <summary>Glow layer tinted with a colour (cached per colour and width).</summary>
        public static Image? TintedGlow(Color color, int width)
        {
            if (width <= 0) return null;
            return Cache.GetOrAdd($"glow:{color.ToArgb()}:{width}", _ =>
            {
                var glow = HeroTopGlow;
                if (glow is null) return null;
                var scaled = glow.Width == width ? glow : ControlHelper.ResizeImage(glow, (float)width / glow.Width);
                return ControlHelper.TintImage(scaled, color);
            });
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
