using GHelper.AutoUpdate;
using GHelper.Display;
using GHelper.Mode;
using GHelper.UI.SignalGlass.Pages;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace GHelper.UI.SignalGlass
{
    /// <summary>
    /// Signal Glass main window (1440×1060 DIP base, responsive down to 900×640 DIP, full RTL support).
    /// Implements GDI+ Glass Lite rendering, 60s CPU/GPU ring buffers, constant quick inspector,
    /// local settings search (Ctrl+K), keyboard focus rings, Windows UI Automation, and modal states.
    /// </summary>
    public sealed class SignalGlassForm : RForm
    {
        private readonly ISignalBackend backend;
        private readonly CommandCoordinator commands = new();
        private readonly GlassPainter painter = new();
        private readonly RingHistoryBuffer cpuHistory = new(60);
        private readonly RingHistoryBuffer gpuHistory = new(60);

        private CapabilitySet caps = new();
        private SensorSnapshot snapshot = new();
        private SignalTheme theme = SignalTheme.Dark;
        private SignalShellLayout layout = SignalShellLayout.Compute(1440, 1060, 96f, false, false);

        private readonly Dictionary<string, SignalPage> pages = new();
        private readonly Stack<(string pageId, float scroll)> navHistory = new();
        private SignalPage currentPage;
        private string currentGroup = "overview";
        private string lastTuningPage = "cooling";
        private string lastDevicesPage = "devices";
        private string lastSystemPage = "settings";

        private float scrollDip;
        private bool drawerOpen;
        private string hoverId = "";
        private string pressedId = "";
        private string focusedId = "";
        private bool showFocusRing;
        private Point? mouseClient;

        private (string id, Action<float, float, bool> onDrag, RectangleF trackPx)? activeDrag;
        private int chargeDraftLimit = -1;

        private readonly List<UiElement> uiElements = new();
        private readonly ToolTip toolTip = new() { InitialDelay = 450, ReshowDelay = 200 };
        private string shownTooltip = "";

        // Search overlay (Ctrl+K)
        private bool searchOpen;
        private string searchQuery = "";
        private int searchSelectedIdx;
        private readonly TextBox searchInput = new() { Visible = false, BorderStyle = BorderStyle.None };

        // Toast & Confirm modals
        private SignalToastState? toast;
        private SignalConfirmState? confirmModal;
        private bool menuOpen;

        private readonly System.Windows.Forms.Timer pollTimer = new() { Interval = 1000 };
        private readonly System.Windows.Forms.Timer toastTimer = new() { Interval = 250 };
        private CancellationTokenSource pollCts = new();
        private bool polling;

        public SignalGlassForm(ISignalBackend? customBackend = null)
        {
            backend = customBackend ?? new SignalHardwareAdapter();

            Text = "Crateless";
            try { Icon = SignalAssets.CloneAppIcon() ?? System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { Icon = Properties.Resources.standard; }

            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;

            foreach (SignalPage p in new SignalPage[]
            {
                new OverviewPage(),
                new CoolingPage(),
                new GraphicsPage(),
                new DisplayPage(),
                new BatteryPage(),
                new PowerPage(),
                new LightingPage(),
                new KeysPage(),
                new DevicesPage(),
                new WallpapersPage(),
                new UpdatesPage(),
                new SettingsPage(),
            })
            {
                pages[p.Id] = p;
            }
            currentPage = pages["overview"];

            ConfigureSearchInput();
            Controls.Add(searchInput);

            InitTheme();
            ApplyDpiAndBounds(initial: true);

            commands.CommandCompleted += OnCommandCompleted;
            pollTimer.Interval = Math.Clamp(AppConfig.Get("sensor_timer", 1000), 500, 5000);
            pollTimer.Tick += async (_, _) => await PollOnceAsync();
            toastTimer.Tick += OnToastTick;

            VisibleChanged += (_, _) => OnVisibilityChanged();
            FormClosing += (_, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
            MouseLeave += (_, _) =>
            {
                mouseClient = null;
                if (!string.IsNullOrEmpty(hoverId)) { hoverId = ""; Invalidate(); }
                shownTooltip = "";
                toolTip.Hide(this);
            };
        }

        protected override AccessibleObject CreateAccessibilityInstance() =>
            new SignalAccessibleObject(
                this,
                () => uiElements,
                () => focusedId,
                id => { focusedId = id; showFocusRing = true; Invalidate(); });

        private void ConfigureSearchInput()
        {
            searchInput.TextChanged += (_, _) =>
            {
                searchQuery = searchInput.Text;
                searchSelectedIdx = 0;
                Invalidate();
            };
            searchInput.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    CloseSearch();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
                else if (e.KeyCode == Keys.Down)
                {
                    var hits = MatchingSearchItems();
                    if (hits.Count > 0) searchSelectedIdx = (searchSelectedIdx + 1) % hits.Count;
                    Invalidate();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Up)
                {
                    var hits = MatchingSearchItems();
                    if (hits.Count > 0) searchSelectedIdx = (searchSelectedIdx - 1 + hits.Count) % hits.Count;
                    Invalidate();
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Enter)
                {
                    ActivateSearchItem();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
        }

        private void ApplyDpiAndBounds(bool initial)
        {
            float scale = Math.Max(1f, DeviceDpi / 96f);
            MinimumSize = new Size((int)Math.Ceiling(SignalTheme.MinWindowW * scale), (int)Math.Ceiling(SignalTheme.MinWindowH * scale));
            if (initial)
            {
                var wa = (Screen.PrimaryScreen ?? Screen.FromControl(this)).WorkingArea;
                int w = (int)Math.Min(SignalTheme.BaseWindowW * scale, wa.Width - 36);
                int h = (int)Math.Min(SignalTheme.BaseWindowH * scale, wa.Height - 36);
                w = Math.Max(MinimumSize.Width, w);
                h = Math.Max(MinimumSize.Height, h);
                Size = new Size(w, h);
                Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
            }
            UpdateLayout();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            painter.InvalidateBackdrop();
            ApplyDpiAndBounds(initial: false);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateLayout();
            PositionSearchBox();
            Invalidate();
        }

        private void UpdateLayout()
        {
            layout = SignalShellLayout.Compute(ClientSize.Width, ClientSize.Height, DeviceDpi, SignalText.IsRtl, drawerOpen);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            OnThemeChanged();
        }

        public void OnThemeChanged()
        {
            InitTheme();
            theme = SignalTheme.ForMode(darkTheme);
            BackColor = theme.Bg;
            painter.InvalidateBackdrop();
            searchInput.BackColor = theme.Plot;
            searchInput.ForeColor = theme.Text;
            Invalidate();
        }

        public void OnLanguageChanged()
        {
            UpdateLayout();
            PositionSearchBox();
            currentPage.OnEnter(null);
            Invalidate();
        }

        public void UpdatePollingInterval(int ms)
        {
            pollTimer.Interval = Math.Clamp(ms, 500, 5000);
        }

        public void Toggle()
        {
            if (Visible)
            {
                if (ContainsFocus) Hide();
                else Activate();
            }
            else
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Show();
                Activate();
            }
        }

        public void ShowPage(string pageId)
        {
            if (!Visible)
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Show();
            }
            NavigateTo(pageId, recordHistory: true);
            Activate();
        }

        public void NavigateTo(string pageId, bool recordHistory = true)
        {
            if (!pages.TryGetValue(pageId, out var next)) return;
            if (recordHistory && currentPage.Id != pageId)
                navHistory.Push((currentPage.Id, scrollDip));

            currentPage = next;
            currentGroup = next.GroupId;
            if (next.GroupId == "tuning") lastTuningPage = next.Id;
            else if (next.GroupId == "devices") lastDevicesPage = next.Id;
            else if (next.GroupId == "system") lastSystemPage = next.Id;

            scrollDip = 0;
            drawerOpen = false;
            CloseSearch();
            UpdateLayout();
            currentPage.OnEnter(BuildContext(null!));
            Invalidate();
        }

        private void NavigateBack()
        {
            if (navHistory.Count == 0) return;
            var (prevId, prevScroll) = navHistory.Pop();
            if (!pages.TryGetValue(prevId, out var prev)) return;
            currentPage = prev;
            currentGroup = prev.GroupId;
            scrollDip = prevScroll;
            currentPage.OnEnter(BuildContext(null!));
            Invalidate();
        }

        private void OnVisibilityChanged()
        {
            if (Visible)
            {
                OnThemeChanged();
                _ = RefreshCapabilitiesAndSensorsAsync();
                pollTimer.Start();
            }
            else
            {
                // Zero background animation/UI invalidation while hidden
                pollTimer.Stop();
                toastTimer.Stop();
                pollCts.Cancel();
                pollCts.Dispose();
                pollCts = new CancellationTokenSource();
            }
        }

        private async Task RefreshCapabilitiesAndSensorsAsync()
        {
            try
            {
                caps = await backend.GetCapabilitiesAsync(pollCts.Token);
                currentPage.OnEnter(null);
            }
            catch { }
            await PollOnceAsync();
        }

        private async Task PollOnceAsync()
        {
            if (polling || !Visible || IsDisposed) return;
            polling = true;
            try
            {
                var snap = await backend.ReadSensorsAsync(pollCts.Token);
                if (IsDisposed) return;

                if (snap.Status == SampleStatus.Error && snapshot.Timestamp != DateTimeOffset.MinValue)
                {
                    snapshot = snapshot with { Status = SampleStatus.Stale, ErrorMessage = snap.ErrorMessage };
                    cpuHistory.Append(null);
                    gpuHistory.Append(null);
                }
                else
                {
                    snapshot = snap;
                    cpuHistory.Append(snap.CpuTempC);
                    gpuHistory.Append(snap.DgpuState == DgpuActivityState.Active ? snap.GpuTempC : null);

                    if (!commands.IsPending("performance_mode"))
                        commands.SeedObserved("performance_mode", snap.PerformanceMode.ToString());
                    if (!commands.IsPending("charge_limit") && chargeDraftLimit < 0)
                        commands.SeedObserved("charge_limit", snap.ChargeLimitPct.ToString());
                }

                Invalidate();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.WriteLine("Signal poll: " + ex.Message);
            }
            finally
            {
                polling = false;
            }
        }

        private void OnCommandCompleted(string capabilityId, CommandResult result)
        {
            if (IsDisposed) return;
            BeginInvoke(() =>
            {
                if (result.Success)
                {
                    if (capabilityId == "charge_limit")
                    {
                        chargeDraftLimit = -1;
                        int prev = snapshot.ChargeLimitPct;
                        ShowToast(new SignalToastState
                        {
                            Title = SignalText.T("Settings saved", "Настройки сохранены"),
                            Message = SignalText.Tf("Charge limit set to {0}%.", "Лимит заряда: {0}%.", result.ConfirmedValue),
                            ActionLabel = SignalText.T("Undo", "Отменить"),
                            OnAction = () => _ = commands.DispatchAsync(backend, "charge_limit", prev.ToString()),
                        });
                    }
                }
                else
                {
                    chargeDraftLimit = -1;
                    ShowToast(new SignalToastState
                    {
                        Title = SignalText.T("Could not apply setting", "Не удалось применить параметр"),
                        Message = string.IsNullOrEmpty(result.ErrorMessage) ? result.ErrorCode : result.ErrorMessage,
                        IsError = true,
                        ActionLabel = SignalText.T("Retry", "Повторить"),
                        OnAction = () => _ = commands.DispatchAsync(backend, capabilityId, commands.GetVisibleValue(capabilityId)),
                    });
                }
                Invalidate();
            });
        }

        private void ShowToast(SignalToastState t)
        {
            t.RemainingMs = 8000;
            t.ExpiresAtTick = Environment.TickCount64 + 8000;
            toast = t;
            if (!t.IsError) toastTimer.Start();
            else toastTimer.Stop();
            Invalidate();
        }

        private void OnToastTick(object? sender, EventArgs e)
        {
            if (toast is null || toast.IsError) { toastTimer.Stop(); return; }
            bool paused = (!string.IsNullOrEmpty(hoverId) && hoverId.StartsWith("toast:"))
                       || (!string.IsNullOrEmpty(focusedId) && focusedId.StartsWith("toast:"));
            if (paused)
            {
                toast.ExpiresAtTick = Environment.TickCount64 + toast.RemainingMs;
                return;
            }
            long rem = toast.ExpiresAtTick - Environment.TickCount64;
            toast.RemainingMs = rem;
            if (rem <= 0)
            {
                toast = null;
                toastTimer.Stop();
                Invalidate();
            }
        }

        private SignalPaintContext BuildContext(Graphics g) => new()
        {
            G = g,
            Theme = theme,
            Painter = painter,
            Layout = layout,
            Dark = darkTheme,
            Opaque = SignalTheme.EffectiveOpaque,
            Rtl = layout.Rtl,
            Snapshot = snapshot,
            Caps = caps,
            Backend = backend,
            Commands = commands,
            CpuHistory = cpuHistory,
            GpuHistory = gpuHistory,
            HoverId = hoverId,
            PressedId = pressedId,
            FocusedId = focusedId,
            ShowFocusRing = showFocusRing,
            MouseClient = mouseClient,
            ScrollDip = scrollDip,
            Elements = uiElements,
            Navigate = id => NavigateTo(id, true),
            Toast = (title, msg, err) => ShowToast(new SignalToastState { Title = title, Message = msg, IsError = err }),
            ShowToast = ShowToast,
            Confirm = modal =>
            {
                modal.ReturnFocusId = focusedId;
                confirmModal = modal;
                focusedId = "confirm:cancel";
                showFocusRing = true;
                Invalidate();
            },
            ShowMenu = (pt, items) => OpenContextMenu(pt, items),
            RequestRefresh = () => { if (IsHandleCreated && !IsDisposed) BeginInvoke(() => Invalidate()); },
            RequestFocus = id => { focusedId = id; showFocusRing = true; Invalidate(); },
            StartSliderDrag = d => activeDrag = d,
        };

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            uiElements.Clear();
            bool opaque = SignalTheme.EffectiveOpaque;
            painter.DrawBackdrop(g, ClientSize, layout.Scale, theme, darkTheme, opaque);

            var ctx = BuildContext(g);
            PaintTitlebar(ctx);
            PaintTopNav(ctx);
            PaintWorkspaceHeader(ctx);

            // Clipped page body (pages without a footer may use the full workspace height)
            var savedClip = g.Clip;
            var bodyClip = currentPage.HasFooter
                ? layout.WorkspaceBody
                : new RectangleF(layout.WorkspaceBody.X, layout.WorkspaceBody.Y, layout.WorkspaceBody.Width, layout.Workspace.Bottom - layout.WorkspaceBody.Y + 6f);
            var bodyClipPx = ctx.Px(bodyClip);
            g.SetClip(bodyClipPx);
            ctx.ActiveClipPx = bodyClipPx;
            try { currentPage.Paint(ctx); }
            catch (Exception ex) { Logger.WriteLine($"Signal paint {currentPage.Id}: {ex.Message}"); }
            finally { ctx.ActiveClipPx = null; }
            g.Clip = savedClip;

            PaintScrollbarIfNeeded(ctx);
            if (currentPage.HasFooter) PaintWorkspaceFooter(ctx);
            if (!layout.CompactWidth || drawerOpen) PaintInspector(ctx);
            if (searchOpen) PaintSearchDropdown(ctx);
            if (toast is not null) PaintToast(ctx, toast);
            if (confirmModal is not null) PaintConfirmModal(ctx, confirmModal);
        }

        private void PaintTitlebar(SignalPaintContext ctx)
        {
            float sc = ctx.Scale;
            var tb = layout.Titlebar;
            var logo = SignalAssets.Logo((int)Math.Round(20f * sc));
            float xStart = layout.Rtl ? (tb.Width - 24f - 20f) : 24f;
            if (logo is not null)
                ctx.G.DrawImage(logo, xStart * sc, 11f * sc, 20f * sc, 20f * sc);

            var titleRect = layout.Rtl
                ? ctx.Px(tb.Width - 220f, 10f, 166f, 24f)
                : ctx.Px(52f, 10f, 180f, 24f);
            ctx.Painter.DrawText(ctx.G, "Crateless", titleRect, ctx.Painter.Font(14f, sc, FontStyle.Bold),
                theme.Text, layout.Rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft);

            float rightX = layout.Rtl ? 24f : (tb.Width - 24f);
            float btnW = 110f;
            float fx = layout.Rtl ? rightX : (rightX - btnW);
            ctx.DrawButton("title:flyout", new RectangleF(fx, 6f, btnW, 31f),
                SignalText.T("Fly-out", "Мини-окно"),
                _ => { Hide(); Program.SignalFlyoutToggle(); }, fontSize: 12f);

            if (layout.CompactWidth)
            {
                float dx = layout.Rtl ? (fx + btnW + 8f) : (fx - 124f);
                ctx.DrawButton("title:drawer", new RectangleF(dx, 6f, 116f, 31f),
                    SignalText.T("Quick control", "Управление"),
                    _ => { drawerOpen = !drawerOpen; UpdateLayout(); }, selected: drawerOpen, fontSize: 12f);
            }

            if (navHistory.Count > 0)
            {
                float bx = layout.Rtl ? (tb.Width - 310f) : 168f;
                ctx.DrawButton("title:back", new RectangleF(bx, 6f, 84f, 31f),
                    (layout.Rtl ? "→ " : "← ") + SignalText.T("Back", "Назад"),
                    _ => NavigateBack(), fontSize: 12f);
            }
        }

        private void PaintTopNav(SignalPaintContext ctx)
        {
            var nav = layout.TopNav;
            ctx.DrawPanel(nav, SignalTheme.RadiusPill);

            var groups = new[]
            {
                ("overview", SignalText.T("Overview", "Обзор"), "overview"),
                ("tuning",   SignalText.T("Tuning", "Настройка"), lastTuningPage),
                ("devices",  SignalText.T("Devices", "Устройства"), lastDevicesPage),
                ("system",   SignalText.T("System", "Система"), lastSystemPage),
            };

            float tabW = layout.CompactSearch ? 96f : 108f;
            float tabGap = 6f;
            float btnH = Math.Min(38f, nav.Height - 12f);
            float btnY = nav.Y + (nav.Height - btnH) / 2f;
            float x = layout.Rtl ? (nav.Right - 14f - tabW) : (nav.X + 14f);
            for (int i = 0; i < groups.Length; i++)
            {
                var (gid, title, target) = groups[i];
                bool sel = currentGroup == gid;
                ctx.DrawButton("nav:" + gid, new RectangleF(x, btnY, tabW, btnH), title,
                    _ => NavigateTo(target, true), selected: sel, role: AccessibleRole.PageTab);
                x += layout.Rtl ? -(tabW + tabGap) : (tabW + tabGap);
            }

            var sb = layout.SearchBox;
            var sbPx = ctx.Px(sb);
            using (var sp = GlassPainter.Rounded(sbPx, 14f * ctx.Scale))
            using (var sFill = new SolidBrush(theme.Plot))
            using (var sPen = new Pen(searchOpen ? theme.Accent : theme.Line, Math.Max(1f, ctx.Scale)))
            {
                ctx.G.FillPath(sFill, sp);
                ctx.G.DrawPath(sPen, sp);
            }

            var searchIcon = SignalAssets.IconScaled("search", darkTheme, (int)Math.Round(18f * ctx.Scale), theme.Muted);
            if (searchIcon is not null)
            {
                float ix = layout.CompactSearch ? (sb.X + 10f) : (layout.Rtl ? sb.Right - 28f : sb.X + 12f);
                ctx.G.DrawImage(searchIcon, ix * ctx.Scale, (sb.Y + 10f) * ctx.Scale, 18f * ctx.Scale, 18f * ctx.Scale);
            }

            if (!layout.CompactSearch && !searchOpen)
            {
                var tr = layout.Rtl
                    ? ctx.Px(sb.X + 12f, sb.Y + 9f, sb.Width - 48f, 22f)
                    : ctx.Px(sb.X + 38f, sb.Y + 9f, sb.Width - 50f, 22f);
                ctx.Painter.DrawText(ctx.G, SignalText.T("Search settings…", "Поиск настроек…") + "  Ctrl+K",
                    tr, ctx.Painter.Font(12.5f, ctx.Scale), theme.Muted, layout.Rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft);
            }

            if (ctx.IsFocused("nav:search"))
                GlassPainter.DrawFocusRing(ctx.G, sbPx, ctx.Scale, theme, false, 14f);

            ctx.Register(new UiElement
            {
                Id = "nav:search",
                BoundsPx = sbPx,
                Name = SignalText.T("Search settings…", "Поиск настроек…"),
                Tooltip = "Ctrl+K",
                Role = AccessibleRole.Text,
                RadiusDip = 14f,
                OnActivate = _ => OpenSearch(),
            });
        }

        private void PaintWorkspaceHeader(SignalPaintContext ctx)
        {
            var hdr = layout.WorkspaceHeader;
            var fmt = layout.Rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft;
            ctx.Painter.DrawText(ctx.G, currentPage.Title, ctx.Px(hdr.X, hdr.Y, hdr.Width, 36f),
                ctx.Painter.Font(26f, ctx.Scale, FontStyle.Bold, true), theme.Text, fmt);
            ctx.Painter.DrawText(ctx.G, currentPage.Subtitle, ctx.Px(hdr.X, hdr.Y + 36f, hdr.Width, 22f),
                ctx.Painter.Font(13f, ctx.Scale), snapshot.IsStale ? theme.Warning : theme.Muted, fmt);

            if (snapshot.Status == SampleStatus.Error && snapshot.Timestamp == DateTimeOffset.MinValue)
            {
                var rb = new RectangleF(layout.Rtl ? hdr.X : hdr.Right - 96f, hdr.Y + 12f, 96f, 34f);
                ctx.DrawButton("hdr:retry", rb, SignalText.T("Retry", "Повторить"), pt => { _ = PollOnceAsync(); });
            }
        }

        private float PageViewHeightDip() => currentPage.HasFooter
            ? layout.WorkspaceBody.Height
            : Math.Max(layout.WorkspaceBody.Height, layout.Workspace.Bottom - layout.WorkspaceBody.Y);

        private void PaintScrollbarIfNeeded(SignalPaintContext ctx)
        {
            float contentH = currentPage.ContentHeightDip(ctx);
            float viewH = PageViewHeightDip();
            if (contentH <= viewH + 2f) { scrollDip = 0; return; }

            float maxScroll = contentH - viewH;
            scrollDip = Math.Clamp(scrollDip, 0, maxScroll);
            float trackX = layout.Rtl ? (layout.WorkspaceBody.X - 8f) : (layout.WorkspaceBody.Right + 4f);
            float thumbH = Math.Max(32f, viewH * viewH / contentH);
            float thumbY = layout.WorkspaceBody.Y + (viewH - thumbH) * (scrollDip / maxScroll);
            var rPx = ctx.Px(trackX, thumbY, 4f, thumbH);
            using var b = new SolidBrush(Color.FromArgb(140, theme.Line));
            using var p = GlassPainter.Rounded(rPx, 2f * ctx.Scale);
            ctx.G.FillPath(b, p);
        }

        private void PaintWorkspaceFooter(SignalPaintContext ctx)
        {
            var f = layout.WorkspaceFooter;
            var fmt = layout.Rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft;
            var textRect = layout.Rtl
                ? ctx.Px(f.X + 270f, f.Y + 12f, Math.Max(100f, f.Width - 280f), 22f)
                : ctx.Px(f.X + 8f, f.Y + 12f, Math.Max(100f, f.Width - 280f), 22f);
            ctx.Painter.DrawText(ctx.G,
                SignalText.T("Changes are validated before writing to hardware.", "Изменения проверяются перед записью в оборудование."),
                textRect, ctx.Painter.Font(12f, ctx.Scale), theme.Muted, fmt);

            if (currentPage.CanReset || currentPage.CanApply)
            {
                float resetX = layout.Rtl ? f.X : (f.Right - 252f);
                float applyX = layout.Rtl ? (f.X + 124f) : (f.Right - 128f);
                if (currentPage.CanReset)
                    ctx.DrawButton("footer:reset", new RectangleF(resetX, f.Y + 4f, 112f, 38f),
                        SignalText.T("Reset", "Сбросить"), _ => currentPage.Reset());
                if (currentPage.CanApply)
                    ctx.DrawButton("footer:apply", new RectangleF(applyX, f.Y + 4f, 128f, 38f),
                        SignalText.T("Apply", "Применить"), _ => { currentPage.Apply(); ShowToast(new SignalToastState { Title = SignalText.T("Settings saved", "Настройки сохранены"), Message = currentPage.Title }); }, selected: true);
            }
        }

        private void PaintInspector(SignalPaintContext ctx)
        {
            var ins = layout.Inspector;
            ctx.DrawPanel(ins);

            float pad = 22f;
            float ix = ins.X + pad;
            float iw = ins.Width - pad * 2f;
            float y = ins.Y + 20f;
            var fmt = layout.Rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft;

            ctx.Painter.DrawText(ctx.G, SignalText.T("Quick control", "Управление"),
                ctx.Px(ix, y, iw - 36f, 28f),
                ctx.Painter.Font(20f, ctx.Scale, FontStyle.Bold, true), theme.Text, fmt);

            float gearX = layout.Rtl ? ix : (ins.Right - pad - 30f);
            ctx.DrawButton("insp:settings", new RectangleF(gearX, y - 2f, 30f, 30f), "⚙", _ => NavigateTo("settings", true),
                tooltip: SignalText.T("Settings", "Настройки"), radiusDip: 8f, fontSize: 13f);
            y += 42f;

            // Performance modes (Silent / Balanced / Turbo)
            ctx.Painter.DrawText(ctx.G, SignalText.T("Performance mode", "Режим работы"),
                ctx.Px(ix, y, iw, 20f), ctx.Painter.Font(13f, ctx.Scale), theme.Muted, fmt);
            y += 24f;

            var list = Modes.GetList();
            int curMode = snapshot.Timestamp != DateTimeOffset.MinValue ? snapshot.PerformanceMode : Modes.GetCurrent();
            int n = Math.Min(3, list.Count);
            float mGap = 6f;
            float mw = (iw - mGap * (n - 1)) / Math.Max(1, n);
            for (int i = 0; i < n; i++)
            {
                int m = list[i];
                float mx = ix + i * (mw + mGap);
                ctx.DrawButton("insp:mode:" + m, new RectangleF(mx, y, mw, 36f), Modes.GetName(m),
                    pt => { _ = commands.DispatchAsync(backend, "performance_mode", m.ToString()); },
                    selected: m == curMode,
                    enabled: !commands.IsPending("performance_mode"),
                    fontSize: 12f);
            }
            y += 48f;

            // GPU mode row
            string gpuName = snapshot.GpuAuto ? "Optimized" : snapshot.GpuMode switch
            {
                AsusACPI.GPUModeEco => "Eco",
                AsusACPI.GPUModeUltimate => "Ultimate",
                _ => "Standard",
            };
            float btnW = Math.Min(138f, iw * 0.48f);
            float lblX = layout.Rtl ? (ix + btnW + 8f) : ix;
            ctx.Painter.DrawText(ctx.G, SignalText.T("GPU mode", "Режим GPU"), ctx.Px(lblX, y + 8f, iw - btnW - 8f, 22f),
                ctx.Painter.Font(13f, ctx.Scale), theme.Muted, fmt);
            float gx = layout.Rtl ? ix : (ix + iw - btnW);
            ctx.DrawButton("insp:gpu", new RectangleF(gx, y, btnW, 36f), gpuName + "  ⌄",
                pt => OpenInspectorGpuMenu(ctx, pt), enabled: caps.SupportsGpuControl, role: AccessibleRole.ComboBox, fontSize: 12f);
            y += 48f;

            // Refresh rate row
            int hz = AppConfig.Get("frequency", 0);
            ctx.Painter.DrawText(ctx.G, SignalText.T("Refresh rate", "Частота"), ctx.Px(lblX, y + 8f, iw - btnW - 8f, 22f),
                ctx.Painter.Font(13f, ctx.Scale), theme.Muted, fmt);
            ctx.DrawButton("insp:hz", new RectangleF(gx, y, btnW, 36f), (hz > 0 ? hz + " Hz" : "—") + "  ⌄",
                pt => OpenInspectorHzMenu(ctx, pt), enabled: hz > 0, role: AccessibleRole.ComboBox, fontSize: 12f);
            y += 50f;

            // Charge limit slider
            int minLim = caps.BatteryLimitMin;
            int confirmedLim = Math.Clamp(AppConfig.Get("charge_limit", 100), minLim, 100);
            int visLim = chargeDraftLimit >= 0 ? chargeDraftLimit : confirmedLim;
            ctx.Painter.DrawText(ctx.G, SignalText.T("Charge limit", "Лимит заряда"), ctx.Px(ix, y, iw - 60f, 20f),
                ctx.Painter.Font(13f, ctx.Scale), theme.Muted, fmt);
            ctx.Painter.DrawText(ctx.G, visLim + "%", ctx.Px(layout.Rtl ? ix : ix + iw - 60f, y, 60f, 20f),
                ctx.Painter.Font(14f, ctx.Scale, FontStyle.Bold), theme.Text, layout.Rtl ? GlassPainter.FmtLeft : GlassPainter.FmtRight);
            y += 24f;

            ctx.DrawSlider("insp:charge", new RectangleF(ix, y, iw, 20f), SignalText.T("Charge limit", "Лимит заряда"),
                (visLim - minLim) / (float)Math.Max(1, 100 - minLim), visLim + "%",
                (t, done) =>
                {
                    int val = minLim + (int)Math.Round(t * (100 - minLim) / 5f) * 5;
                    if (caps.BatteryDiscrete6080) val = val <= 70 ? 60 : val <= 90 ? 80 : 100;
                    chargeDraftLimit = Math.Clamp(val, minLim, 100);
                    if (done) _ = commands.DispatchAsync(backend, "charge_limit", chargeDraftLimit.ToString());
                },
                delta =>
                {
                    int next = Math.Clamp(visLim + delta * 5, minLim, 100);
                    if (caps.BatteryDiscrete6080) next = next <= 70 ? 60 : next <= 90 ? 80 : 100;
                    chargeDraftLimit = next;
                    _ = commands.DispatchAsync(backend, "charge_limit", next.ToString());
                });
            y += 38f;

            // Status lines (embedded vector PNG icons instead of ClearType-tofu emojis)
            string acTxt = snapshot.IsPlugged
                ? (snapshot.BatteryStatus == BatteryStatusKind.LimitReached ? SignalText.T("Plugged in", "От сети") + " · " + SignalText.T("Limit reached", "Лимит достигнут") : SignalText.T("Plugged in", "От сети"))
                : SignalText.T("On battery", "От батареи");
            string devTxt = snapshot.ConnectedDevicesCount == 0
                ? SignalText.T("No compatible devices", "Нет устройств")
                : snapshot.PrimaryDeviceSummary;

            int stIconPx = (int)Math.Round(15f * ctx.Scale);
            float stIconX = layout.Rtl ? (ix + iw - 15f) : ix;
            float stTextX = layout.Rtl ? ix : (ix + 21f);
            float stTextW = iw - 21f;

            var pwrIc = SignalAssets.IconScaled("power", darkTheme, stIconPx, theme.Muted);
            if (pwrIc is not null) ctx.G.DrawImage(pwrIc, stIconX * ctx.Scale, (y + 2f) * ctx.Scale, 15f * ctx.Scale, 15f * ctx.Scale);
            ctx.Painter.DrawText(ctx.G, acTxt, ctx.Px(stTextX, y, stTextW, 20f), ctx.Painter.Font(12f, ctx.Scale), theme.Muted, fmt);
            y += 24f;

            var devIc = SignalAssets.IconScaled("mouse", darkTheme, stIconPx, theme.Muted);
            if (devIc is not null) ctx.G.DrawImage(devIc, stIconX * ctx.Scale, (y + 2f) * ctx.Scale, 15f * ctx.Scale, 15f * ctx.Scale);
            ctx.Painter.DrawText(ctx.G, devTxt, ctx.Px(stTextX, y, stTextW, 20f), ctx.Painter.Font(12f, ctx.Scale), theme.Muted, fmt);
            y += 30f;

            // Quick section jump list
            bool hasUpdates = (AutoUpdateControl.UpdateAvailable || DriverUpdateCheck.NewCount > 0) && !AppConfig.Is("skip_updates");
            float availListH = Math.Max(120f, ins.Bottom - 16f - y);
            float rowH = Math.Clamp(availListH / SignalRegistry.InspectorTargets.Length, 28f, 43f);

            foreach (var item in SignalRegistry.InspectorTargets)
            {
                bool active = currentPage.Id == item.PageId;
                var rowDip = new RectangleF(ix, y, iw, rowH - 2f);
                var rowPx = ctx.Px(rowDip);

                if (active || ctx.IsHover("insp:nav:" + item.PageId))
                {
                    using var p = GlassPainter.Rounded(rowPx, 8f * ctx.Scale);
                    using var b = new SolidBrush(active ? Color.FromArgb(42, theme.Accent) : Color.FromArgb(140, theme.Raised));
                    ctx.G.FillPath(b, p);
                }

                var ic = SignalAssets.IconScaled(item.Icon, darkTheme, (int)Math.Round(18f * ctx.Scale), active ? theme.Accent : theme.Muted);
                if (ic is not null)
                {
                    float iconX = layout.Rtl ? (rowDip.Right - 24f) : (rowDip.X + 6f);
                    ctx.G.DrawImage(ic, iconX * ctx.Scale, (rowDip.Y + (rowDip.Height - 18f) / 2f) * ctx.Scale, 18f * ctx.Scale, 18f * ctx.Scale);
                }

                string title = SignalText.T(item.TitleEn, item.TitleRu);
                var tr = layout.Rtl
                    ? ctx.Px(rowDip.X + 24f, rowDip.Y + (rowDip.Height - 18f) / 2f, rowDip.Width - 56f, 20f)
                    : ctx.Px(rowDip.X + 32f, rowDip.Y + (rowDip.Height - 18f) / 2f, rowDip.Width - 56f, 20f);
                ctx.Painter.DrawText(ctx.G, title, tr, ctx.Painter.Font(13f, ctx.Scale, active ? FontStyle.Bold : FontStyle.Regular),
                    active ? theme.Accent : theme.Text, fmt);

                if (item.PageId == "updates" && hasUpdates)
                {
                    float dotX = layout.Rtl ? (rowDip.X + 22f) : (rowDip.Right - 30f);
                    using var db = new SolidBrush(theme.Warning);
                    ctx.G.FillEllipse(db, dotX * ctx.Scale, (rowDip.Y + rowDip.Height / 2f - 4f) * ctx.Scale, 8f * ctx.Scale, 8f * ctx.Scale);
                }

                ctx.Painter.DrawText(ctx.G, layout.Rtl ? "‹" : "›",
                    ctx.Px(layout.Rtl ? rowDip.X + 4f : rowDip.Right - 18f, rowDip.Y + (rowDip.Height - 20f) / 2f, 14f, 20f),
                    ctx.Painter.Font(16f, ctx.Scale), theme.Muted, GlassPainter.FmtCenter);

                if (ctx.IsFocused("insp:nav:" + item.PageId))
                    GlassPainter.DrawFocusRing(ctx.G, rowPx, ctx.Scale, theme, false, 8f);

                var targetId = item.PageId;
                ctx.Register(new UiElement
                {
                    Id = "insp:nav:" + targetId,
                    BoundsPx = rowPx,
                    Name = title,
                    Selected = active,
                    RadiusDip = 8f,
                    OnActivate = _ => NavigateTo(targetId, true),
                });

                y += rowH;
            }
        }

        private void PaintSearchDropdown(SignalPaintContext ctx)
        {
            var hits = MatchingSearchItems();
            var sb = layout.SearchBox;
            float dropW = Math.Max(360f, sb.Width);
            float dropX = layout.Rtl ? sb.X : Math.Max(layout.TopNav.X, sb.Right - dropW);
            float dropY = sb.Bottom + 8f;
            float rowH = 54f;
            float dropH = Math.Max(80f, Math.Min(6, hits.Count) * rowH + 24f);
            var panel = new RectangleF(dropX, dropY, dropW, dropH);
            ctx.DrawPanel(panel);

            if (hits.Count == 0)
            {
                ctx.Painter.DrawText(ctx.G, SignalText.T("No matching settings", "Ничего не найдено"),
                    ctx.Px(panel.X + 20f, panel.Y + 28f, dropW - 40f, 24f),
                    ctx.Painter.Font(13f, ctx.Scale), theme.Muted, GlassPainter.FmtCenter);
                return;
            }

            float ry = panel.Y + 12f;
            for (int i = 0; i < Math.Min(6, hits.Count); i++)
            {
                var h = hits[i];
                bool sel = i == searchSelectedIdx;
                var rDip = new RectangleF(panel.X + 12f, ry, dropW - 24f, rowH - 4f);
                var rPx = ctx.Px(rDip);
                if (sel || ctx.IsHover("search:hit:" + i))
                {
                    using var p = GlassPainter.Rounded(rPx, 8f * ctx.Scale);
                    using var b = new SolidBrush(Color.FromArgb(40, theme.Accent));
                    ctx.G.FillPath(b, p);
                }
                string t = SignalText.T(h.TitleEn, h.TitleRu);
                string sub = SignalText.T(h.SubtitleEn, h.SubtitleRu);
                ctx.Painter.DrawText(ctx.G, t, ctx.Px(rDip.X + 12f, rDip.Y + 6f, rDip.Width - 24f, 20f),
                    ctx.Painter.Font(13.5f, ctx.Scale, FontStyle.Bold), sel ? theme.Accent : theme.Text, GlassPainter.FmtLeft);
                ctx.Painter.DrawText(ctx.G, sub, ctx.Px(rDip.X + 12f, rDip.Y + 27f, rDip.Width - 24f, 18f),
                    ctx.Painter.Font(11.5f, ctx.Scale), theme.Muted, GlassPainter.FmtLeft);

                var item = h;
                ctx.Register(new UiElement
                {
                    Id = "search:hit:" + i,
                    BoundsPx = rPx,
                    Name = t,
                    OnActivate = _ =>
                    {
                        CloseSearch();
                        NavigateTo(item.PageId, true);
                        if (!string.IsNullOrEmpty(item.ControlId)) { focusedId = item.ControlId; showFocusRing = true; }
                    },
                });
                ry += rowH;
            }
        }

        private void PaintToast(SignalPaintContext ctx, SignalToastState t)
        {
            float tw = Math.Min(460f, layout.Workspace.Width - 32f);
            float th = 76f;
            float tx = layout.Rtl ? layout.Workspace.X + 16f : layout.Workspace.Right - tw - 16f;
            float ty = layout.WorkspaceFooter.Top - th - 12f;
            var rect = new RectangleF(tx, ty, tw, th);
            ctx.DrawPanel(rect, 14f);

            using (var bar = new SolidBrush(t.IsError ? theme.Danger : theme.Accent))
                ctx.G.FillRectangle(bar, (tx + 12f) * ctx.Scale, (ty + 14f) * ctx.Scale, 4f * ctx.Scale, (th - 28f) * ctx.Scale);

            ctx.Painter.DrawText(ctx.G, t.Title, ctx.Px(tx + 26f, ty + 14f, tw - 140f, 22f),
                ctx.Painter.Font(14f, ctx.Scale, FontStyle.Bold), theme.Text, GlassPainter.FmtLeft);
            ctx.Painter.DrawText(ctx.G, t.Message, ctx.Px(tx + 26f, ty + 38f, tw - 140f, 22f),
                ctx.Painter.Font(12f, ctx.Scale), theme.Muted, GlassPainter.FmtLeft);

            if (!string.IsNullOrEmpty(t.ActionLabel) && t.OnAction is not null)
            {
                ctx.DrawButton("toast:action", new RectangleF(rect.Right - 110f, ty + 19f, 96f, 36f),
                    t.ActionLabel, _ => { var a = t.OnAction; toast = null; a(); }, selected: true, fontSize: 12f);
            }
            else
            {
                ctx.DrawButton("toast:dismiss", new RectangleF(rect.Right - 46f, ty + 20f, 32f, 32f),
                    "×", _ => toast = null, fontSize: 14f);
            }
        }

        private void PaintConfirmModal(SignalPaintContext ctx, SignalConfirmState m)
        {
            using (var dim = new SolidBrush(Color.FromArgb(145, 0, 0, 0)))
                ctx.G.FillRectangle(dim, 0, 0, layout.WindowW * ctx.Scale, layout.WindowH * ctx.Scale);

            float mw = Math.Min(560f, layout.WindowW - 48f);
            float mh = 230f;
            var modal = new RectangleF((layout.WindowW - mw) / 2f, (layout.WindowH - mh) / 2f, mw, mh);
            ctx.DrawPanel(modal);

            float pad = 24f;
            ctx.Painter.DrawText(ctx.G, m.Title, ctx.Px(modal.X + pad, modal.Y + 28f, mw - pad * 2f, 32f),
                ctx.Painter.Font(20f, ctx.Scale, FontStyle.Bold), theme.Text, GlassPainter.FmtLeft);
            ctx.Painter.DrawText(ctx.G, m.Message, ctx.Px(modal.X + pad, modal.Y + 72f, mw - pad * 2f, 68f),
                ctx.Painter.Font(14f, ctx.Scale), theme.Muted, GlassPainter.FmtWrap);

            // Cancel is default focused button per COMPONENTS.md
            ctx.DrawButton("confirm:cancel", new RectangleF(modal.Right - pad - 244f, modal.Bottom - 62f, 114f, 38f),
                string.IsNullOrEmpty(m.CancelLabel) ? SignalText.T("Cancel", "Отмена") : m.CancelLabel,
                _ => CloseConfirm(false));
            ctx.DrawButton("confirm:ok", new RectangleF(modal.Right - pad - 118f, modal.Bottom - 62f, 118f, 38f),
                string.IsNullOrEmpty(m.ConfirmLabel) ? SignalText.T("Confirm", "Подтвердить") : m.ConfirmLabel,
                _ => CloseConfirm(true), selected: true);
        }

        private void CloseConfirm(bool accepted)
        {
            var m = confirmModal;
            confirmModal = null;
            if (accepted) m?.OnConfirm();
            if (!string.IsNullOrEmpty(m?.ReturnFocusId)) focusedId = m.ReturnFocusId;
            Invalidate();
        }

        private void OpenInspectorGpuMenu(SignalPaintContext ctx, Point pt)
        {
            int cur = AppConfig.Get("gpu_mode");
            bool auto = AppConfig.Is("gpu_auto");
            var items = new List<(string, bool, string?, Action)>();
            if (caps.SupportsEco)
                items.Add(("Eco", !auto && cur == AsusACPI.GPUModeEco, null, () => _ = commands.DispatchAsync(backend, "gpu_mode", "eco")));
            items.Add(("Standard", !auto && cur == AsusACPI.GPUModeStandard, null, () => _ = commands.DispatchAsync(backend, "gpu_mode", "standard")));
            if (caps.SupportsMuxUltimate)
            {
                items.Add(("Ultimate", !auto && cur == AsusACPI.GPUModeUltimate, null, () =>
                {
                    ctx.Confirm(new SignalConfirmState
                    {
                        Title = SignalText.T("Switch to Ultimate mode?", "Переключить в Ultimate?"),
                        Message = SignalText.T("A restart may be required. Save your work first.", "Может потребоваться перезагрузка. Сначала сохраните работу."),
                        OnConfirm = () => Program.gpuControl.SetGPUMode(AsusACPI.GPUModeUltimate),
                    });
                }));
            }
            if (caps.SupportsEco)
                items.Add(("Optimized", auto, null, () => _ = commands.DispatchAsync(backend, "gpu_mode", "optimized")));
            ctx.ShowMenu(pt, items);
        }

        private void OpenInspectorHzMenu(SignalPaintContext ctx, Point pt)
        {
            int cur = AppConfig.Get("frequency", 0);
            bool auto = AppConfig.Is("screen_auto");
            int min = ScreenControl.MIN_RATE;
            int max = AppConfig.Get("max_frequency", 0);

            var items = new List<(string, bool, string?, Action)>
            {
                (SignalText.T("Auto", "Авто"), auto, null, () => _ = commands.DispatchAsync(backend, "refresh_rate", "auto")),
                ($"{min} Hz", !auto && cur == min, null, () => _ = commands.DispatchAsync(backend, "refresh_rate", min.ToString())),
            };
            if (max > min)
                items.Add(($"{max} Hz", !auto && cur > min, null, () => _ = commands.DispatchAsync(backend, "refresh_rate", ScreenControl.MAX_REFRESH.ToString())));
            ctx.ShowMenu(pt, items);
        }

        private void OpenContextMenu(Point clientPt, IEnumerable<(string text, bool isChecked, string? badge, Action action)> items)
        {
            var menu = new CustomContextMenu { BackColor = theme.Surface, ForeColor = theme.Text };
            foreach (var (text, isChecked, _, action) in items)
            {
                var item = new ToolStripMenuItem(text) { Checked = isChecked };
                item.Click += (_, _) =>
                {
                    try { action(); }
                    catch (Exception ex) { Logger.WriteLine("Signal menu: " + ex.Message); }
                    Invalidate();
                };
                menu.Items.Add(item);
            }
            menuOpen = true;
            menu.Closed += (_, _) => menuOpen = false;
            menu.Show(this, clientPt);
        }

        private void OpenSearch()
        {
            searchOpen = true;
            PositionSearchBox();
            searchInput.Text = searchQuery;
            searchInput.Visible = true;
            searchInput.Focus();
            searchInput.SelectAll();
            Invalidate();
        }

        private void CloseSearch()
        {
            if (!searchOpen) return;
            searchOpen = false;
            searchQuery = "";
            searchInput.Visible = false;
            searchInput.Text = "";
            Focus();
            Invalidate();
        }

        private void PositionSearchBox()
        {
            if (!searchOpen) return;
            var sb = layout.SearchBox;
            float x = layout.CompactSearch ? layout.TopNav.X + 16f : sb.X + (layout.Rtl ? 14f : 38f);
            float w = layout.CompactSearch ? layout.TopNav.Width - 32f : Math.Max(120f, sb.Width - 52f);
            searchInput.Bounds = new Rectangle((int)(x * layout.Scale), (int)((sb.Y + 9f) * layout.Scale), (int)(w * layout.Scale), (int)(22f * layout.Scale));
            searchInput.Font = painter.Font(13f, layout.Scale);
        }

        private List<SignalSearchItem> MatchingSearchItems()
        {
            string q = searchQuery.Trim();
            if (q.Length == 0) return SignalRegistry.SearchIndex.ToList();
            return SignalRegistry.SearchIndex.Where(s =>
                s.TitleEn.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.TitleRu.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.PageId.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.Keywords.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private void ActivateSearchItem()
        {
            var hits = MatchingSearchItems();
            if (hits.Count == 0) return;
            var hit = hits[Math.Clamp(searchSelectedIdx, 0, hits.Count - 1)];
            CloseSearch();
            NavigateTo(hit.PageId, true);
            if (!string.IsNullOrEmpty(hit.ControlId))
            {
                focusedId = hit.ControlId;
                showFocusRing = true;
            }
            Invalidate();
        }

        // ---- Input & Keyboard Navigation -------------------------------------------------------
        private UiElement? HitElement(Point p)
        {
            for (int i = uiElements.Count - 1; i >= 0; i--)
            {
                if (uiElements[i].BoundsPx.Contains(p)) return uiElements[i];
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            mouseClient = e.Location;

            if (activeDrag is { } drag)
            {
                float tx = drag.trackPx.Width > 0 ? Math.Clamp((e.X - drag.trackPx.X) / drag.trackPx.Width, 0f, 1f) : 0f;
                float ty = drag.trackPx.Height > 0 ? Math.Clamp((e.Y - drag.trackPx.Y) / drag.trackPx.Height, 0f, 1f) : 0f;
                drag.onDrag(tx, ty, false);
                Invalidate();
                return;
            }

            var hit = HitElement(e.Location);
            string nextHover = hit is { Enabled: true, Focusable: true } ? hit.Id : "";
            string nextTip = hit?.Tooltip ?? "";

            if (nextTip != shownTooltip)
            {
                shownTooltip = nextTip;
                if (nextTip.Length > 0) toolTip.Show(nextTip, this, e.X + 14, e.Y + 18, 2500);
                else toolTip.Hide(this);
            }

            bool onChart = layout.ToPx(layout.CpuPlot).Contains(e.Location) || layout.ToPx(layout.GpuPlot).Contains(e.Location);
            if (nextHover != hoverId || onChart)
            {
                hoverId = nextHover;
                Cursor = nextHover.Length > 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            showFocusRing = false;

            var hit = HitElement(e.Location);
            if (hit is not { Enabled: true }) return;
            pressedId = hit.Id;
            focusedId = hit.Id;

            if (hit.IsSlider)
            {
                Capture = true;
                hit.OnActivate?.Invoke(e.Location);
                if (activeDrag is { } drag)
                {
                    float tx = drag.trackPx.Width > 0 ? Math.Clamp((e.X - drag.trackPx.X) / drag.trackPx.Width, 0f, 1f) : 0f;
                    float ty = drag.trackPx.Height > 0 ? Math.Clamp((e.Y - drag.trackPx.Y) / drag.trackPx.Height, 0f, 1f) : 0f;
                    drag.onDrag(tx, ty, false);
                }
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (activeDrag is { } drag)
            {
                float tx = drag.trackPx.Width > 0 ? Math.Clamp((e.X - drag.trackPx.X) / drag.trackPx.Width, 0f, 1f) : 0f;
                float ty = drag.trackPx.Height > 0 ? Math.Clamp((e.Y - drag.trackPx.Y) / drag.trackPx.Height, 0f, 1f) : 0f;
                activeDrag = null;
                Capture = false;
                pressedId = "";
                drag.onDrag(tx, ty, true);
                Invalidate();
                return;
            }

            if (!string.IsNullOrEmpty(pressedId))
            {
                string wasPressed = pressedId;
                pressedId = "";
                var hit = HitElement(e.Location);
                if (hit is { Enabled: true, IsSlider: false } && hit.Id == wasPressed)
                {
                    hit.OnActivate?.Invoke(e.Location);
                }
                Invalidate();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            float maxScroll = Math.Max(0f, currentPage.ContentHeightDip(BuildContext(null!)) - PageViewHeightDip());
            if (maxScroll <= 0) return;
            scrollDip = Math.Clamp(scrollDip - e.Delta / 120f * 84f, 0f, maxScroll);
            Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.K))
            {
                if (searchOpen) CloseSearch(); else OpenSearch();
                return true;
            }
            if (keyData == (Keys.Alt | Keys.Left) && confirmModal is null)
            {
                NavigateBack();
                return true;
            }
            if (keyData == Keys.Escape)
            {
                if (confirmModal is not null) { CloseConfirm(false); return true; }
                if (currentPage.HandleEscape()) { Invalidate(); return true; }
                if (drawerOpen) { drawerOpen = false; UpdateLayout(); Invalidate(); return true; }
                if (searchOpen) { CloseSearch(); return true; }
                if (!menuOpen && toast is { IsError: false }) { toast = null; Invalidate(); return true; }
            }

            if (searchInput.Focused) return base.ProcessCmdKey(ref msg, keyData);

            var focusList = confirmModal is not null
                ? uiElements.Where(e => e.Focusable && e.Enabled && e.Id.StartsWith("confirm:")).ToList()
                : uiElements.Where(e => e.Focusable && e.Enabled).ToList();

            if (keyData == Keys.Tab || keyData == (Keys.Shift | Keys.Tab))
            {
                if (focusList.Count == 0) return true;
                showFocusRing = true;
                int cur = focusList.FindIndex(e => e.Id == focusedId);
                int step = (keyData & Keys.Shift) == Keys.Shift ? -1 : 1;
                int next = cur < 0 ? 0 : (cur + step + focusList.Count) % focusList.Count;
                focusedId = focusList[next].Id;
                Invalidate();
                return true;
            }

            var focusedEl = focusList.FirstOrDefault(e => e.Id == focusedId);
            if (focusedEl is not null)
            {
                if (keyData == Keys.Enter || keyData == Keys.Space)
                {
                    var pt = new Point((int)(focusedEl.BoundsPx.X + focusedEl.BoundsPx.Width / 2f),
                                       (int)(focusedEl.BoundsPx.Y + focusedEl.BoundsPx.Height / 2f));
                    focusedEl.OnActivate?.Invoke(pt);
                    Invalidate();
                    return true;
                }
                if (focusedEl.OnStep is not null)
                {
                    int delta = keyData switch
                    {
                        Keys.Right or Keys.Up => 1,
                        Keys.Left or Keys.Down => -1,
                        Keys.PageUp => 3,
                        Keys.PageDown => -3,
                        _ => 0,
                    };
                    if (delta != 0)
                    {
                        focusedEl.OnStep(delta);
                        Invalidate();
                        return true;
                    }
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                pollTimer.Dispose();
                toastTimer.Dispose();
                pollCts.Dispose();
                toolTip.Dispose();
                searchInput.Dispose();
                painter.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
