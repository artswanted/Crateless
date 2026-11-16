using GHelper.AutoUpdate;
using GHelper.Battery;
using GHelper.Display;
using GHelper.Mode;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace GHelper.UI.SignalGlass
{
    /// <summary>
    /// 13 Tray fly-out (360×560 DIP): anchored near the system tray inside the active screen
    /// work area, scales with DPI, closes on Esc or focus loss, and switches into the full Signal Glass window.
    /// </summary>
    public sealed class SignalFlyoutForm : RForm
    {
        private const float DesignW = 360f;
        private const float DesignH = 560f;
        private const float IdleExtraH = 84f;

        private static bool ShowIdle => AppConfig.Is("compact_idle");
        private float TotalDesignH => DesignH + (ShowIdle ? IdleExtraH : 0f);

        private readonly ISignalBackend backend;
        private readonly GlassPainter painter = new();
        private readonly List<UiElement> uiElements = new();
        private readonly System.Windows.Forms.Timer pollTimer = new() { Interval = 1000 };

        private SignalTheme theme = SignalTheme.Dark;
        private SensorSnapshot snapshot = new();
        private CapabilitySet caps = new();

        private float scale = 1f;
        private string hoverId = "";
        private string focusedId = "";
        private bool showFocusRing;
        private bool menuOpen;
        private bool polling;
        private long hiddenAt;

        public SignalFlyoutForm(ISignalBackend? customBackend = null)
        {
            backend = customBackend ?? new SignalHardwareAdapter();

            Text = "Crateless";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;

            ApplySizeAndRegion();

            pollTimer.Tick += async (_, _) => await RefreshAsync();
            Deactivate += (_, _) =>
            {
                if (menuOpen) return;
                hiddenAt = Environment.TickCount64;
                TopMost = false;
                Hide();
            };
            VisibleChanged += async (_, _) =>
            {
                if (Visible)
                {
                    ApplyTheme();
                    await RefreshCapabilitiesAndSensorsAsync();
                    pollTimer.Start();
                }
                else
                {
                    pollTimer.Stop();
                }
            };
            MouseLeave += (_, _) => { hoverId = ""; Invalidate(); };
        }

        protected override AccessibleObject CreateAccessibilityInstance() =>
            new SignalAccessibleObject(
                this,
                () => uiElements,
                () => focusedId,
                id => { focusedId = id; showFocusRing = true; Invalidate(); });

        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return p;
            }
        }

        public void ApplySizeAndRegion()
        {
            scale = Math.Max(1f, DeviceDpi / 96f);
            var size = new Size((int)Math.Round(DesignW * scale), (int)Math.Round(TotalDesignH * scale));
            if (Size != size) Size = size;

            using var path = GlassPainter.Rounded(new RectangleF(0, 0, Width, Height), 20f * scale);
            Region = new Region(path);
            painter.InvalidateBackdrop();
        }

        public void ApplyTheme()
        {
            InitTheme();
            theme = SignalTheme.ForMode(darkTheme);
            BackColor = theme.Bg;
            ApplySizeAndRegion();
            Invalidate();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            ApplySizeAndRegion();
            ClampToWorkArea();
            Invalidate();
        }

        public void Toggle()
        {
            if (Visible)
            {
                TopMost = false;
                Hide();
                return;
            }
            if (Environment.TickCount64 - hiddenAt < 300) return;

            ApplySizeAndRegion();
            ClampToWorkArea();
            TopMost = true;
            Show();
            Activate();
        }

        private void ClampToWorkArea()
        {
            var scr = Screen.FromPoint(Cursor.Position);
            var wa = scr.WorkingArea;
            int x = wa.Right - Width - (int)(12 * scale);
            int y = wa.Bottom - Height - (int)(12 * scale);
            x = Math.Clamp(x, wa.Left + 4, Math.Max(wa.Left + 4, wa.Right - Width - 4));
            y = Math.Clamp(y, wa.Top + 4, Math.Max(wa.Top + 4, wa.Bottom - Height - 4));
            Location = new Point(x, y);
        }

        private async Task RefreshCapabilitiesAndSensorsAsync()
        {
            try { caps = await backend.GetCapabilitiesAsync(CancellationToken.None); } catch { }
            await RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            if (polling || !Visible || IsDisposed) return;
            polling = true;
            try
            {
                snapshot = await backend.ReadSensorsAsync(CancellationToken.None);
                if (!IsDisposed) Invalidate();
            }
            catch { }
            finally { polling = false; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            uiElements.Clear();
            bool opaque = SignalTheme.EffectiveOpaque;
            bool rtl = SignalText.IsRtl;
            painter.DrawBackdrop(g, ClientSize, scale, theme, darkTheme, opaque);

            GlassPainter.DrawPanel(g, new RectangleF(4f * scale, 4f * scale, ClientSize.Width - 8f * scale, ClientSize.Height - 8f * scale),
                scale, theme, opaque, 20f);

            float pad = 22f;
            float w = DesignW - pad * 2f;
            var fmt = rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft;

            // Header
            var logo = SignalAssets.Logo((int)Math.Round(20f * scale));
            if (logo is not null)
            {
                float lx = rtl ? (DesignW - pad - 20f) : pad;
                g.DrawImage(logo, lx * scale, 22f * scale, 20f * scale, 20f * scale);
            }
            painter.DrawText(g, "Crateless", Px(rtl ? pad + 36f : pad + 28f, 18f, w - 70f, 28f),
                painter.Font(19f, scale, FontStyle.Bold, true), theme.Text, fmt);

            DrawBtn(g, "flyout:close", new RectangleF(rtl ? pad : DesignW - pad - 30f, 18f, 30f, 30f), "×", () => { TopMost = false; Hide(); }, false);

            string model = string.IsNullOrWhiteSpace(snapshot.ModelName) ? AppConfig.GetModelShort() : snapshot.ModelName;
            string powerSrc = snapshot.IsPlugged ? SignalText.T("Plugged in", "От сети") : SignalText.T("On battery", "От батареи");
            painter.DrawText(g, $"{model} · {powerSrc}", Px(pad, 52f, w, 20f),
                painter.Font(12f, scale), theme.Muted, fmt);

            // Modes (Silent / Balanced / Turbo)
            var modes = Modes.GetList();
            int curMode = Modes.GetCurrent();
            int n = Math.Min(3, modes.Count);
            float mGap = 8f;
            float mw = (w - mGap * (n - 1)) / Math.Max(1, n);
            for (int i = 0; i < n; i++)
            {
                int m = modes[i];
                float mx = pad + i * (mw + mGap);
                DrawBtn(g, "flyout:mode:" + m, new RectangleF(mx, 86f, mw, 38f), Modes.GetName(m),
                    () => { Program.modeControl.SetPerformanceMode(m, true); Invalidate(); }, m == curMode);
            }

            // Rows: GPU / Display / Charge limit
            float y = 148f;
            string gpuName = snapshot.GpuAuto ? "Optimized" : snapshot.GpuMode switch
            {
                AsusACPI.GPUModeEco => "Eco",
                AsusACPI.GPUModeUltimate => "Ultimate",
                _ => "Standard",
            };
            DrawRow(g, pad, y, w, "GPU", gpuName, "flyout:gpu", OpenGpuMenu, caps.SupportsGpuControl);
            y += 58f;

            int hz = AppConfig.Get("frequency", 0);
            DrawRow(g, pad, y, w, SignalText.T("Display", "Экран"), hz > 0 ? $"{hz} Hz" : "—", "flyout:hz", OpenHzMenu, hz > 0);
            y += 58f;

            int lim = Math.Clamp(AppConfig.Get("charge_limit", 100), 40, 100);
            DrawRow(g, pad, y, w, SignalText.T("Charge limit", "Лимит заряда"), $"{lim}%", "flyout:charge", OpenChargeMenu, true);
            y += 58f;

            if (ShowIdle)
            {
                bool onAc = Gpu.GPUModeControl.IsPlugged();
                int scr = PowerPlan.Get(PowerPlan.Idle.ScreenOff, onAc);
                int slp = PowerPlan.Get(PowerPlan.Idle.Sleep, onAc);
                DrawRow(g, pad, y, w, SignalText.T("Turn screen off", "Экран гаснет"), PowerPlan.Label(scr), "flyout:idle:scr",
                    pt => OpenIdleMenu(pt, PowerPlan.Idle.ScreenOff, onAc), scr >= 0);
                y += 42f;
                DrawRow(g, pad, y, w, SignalText.T("Sleep after", "Спящий режим"), PowerPlan.Label(slp), "flyout:idle:slp",
                    pt => OpenIdleMenu(pt, PowerPlan.Idle.Sleep, onAc), slp >= 0);
                y += 42f;
            }

            // Divider
            using (var pen = new Pen(Color.FromArgb(90, theme.Line), Math.Max(1f, scale)))
                g.DrawLine(pen, pad * scale, y * scale, (DesignW - pad) * scale, y * scale);
            y += 14f;

            // Live CPU & GPU readouts
            string cpuStr = snapshot.CpuTempC is > 0 ? $"CPU  {Math.Round(snapshot.CpuTempC.Value)}°C" : "CPU  —";
            string gpuStr = snapshot.GpuTempC is > 0 ? $"GPU  {Math.Round(snapshot.GpuTempC.Value)}°C"
                : snapshot.DgpuState == DgpuActivityState.OffEco ? "GPU  Eco"
                : snapshot.DgpuState == DgpuActivityState.Sleeping ? "GPU  " + SignalText.T("Sleeping", "Спит")
                : "GPU  —";

            painter.DrawText(g, cpuStr, Px(pad, y, w, 30f), painter.Font(23f, scale, FontStyle.Bold, true), theme.Accent, fmt);
            y += 38f;
            painter.DrawText(g, gpuStr, Px(pad, y, w, 30f), painter.Font(23f, scale, FontStyle.Bold, true), theme.Gpu, fmt);
            y += 42f;

            string batStatus = snapshot.BatteryStatus switch
            {
                BatteryStatusKind.LimitReached => SignalText.T("Limit reached", "Лимит достигнут"),
                BatteryStatusKind.Charging => SignalText.T("Charging", "Зарядка"),
                BatteryStatusKind.Discharging => SignalText.T("Discharging", "Разряд"),
                BatteryStatusKind.PluggedIdle => SignalText.T("Plugged in", "От сети"),
                BatteryStatusKind.OnBattery => SignalText.T("On battery", "От батареи"),
                _ => "—",
            };
            string batLine = snapshot.ChargePct is >= 0
                ? $"{SignalText.T("Battery", "Батарея")} {snapshot.ChargePct}% · {batStatus}"
                : $"{SignalText.T("Battery", "Батарея")} —";
            painter.DrawText(g, batLine, Px(pad, y, w, 22f), painter.Font(13f, scale), theme.Muted, fmt);

            bool hasUpd = (AutoUpdateControl.UpdateAvailable || DriverUpdateCheck.NewCount > 0) && !AppConfig.Is("skip_updates");
            float fullBtnY = TotalDesignH - 64f;
            DrawBtn(g, "flyout:full", new RectangleF(pad, fullBtnY, w, 40f),
                SignalText.T("Open full window", "Открыть полное окно") + (hasUpd ? "  •" : ""),
                () =>
                {
                    AppConfig.Set("signal_compact", 0);
                    TopMost = false;
                    Hide();
                    Program.SignalPage("overview");
                }, false);
        }

        private void DrawRow(Graphics g, float x, float y, float w, string label, string value, string id, Action<Point> onClick, bool enabled)
        {
            bool rtl = SignalText.IsRtl;
            float btnW = 160f;
            painter.DrawText(g, label, Px(rtl ? x + btnW + 8f : x, y + 8f, w - btnW - 8f, 22f),
                painter.Font(13.5f, scale), theme.Text, rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft);

            float bx = rtl ? x : (x + w - btnW);
            var rDip = new RectangleF(bx, y, btnW, 38f);
            var rPx = Px(rDip);
            GlassPainter.DrawControlBox(g, rPx, scale, theme, SignalTheme.EffectiveOpaque, false, enabled && hoverId == id, false, enabled);
            painter.DrawText(g, value, rPx, painter.Font(13f, scale, FontStyle.Bold), enabled ? theme.Text : theme.Muted, GlassPainter.FmtMiddleCenter);
            if (showFocusRing && focusedId == id) GlassPainter.DrawFocusRing(g, rPx, scale, theme, false);

            uiElements.Add(new UiElement
            {
                Id = id,
                BoundsPx = rPx,
                Name = label,
                Value = value,
                Role = AccessibleRole.ComboBox,
                Enabled = enabled,
                Focusable = enabled,
                OnActivate = onClick,
            });
        }

        private void DrawBtn(Graphics g, string id, RectangleF dip, string text, Action onClick, bool selected)
        {
            var rPx = Px(dip);
            GlassPainter.DrawControlBox(g, rPx, scale, theme, SignalTheme.EffectiveOpaque, selected, hoverId == id, false, true);
            painter.DrawText(g, text, rPx, painter.Font(13f, scale, FontStyle.Bold), selected ? theme.OnAccent : theme.Text, GlassPainter.FmtMiddleCenter);
            if (showFocusRing && focusedId == id) GlassPainter.DrawFocusRing(g, rPx, scale, theme, selected);

            uiElements.Add(new UiElement
            {
                Id = id,
                BoundsPx = rPx,
                Name = text,
                Selected = selected,
                OnActivate = _ => onClick(),
            });
        }

        private void OpenGpuMenu(Point pt)
        {
            int cur = AppConfig.Get("gpu_mode");
            bool auto = AppConfig.Is("gpu_auto");
            var items = new List<(string, bool, Action)>();
            if (caps.SupportsEco) items.Add(("Eco", !auto && cur == AsusACPI.GPUModeEco, () => Program.gpuControl.SetGPUMode(AsusACPI.GPUModeEco)));
            items.Add(("Standard", !auto && cur == AsusACPI.GPUModeStandard, () => Program.gpuControl.SetGPUMode(AsusACPI.GPUModeStandard)));
            if (caps.SupportsMuxUltimate) items.Add(("Ultimate", !auto && cur == AsusACPI.GPUModeUltimate, () => Program.gpuControl.SetGPUMode(AsusACPI.GPUModeUltimate)));
            if (caps.SupportsEco) items.Add(("Optimized", auto, () => { AppConfig.Set("gpu_auto", auto ? 0 : 1); Program.settingsForm.VisualiseGPUMode(); Program.gpuControl.AutoGPUMode(true); }));
            ShowPopupMenu(pt, items);
        }

        private void OpenHzMenu(Point pt)
        {
            int cur = AppConfig.Get("frequency", 0);
            bool auto = AppConfig.Is("screen_auto");
            int min = ScreenControl.MIN_RATE;
            int max = AppConfig.Get("max_frequency", 0);
            var items = new List<(string, bool, Action)>
            {
                (SignalText.T("Auto", "Авто"), auto, () => { ScreenControl.SetAutoRefresh(1); ScreenControl.AutoScreen(); }),
                ($"{min} Hz", !auto && cur == min, () => { ScreenControl.SetAutoRefresh(0); ScreenControl.SetScreen(min, 0); }),
            };
            if (max > min)
                items.Add(($"{max} Hz", !auto && cur > min, () => { ScreenControl.SetAutoRefresh(0); ScreenControl.SetScreen(ScreenControl.MAX_REFRESH, 1); }));
            ShowPopupMenu(pt, items);
        }

        private void OpenChargeMenu(Point pt)
        {
            int cur = AppConfig.Get("charge_limit", 100);
            int[] values = AppConfig.IsChargeLimit6080() ? new[] { 60, 80, 100 } : new[] { 50, 60, 70, 80, 90, 100 };
            ShowPopupMenu(pt, values.Select(v => ($"{v}%", cur == v, (Action)(() => BatteryControl.SetBatteryChargeLimit(v)))));
        }

        private void OpenIdleMenu(Point pt, PowerPlan.Idle what, bool onAc)
        {
            int now = PowerPlan.Get(what, onAc);
            ShowPopupMenu(pt, PowerPlan.Spans.Select(s => (PowerPlan.Label(s), s == now, (Action)(() => PowerPlan.Set(what, onAc, s)))));
        }

        private void ShowPopupMenu(Point pt, IEnumerable<(string text, bool isChecked, Action action)> items)
        {
            var menu = new CustomContextMenu { BackColor = theme.Surface, ForeColor = theme.Text };
            foreach (var (text, isChecked, action) in items)
            {
                var mi = new ToolStripMenuItem(text) { Checked = isChecked };
                mi.Click += (_, _) => { try { action(); } catch { } Invalidate(); };
                menu.Items.Add(mi);
            }
            menuOpen = true;
            menu.Closed += (_, _) => { menuOpen = false; if (Visible && !ContainsFocus) Activate(); };
            menu.Show(this, pt);
        }

        private RectangleF Px(float x, float y, float w, float h) => new(x * scale, y * scale, w * scale, h * scale);
        private RectangleF Px(RectangleF r) => new(r.X * scale, r.Y * scale, r.Width * scale, r.Height * scale);

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string hit = "";
            for (int i = uiElements.Count - 1; i >= 0; i--)
                if (uiElements[i].Enabled && uiElements[i].BoundsPx.Contains(e.Location)) { hit = uiElements[i].Id; break; }
            if (hit != hoverId)
            {
                hoverId = hit;
                Cursor = hit.Length > 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            showFocusRing = false;
            for (int i = uiElements.Count - 1; i >= 0; i--)
            {
                var el = uiElements[i];
                if (el.Enabled && el.BoundsPx.Contains(e.Location))
                {
                    focusedId = el.Id;
                    el.OnActivate?.Invoke(e.Location);
                    Invalidate();
                    return;
                }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { TopMost = false; Hide(); return true; }
            var list = uiElements.Where(e => e.Focusable && e.Enabled).ToList();
            if (keyData == Keys.Tab || keyData == (Keys.Shift | Keys.Tab))
            {
                if (list.Count == 0) return true;
                showFocusRing = true;
                int cur = list.FindIndex(e => e.Id == focusedId);
                int step = (keyData & Keys.Shift) == Keys.Shift ? -1 : 1;
                focusedId = list[cur < 0 ? 0 : (cur + step + list.Count) % list.Count].Id;
                Invalidate();
                return true;
            }
            if ((keyData == Keys.Enter || keyData == Keys.Space) && list.FirstOrDefault(e => e.Id == focusedId) is { } el)
            {
                var pt = new Point((int)(el.BoundsPx.X + el.BoundsPx.Width / 2f), (int)(el.BoundsPx.Y + el.BoundsPx.Height / 2f));
                el.OnActivate?.Invoke(pt);
                Invalidate();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { pollTimer.Dispose(); painter.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
