namespace GHelper.UI
{
    public enum UiShellMode
    {
        Classic,
        Nebula,
        SignalGlass,
    }

    /// <summary>
    /// Tracks which UI shell is active via the "theme" config flag
    /// ("signal", "nebula", or "classic"/"flat"/"") and whether the first-launch
    /// interface picker still needs to run.
    /// </summary>
    public static class UiShell
    {
        public const string KeySignal = "signal";
        public const string KeyNebula = "nebula";
        public const string KeyClassic = "classic";

        public static UiShellMode Current => Parse(AppConfig.GetString("theme"));

        public static bool IsSignalGlass => Current == UiShellMode.SignalGlass;
        public static bool IsNebula => Current == UiShellMode.Nebula;
        public static bool IsClassic => Current == UiShellMode.Classic;

        /// <summary>
        /// True only when the user has never configured a theme flag and has not yet
        /// dismissed the first-launch interface picker.
        /// </summary>
        public static bool NeedsFirstLaunchPrompt =>
            !AppConfig.Exists("theme") && !AppConfig.Is("ui_shell_prompted");

        public static UiShellMode Parse(string? raw)
        {
            string norm = (raw ?? "").Trim().ToLowerInvariant();
            return norm switch
            {
                "signal" or "signal-glass" or "signalglass" => UiShellMode.SignalGlass,
                "nebula" => UiShellMode.Nebula,
                _ => UiShellMode.Classic,
            };
        }

        public static string ToConfigKey(UiShellMode mode) => mode switch
        {
            UiShellMode.SignalGlass => KeySignal,
            UiShellMode.Nebula => KeyNebula,
            _ => KeyClassic,
        };

        public static void ApplySelection(UiShellMode mode)
        {
            AppConfig.Set("theme", ToConfigKey(mode));
            AppConfig.Set("ui_shell_prompted", 1);
        }
    }
}
