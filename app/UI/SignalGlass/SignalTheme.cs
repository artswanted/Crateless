using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace GHelper.UI.SignalGlass
{
    public enum GlassIntensityMode
    {
        Standard = 0,
        Opaque = 1,
    }

    public enum ReducedMotionMode
    {
        FollowWindows = 0,
        On = 1,
        Off = 2,
    }

    /// <summary>
    /// Semantic design tokens for Crateless Signal Glass (dark and light).
    /// Loaded from embedded SignalGlass.design-tokens.json with compiled defaults matching v1.0.
    /// </summary>
    public sealed class SignalTheme
    {
        public Color Bg { get; init; }
        public Color Surface { get; init; }
        public Color Raised { get; init; }
        public Color Text { get; init; }
        public Color Muted { get; init; }
        public Color Line { get; init; }
        public Color Accent { get; init; }
        public Color OnAccent { get; init; }
        public Color Gpu { get; init; }
        public Color Danger { get; init; }
        public Color Warning { get; init; }
        public Color Plot { get; init; }

        public const int BaseWindowW = 1440;
        public const int BaseWindowH = 1060;
        public const int MinWindowW = 900;
        public const int MinWindowH = 640;
        public const int TitlebarH = 43;

        public const float RadiusWindow = 24f;
        public const float RadiusPanel = 20f;
        public const float RadiusControl = 12f;
        public const float RadiusPill = 22f;
        public const float RadiusPlot = 12f;

        public const float PanelOpacity = 0.96f;
        public const float RimTopOpacity = 0.55f;
        public const float RimBottomOpacity = 0.12f;
        public const float HighlightHeight = 52f;
        public const float ShadowOffsetY = 6f;
        public const float ShadowOpacity = 0.18f;

        public const int HoverMs = 100;
        public const int PressMs = 80;
        public const int NavigationMs = 160;
        public const int MaxMotionMs = 200;

        public static readonly SignalTheme Dark;
        public static readonly SignalTheme Light;

        static SignalTheme()
        {
            var (dark, light) = LoadFromEmbeddedTokens();
            Dark = dark;
            Light = light;
        }

        public static bool IsEnabled => UiShell.IsSignalGlass;

        public static SignalTheme ForMode(bool darkTheme) => darkTheme ? Dark : Light;

        public static SignalTheme? Current(bool darkTheme) => IsEnabled ? ForMode(darkTheme) : null;

        public static GlassIntensityMode GlassIntensity
        {
            get => AppConfig.Get("signal_glass", 0) == 1 ? GlassIntensityMode.Opaque : GlassIntensityMode.Standard;
            set => AppConfig.Set("signal_glass", value == GlassIntensityMode.Opaque ? 1 : 0);
        }

        public static ReducedMotionMode ReducedMotion
        {
            get
            {
                int v = AppConfig.Get("signal_motion", 0);
                return v is >= 0 and <= 2 ? (ReducedMotionMode)v : ReducedMotionMode.FollowWindows;
            }
            set => AppConfig.Set("signal_motion", (int)value);
        }

        public static bool EffectiveOpaque
        {
            get
            {
                if (SystemInformation.HighContrast) return true;
                if (GlassIntensity == GlassIntensityMode.Opaque) return true;
                return !IsWindowsTransparencyEnabled();
            }
        }

        public static bool EffectiveReducedMotion => ReducedMotion switch
        {
            ReducedMotionMode.On => true,
            ReducedMotionMode.Off => false,
            _ => !IsWindowsAnimationEnabled(),
        };

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out bool pvParam, uint fWinIni);

        private const uint SPI_GETCLIENTAREAANIMATION = 0x1042;

        private static bool IsWindowsAnimationEnabled()
        {
            try { return SystemParametersInfo(SPI_GETCLIENTAREAANIMATION, 0, out bool enabled, 0) ? enabled : true; }
            catch { return true; }
        }

        private static bool IsWindowsTransparencyEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key?.GetValue("EnableTransparency") is int v) return v != 0;
            }
            catch { }
            return true;
        }

        public Color FocusRingColor(bool onAccent) =>
            onAccent ? (ContrastRatio(OnAccent, Accent) >= 3.0 ? OnAccent : Text) : Accent;

        public static double ContrastRatio(Color fg, Color bg)
        {
            double l1 = RelativeLuminance(fg);
            double l2 = RelativeLuminance(bg);
            if (l1 < l2) (l1, l2) = (l2, l1);
            return Math.Round((l1 + 0.05) / (l2 + 0.05), 2);
        }

        private static double RelativeLuminance(Color c)
        {
            static double Channel(byte v)
            {
                double s = v / 255.0;
                return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        private static (SignalTheme dark, SignalTheme light) LoadFromEmbeddedTokens()
        {
            var dark = DefaultDark();
            var light = DefaultLight();
            try
            {
                using var stream = typeof(SignalTheme).Assembly.GetManifestResourceStream("SignalGlass.design-tokens.json");
                if (stream is null) return (dark, light);
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.TryGetProperty("themes", out var themes))
                {
                    if (themes.TryGetProperty("dark", out var d)) dark = ParseTheme(d, dark);
                    if (themes.TryGetProperty("light", out var l)) light = ParseTheme(l, light);
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Signal tokens load: " + ex.Message);
            }
            return (dark, light);
        }

        private static SignalTheme ParseTheme(JsonElement el, SignalTheme fallback)
        {
            Color Pick(string key, Color fb) =>
                el.TryGetProperty(key, out var val) && val.GetString() is { Length: > 0 } hex
                    ? ColorTranslator.FromHtml(hex)
                    : fb;

            return new SignalTheme
            {
                Bg = Pick("bg", fallback.Bg),
                Surface = Pick("surface", fallback.Surface),
                Raised = Pick("raised", fallback.Raised),
                Text = Pick("text", fallback.Text),
                Muted = Pick("muted", fallback.Muted),
                Line = Pick("line", fallback.Line),
                Accent = Pick("accent", fallback.Accent),
                OnAccent = Pick("onAccent", fallback.OnAccent),
                Gpu = Pick("gpu", fallback.Gpu),
                Danger = Pick("danger", fallback.Danger),
                Warning = Pick("warning", fallback.Warning),
                Plot = Pick("plot", fallback.Plot),
            };
        }

        private static SignalTheme DefaultDark() => new()
        {
            Bg = ColorTranslator.FromHtml("#10191F"),
            Surface = ColorTranslator.FromHtml("#1C2C34"),
            Raised = ColorTranslator.FromHtml("#2C414A"),
            Text = ColorTranslator.FromHtml("#F3F7F8"),
            Muted = ColorTranslator.FromHtml("#BCCBD0"),
            Line = ColorTranslator.FromHtml("#607680"),
            Accent = ColorTranslator.FromHtml("#81E5C5"),
            OnAccent = ColorTranslator.FromHtml("#102D26"),
            Gpu = ColorTranslator.FromHtml("#FFB391"),
            Danger = ColorTranslator.FromHtml("#FFA7AC"),
            Warning = ColorTranslator.FromHtml("#FFD18A"),
            Plot = ColorTranslator.FromHtml("#132229"),
        };

        private static SignalTheme DefaultLight() => new()
        {
            Bg = ColorTranslator.FromHtml("#E9F0F2"),
            Surface = ColorTranslator.FromHtml("#F6FAFB"),
            Raised = ColorTranslator.FromHtml("#DCE8EC"),
            Text = ColorTranslator.FromHtml("#13282F"),
            Muted = ColorTranslator.FromHtml("#445F69"),
            Line = ColorTranslator.FromHtml("#768D96"),
            Accent = ColorTranslator.FromHtml("#086950"),
            OnAccent = ColorTranslator.FromHtml("#FFFFFF"),
            Gpu = ColorTranslator.FromHtml("#93431E"),
            Danger = ColorTranslator.FromHtml("#A91E36"),
            Warning = ColorTranslator.FromHtml("#805000"),
            Plot = ColorTranslator.FromHtml("#F4F9FA"),
        };
    }
}
