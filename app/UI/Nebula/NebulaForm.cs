using GHelper.UI.Nebula.Pages;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace GHelper.UI.Nebula
{
    /// <summary>
    /// Nebula control center: the main window when config "theme" is "nebula".
    /// Draws chrome (top bar, rail, header, footer) and the current page with GDI+ against a
    /// 1440×1060 design grid scaled uniformly to the client area. Pages that are not redesigned
    /// yet host the classic forms as child controls (V1 bridge).
    ///
    /// The window never touches hardware itself: readings come from NebulaSensors /
    /// HardwareControl, commands go through the existing controllers.
    /// </summary>
    public class NebulaForm : RForm
    {
        private const float DesignW = 1440f;
        private const float DesignH = 1060f;
        private const float RailCollapsed = 86f;
        private const float RailExpanded = 236f;
        private bool railExpanded = AppConfig.Is("nebula_rail");
        private float RailW => railExpanded ? RailExpanded : RailCollapsed;
        /// <summary>Horizontal shift of the page grid (client px) when the rail is expanded.</summary>
        private float PageShift => S(RailW - RailCollapsed);
        private const float TopBarH = 43f;

        private float k = 1f;
        private NebulaTheme theme = NebulaTheme.Dark;
        private readonly NebulaCanvas canvas = new();

        // pages
        private readonly Dictionary<string, NebulaPage> pages = new();
        private NebulaPage? page;
        private string pageId = "overview";

        // legacy host
        private readonly Panel host = new() { AutoScroll = true, Visible = false };
        private Form? embedded;

        // sensors
        private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 1000 };
        private bool reading;

        // interaction
        private string hover = "";
        private RectangleF hoverRect;
        private readonly ToolTip tip = new() { InitialDelay = 400, ReshowDelay = 200 };
        private string shownTip = "";
        private string dragId = "";
        private RectangleF dragTrack;
        private float scroll; // design px
        private const float ViewBottom = 1013f;

        private static readonly (string id, string icon, Func<string> title, bool legacy)[] Rail =
        {
            ("overview", "overview", () => NebulaText.RailOverview, false),
            ("power",    "power",    () => NebulaText.RailPower,    false),
            ("fan",      "fan",      () => NebulaText.RailFan,      false),
            ("gpu",      "gpu",      () => NebulaText.RailGpu,      false),
            ("display",  "display",  () => NebulaText.RailDisplay,  false),
            ("visual",   "sun",      () => "GameVisual",             false),
            ("battery",  "battery",  () => NebulaText.RailBattery,  false),
            ("light",    "light",    () => NebulaText.RailLight,    false),
            ("wallpaper","copy",     () => NebulaText.T("Wallpaper", "Обои"), false),
            ("keyboard", "keyboard", () => NebulaText.RailKeyboard, false),
            ("mouse",    "mouse",    () => NebulaText.RailMouse,    false),
            ("settings", "settings", () => NebulaText.RailSettings, false),
            ("updates",  "download", () => NebulaText.T("Updates", "Обновления"), false),
        };

        public NebulaForm()
        {
            Text = "Crateless";
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { Icon = Properties.Resources.standard; }
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(900, 640);
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;

            foreach (var p in new NebulaPage[] { new OverviewPage(), new PowerPage(), new FanPage(), new GpuPage(), new BatteryPage(), new DisplayPage(), new GameVisualPage(), new LightPage(), new WallpaperPage(), new KeysPage(), new DevicesPage(), new SettingsPage(), new UpdatesPage() })
                pages[p.Id] = p;
            page = pages["overview"];

            PlaceOnScreen();
            Controls.Add(host);

            refreshTimer.Tick += (_, _) => RefreshSensors(false);
            FormClosing += NebulaForm_FormClosing;
            VisibleChanged += NebulaForm_VisibleChanged;
            MouseLeave += (_, _) => { SetHover("", RectangleF.Empty); shownTip = ""; tip.Hide(this); };
        }

        private void PlaceOnScreen()
        {
            var wa = (Screen.PrimaryScreen ?? Screen.FromControl(this)).WorkingArea;
            float dpi = DeviceDpi / 96f;
            int w = (int)Math.Min(DesignW * dpi, wa.Width - 40);
            int h = (int)Math.Min(DesignH * dpi, wa.Height - 40);
            float scale = Math.Min(w / DesignW, h / DesignH);
            Size = new Size((int)(DesignW * scale), (int)(DesignH * scale));
            Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplyTheme();
        }

        public void ApplyTheme()
        {
            InitTheme();
            theme = NebulaTheme.Current(darkTheme) ?? (darkTheme ? NebulaTheme.Dark : NebulaTheme.Light);
            BackColor = theme.Bg;
            host.BackColor = theme.Bg;
            (embedded as RForm)?.InitTheme();
            Invalidate();
        }

        // ---- pages ---------------------------------------------------------------------------------
        /// <summary>Shows the window on the given rail page.</summary>
        public void ScrollTop() { scroll = 0; Invalidate(); }

        public void ShowPage(string id)
        {
            if (!Visible)
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Show();
            }
            SetPage(id);
            Activate();
        }

        private void SetPage(string id)
        {
            if (pages.TryGetValue(id, out var p))
            {
                page = p;
                pageId = id;
                scroll = 0;
                host.Visible = false;
                p.Refresh(true);
                Invalidate();
                return;
            }

            switch (id)
            {
                case "extra":
                {
                    var extra = Program.settingsForm.extraForm;
                    if (extra is null || extra.Text == "" || extra.IsDisposed)
                    {
                        extra = new Extra { Embedded = true };
                        Program.settingsForm.extraForm = extra;
                    }
                    Embed(extra);
                    page = null; pageId = "keyboard";
                    Invalidate();
                    return;
                }
                default:
                    Program.ShowLegacySettings();
                    return;
            }
        }

        private void Embed(Form form)
        {
            if (embedded == form && form.Parent == host) { host.Visible = true; return; }
            if (embedded is not null && embedded != form) embedded.Visible = false;

            if (form.Parent != host)
            {
                if (form is RForm r) r.Embedded = true;
                form.TopLevel = false;
                form.FormBorderStyle = FormBorderStyle.None;
                form.MinimumSize = Size.Empty;
                form.MaximumSize = Size.Empty;
                form.Location = new Point(0, 0);
                host.Controls.Add(form);
            }

            embedded = form;
            host.Visible = true;
            form.Visible = true;
            form.BringToFront();
            LayoutHost();
        }

        private void LayoutHost()
        {
            int x = (int)S(RailW) + 1 + (int)S(24);
            int y = (int)S(TopBarH) + 1 + (int)S(20);
            host.Bounds = new Rectangle(x, y, Math.Max(100, ClientSize.Width - x - (int)S(16)), Math.Max(100, ClientSize.Height - y - (int)S(12)));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            k = Math.Min(ClientSize.Width / DesignW, ClientSize.Height / DesignH);
            LayoutHost();
        }

        // ---- show / hide -------------------------------------------------------------------------
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

        private void NebulaForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        }

        private void NebulaForm_VisibleChanged(object? sender, EventArgs e)
        {
            if (Visible)
            {
                ApplyTheme();
                page?.Refresh(true);
                RefreshSensors(true);
                refreshTimer.Start();
            }
            else
            {
                refreshTimer.Stop();
            }
        }

        // ---- sensors -------------------------------------------------------------------------------
        private async void RefreshSensors(bool force)
        {
            if (reading || !Visible) return;
            if (!force && (DateTime.Now - NebulaSensors.LastRead).TotalMilliseconds < 900) return;
            reading = true;
            try { await Task.Run(NebulaSensors.Read); }
            catch (Exception ex) { Logger.WriteLine("Nebula refresh: " + ex.Message); }
            finally { reading = false; }

            if (IsDisposed) return;
            NebulaSensors.Commit();
            page?.Refresh(false);
            // nothing in the rail or the top bar changes with a sensor reading
            if (!host.Visible) Invalidate(new Rectangle((int)S(RailW), (int)S(TopBarH), ClientSize.Width, ClientSize.Height));
        }

        // ---- painting ------------------------------------------------------------------------------
        private float S(float v) => v * k;
        /// <summary>Scale applied to the page grid so that x = 1394 lands at the same client edge whatever the rail width.</summary>
        private float PageScale => railExpanded ? (DesignW - (RailExpanded - RailCollapsed)) / DesignW : 1f;
        private RectangleF R(float x, float y, float w, float h) => new(S(x), S(y), S(w), S(h));

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            k = Math.Min(ClientSize.Width / DesignW, ClientSize.Height / DesignH);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            canvas.Begin(g, k, theme, darkTheme, hover);
            g.Clear(theme.Bg);
            PaintChrome(canvas);

            if (page is not null)
            {
                {
                    var hs = g.Save();
                    g.TranslateTransform(PageShift, 0);
                    g.ScaleTransform(PageScale, 1f);
                    PaintHeader(canvas, page.Title, page.Subtitle);
                    g.Restore(hs);
                }

                float maxScroll = Math.Max(0, page.ContentHeight - ViewBottom);
                scroll = Math.Clamp(scroll, 0, maxScroll);
                float off = S(scroll);

                float ps = PageScale;
                float clipTop = S(130), clipBottom = S(ViewBottom + 12);
                // when only the rail changed, the page is outside the dirty region: skip it entirely
                // and keep its click regions, instead of running a full page paint for nothing
                var contentBox = Rectangle.Ceiling(new RectangleF(S(RailW), clipTop, ClientSize.Width - S(RailW), clipBottom - clipTop));
                if (e.ClipRectangle.IntersectsWith(contentBox))
                {
                var saved = g.Save();
                g.SetClip(new RectangleF(S(RailW) + 1, clipTop, ClientSize.Width, clipBottom - clipTop));
                // page grid: shift right of the rail and shrink so 1394 still fits the window
                g.TranslateTransform(PageShift, -off);
                g.ScaleTransform(ps, 1f);
                canvas.BeginPageLayer();
                try { page.Paint(canvas); }
                catch (Exception ex) { Logger.WriteLine($"Nebula paint {page.Id}: {ex.Message}"); }
                g.Restore(saved);

                for (int i = 0; i < canvas.PageHits.Count; i++)
                {
                    var h = canvas.PageHits[i];
                    var r = new RectangleF(h.rect.X * ps + PageShift, h.rect.Y - off, h.rect.Width * ps, h.rect.Height);
                    r = RectangleF.Intersect(r, new RectangleF(S(RailW) + 1, clipTop,
                        ClientSize.Width - S(RailW) - 1, clipBottom - clipTop));
                    canvas.PageHits[i] = (r, h.id, h.tip);
                }
                canvas.EndPageLayer();
                }

                if (maxScroll > 0)
                {
                    float trackTop = S(139), trackH = S(ViewBottom) - S(139);
                    float thumbH = Math.Max(S(30), trackH * (ViewBottom - 139) / (page.ContentHeight - 139));
                    float thumbY = trackTop + (trackH - thumbH) * (scroll / maxScroll);
                    using var b = new SolidBrush(Color.FromArgb(120, theme.Line));
                    g.FillRectangle(b, ClientSize.Width - S(8), thumbY, S(4), thumbH);
                }
            }
        }

        private void PaintChrome(NebulaCanvas c)
        {
            using (var b = new SolidBrush(theme.Sidebar))
            {
                c.G.FillRectangle(b, 0, 0, ClientSize.Width, S(TopBarH));
                c.G.FillRectangle(b, 0, S(TopBarH), S(RailW), ClientSize.Height);
            }
            using (var pen = new Pen(theme.Line, 1f))
            {
                c.G.DrawLine(pen, 0, S(TopBarH), ClientSize.Width, S(TopBarH));
                c.G.DrawLine(pen, S(RailW), S(TopBarH), S(RailW), ClientSize.Height);
            }

            c.Txt("C R A T E L E S S", 28, 28, c.F(12, FontStyle.Bold), theme.Accent);
            c.Txt("CONTROL CENTER", 226, 28, c.F(9), theme.Faint);

            // "update available" chip in the top bar; it opens the updates page
            if (AutoUpdate.AutoUpdateControl.UpdateAvailable && !AppConfig.Is("skip_updates"))
            {
                string tag = AutoUpdate.AutoUpdateControl.LatestTag;
                string text = NebulaText.T("Update available", "Доступно обновление") + (tag.Length > 0 ? "  " + tag : "");
                var f = c.F(11, FontStyle.Bold);
                bool hv = hover == "rail:updatechip";
                float w = c.TextWidth(text, f) + 40;
                var chip = R(380, 9, w, 26);
                c.Card(chip, 8, hv ? theme.Warning : Color.FromArgb(60, theme.Warning));
                using (var dot = new SolidBrush(hv ? theme.Bg : theme.Warning))
                    c.G.FillEllipse(dot, R(394, 18, 8, 8));
                c.Txt(text, 410, 27, f, hv ? theme.Bg : theme.Warning);
                c.Hit(chip, "rail:updatechip", NebulaText.T("Open the updates page", "Открыть страницу обновлений"));
            }

            // compact view switch, top-right of the top bar
            {
                // the compact switch used to appear only under the cursor; it now always reads as a button
                float cx = ClientSize.Width / k - 52;
                var cr = R(cx, 6, 40, 31);
                bool hv = hover == "rail:compact";
                c.Card(cr, 8, hv ? theme.Line : theme.Raised, hv ? theme.Accent : theme.Line);
                c.IconAt("compact", cx + 8, 9.5f, 24, true);
                c.Hit(cr, "rail:compact", NebulaText.T("Compact view", "Компактный вид"));
            }

            // rail toggle (hamburger) at the top of the rail
            {
                var tr = R(16, 62, 54, 40);
                if (hover == "rail:toggle") c.Card(tr, 10, theme.Raised);
                using var pen = new Pen(theme.Muted, Math.Max(1.5f, 1.8f * k)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                for (int i = 0; i < 3; i++)
                    c.G.DrawLine(pen, S(33), S(75 + i * 7), S(53), S(75 + i * 7));
                c.Hit(tr, "rail:toggle", railExpanded ? NebulaText.T("Collapse menu", "Свернуть меню") : NebulaText.T("Expand menu", "Развернуть меню"));
            }

            float y = 163;
            foreach (var item in Rail)
            {
                bool active = item.id == pageId;
                var slot = R(16, y, RailW - 32, 48);
                if (active)
                {
                    c.Card(slot, 12, theme.AccentBg);
                    using var bar = new SolidBrush(theme.Accent);
                    c.G.FillRectangle(bar, R(0, y + 9, 3, 30));
                }
                else if (hover == "rail:" + item.id)
                {
                    c.Card(slot, 12, theme.Raised);
                }
                c.IconAt(item.icon, 31, y + 12, 24, active);
                if (railExpanded)
                    c.Txt(item.title(), 70, y + 30, c.F(13, active ? FontStyle.Bold : FontStyle.Regular), active ? theme.Accent : theme.Text);
                // a dot on the updates entry while a newer release is waiting
                if (item.id == "updates" && AutoUpdate.AutoUpdateControl.UpdateAvailable && !AppConfig.Is("skip_updates"))
                {
                    using var dot = new SolidBrush(theme.Warning);
                    c.G.FillEllipse(dot, R(railExpanded ? 198 : 46, y + 19, 10, 10));
                }
                c.Hit(slot, "rail:" + item.id, active || railExpanded ? "" : item.title());
                y += 60;
            }

            string sensors = NebulaSensors.LastRead == DateTime.MinValue
                ? NebulaText.SensorsWaiting
                : NebulaText.SensorsUpdated + " " + NebulaSensors.LastRead.ToString("HH:mm:ss");
            {
                var fs = c.G.Save();
                c.G.TranslateTransform(PageShift, 0);
                c.G.ScaleTransform(PageScale, 1f);
                c.Txt("●  " + sensors, 124, 1040, c.F(11), theme.Faint);
                var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                c.Txt($"CRATELESS {ver?.Major}.{ver?.Minor}.{ver?.Build}  /  NEBULA", 1394, 1040, c.F(10), theme.Faint, StringAlignment.Far);
                c.G.Restore(fs);
            }
        }

        private void PaintHeader(NebulaCanvas c, string title, string subtitle)
        {
            c.Txt(title, 124, 96, c.F(25, FontStyle.Bold, true), theme.Text);
            c.Txt(subtitle, 124, 122, c.F(12), theme.Muted);

            bool plugged = Gpu.GPUModeControl.IsPlugged();
            string charge = HardwareControl.batteryCharge ?? "";
            var fpill = c.F(11, FontStyle.Bold);
            float px = 1394;
            if (charge.Length > 0)
            {
                px -= c.TextWidth(charge, fpill) + 24;
                c.Pill(px, 79, charge, theme.AccentBg, theme.Accent);
                px -= 12;
            }
            string src = "●  " + (plugged ? NebulaText.OnAc : NebulaText.OnBattery);
            px -= c.TextWidth(src, fpill) + 24;
            c.Pill(px, 79, src, theme.AccentBg, theme.Accent);
        }

        // ---- interaction ---------------------------------------------------------------------------
        /// <summary>
        /// Repainting the whole window on every hover change costs a full frame (tens of
        /// milliseconds on a large screen) and did it up to twenty times a second while the mouse
        /// moved, which left the window too busy to answer the caption buttons. Only the region
        /// that actually changes appearance is invalidated: the row the cursor left and the one it
        /// entered.
        /// </summary>
        private void SetHover(string id, RectangleF rect)
        {
            if (hover == id) return;
            var before = hoverRect;
            hover = id;
            hoverRect = rect;
            Cursor = id.Length > 0 ? Cursors.Hand : Cursors.Default;
            InvalidateArea(before);
            InvalidateArea(rect);
        }

        /// <summary>Invalidates one design region, with a margin for antialiased edges.</summary>
        private void InvalidateArea(RectangleF r)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            var box = Rectangle.Ceiling(r);
            box.Inflate(6, 6);
            Invalidate(box);
        }

        private (string id, string tip, RectangleF rect) HitAt(Point p)
        {
            foreach (var h in canvas.PageHits)
                if (h.rect.Contains(p)) return (h.id, h.tip, h.rect);
            foreach (var h in canvas.Hits)
                if (h.rect.Contains(p)) return (h.id, h.tip, h.rect);
            return ("", "", RectangleF.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragId.Length > 0)
            {
                Drag(e.Location, false);
                return;
            }
            var (id, t, rect) = HitAt(e.Location);
            SetHover(id, rect);
            if (t != shownTip)
            {
                shownTip = t;
                if (t.Length > 0) tip.Show(t, this, e.X + 16, e.Y + 20, 2500); else tip.Hide(this);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (page is null) return;
            float maxScroll = Math.Max(0, page.ContentHeight - ViewBottom);
            if (maxScroll <= 0) return;
            scroll = Math.Clamp(scroll - e.Delta / 120f * 60f, 0, maxScroll);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || page is null) return;
            var (id, _, rect) = HitAt(e.Location);
            if (id.StartsWith("slider:"))
            {
                dragId = id;
                // the hit rect has 8 px padding on each side of the track
                dragTrack = new RectangleF(rect.X + S(8), rect.Y, rect.Width - S(16), rect.Height);
                Capture = true;
                Drag(e.Location, false);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragId.Length > 0)
            {
                Drag(e.Location, true);
                dragId = "";
                Capture = false;
            }
        }

        private void Drag(Point p, bool done)
        {
            if (page is null) return;
            try
            {
                if (dragId.StartsWith("slider:fan:pt:") && page is FanPage fan)
                {
                    var plot = FanPage.PlotRect;
                    float ps = PageScale;
                    float dx = (p.X - PageShift) / ps, dy = p.Y + S(scroll);
                    float tx = Math.Clamp((dx / k - plot.X) / plot.Width, 0f, 1f);
                    float ty = Math.Clamp((dy / k - plot.Y) / plot.Height, 0f, 1f);
                    fan.DragPoint(int.Parse(dragId[14..]), tx, ty, (ModifierKeys & Keys.Shift) == Keys.Shift);
                    if (done) fan.Drag(dragId, 0, true);
                }
                else if (dragTrack.Width > 0)
                {
                    float t = Math.Clamp((p.X - dragTrack.X) / dragTrack.Width, 0f, 1f);
                    page.Drag(dragId, t, done);
                }
            }
            catch (Exception ex) { Logger.WriteLine($"Nebula drag {dragId}: {ex.Message}"); }
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            var (id, _, _) = HitAt(e.Location);
            if (id.Length == 0 || id.StartsWith("slider:")) return;

            try
            {
                if (id == "rail:compact")
                {
                    Hide();
                    Program.NebulaCompactToggle();
                    return;
                }
                if (id == "rail:toggle")
                {
                    railExpanded = !railExpanded;
                    AppConfig.Set("nebula_rail", railExpanded ? 1 : 0);
                    LayoutHost();
                    Invalidate();
                    return;
                }
                if (id == "rail:updatechip") { SetPage("updates"); return; }
                if (id.StartsWith("rail:")) { SetPage(id[5..]); return; }
                if (page is not null && page.Click(id, e.Location, this)) Invalidate();
            }
            catch (Exception ex) { Logger.WriteLine($"Nebula action {id}: {ex.Message}"); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refreshTimer.Dispose();
                tip.Dispose();
                canvas.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
