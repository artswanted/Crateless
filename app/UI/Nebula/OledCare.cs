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
