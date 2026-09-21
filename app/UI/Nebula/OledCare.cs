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
            // a previous instance may have been closed while the screen was dimmed
            if (AppConfig.Get("oled_idle_saved", 0) > 0) RestoreIdle();
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
                Application.ApplicationExit -= OnExit;
                Application.ApplicationExit += OnExit;
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
                        AppConfig.Set("oled_idle_saved", savedBrightness);
                        Display.VisualControl.SetBrightness(IdleDimLevel);
                        Logger.WriteLine("OLED idle dim: dimmed from " + savedBrightness);
                    }
                }
                else if (idleDimmed && idle < 1500)
                {
                    RestoreIdle();
                }
                idleTimer!.Interval = idleDimmed ? 500 : 5000;
            }
            catch (Exception ex) { Logger.WriteLine("OLED idle dim: " + ex.Message); }
        }

        private static void OnExit(object? sender, EventArgs e) => RestoreIdle();

        private static void RestoreIdle()
        {
            int saved = idleDimmed ? savedBrightness : AppConfig.Get("oled_idle_saved", 0);
            idleDimmed = false;
            if (saved <= 0) return;
            AppConfig.Set("oled_idle_saved", 0);
            try
            {
                Display.VisualControl.SetBrightness(saved);
                Program.settingsForm?.VisualiseBrightness();
                Logger.WriteLine("OLED idle dim: restored " + saved);
            }
            catch (Exception ex) { Logger.WriteLine("OLED idle dim: " + ex.Message); }
        }

        // ---- pixel shift (nudge top-level windows, the OLEDShift approach) -------------------------
        // The Magnification API accepts a full-screen transform from a normal process but never
        // applies it, so we move the windows themselves by a pixel or two along a closed path.
        private delegate bool EnumWindowsProc(nint hWnd, nint lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc proc, nint lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(nint hWnd);
        [DllImport("user32.dll")] private static extern bool IsZoomed(nint hWnd);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(nint hWnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hWnd, nint after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] private static extern nint GetWindowLongPtr(nint hWnd, int index);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hWnd, System.Text.StringBuilder sb, int max);

        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
        private const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;

        private static System.Windows.Forms.Timer? shiftTimer;
        private static int shiftStep;
        private static long lastShift;
        private static readonly (int x, int y)[] ShiftPath = { (0, 0), (1, 0), (2, 0), (2, 1), (2, 2), (1, 2), (0, 2), (0, 1) };

        public static bool IsPixelShift => shiftTimer is { Enabled: true };
        public static bool PixelShiftUnsupported => false;
        public static int ShiftSeconds => Math.Clamp(AppConfig.Get("oled_shift_sec", 60), 10, 600);

        public static void SetPixelShift(bool on)
        {
            AppConfig.Set("oled_pixel_shift", on ? 1 : 0);
            if (on)
            {
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
                // walk back to the origin so windows end where the user left them
                if (shiftStep != 0) { var (x, y) = ShiftPath[shiftStep]; Nudge(-x, -y); shiftStep = 0; }
                Logger.WriteLine("OLED pixel shift: off");
            }
        }

        private static void ShiftTick(object? sender, EventArgs e)
        {
            if (Environment.TickCount64 - lastShift < ShiftSeconds * 1000L) return;
            lastShift = Environment.TickCount64;
            var (x0, y0) = ShiftPath[shiftStep];
            shiftStep = (shiftStep + 1) % ShiftPath.Length;
            var (x1, y1) = ShiftPath[shiftStep];
            Nudge(x1 - x0, y1 - y0);
        }

        /// <summary>Moves every ordinary top-level window by (dx, dy). Maximized, full-screen, tool and own windows are left alone.</summary>
        private static int Nudge(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return 0;
            int moved = 0;
            try
            {
                EnumWindows((h, _) =>
                {
                    try
                    {
                        if (!IsWindowVisible(h) || IsIconic(h) || IsZoomed(h)) return true;
                        if (GetWindowTextLength(h) == 0) return true;
                        long ex = (long)GetWindowLongPtr(h, GWL_EXSTYLE);
                        if ((ex & WS_EX_TOOLWINDOW) != 0 || (ex & WS_EX_NOACTIVATE) != 0) return true;
                        GetWindowThreadProcessId(h, out uint pid);
                        if (pid == Environment.ProcessId) return true;
                        var sb = new System.Text.StringBuilder(64);
                        GetClassName(h, sb, sb.Capacity);
                        string cls = sb.ToString();
                        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow") return true;
                        if (!GetWindowRect(h, out var r)) return true;
                        var mon = Screen.FromHandle(h).Bounds;
                        bool full = r.left <= mon.Left && r.top <= mon.Top && r.right >= mon.Right && r.bottom >= mon.Bottom;
                        if (full) return true;
                        if (SetWindowPos(h, 0, r.left + dx, r.top + dy, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOOWNERZORDER)) moved++;
                    }
                    catch { }
                    return true;
                }, 0);
            }
            catch (Exception ex) { Logger.WriteLine("OLED pixel shift: " + ex.Message); }
            return moved;
        }

        /// <summary>Visible check: nudges the windows back and forth by 12 px a few times.</summary>
        public static void TestPixelShift()
        {
            int n = 0;
            var t = new System.Windows.Forms.Timer { Interval = 350 };
            t.Tick += (_, _) =>
            {
                n++;
                Nudge(n % 2 == 1 ? 12 : -12, n % 2 == 1 ? 12 : -12);
                if (n >= 6) { t.Stop(); t.Dispose(); }
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
