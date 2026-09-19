using GHelper.Display;
using GHelper.Mode;
using GHelper.USB;
using System.Drawing.Drawing2D;

namespace GHelper.UI.Nebula.Pages
{
    /// <summary>Overview page (design kit screen "overview").</summary>
    public sealed class OverviewPage : NebulaPage
    {
        public override string Id => "overview";
        public override string Title => NebulaText.ControlCenter;
        public override string Subtitle => NebulaText.Tagline;

        public override void Paint(NebulaCanvas c)
        {
            PaintHero(c);
            PaintCpuGpu(c);
            PaintFans(c);
            PaintMemory(c);
            PaintModes(c);
            PaintQuick(c);
        }

        // ---- hero ------------------------------------------------------------------------------
        private void PaintHero(NebulaCanvas c)
        {
            var th = c.Theme;
            c.Txt(NebulaText.YourDevice, 126, 181, c.F(10, FontStyle.Bold), th.Faint);
            c.Txt(AppConfig.GetModelShort(), 124, 228, c.F(36, FontStyle.Bold, true), th.Text);

            string cpu = PawnIO.CpuInfo.Name;
            string gpu = HardwareControl.GpuControl?.FullName ?? "";
            string line = cpu.Length > 0 && gpu.Length > 0 ? cpu + "  /  " + gpu : cpu + gpu;
            c.Txt(line, 126, 258, c.F(13), th.Muted);

            var box = c.R(124, 282, 596, 356);
            var hero = NebulaAssets.HeroScaled((int)box.Width);
            if (hero is not null)
            {
                float ratio = Math.Min(box.Width / hero.Width, box.Height / hero.Height);
                float w = hero.Width * ratio, h = hero.Height * ratio;
                var dst = new RectangleF(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
                using var clip = NebulaCanvas.Rounded(dst, c.S(NebulaTheme.RadiusCard + 4));
                var saved = c.G.Clip;
                c.G.SetClip(clip, CombineMode.Intersect);
                c.G.DrawImage(hero, dst);
                c.FadeEdges(dst, c.S(48));
                c.G.Clip = saved;
            }

            bool connected = Program.acpi?.IsConnected() ?? false;
            c.Txt("●  " + (connected ? NebulaText.Connected : NebulaText.NotConnected), 139, 666,
                  c.F(11, FontStyle.Bold), connected ? th.Accent : th.Warning);
        }

        // ---- cpu / gpu -------------------------------------------------------------------------
        private void PaintCpuGpu(NebulaCanvas c)
        {
            var cpuRows = new List<(string, string)>();
            if (HardwareControl.cpuUsage is >= 0) cpuRows.Add((NebulaText.Load, HardwareControl.cpuUsage + " %"));
            if (HardwareControl.cpuPower is > 0) cpuRows.Add((NebulaText.Power, Math.Round(HardwareControl.cpuPower.Value) + " " + NebulaText.Watt));
            PaintMetric(c, 762, NebulaText.Cpu, "cpu", HardwareControl.cpuTemp, cpuRows, NebulaSensors.CpuHistory, c.Theme.Accent, false);

            bool sleeping = HardwareControl.gpuTemp is null || HardwareControl.gpuTemp <= 0;
            bool igpu = NebulaSensors.HasIgpu;
            var rows = new List<(string, string)>();
            if (HardwareControl.gpuUsage is >= 0) rows.Add((NebulaText.Load + (igpu ? " dGPU" : ""), HardwareControl.gpuUsage + " %"));
            if (NebulaSensors.DgpuPower is > 0) rows.Add((NebulaText.Power + (igpu ? " dGPU" : ""), Math.Round(NebulaSensors.DgpuPower.Value) + " " + NebulaText.Watt));
            if (igpu && NebulaSensors.IgpuUse is >= 0)
            {
                string v = NebulaSensors.IgpuUse + " %";
                if (NebulaSensors.IgpuTemp is > 0) v += "  ·  " + NebulaSensors.IgpuTemp + " °C";
                if (NebulaSensors.IgpuPower is > 0) v += "  ·  " + NebulaSensors.IgpuPower + " " + NebulaText.Watt;
                rows.Add((NebulaText.Load + " iGPU", v));
            }
            PaintMetric(c, 1088, NebulaText.Gpu, "gpu", HardwareControl.gpuTemp, rows, NebulaSensors.GpuHistory, c.Theme.Blue, sleeping);
        }

        private static void PaintMetric(NebulaCanvas c, float x, string caption, string icon, float? temp,
                                        List<(string label, string value)> rows, Queue<float> history, Color color, bool sleeping)
        {
            const float colW = 306;
            var th = c.Theme;
            c.IconAt(icon, x, 168, 24);
            c.Txt(caption, x + 32, 184, c.F(11, FontStyle.Bold), th.Faint);

            if (temp is > 0)
            {
                string t = Math.Round(temp.Value).ToString();
                var big = c.F(62, FontStyle.Bold, true);
                c.Txt(t, x, 258, big, th.Text);
                c.Txt("°C", x + c.TextWidth(t, big) + 6, 257, c.F(23), th.Muted);
            }
            else
            {
                c.Txt("—", x, 258, c.F(62, FontStyle.Bold, true), th.Faint);
                if (sleeping) c.Txt(NebulaText.Sleeping, x + 60, 257, c.F(23), th.Faint);
            }

            c.Sparkline(c.R(x, 292, colW, 76), history.ToArray(), NebulaSensors.HistoryLength, color);

            float row = 389;
            float step = rows.Count > 2 ? 30 : 33;
            foreach (var (label, value) in rows)
            {
                c.Txt(label, x, row, c.F(12), th.Muted);
                c.Txt(value, x + colW, row, c.F(13, FontStyle.Bold), th.Text, StringAlignment.Far);
                row += step;
            }
        }

        // ---- fans ------------------------------------------------------------------------------
        private void PaintFans(NebulaCanvas c)
        {
            var th = c.Theme;
            c.Txt(NebulaText.Cooling, 762, 489, c.F(11, FontStyle.Bold), th.Faint);
            var link = c.F(12, FontStyle.Bold);
            c.Txt(NebulaText.Configure, 1394, 489, link, c.IsHover("overview:fans") ? th.Text : th.Accent, StringAlignment.Far);
            float lw = c.TextWidth(NebulaText.Configure, link);
            c.Hit(c.R(1394 - lw - 8, 470, lw + 16, 28), "overview:fans");

            FanRow(c, 539, NebulaText.CpuFan, HardwareControl.cpuFan, "fan_max_0");
            FanRow(c, 596, NebulaText.GpuFan, HardwareControl.gpuFan, "fan_max_1");
            if (AppConfig.Is("mid_fan"))
                FanRow(c, 653, NebulaText.SystemFan, HardwareControl.midFan, "fan_max_2");
        }

        private static void FanRow(NebulaCanvas c, float y, string label, string? reading, string maxKey)
        {
            var th = c.Theme;
            c.IconAt("fan", 762, y - 17, 24);
            c.Txt(label, 794, y, c.F(11, FontStyle.Bold), th.Faint);

            if (string.IsNullOrEmpty(reading))
            {
                c.Txt("—", 1394, y + 1, c.F(18, FontStyle.Bold, true), th.Faint, StringAlignment.Far);
                return;
            }

            int rpm = ParseLeadingInt(reading);
            int pct = ParsePercent(reading);
            if (pct < 0)
            {
                int max = AppConfig.Get(maxKey, 0) * 100;
                if (max > 0 && rpm > 0) pct = Math.Clamp(rpm * 100 / max, 0, 100);
            }
            if (pct >= 0)
            {
                using (var track = new SolidBrush(th.Line)) c.G.FillRectangle(track, c.R(902, y - 9, 306, 4));
                using var lg = new LinearGradientBrush(c.R(902, y - 9, 306, 4), th.Accent, th.Blue, LinearGradientMode.Horizontal);
                c.G.FillRectangle(lg, c.R(902, y - 9, 306f * pct / 100f, 4));
            }

            c.Txt(rpm > 0 ? rpm.ToString("N0") : reading, 1340, y + 1, c.F(18, FontStyle.Bold, true), th.Text, StringAlignment.Far);
            c.Txt(rpm > 0 ? NebulaText.Rpm : "", 1394, y, c.F(11), th.Muted, StringAlignment.Far);
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

        // ---- memory ----------------------------------------------------------------------------
        private static void PaintMemory(NebulaCanvas c)
        {
            var used = HardwareControl.ramUsedMb;
            var pct = HardwareControl.ramUsage;
            if (used is null || pct is null) return;
            var th = c.Theme;

            c.Txt(NebulaText.Memory, 762, 741, c.F(10, FontStyle.Bold), th.Faint);
            double totalGb = used.Value / 1024.0 / Math.Max(1, pct.Value) * 100.0;
            c.Txt($"{used.Value / 1024.0:0.0} / {Math.Round(totalGb)} {NebulaText.Gb}", 906, 741, c.F(17, FontStyle.Bold, true), th.Text);
            c.Bar(1110, 730, 284, 5, pct.Value / 100f, th.Blue);
        }

        // ---- modes -----------------------------------------------------------------------------
        private static void PaintModes(NebulaCanvas c)
        {
            var th = c.Theme;
            c.Txt(NebulaText.Mode, 124, 724, c.F(11, FontStyle.Bold), th.Faint);

            var list = Modes.GetList();
            int current = Modes.GetCurrent();
            float x = 124;
            int shown = 0;
            var extra = new List<int>();

            foreach (int mode in list)
            {
                if (shown >= 3) { extra.Add(mode); continue; }
                ModeTile(c, x, mode, current == mode);
                x += 198;
                shown++;
            }

            if (extra.Count > 0)
            {
                bool active = extra.Contains(current);
                string name = active ? Modes.GetName(current) : NebulaText.More;
                var r = c.R(x, 745, 80, 84);
                c.Card(r, 12, active ? th.AccentBg : c.IsHover("mode:more") ? th.Raised : th.Card, active ? th.Accent : th.Line);
                c.Txt(name, x + 40, 793, c.F(13, FontStyle.Bold), active ? th.Accent : th.Text, StringAlignment.Center);
                c.Hit(r, "mode:more");
            }
        }

        private static void ModeTile(NebulaCanvas c, float x, int mode, bool active)
        {
            var th = c.Theme;
            var r = c.R(x, 745, 185, 84);
            c.Card(r, 12, active ? th.AccentBg : c.IsHover("mode:" + mode) ? th.Raised : th.Card, active ? th.Accent : th.Line);
            if (active)
                using (var b = new SolidBrush(th.Accent)) c.G.FillRectangle(b, c.R(x + 12, 827, 161, 2));
            c.IconAt(IconFor(mode), x + 15, 761, 24, active);
            c.Txt(Modes.GetName(mode), x + 49, 776, c.F(15, FontStyle.Bold), active ? th.Accent : th.Text);
            c.Txt(HintFor(mode), x + 15, 811, c.F(11), th.Muted);
            c.Hit(r, "mode:" + mode);
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

        // ---- quick access ----------------------------------------------------------------------
        private static void PaintQuick(NebulaCanvas c)
        {
            c.Txt(NebulaText.QuickAccess, 124, 871, c.F(11, FontStyle.Bold), c.Theme.Faint);

            int gpuMode = AppConfig.Get("gpu_mode");
            bool gpuAuto = AppConfig.Is("gpu_auto");
            string gpuName = gpuMode switch { AsusACPI.GPUModeEco => NebulaText.Eco, AsusACPI.GPUModeUltimate => NebulaText.Ultimate, _ => NebulaText.Standard };
            QuickCard(c, 124, "quick:gpu", "gpu", gpuAuto ? NebulaText.Optimized : gpuName,
                      gpuAuto ? NebulaText.GraphicsAuto + " · " + gpuName : NebulaText.GraphicsMode);

            int hz = AppConfig.Get("frequency", 0);
            bool auto = AppConfig.Is("screen_auto");
            QuickCard(c, 446, "quick:screen", "display", hz > 0 ? hz + " " + NebulaText.T("Hz", "Гц") : "—",
                      NebulaText.ScreenHint + (auto ? " · " + NebulaText.ScreenAuto : ""));

            int limit = AppConfig.Get("charge_limit", 100);
            QuickCard(c, 768, "quick:battery", "battery", limit + "%", NebulaText.BatteryHint);

            string aura = "—";
            try
            {
                var modes = Aura.GetModes();
                var m = (AuraMode)AppConfig.Get("aura_mode", (int)AuraMode.AuraStatic);
                if (modes.TryGetValue(m, out var name)) aura = name;
            }
            catch { }
            QuickCard(c, 1090, "quick:light", "light", aura, NebulaText.LightHint);
        }

        private static void QuickCard(NebulaCanvas c, float x, string id, string icon, string value, string hint)
        {
            var th = c.Theme;
            var r = c.R(x, 888, 304, 125);
            bool hv = c.IsHover(id);
            c.Card(r, 14, hv ? th.Raised : th.Card, hv ? th.Accent : th.Line);
            c.IconAt(icon, x + 20, 906, 24, hv);
            c.Txt(value, x + 20, 964, c.F(21, FontStyle.Bold, true), th.Text);
            c.Txt(hint, x + 20, 991, c.F(11), th.Muted);
            c.Hit(r, id);
        }

        // ---- actions ---------------------------------------------------------------------------
        public override bool Click(string id, Point at, NebulaForm form)
        {
            if (id == "mode:more")
            {
                int current = Modes.GetCurrent();
                Menu(form, at, Modes.GetList().Skip(3).Select(m => (Modes.GetName(m), m == current, (Action)(() => Program.modeControl.SetPerformanceMode(m, true)))));
                return true;
            }
            if (id.StartsWith("mode:"))
            {
                Program.modeControl.SetPerformanceMode(int.Parse(id[5..]), true);
                return true;
            }

            switch (id)
            {
                case "overview:fans": form.ShowPage("fan"); return true;
                case "quick:gpu": ShowGpuMenu(form, at); return true;
                case "quick:screen": form.ShowPage("display"); return true;
                case "quick:battery": form.ShowPage("battery"); return true;
                case "quick:light": Program.settingsForm.CycleAuraMode(1); return true;
            }
            return false;
        }

        private static void ShowGpuMenu(NebulaForm form, Point at)
        {
            int current = AppConfig.Get("gpu_mode");
            bool auto = AppConfig.Is("gpu_auto");
            var items = new List<(string, bool, Action)>
            {
                (NebulaText.Eco, !auto && current == AsusACPI.GPUModeEco, () => Program.gpuControl.SetGPUMode(AsusACPI.GPUModeEco)),
                (NebulaText.Standard, !auto && current == AsusACPI.GPUModeStandard, () => Program.gpuControl.SetGPUMode(AsusACPI.GPUModeStandard)),
            };
            if (Program.settingsForm.isMuxGpu)
                items.Add((NebulaText.Ultimate, !auto && current == AsusACPI.GPUModeUltimate, () => Program.gpuControl.SetGPUMode(AsusACPI.GPUModeUltimate)));
            items.Add((NebulaText.Optimized, auto, () =>
            {
                AppConfig.Set("gpu_auto", auto ? 0 : 1);
                Program.settingsForm.VisualiseGPUMode();
                Program.gpuControl.AutoGPUMode(true);
            }));
            Menu(form, at, items);
        }
    }
}
