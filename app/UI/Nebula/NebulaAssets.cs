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

        public static Image? Hero => Load("Nebula.hero-laptop.jpg");

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
