using GHelper.AnimeMatrix;

namespace GHelper.UI
{
    /// <summary>
    /// Shared controller helper for Slash Lighting / AniMe Matrix brightness and effect mode
    /// across Classic, Nebula, and Signal Glass shells.
    /// </summary>
    public static class LidLightingHelper
    {
        public static bool IsSupported => Program.settingsForm?.matrixControl?.IsValid ?? false;

        public static bool IsSlash => Program.settingsForm?.matrixControl?.IsSlash ?? false;

        public static string DeviceTitle => IsSlash ? "Slash Lighting" : Properties.Strings.AnimeMatrix;

        public static string ButtonTitle => IsSlash ? "Slash" : "Matrix";

        public static string[] BrightnessLabels => new[]
        {
            Properties.Strings.MatrixOff,
            Properties.Strings.MatrixDim,
            Properties.Strings.MatrixMedium,
            Properties.Strings.MatrixBright,
        };

        public static IReadOnlyList<string> ModeLabels() => ModeLabels(IsSlash);

        public static IReadOnlyList<string> ModeLabels(bool isSlash)
        {
            if (isSlash) return SlashDevice.Modes.Values.ToArray();
            return new[]
            {
                Properties.Strings.MatrixBanner,
                Properties.Strings.MatrixLogo,
                Properties.Strings.MatrixPicture,
                Properties.Strings.MatrixClock,
                Properties.Strings.MatrixAudio,
                Properties.Strings.MatrixText,
            };
        }

        public static int CurrentBrightnessIndex()
        {
            var labels = BrightnessLabels;
            return Math.Clamp(AppConfig.Get("matrix_brightness", 0), 0, Math.Max(0, labels.Length - 1));
        }

        public static int CurrentModeIndex()
        {
            var modes = ModeLabels();
            return Math.Clamp(AppConfig.Get("matrix_running", 0), 0, Math.Max(0, modes.Count - 1));
        }

        public static void SetBrightness(int index)
        {
            int clamped = Math.Clamp(index, 0, BrightnessLabels.Length - 1);
            AppConfig.Set("matrix_brightness", clamped);
            Program.settingsForm?.VisualiseMatrixBrightness(clamped);
            Program.settingsForm?.matrixControl?.SetDevice();
        }

        public static void SetMode(int index)
        {
            int max = Math.Max(0, ModeLabels().Count - 1);
            int clamped = Math.Clamp(index, 0, max);
            Program.settingsForm?.SetMatrixRunning(clamped);
        }

        public static void OpenDevicePanel()
        {
            Program.settingsForm?.MatrixToggle();
        }
    }
}
