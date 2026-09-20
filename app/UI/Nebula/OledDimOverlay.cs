using System.Runtime.InteropServices;

namespace GHelper.UI.Nebula
{
    /// <summary>
    /// "Focus mode" for OLED: a click-through layered window that darkens everything except the
    /// foreground window. Follows focus and window moves through a WinEvent hook, steps aside for
    /// full-screen apps, the desktop, the taskbar and our own windows. No timers, no polling.
    /// </summary>
    public sealed class OledDimOverlay : Form
    {
        private const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x8000000;
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_OBJECT_LOCATIONCHANGE = 0x800B, EVENT_SYSTEM_MINIMIZESTART = 0x0016, EVENT_SYSTEM_MINIMIZEEND = 0x0017;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;

        private delegate void WinEventDelegate(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint hmod, WinEventDelegate proc, uint idProcess, uint idThread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
        [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hWnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(nint hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hWnd, System.Text.StringBuilder sb, int max);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint pid);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint hwnd, int attr, out RECT rect, int size);

        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int left, top, right, bottom; }

        private readonly WinEventDelegate hookProc;
        private nint hookFg, hookLoc, hookMin;
        private readonly System.Windows.Forms.Timer settle = new() { Interval = 60 };
        private nint lastHwnd;
        private Rectangle lastHole;

        public OledDimOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            BackColor = Color.Black;
            Opacity = Math.Clamp(AppConfig.Get("oled_focus_alpha", 35), 10, 80) / 100.0;
            Bounds = SystemInformation.VirtualScreen;
            SetStyle(ControlStyles.Selectable, false);

            hookProc = OnWinEvent;
            settle.Tick += (_, _) => { settle.Stop(); Recompute(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return p;
            }
        }

        protected override bool ShowWithoutActivation => true;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            hookFg = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, hookProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            hookLoc = SetWinEventHook(EVENT_OBJECT_LOCATIONCHANGE, EVENT_OBJECT_LOCATIONCHANGE, 0, hookProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            hookMin = SetWinEventHook(EVENT_SYSTEM_MINIMIZESTART, EVENT_SYSTEM_MINIMIZEEND, 0, hookProc, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            Recompute();
        }

        private void OnWinEvent(nint hook, uint type, nint hwnd, int idObject, int idChild, uint thread, uint time)
        {
            if (type == EVENT_OBJECT_LOCATIONCHANGE && (idObject != 0 || hwnd != lastHwnd)) return;
            if (!settle.Enabled) settle.Start();
        }

        private void Recompute()
        {
            try
            {
                nint fg = GetForegroundWindow();
                var screen = SystemInformation.VirtualScreen;
                if (Bounds != screen) Bounds = screen;

                if (fg == 0 || !IsWindowVisible(fg) || IsIconic(fg) || IsOwn(fg) || IsShell(fg))
                {
                    // nothing to focus on: dim nothing rather than everything
                    Apply(Rectangle.Empty, hide: true);
                    lastHwnd = fg;
                    return;
                }

                Rectangle r = WindowRect(fg);
                var mon = Screen.FromHandle(fg).Bounds;
                bool fullscreen = r.Left <= mon.Left && r.Top <= mon.Top && r.Right >= mon.Right && r.Bottom >= mon.Bottom;
                lastHwnd = fg;
                if (fullscreen) { Apply(Rectangle.Empty, hide: true); return; }
                Apply(r, hide: false);
            }
            catch (Exception ex) { Logger.WriteLine("OLED overlay: " + ex.Message); }
        }

        private void Apply(Rectangle hole, bool hide)
        {
            if (hide) { if (Visible) Hide(); return; }
            if (!Visible) Show();
            if (hole == lastHole) return;
            lastHole = hole;
            var region = new Region(new Rectangle(0, 0, Width, Height));
            var local = new Rectangle(hole.X - Left, hole.Y - Top, hole.Width, hole.Height);
            region.Exclude(local);
            Region?.Dispose();
            Region = region;
        }

        private static Rectangle WindowRect(nint hwnd)
        {
            // DWM frame bounds exclude the invisible resize borders of Win10/11 windows
            if (DwmGetWindowAttribute(hwnd, 9, out var dr, Marshal.SizeOf<RECT>()) == 0 && dr.right > dr.left)
                return Rectangle.FromLTRB(dr.left, dr.top, dr.right, dr.bottom);
            GetWindowRect(hwnd, out var r);
            return Rectangle.FromLTRB(r.left, r.top, r.right, r.bottom);
        }

        private static bool IsOwn(nint hwnd)
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            return pid == Environment.ProcessId;
        }

        private static bool IsShell(nint hwnd)
        {
            var sb = new System.Text.StringBuilder(64);
            GetClassName(hwnd, sb, sb.Capacity);
            string cls = sb.ToString();
            return cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow" or "XamlExplorerHostIslandWindow";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                settle.Dispose();
                if (hookFg != 0) UnhookWinEvent(hookFg);
                if (hookLoc != 0) UnhookWinEvent(hookLoc);
                if (hookMin != 0) UnhookWinEvent(hookMin);
            }
            base.Dispose(disposing);
        }
    }
}
