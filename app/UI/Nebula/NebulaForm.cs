using GHelper.Battery;
using GHelper.Display;
using GHelper.Mode;
using GHelper.USB;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace GHelper.UI.Nebula
{
    /// <summary>
    /// Nebula control center: the new main window (design kit v2, screen "overview").
    /// Enabled with config "theme": "nebula". Draws the whole page with GDI+ against a
    /// 1440×1060 design grid scaled uniformly to the client area.
    ///
    /// The window never touches hardware itself: readings come from HardwareControl,
    /// commands go through the existing controllers (ModeControl, GPUModeControl, ...).
    /// Secondary sections open the classic windows until they are redesigned (V1 bridge).
    /// </summary>
    public class NebulaForm : RForm
    {
        // ---- design grid -------------------------------------------------------------
        private const float DesignW = 1440f;
        private const float DesignH = 1060f;
        private const float RailW = 86f;
        private const float TopBarH = 43f;
        private const int HistoryLength = 90;

        private float k = 1f; // design px -> client px

        private NebulaTheme theme = NebulaTheme.Dark;

        // ---- state -----------------------------------------------------------------------
        private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 1000 };
        private bool reading;
        private DateTime lastRead = DateTime.MinValue;

        private readonly Queue<float> cpuHistory = new();
        private readonly Queue<float> gpuHistory = new();

        private string hover = "";
        private readonly List<(RectangleF rect, string id, string tip)> hits = new();
        private readonly ToolTip tip = new() { InitialDelay = 400, ReshowDelay = 200 };
        private string shownTip = "";

        private static readonly (string id, string icon, Func<string> title)[] Rail =
        {
            ("overview", "overview", () => NebulaText.RailOverview),
            ("power",    "power",    () => NebulaText.RailPower),
            ("fan",      "fan",      () => NebulaText.RailFan),
            ("gpu",      "gpu",      () => NebulaText.RailGpu),
            ("display",  "display",  () => NebulaText.RailDisplay),
            ("battery",  "battery",  () => NebulaText.RailBattery),
            ("light",    "light",    () => NebulaText.RailLight),
            ("keyboard", "keyboard", () => NebulaText.RailKeyboard),
            ("mouse",    "mouse",    () => NebulaText.RailMouse),
            ("settings", "settings", () => NebulaText.RailSettings),
        };

        // ---- fonts -----------------------------------------------------------------------
        private readonly Dictionary<string, Font> fonts = new();
        private static readonly string TextFamily = PickFamily("Segoe UI Variable Text", "Segoe UI");
        private static readonly string DisplayFamily = PickFamily("Segoe UI Variable Display", "Segoe UI");

        private static string PickFamily(string preferred, string fallback)
        {
            try { using var f = new FontFamily(preferred); return preferred; }
            catch { return fallback; }
        }

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

            PlaceOnScreen();

            refreshTimer.Tick += (_, _) => Refresh(false);
            FormClosing += NebulaForm_FormClosing;
            VisibleChanged += NebulaForm_VisibleChanged;
            MouseLeave += (_, _) => { SetHover(""); shownTip = ""; tip.Hide(this); };
        }

        private void PlaceOnScreen()
        {
            var wa = (Screen.PrimaryScreen ?? Screen.FromControl(this)).WorkingArea;
            float dpi = DeviceDpi / 96f;
            int w = (int)Math.Min(DesignW * dpi, wa.Width - 40);
            int h = (int)Math.Min(DesignH * dpi, wa.Height - 40);
            // keep design aspect so the grid scales uniformly
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
            Invalidate();
        }

        // ---- show / hide ---------------------------------------------------------------------
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
                Refresh(true);
                refreshTimer.Start();
            }
            else
            {
                refreshTimer.Stop();
            }
        }

        // ---- data ----------------------------------------------------------------------------
        private async void Refresh(bool force)
        {
            if (reading || !Visible) return;
            if (!force && (DateTime.Now - lastRead).TotalMilliseconds < 900) return;
            reading = true;
            try
            {
                await Task.Run(() =>
                {
                    HardwareControl.ReadSensors();
                    HardwareControl.cpuUsage = HardwareControl.GetCPUUsage();
                    HardwareControl.cpuPower = HardwareControl.GetCPUPower();
                    var ram = HardwareControl.GetRAMInfo();
                    HardwareControl.ramUsage = ram?.percent;
                    HardwareControl.ramUsedMb = ram?.usedMb;
                });
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Nebula refresh: " + ex.Message);
            }
            finally
            {
                reading = false;
            }

            lastRead = DateTime.Now;
            Push(cpuHistory, HardwareControl.cpuTemp);
            Push(gpuHistory, HardwareControl.gpuTemp);
            if (!IsDisposed) Invalidate();
        }

        private static void Push(Queue<float> q, float? v)
        {
            if (v is > 0) q.Enqueue(v.Value); else q.Enqueue(float.NaN);
            while (q.Count > HistoryLength) q.Dequeue();
        }

        // ---- geometry helpers ------------------------------------------------------------------
        private float S(float v) => v * k;
        private RectangleF R(float x, float y, float w, float h) => new(S(x), S(y), S(w), S(h));

        private Font F(float designPx, FontStyle style = FontStyle.Regular, bool display = false)
        {
            float px = Math.Max(8f, designPx * k);
            string key = $"{px:F1}|{(int)style}|{display}";
            if (!fonts.TryGetValue(key, out var f))
            {
                f = new Font(display ? DisplayFamily : TextFamily, px, style, GraphicsUnit.Pixel);
                fonts[key] = f;
            }
            return f;
        }

        private static readonly StringFormat FmtLeft = new(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip };
        private static readonly StringFormat FmtRight = new(StringFormat.GenericTypographic) { Alignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip };
        private static readonly StringFormat FmtCenter = new(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip };

        /// <summary>Draw text with the design baseline at y (design px).</summary>
        private void Txt(Graphics g, string s, float x, float baseline, Font f, Color c, StringAlignment align = StringAlignment.Near)
        {
            float ascent = f.Size * f.FontFamily.GetCellAscent(f.Style) / f.FontFamily.GetEmHeight(f.Style);
            float top = S(baseline) - ascent;
            using var b = new SolidBrush(c);
            var fmt = align == StringAlignment.Far ? FmtRight : align == StringAlignment.Center ? FmtCenter : FmtLeft;
            float w = 2000f;
            var rect = align switch
            {
                StringAlignment.Far => new RectangleF(S(x) - w, top, w, f.Height * 1.5f),
                StringAlignment.Center => new RectangleF(S(x) - w / 2, top, w, f.Height * 1.5f),
                _ => new RectangleF(S(x), top, w, f.Height * 1.5f),
            };
            g.DrawString(s, f, b, rect, fmt);
        }

        private float TextWidth(Graphics g, string s, Font f) => g.MeasureString(s, f, 4000, FmtLeft).Width;

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Max(1f, radius * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private void Card(Graphics g, RectangleF r, float radius, Color fill, Color? stroke = null, float strokeWidth = 1f)
        {
            using var path = Rounded(r, S(radius));
            using var b = new SolidBrush(fill);
            g.FillPath(b, path);
            if (stroke is not null)
            {
                using var pen = new Pen(stroke.Value, Math.Max(1f, strokeWidth * k));
                g.DrawPath(pen, path);
            }
        }

        private void IconAt(Graphics g, string name, float x, float y, float designSize, bool accent = false)
        {
            int px = Math.Max(12, (int)Math.Round(designSize * k));
            var img = NebulaAssets.IconScaled(name, darkTheme, px);
            if (img is null) return;
            if (accent)
            {
                using var tinted = ControlHelper.TintImage(img, theme.Accent);
                g.DrawImage(tinted, S(x), S(y), px, px);
            }
            else
            {
                g.DrawImage(img, S(x), S(y), px, px);
            }
        }

        private void Hit(RectangleF r, string id, string tooltip = "") => hits.Add((r, id, tooltip));

        // ---- painting ------------------------------------------------------------------------------
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            k = Math.Min(ClientSize.Width / DesignW, ClientSize.Height / DesignH);
            hits.Clear();

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            g.Clear(theme.Bg);
            PaintChrome(g);
            PaintOverview(g);
        }

        private void PaintChrome(Graphics g)
        {
            using (var b = new SolidBrush(theme.Sidebar))
            {
                g.FillRectangle(b, 0, 0, ClientSize.Width, S(TopBarH));
                g.FillRectangle(b, 0, S(TopBarH), S(RailW), ClientSize.Height);
            }
            using (var pen = new Pen(theme.Line, 1f))
            {
                g.DrawLine(pen, 0, S(TopBarH), ClientSize.Width, S(TopBarH));
                g.DrawLine(pen, S(RailW), S(TopBarH), S(RailW), ClientSize.Height);
            }

            Txt(g, "C R A T E L E S S", 28, 28, F(12, FontStyle.Bold), theme.Accent);
            Txt(g, "CONTROL CENTER", 226, 28, F(9), theme.Faint);

            // rail
            float y = 163;
            for (int i = 0; i < Rail.Length; i++)
            {
                var item = Rail[i];
                bool active = item.id == "overview";
                bool hovered = hover == "rail:" + item.id;
                var slot = R(16, y, 54, 48);

                if (active)
                {
                    Card(g, slot, 12, theme.AccentBg);
                    using var bar = new SolidBrush(theme.Accent);
                    g.FillRectangle(bar, R(0, y + 9, 3, 30));
                }
                else if (hovered)
                {
                    Card(g, slot, 12, theme.Raised);
                }

                IconAt(g, item.icon, 31, y + 12, 24, active);
                Hit(slot, "rail:" + item.id, active ? "" : item.title() + (i is 0 or 1 or 2 or 3 or 7 ? "" : " · " + NebulaText.LegacyWindow));
                y += 60;
            }

            // footer
            string sensors = lastRead == DateTime.MinValue
                ? NebulaText.SensorsWaiting
                : NebulaText.SensorsUpdated + " " + lastRead.ToString("HH:mm:ss");
            Txt(g, "●  " + sensors, 124, 1040, F(11), theme.Faint);
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            Txt(g, $"CRATELESS {ver?.Major}.{ver?.Minor}.{ver?.Build}  /  NEBULA", 1394, 1040, F(10), theme.Faint, StringAlignment.Far);
        }

        private void Pill(Graphics g, float x, float y, string text, Color fill, Color fore, out float width)
        {
            var f = F(11, FontStyle.Bold);
            width = TextWidth(g, text, f) / k + 24;
            Card(g, R(x, y, width, 25), 6, fill);
            Txt(g, text, x + 12, y + 17, f, fore);
        }

        private void PaintOverview(Graphics g)
        {
            // header
            Txt(g, NebulaText.ControlCenter, 124, 96, F(25, FontStyle.Bold, true), theme.Text);
            Txt(g, NebulaText.Tagline, 124, 122, F(12), theme.Muted);

            bool plugged = Gpu.GPUModeControl.IsPlugged();
            string charge = HardwareControl.batteryCharge ?? "";
            float px = 1394;
            if (charge.Length > 0)
            {
                var fpill = F(11, FontStyle.Bold);
                float w = TextWidth(g, charge, fpill) / k + 24;
                px -= w;
                Pill(g, px, 79, charge, theme.AccentBg, theme.Accent, out _);
                px -= 12;
            }
            {
                string src = plugged ? NebulaText.OnAc : NebulaText.OnBattery;
                var fpill = F(11, FontStyle.Bold);
                float w = TextWidth(g, "●  " + src, fpill) / k + 24;
                px -= w;
                Pill(g, px, 79, "●  " + src, theme.AccentBg, theme.Accent, out _);
            }

            PaintHero(g);
            PaintCpuGpu(g);
            PaintFans(g);
            PaintMemory(g);
            PaintModes(g);
            PaintQuick(g);
        }

        private void PaintHero(Graphics g)
        {
            Txt(g, NebulaText.YourDevice, 126, 181, F(10, FontStyle.Bold), theme.Faint);
            Txt(g, AppConfig.GetModelShort(), 124, 228, F(36, FontStyle.Bold, true), theme.Text);

            string cpu = PawnIO.CpuInfo.Name;
            string gpu = HardwareControl.GpuControl?.FullName ?? "";
            string line = cpu.Length > 0 && gpu.Length > 0 ? cpu + "  /  " + gpu : cpu + gpu;
            Txt(g, line, 126, 258, F(13), theme.Muted);

            // render: fit into 124..720 × 280..640
            var box = R(124, 282, 596, 356);
            var hero = NebulaAssets.HeroScaled((int)box.Width);
            if (hero is not null)
            {
                float ratio = Math.Min(box.Width / hero.Width, box.Height / hero.Height);
                float w = hero.Width * ratio, h = hero.Height * ratio;
                g.DrawImage(hero, box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
            }

            bool connected = Program.acpi?.IsConnected() ?? false;
            Txt(g, "●  " + (connected ? NebulaText.Connected : NebulaText.NotConnected), 139, 666,
                F(11, FontStyle.Bold), connected ? theme.Accent : theme.Warning);
        }

        private void PaintCpuGpu(Graphics g)
        {
            PaintMetric(g, 762, NebulaText.Cpu, "cpu", HardwareControl.cpuTemp, HardwareControl.cpuUsage,
                        HardwareControl.cpuPower, cpuHistory, theme.Accent, sleeping: false);

            bool gpuSleeping = HardwareControl.gpuTemp is null || HardwareControl.gpuTemp <= 0;
            PaintMetric(g, 1088, NebulaText.Gpu, "gpu", HardwareControl.gpuTemp, HardwareControl.gpuUsage,
                        HardwareControl.gpuPower, gpuHistory, theme.Blue, sleeping: gpuSleeping);
        }

        private void PaintMetric(Graphics g, float x, string caption, string icon, float? temp, int? usage,
                                 float? power, Queue<float> history, Color color, bool sleeping)
        {
            const float colW = 306; // 762..1068 / 1088..1394
            IconAt(g, icon, x, 168, 24);
            Txt(g, caption, x + 32, 184, F(11, FontStyle.Bold), theme.Faint);

            if (temp is > 0)
            {
                string t = Math.Round(temp.Value).ToString();
                var big = F(62, FontStyle.Bold, true);
                Txt(g, t, x, 258, big, theme.Text);
                float w = TextWidth(g, t, big) / k;
                Txt(g, "°C", x + w + 6, 257, F(23), theme.Muted);
            }
            else
            {
                Txt(g, "—", x, 258, F(62, FontStyle.Bold, true), theme.Faint);
                if (sleeping) Txt(g, NebulaText.Sleeping, x + 60, 257, F(23), theme.Faint);
            }

            if (usage is >= 0) Txt(g, usage + "%", x + colW, 240, F(11, FontStyle.Bold), theme.Muted, StringAlignment.Far);

            // sparkline 300..370
            var chart = R(x, 292, colW, 76);
            PaintSparkline(g, chart, history, color);

            float row = 389;
            if (usage is >= 0)
            {
                Txt(g, NebulaText.Load, x, row, F(12), theme.Muted);
                Txt(g, usage + " %", x + colW, row, F(13, FontStyle.Bold), theme.Text, StringAlignment.Far);
                row += 33;
            }
            if (power is > 0)
            {
                Txt(g, NebulaText.Power, x, row, F(12), theme.Muted);
                Txt(g, Math.Round(power.Value) + " " + NebulaText.Watt, x + colW, row, F(13, FontStyle.Bold), theme.Text, StringAlignment.Far);
            }
        }

        private void PaintSparkline(Graphics g, RectangleF r, Queue<float> history, Color color)
        {
            using (var pen = new Pen(theme.Line, 1f)) g.DrawLine(pen, r.X, r.Bottom, r.Right, r.Bottom);
            if (history.Count < 2) return;

            var data = history.ToArray();
            float min = 30, max = 100;
            var pts = new List<PointF>();
            for (int i = 0; i < data.Length; i++)
            {
                if (float.IsNaN(data[i])) continue;
                float xx = r.X + r.Width * i / (HistoryLength - 1);
                float v = Math.Clamp((data[i] - min) / (max - min), 0, 1);
                pts.Add(new PointF(xx, r.Bottom - v * r.Height));
            }
            if (pts.Count < 2) return;

            using (var fillPath = new GraphicsPath())
            {
                fillPath.AddLine(pts[0].X, r.Bottom, pts[0].X, pts[0].Y);
                fillPath.AddLines(pts.ToArray());
                fillPath.AddLine(pts[^1].X, pts[^1].Y, pts[^1].X, r.Bottom);
                fillPath.CloseFigure();
                using var lg = new LinearGradientBrush(r, Color.FromArgb(70, color), Color.FromArgb(0, color), LinearGradientMode.Vertical);
                g.FillPath(lg, fillPath);
            }
            using (var pen = new Pen(color, Math.Max(1.5f, 2f * k)) { LineJoin = LineJoin.Round })
            {
                g.DrawLines(pen, pts.ToArray());
            }
        }

        private void PaintFans(Graphics g)
        {
            Txt(g, NebulaText.Cooling, 762, 489, F(11, FontStyle.Bold), theme.Faint);
            var link = F(12, FontStyle.Bold);
            bool hv = hover == "fans:configure";
            Txt(g, NebulaText.Configure, 1394, 489, link, hv ? theme.Text : theme.Accent, StringAlignment.Far);
            float lw = TextWidth(g, NebulaText.Configure, link) / k;
            Hit(R(1394 - lw - 8, 470, lw + 16, 28), "fans:configure");

            FanRow(g, 539, NebulaText.CpuFan, HardwareControl.cpuFan, "fan_max_0");
            FanRow(g, 596, NebulaText.GpuFan, HardwareControl.gpuFan, "fan_max_1");
            if (AppConfig.Is("mid_fan"))
                FanRow(g, 653, NebulaText.SystemFan, HardwareControl.midFan, "fan_max_2");
        }

        private void FanRow(Graphics g, float y, string label, string? reading, string maxKey)
        {
            IconAt(g, "fan", 762, y - 17, 24);
            Txt(g, label, 794, y, F(11, FontStyle.Bold), theme.Faint);

            if (string.IsNullOrEmpty(reading))
            {
                Txt(g, "—", 1394, y + 1, F(18, FontStyle.Bold, true), theme.Faint, StringAlignment.Far);
                return;
            }

            // reading comes formatted from FanSensorControl ("3400 RPM" / "3400 RPM (56%)")
            int rpm = ParseLeadingInt(reading);
            int pct = ParsePercent(reading);
            if (pct < 0)
            {
                int max = AppConfig.Get(maxKey, 0) * 100;
                if (max > 0 && rpm > 0) pct = Math.Clamp(rpm * 100 / max, 0, 100);
            }
            if (pct >= 0)
            {
                using (var track = new SolidBrush(theme.Line))
                    g.FillRectangle(track, R(902, y - 9, 306, 4));
                using var lg = new LinearGradientBrush(R(902, y - 9, 306, 4), theme.Accent, theme.Blue, LinearGradientMode.Horizontal);
                g.FillRectangle(lg, R(902, y - 9, 306f * pct / 100f, 4));
            }

            string value = rpm > 0 ? rpm.ToString("N0") : reading;
            var fv = F(18, FontStyle.Bold, true);
            Txt(g, value, 1340, y + 1, fv, theme.Text, StringAlignment.Far);
            Txt(g, rpm > 0 ? NebulaText.Rpm : "", 1394, y, F(11), theme.Muted, StringAlignment.Far);
        }

        private static int ParseLeadingInt(string s)
        {
            int i = 0; while (i < s.Length && !char.IsDigit(s[i])) i++;
            int j = i; while (j < s.Length && char.IsDigit(s[j])) j++;
            return j > i && int.TryParse(s[i..j], out var v) ? v : -1;
        }

        private static int ParsePercent(string s)
        {
            int p = s.IndexOf('%');
            if (p <= 0) return -1;
            int i = p - 1; while (i >= 0 && char.IsDigit(s[i])) i--;
            return int.TryParse(s[(i + 1)..p], out var v) ? v : -1;
        }

        private void PaintMemory(Graphics g)
        {
            var used = HardwareControl.ramUsedMb;
            var pct = HardwareControl.ramUsage;
            if (used is null || pct is null) return;

            Txt(g, NebulaText.Memory, 762, 741, F(10, FontStyle.Bold), theme.Faint);
            double totalGb = used.Value / 1024.0 / Math.Max(1, pct.Value) * 100.0;
            string text = $"{used.Value / 1024.0:0.0} / {Math.Round(totalGb)} {NebulaText.Gb}";
            Txt(g, text, 906, 741, F(17, FontStyle.Bold, true), theme.Text);

            using (var track = new SolidBrush(theme.Line)) g.FillRectangle(track, R(1110, 730, 284, 5));
            using (var fill = new SolidBrush(theme.Blue)) g.FillRectangle(fill, R(1110, 730, 284f * pct.Value / 100f, 5));
        }

        private void PaintModes(Graphics g)
        {
            Txt(g, NebulaText.Mode, 124, 724, F(11, FontStyle.Bold), theme.Faint);

            var list = Modes.GetList();
            int current = Modes.GetCurrent();
            float x = 124;
            int shown = 0;
            var extra = new List<int>();

            foreach (int mode in list)
            {
                if (shown >= 3) { extra.Add(mode); continue; }
                ModeTile(g, x, mode, current == mode, Modes.GetName(mode), HintFor(mode), IconFor(mode));
                x += 198;
                shown++;
            }

            if (extra.Count > 0)
            {
                bool active = extra.Contains(current);
                string name = active ? Modes.GetName(current) : NebulaText.More;
                var r = R(x, 745, 80, 84);
                bool hv = hover == "mode:more";
                Card(g, r, 12, active ? theme.AccentBg : hv ? theme.Raised : theme.Card, active ? theme.Accent : theme.Line);
                Txt(g, name, x + 40, 793, F(13, FontStyle.Bold), active ? theme.Accent : theme.Text, StringAlignment.Center);
                Hit(r, "mode:more");
            }
        }

        private void ModeTile(Graphics g, float x, int mode, bool active, string title, string hint, string icon)
        {
            var r = R(x, 745, 185, 84);
            bool hv = hover == "mode:" + mode;
            Card(g, r, 12, active ? theme.AccentBg : hv ? theme.Raised : theme.Card, active ? theme.Accent : theme.Line);
            if (active)
            {
                using var b = new SolidBrush(theme.Accent);
                g.FillRectangle(b, R(x + 12, 827, 161, 2));
            }
            IconAt(g, icon, x + 15, 761, 24, active);
            Txt(g, title, x + 49, 776, F(15, FontStyle.Bold), active ? theme.Accent : theme.Text);
            Txt(g, hint, x + 15, 811, F(11), theme.Muted);
            Hit(r, "mode:" + mode);
        }

        private static string HintFor(int mode) => Modes.GetBase(mode) switch
        {
            2 => NebulaText.SilentHint,
            1 => NebulaText.TurboHint,
            _ => mode > 2 ? NebulaText.CustomHint : NebulaText.BalancedHint,
        };

        private static string IconFor(int mode) => Modes.GetBase(mode) switch
        {
            2 => "leaf",
            1 => "power",
            _ => "chart",
        };

        private void PaintQuick(Graphics g)
        {
            Txt(g, NebulaText.QuickAccess, 124, 871, F(11, FontStyle.Bold), theme.Faint);

            // GPU
            int gpuMode = AppConfig.Get("gpu_mode");
            bool gpuAuto = AppConfig.Is("gpu_auto");
            string gpuName = gpuMode switch { AsusACPI.GPUModeEco => NebulaText.Eco, AsusACPI.GPUModeUltimate => NebulaText.Ultimate, _ => NebulaText.Standard };
            QuickCard(g, 124, "quick:gpu", "gpu", gpuAuto ? NebulaText.Optimized : gpuName,
                      gpuAuto ? NebulaText.GraphicsAuto + " · " + gpuName : NebulaText.GraphicsMode);

            // Screen
            int hz = AppConfig.Get("frequency", 0);
            bool auto = AppConfig.Is("screen_auto");
            QuickCard(g, 446, "quick:screen", "display", hz > 0 ? hz + " " + NebulaText.T("Hz", "Гц") : "—",
                      NebulaText.ScreenHint + (auto ? " · " + NebulaText.ScreenAuto : ""));

            // Battery
            int limit = AppConfig.Get("charge_limit", 100);
            QuickCard(g, 768, "quick:battery", "battery", limit + "%", NebulaText.BatteryHint);

            // Lighting
            string aura = "—";
            try
            {
                var modes = Aura.GetModes();
                var m = (AuraMode)AppConfig.Get("aura_mode", (int)AuraMode.AuraStatic);
                if (modes.TryGetValue(m, out var name)) aura = name;
            }
            catch { }
            QuickCard(g, 1090, "quick:light", "light", aura, NebulaText.LightHint);
        }

        private void QuickCard(Graphics g, float x, string id, string icon, string value, string hint)
        {
            var r = R(x, 888, 304, 125);
            bool hv = hover == id;
            Card(g, r, 14, hv ? theme.Raised : theme.Card, hv ? theme.Accent : theme.Line);
            IconAt(g, icon, x + 20, 906, 24, hv);
            Txt(g, value, x + 20, 964, F(21, FontStyle.Bold, true), theme.Text);
            Txt(g, hint, x + 20, 991, F(11), theme.Muted);
            Hit(r, id);
        }

        // ---- interaction ----------------------------------------------------------------------------
        private void SetHover(string id)
        {
            if (hover == id) return;
            hover = id;
            Cursor = id.Length > 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string id = "", t = "";
            foreach (var h in hits)
                if (h.rect.Contains(e.Location)) { id = h.id; t = h.tip; break; }
            SetHover(id);
            if (t != shownTip)
            {
                shownTip = t;
                if (t.Length > 0) tip.Show(t, this, e.X + 16, e.Y + 20, 2500); else tip.Hide(this);
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            string id = "";
            foreach (var h in hits) if (h.rect.Contains(e.Location)) { id = h.id; break; }
            if (id.Length == 0) return;
            try { Activate(id, e.Location); }
            catch (Exception ex) { Logger.WriteLine($"Nebula action {id}: {ex.Message}"); }
        }

        private void Activate(string id, Point at)
        {
            if (id.StartsWith("mode:"))
            {
                if (id == "mode:more")
                {
                    var menu = new ContextMenuStrip();
                    int current = Modes.GetCurrent();
                    foreach (int mode in Modes.GetList().Skip(3))
                    {
                        int m = mode;
                        var item = new ToolStripMenuItem(Modes.GetName(m)) { Checked = m == current };
                        item.Click += (_, _) => { Program.modeControl.SetPerformanceMode(m, true); Invalidate(); };
                        menu.Items.Add(item);
                    }
                    menu.Show(this, at);
                    return;
                }
                Program.modeControl.SetPerformanceMode(int.Parse(id[5..]), true);
                Invalidate();
                return;
            }

            switch (id)
            {
                case "rail:overview": return;
                case "rail:power":
                case "rail:fan": Program.settingsForm.FansToggle(0); return;
                case "rail:gpu": Program.settingsForm.FansToggle(1); return;
                case "rail:keyboard": Program.settingsForm.ExtraToggle(); return;
                case "rail:display":
                case "rail:battery":
                case "rail:light":
                case "rail:mouse":
                case "rail:settings": Program.ShowLegacySettings(); return;

                case "fans:configure": Program.settingsForm.FansToggle(0); return;

                case "quick:gpu": ShowGpuMenu(at); return;
                case "quick:screen": ScreenControl.ToggleScreenRate(); Invalidate(); return;
                case "quick:battery": ShowBatteryMenu(at); return;
                case "quick:light": Program.settingsForm.CycleAuraMode(1); Invalidate(); return;
            }
        }

        private void ShowGpuMenu(Point at)
        {
            var menu = new ContextMenuStrip();
            int current = AppConfig.Get("gpu_mode");
            bool auto = AppConfig.Is("gpu_auto");
            void Add(string name, int mode)
            {
                var item = new ToolStripMenuItem(name) { Checked = !auto && current == mode };
                item.Click += (_, _) => { Program.gpuControl.SetGPUMode(mode); Invalidate(); };
                menu.Items.Add(item);
            }
            Add(NebulaText.Eco, AsusACPI.GPUModeEco);
            Add(NebulaText.Standard, AsusACPI.GPUModeStandard);
            if (Program.settingsForm.isMuxGpu) Add(NebulaText.Ultimate, AsusACPI.GPUModeUltimate);
            var opt = new ToolStripMenuItem(NebulaText.Optimized) { Checked = auto };
            opt.Click += (_, _) =>
            {
                AppConfig.Set("gpu_auto", auto ? 0 : 1);
                Program.settingsForm.VisualiseGPUMode();
                Program.gpuControl.AutoGPUMode(true);
                Invalidate();
            };
            menu.Items.Add(opt);
            menu.Show(this, at);
        }

        private void ShowBatteryMenu(Point at)
        {
            var menu = new ContextMenuStrip();
            int current = AppConfig.Get("charge_limit", 100);
            foreach (int limit in new[] { 60, 70, 80, 90, 100 })
            {
                int l = limit;
                var item = new ToolStripMenuItem(l + "%") { Checked = l == current };
                item.Click += (_, _) => { BatteryControl.SetBatteryChargeLimit(l); Program.settingsForm.VisualiseBattery(l); Invalidate(); };
                menu.Items.Add(item);
            }
            menu.Show(this, at);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                refreshTimer.Dispose();
                tip.Dispose();
                foreach (var f in fonts.Values) f.Dispose();
                fonts.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
