namespace GHelper.UI
{
    /// <summary>
    /// Semantic color tokens of the Crateless "Nebula" visual direction.
    /// Source of truth: Crateless-Design-Kit-Nebula-v2/design/tokens.json (v2.0.0).
    /// Enabled with config key "theme": "nebula"; otherwise the upstream palette is used.
    /// </summary>
    public sealed class NebulaTheme
    {
        public Color Bg { get; init; }
        public Color Sidebar { get; init; }
        public Color Card { get; init; }
        public Color Raised { get; init; }
        public Color Line { get; init; }
        public Color Text { get; init; }
        public Color Muted { get; init; }
        public Color Faint { get; init; }
        public Color Accent { get; init; }
        public Color OnAccent { get; init; }
        public Color AccentBg { get; init; }
        public Color Warning { get; init; }
        public Color Danger { get; init; }
        public Color Blue { get; init; }

        public static readonly NebulaTheme Dark = new()
        {
            Bg = Hex("#0B0C10"),
            Sidebar = Hex("#101116"),
            Card = Hex("#14161D"),
            Raised = Hex("#22232F"),
            Line = Hex("#30313E"),
            Text = Hex("#F6F5FF"),
            Muted = Hex("#ACACBF"),
            Faint = Hex("#8F91A7"),
            Accent = Hex("#BDABFF"),
            OnAccent = Hex("#1D123C"),
            AccentBg = Hex("#2C2341"),
            Warning = Hex("#F8C883"),
            Danger = Hex("#FF98AF"),
            Blue = Hex("#8DCBFF"),
        };

        public static readonly NebulaTheme Light = new()
        {
            Bg = Hex("#EEEFF6"),
            Sidebar = Hex("#FAFAFD"),
            Card = Hex("#FFFFFF"),
            Raised = Hex("#E5E4F0"),
            Line = Hex("#CFCDDF"),
            Text = Hex("#242038"),
            Muted = Hex("#605C74"),
            Faint = Hex("#726D86"),
            Accent = Hex("#6540B0"),
            OnAccent = Hex("#FFFFFF"),
            AccentBg = Hex("#E7DEFA"),
            Warning = Hex("#8A5500"),
            Danger = Hex("#AE2852"),
            Blue = Hex("#2A619F"),
        };

        // Reference sizes from tokens.json, in design px at 100 %; scale with DPI.
        public const int RadiusSmall = 6;
        public const int RadiusControl = 8;
        public const int RadiusCard = 12;
        public const int HoverMs = 100;
        public const int NavigationMs = 140;

        public static bool IsEnabled => AppConfig.GetString("theme")?.ToLower() == "nebula";

        /// <summary>Palette for the requested mode, or null when Nebula is not enabled.</summary>
        public static NebulaTheme? Current(bool darkTheme) => IsEnabled ? (darkTheme ? Dark : Light) : null;

        private static Color Hex(string hex) => ColorTranslator.FromHtml(hex);
    }
}
