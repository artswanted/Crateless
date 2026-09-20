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
            if (AppConfig.Is("oled_pixel_shift")) SetPixelShift(true);
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

        // ---- pixel shift (Magnification API full-screen transform) --------------------------------
        [DllImport("magnification.dll")] private static extern bool MagInitialize();
        [DllImport("magnification.dll")] private static extern bool MagUninitialize();
        [DllImport("magnification.dll")] private static extern bool MagSetFullscreenTransform(float magLevel, int xOffset, int yOffset);

        private static System.Windows.Forms.Timer? shiftTimer;
        private static bool magReady, magFailed;
        private static int shiftStep;
        private static long lastShift;
        private static int shiftLogged;
        private const int ShiftRange = 4; // px of travel; magnification is 1 + ShiftRange/screenWidth
        // 3 px square walk; offsets must be >= 0 for the API, so the image moves up/left by 0..3 px
        private static readonly (int x, int y)[] ShiftPath = { (0, 0), (1, 0), (2, 0), (3, 0), (3, 1), (3, 2), (3, 3), (2, 3), (1, 3), (0, 3), (0, 2), (0, 1), (1, 1), (2, 2), (2, 1), (1, 2) };

        public static bool IsPixelShift => shiftTimer is { Enabled: true };
        public static bool PixelShiftUnsupported => magFailed;
        public static int ShiftSeconds => Math.Clamp(AppConfig.Get("oled_shift_sec", 60), 10, 600);

        public static void SetPixelShift(bool on)
        {
            AppConfig.Set("oled_pixel_shift", on ? 1 : 0);
            if (on)
            {
                if (!magReady)
                {
                    try { magReady = MagInitialize(); } catch (Exception ex) { Logger.WriteLine("Magnification: " + ex.Message); magReady = false; }
                    if (!magReady) { magFailed = true; AppConfig.Set("oled_pixel_shift", 0); return; }
                }
                shiftTimer ??= new System.Windows.Forms.Timer { Interval = 1000 };
                shiftTimer.Tick -= ShiftTick;
                shiftTimer.Tick += ShiftTick;
                lastShift = Environment.TickCount64;
                shiftTimer.Start();
                Logger.WriteLine("OLED pixel shift: on, every " + ShiftSeconds + " s");
            }
            else
            {
                shiftTimer?.Stop();
                if (magReady)
                {
                    try { MagSetFullscreenTransform(1.0f, 0, 0); MagUninitialize(); } catch { }
                    shiftLogged = 0;
                    magReady = false;
                }
                shiftStep = 0;
                Logger.WriteLine("OLED pixel shift: off");
            }
        }

        private static void ShiftTick(object? sender, EventArgs e)
        {
            if (Environment.TickCount64 - lastShift < ShiftSeconds * 1000L) return;
            lastShift = Environment.TickCount64;
            shiftStep = (shiftStep + 1) % ShiftPath.Length;
            var (x, y) = ShiftPath[shiftStep];
            try
            {
                // At exactly 1.0x the API allows an offset range of 0, so we magnify by the smallest
                // amount that leaves ShiftRange px of travel (docs: 0..width - width/magLevel).
                var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
                float mag = bounds.Width / (float)(bounds.Width - ShiftRange);
                bool ok = MagSetFullscreenTransform(mag, x, y);
                if (shiftLogged++ < 3) Logger.WriteLine($"OLED pixel shift: mag {mag:F5} offset {x},{y} -> {ok}");
                if (!ok)
                {
                    Logger.WriteLine("OLED pixel shift: transform refused, disabling");
                    magFailed = true;
                    SetPixelShift(false);
                }
            }
            catch (Exception ex) { Logger.WriteLine("OLED pixel shift: " + ex.Message); magFailed = true; SetPixelShift(false); }
        }

        /// <summary>Visible check: jumps the image between the two extremes a few times.</summary>
        public static void TestPixelShift()
        {
            if (!magReady) return;
            var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            float mag = bounds.Width / (float)(bounds.Width - ShiftRange);
            int n = 0;
            var t = new System.Windows.Forms.Timer { Interval = 400 };
            t.Tick += (_, _) =>
            {
                n++;
                bool far = n % 2 == 1;
                try { MagSetFullscreenTransform(mag, far ? ShiftRange : 0, far ? ShiftRange : 0); } catch { }
                if (n >= 6)
                {
                    t.Stop(); t.Dispose();
                    var (x, y) = ShiftPath[shiftStep];
                    try { MagSetFullscreenTransform(mag, x, y); } catch { }
                }
            };
            t.Start();
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
