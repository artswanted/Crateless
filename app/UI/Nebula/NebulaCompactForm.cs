using GHelper.Mode;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace GHelper.UI.Nebula
{
    /// <summary>
    /// Compact fly-out (design kit screen "compact", 440×700): device, three readings, performance
    /// modes, GPU mode and a way into the full control center. Opens from the tray when the user
    /// last chose the compact view (config nebula_compact = 1). Hides when it loses focus.
    /// </summary>
    public class NebulaCompactForm : RForm
    {
        private const float DesignW = 440f;
        private const float DesignH = 700f;

        private float k = 1f;
        private NebulaTheme theme = NebulaTheme.Dark;
        private readonly NebulaCanvas canvas = new();
        private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 1000 };
        private bool reading;
        private string hover = "";
        private long hiddenAt;

        public NebulaCompactForm()
        {
            Text = "Crateless";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;

            float dpi = DeviceDpi / 96f;
            Size = new Size((int)(DesignW * dpi), (int)(DesignH * dpi));

            refreshTimer.Tick += (_, _) => RefreshSensors(false);
            Deactivate += (_, _) => { hiddenAt = Environment.TickCount64; Hide(); };
            VisibleChanged += (_, _) =>
            {
                if (Visible) { ApplyTheme(); RefreshSensors(true); refreshTimer.Start(); }
                else refreshTimer.Stop();
            };
            MouseLeave += (_, _) => SetHover("");
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                return p;
            }
        }

        public void ApplyTheme()
        {
            InitTheme();
            theme = NebulaTheme.Current(darkTheme) ?? (darkTheme ? NebulaTheme.Dark : NebulaTheme.Light);
            BackColor = theme.Bg;
            using var path = NebulaCanvas.Rounded(new RectangleF(0, 0, Width, Height), DeviceDpi / 96f * 12);
            Region = new Region(path);
            Invalidate();
        }

        /// <summary>Show bottom-right of the primary screen (or hide when visible).</summary>
        public void Toggle()
        {
            if (Visible) { Hide(); return; }
            // a tray click that just deactivated us should not immediately reopen
            if (Environment.TickCount64 - hiddenAt < 300) return;

            var wa = (Screen.PrimaryScreen ?? Screen.FromControl(this)).WorkingArea;
            Location = new Point(wa.Right - Width - 12, wa.Bottom - Height - 12);
            Show();
            Activate();
        }

        // ---- sensors -------------------------------------------------------------------------------
        private async void RefreshSensors(bool force)
        {
            if (reading || !Visible) return;
            if (!force && (DateTime.Now - NebulaSensors.LastRead).TotalMilliseconds < 900) return;
            reading = true;
            try { await Task.Run(NebulaSensors.Read); }
            catch (Exception ex) { Logger.WriteLine("Nebula compact refresh: " + ex.Message); }
            finally { reading = false; }
            if (IsDisposed) return;
            NebulaSensors.Commit();
            Invalidate();
        }

        // ---- painting ------------------------------------------------------------------------------
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            k = Math.Min(ClientSize.Width / DesignW, ClientSize.Height / DesignH);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var c = canvas;
            c.Begin(g, k, theme, darkTheme, hover);
            g.Clear(theme.Bg);
            using (var pen = new Pen(theme.Line, 1f))
            using (var path = NebulaCanvas.Rounded(new RectangleF(0.5f, 0.5f, ClientSize.Width - 1, ClientSize.Height - 1), c.S(12)))
                g.DrawPath(pen, path);

            // header
            c.IconAt("overview", 24, 22, 24, true);
            c.Txt("crateless", 58, 40, c.F(23, FontStyle.Bold, true), theme.Text);
            var closeR = c.R(392, 20, 28, 28);
            c.Card(closeR, 8, c.IsHover("compact:close") ? theme.Raised : theme.Bg);
            c.IconAt("close", 394, 22, 24);
            c.Hit(closeR, "compact:close");

            c.Txt(AppConfig.GetModelShort(), 24, 97, c.F(23, FontStyle.Bold, true), theme.Text);
            bool plugged = Gpu.GPUModeControl.IsPlugged();
            string charge = HardwareControl.batteryCharge ?? "";
            c.Txt((plugged ? NebulaText.OnAc : NebulaText.OnBattery) + (charge.Length > 0 ? "  /  " + charge : ""), 24, 125, c.F(12), theme.Muted);

            // hero
            var box = c.R(24, 140, 392, 290);
            var hero = NebulaAssets.HeroScaled((int)box.Width);
            if (hero is not null)
            {
                float ratio = Math.Min(box.Width / hero.Width, box.Height / hero.Height);
                float w = hero.Width * ratio, h = hero.Height * ratio;
                var dst = new RectangleF(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
                using var clip = NebulaCanvas.Rounded(dst, c.S(12));
                var saved = g.Clip;
                g.SetClip(clip, CombineMode.Intersect);
                g.DrawImage(hero, dst);
                c.FadeEdges(dst, c.S(30));
                g.Clip = saved;
            }

            // three readings
            Reading(c, 24, HardwareControl.cpuTemp is > 0 ? Math.Round(HardwareControl.cpuTemp.Value) + "°" : "—", "CPU");
            bool sleeping = HardwareControl.gpuTemp is null || HardwareControl.gpuTemp <= 0;
            Reading(c, 169, sleeping ? "—" : Math.Round(HardwareControl.gpuTemp!.Value) + "°", sleeping ? "GPU · " + NebulaText.Sleeping : "GPU");
            Reading(c, 314, charge.Length > 0 ? charge : "—", NebulaText.T("Charge", "Заряд"));

            // modes
            var list = Modes.GetList();
            int current = Modes.GetCurrent();
            int n = Math.Min(3, list.Count);
            float mw = (392 - 10 * (n - 1)) / Math.Max(1, n);
            float mx = 24;
            for (int i = 0; i < n; i++)
            {
                c.Segment(mx, 501, mw, 36, Modes.GetName(list[i]), list[i] == current, "compact:mode:" + list[i]);
                mx += mw + 10;
            }
            if (list.Count > 3 && !list.Take(3).Contains(current))
                c.Txt(NebulaText.T("Active: ", "Активен: ") + Modes.GetName(current), 24, 556, c.F(10), theme.Faint);

            // GPU row
            int gpuMode = AppConfig.Get("gpu_mode");
            bool gpuAuto = AppConfig.Is("gpu_auto");
            string gpuName = gpuAuto ? NebulaText.Optimized : gpuMode switch { AsusACPI.GPUModeEco => NebulaText.Eco, AsusACPI.GPUModeUltimate => NebulaText.Ultimate, _ => NebulaText.Standard };
            c.ValueRow(24, 578, 368, NebulaText.T("Graphics", "Графика"), gpuName, "compact:gpu");

            // screen row
            int hz = AppConfig.Get("frequency", 0);
            c.ValueRow(24, 612, 368, NebulaText.T("Display", "Экран"), hz > 0 ? hz + " " + NebulaText.T("Hz", "Гц") : "—", "compact:screen");

            c.Button(24, 633, 392, 36, NebulaText.ControlCenter, "compact:full", primary: false);
            c.Txt("CRATELESS / NEBULA", 24, 690, c.F(9), theme.Faint);
        }

        private static void Reading(NebulaCanvas c, float x, string value, string label)
        {
            c.Txt(value, x, 451, c.F(32, FontStyle.Bold, true), c.Theme.Text);
            c.Txt(label, x, 477, c.F(12), c.Theme.Muted);
        }

        // ---- interaction ---------------------------------------------------------------------------
        private void SetHover(string id)
        {
            if (hover == id) return;
            hover = id;
            Cursor = id.Length > 0 ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

        private string HitAt(Point p)
        {
            foreach (var h in canvas.Hits) if (h.rect.Contains(p)) return h.id;
            return "";
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            SetHover(HitAt(e.Location));
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            string id = HitAt(e.Location);
            if (id.Length == 0) return;
            try
            {
                if (id.StartsWith("compact:mode:")) { Program.modeControl.SetPerformanceMode(int.Parse(id[13..]), true); Invalidate(); return; }
                switch (id)
                {
                    case "compact:close": Hide(); return;
                    case "compact:full":
                        AppConfig.Set("nebula_compact", 0);
                        Hide();
                        Program.NebulaPage("overview");
                        return;
                    case "compact:gpu":
                        ShowGpuMenu(e.Location);
                        return;
                    case "compact:screen":
                        Display.ScreenControl.ToggleScreenRate();
                        Invalidate();
                        return;
                }
            }
            catch (Exception ex) { Logger.WriteLine($"Nebula compact {id}: {ex.Message}"); }
        }

        private void ShowGpuMenu(Point at)
        {
            var menu = new ContextMenuStrip();
            int current = AppConfig.Get("gpu_mode");
            bool auto = AppConfig.Is("gpu_auto");
            void Add(string name, bool isChecked, Action a)
            {
                var item = new ToolStripMenuItem(name) { Checked = isChecked };
                item.Click += (_, _) => { try { a(); } catch (Exception ex) { Logger.WriteLine(ex.Message); } Invalidate(); };
                menu.Items.Add(item);
            }
            Add(NebulaText.Eco, !auto && current == AsusACPI.GPUModeEco, () => Program.gpuControl.SetGPUMode(AsusACPI.GPUModeEco));
            Add(NebulaText.Standard, !auto && current == AsusACPI.GPUModeStandard, () => Program.gpuControl.SetGPUMode(AsusACPI.GPUModeStandard));
            if (Program.settingsForm.isMuxGpu) Add(NebulaText.Ultimate, !auto && current == AsusACPI.GPUModeUltimate, () => Program.gpuControl.SetGPUMode(AsusACPI.GPUModeUltimate));
            Add(NebulaText.Optimized, auto, () => { AppConfig.Set("gpu_auto", auto ? 0 : 1); Program.settingsForm.VisualiseGPUMode(); Program.gpuControl.AutoGPUMode(true); });
            menu.Closed += (_, _) => { if (!ContainsFocus) Activate(); };
            menu.Show(this, at);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { refreshTimer.Dispose(); canvas.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
