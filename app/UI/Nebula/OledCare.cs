using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace GHelper.UI.Nebula
{
    /// <summary>
    /// OLED care helpers: taskbar auto-hide (shell app-bar API) and taskbar transparency
    /// (the Windows personalization switch). Both are Windows settings, nothing is written
    /// to the panel; they only reduce static content on screen.
    /// </summary>
    public static class OledCare
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public uint cbSize;
            public nint hWnd;
            public uint uCallbackMessage;
            public uint uEdge;
            public RECT rc;
            public int lParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        private const uint ABM_GETSTATE = 0x04;
        private const uint ABM_SETSTATE = 0x0A;
        private const int ABS_AUTOHIDE = 0x01;
        private const int ABS_ALWAYSONTOP = 0x02;

        [DllImport("shell32.dll")]
        private static extern nuint SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern nint SendMessageTimeout(nint hWnd, uint msg, nint wParam, string lParam, uint flags, uint timeout, out nint result);

        private const nint HWND_BROADCAST = 0xFFFF;
        private const uint WM_SETTINGCHANGE = 0x001A;

        public static bool IsTaskbarAutoHide()
        {
            var d = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
            return ((int)SHAppBarMessage(ABM_GETSTATE, ref d) & ABS_AUTOHIDE) != 0;
        }

        public static void SetTaskbarAutoHide(bool on)
        {
            var d = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
            int state = (int)SHAppBarMessage(ABM_GETSTATE, ref d);
            state = on ? state | ABS_AUTOHIDE : state & ~ABS_AUTOHIDE;
            d.lParam = state;
            SHAppBarMessage(ABM_SETSTATE, ref d);
            Logger.WriteLine("Taskbar auto-hide: " + on);
        }

        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        // ---- start-up ---------------------------------------------------------------------------
        /// <summary>Restores the OLED care features that were left on. Call once on the UI thread.</summary>
        public static void Init()
        {
            if (!AppConfig.IsOLED()) return;
            if (AppConfig.Is("oled_focus_dim")) SetFocusDim(true);
            if (AppConfig.Is("oled_idle_dim")) SetIdleDim(true);
        }

        // ---- focus mode (dim inactive windows) ------------------------------------------------------
        private static OledDimOverlay? overlay;

        public static bool IsFocusDim => overlay is { IsDisposed: false };

        public static void SetFocusDim(bool on)
        {
            AppConfig.Set("oled_focus_dim", on ? 1 : 0);
            if (on)
            {
                if (overlay is { IsDisposed: false }) return;
                overlay = new OledDimOverlay();
                overlay.Show();
                Logger.WriteLine("OLED focus dim: on");
            }
            else
            {
                overlay?.Close();
                overlay?.Dispose();
                overlay = null;
                Logger.WriteLine("OLED focus dim: off");
            }
        }

        // ---- idle dim ----------------------------------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        private static System.Windows.Forms.Timer? idleTimer;
        private static int savedBrightness = -1;
        private static bool idleDimmed;
        private const int IdleDimLevel = 15;

        public static bool IsIdleDim => idleTimer is { Enabled: true };
        public static int IdleMinutes => Math.Clamp(AppConfig.Get("oled_idle_min", 3), 1, 30);

        public static void SetIdleDim(bool on)
        {
            AppConfig.Set("oled_idle_dim", on ? 1 : 0);
            if (on)
            {
                idleTimer ??= new System.Windows.Forms.Timer { Interval = 5000 };
                idleTimer.Tick -= IdleTick;
                idleTimer.Tick += IdleTick;
                idleTimer.Start();
                Logger.WriteLine("OLED idle dim: on, " + IdleMinutes + " min");
            }
            else
            {
                idleTimer?.Stop();
                RestoreIdle();
                Logger.WriteLine("OLED idle dim: off");
            }
        }

        private static uint IdleMs()
        {
            var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref lii)) return 0;
            return (uint)Environment.TickCount - lii.dwTime;
        }

        private static void IdleTick(object? sender, EventArgs e)
        {
            try
            {
                uint idle = IdleMs();
                if (!idleDimmed && idle >= (uint)IdleMinutes * 60_000)
                {
                    savedBrightness = Display.VisualControl.GetBrightness();
                    if (savedBrightness > IdleDimLevel)
                    {
                        idleDimmed = true;
                        Display.VisualControl.SetBrightness(IdleDimLevel);
                        Logger.WriteLine("OLED idle dim: dimmed from " + savedBrightness);
                    }
                }
                else if (idleDimmed && idle < 5000)
                {
                    RestoreIdle();
                }
            }
            catch (Exception ex) { Logger.WriteLine("OLED idle dim: " + ex.Message); }
        }

        private static void RestoreIdle()
        {
            if (!idleDimmed) return;
            idleDimmed = false;
            if (savedBrightness > 0)
            {
                Display.VisualControl.SetBrightness(savedBrightness);
                Program.settingsForm?.VisualiseBrightness();
                Logger.WriteLine("OLED idle dim: restored " + savedBrightness);
            }
        }

        // ---- dark theme ----------------------------------------------------------------------------
        public static bool IsDarkTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { return false; }
        }

        public static void SetDarkTheme(bool dark)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey);
                key?.SetValue("AppsUseLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
                key?.SetValue("SystemUsesLightTheme", dark ? 0 : 1, RegistryValueKind.DWord);
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, "ImmersiveColorSet", 0x0002, 1000, out _);
                Logger.WriteLine("Windows dark theme: " + dark);
            }
            catch (Exception ex) { Logger.WriteLine("Dark theme: " + ex.Message); }
        }

        public static bool IsTransparencyEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                return key?.GetValue("EnableTransparency") is int v && v != 0;
            }
            catch { return false; }
        }

        public static void SetTransparency(bool on)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(PersonalizeKey);
                key?.SetValue("EnableTransparency", on ? 1 : 0, RegistryValueKind.DWord);
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, 0, "ImmersiveColorSet", 0x0002, 1000, out _);
                Logger.WriteLine("Windows transparency: " + on);
            }
            catch (Exception ex) { Logger.WriteLine("Transparency: " + ex.Message); }
        }
    }
}
