using GHelper.Mode;
using GHelper.Peripherals;
using GHelper.USB;
using System.Drawing.Drawing2D;

namespace GHelper.UI.Nebula.Pages
{
    /// <summary>
    /// Overview: device hero and connected devices on the left, four telemetry blocks
    /// (processor, graphics, fans, memory) on the right, performance modes and quick access below.
    /// Composition follows the Armoury Crate home layout in the Nebula style.
    /// </summary>
    public sealed class OverviewPage : NebulaPage
    {
        public override string Id => "overview";
        public override string Title => NebulaText.ControlCenter;
        public override string Subtitle => NebulaText.Tagline;

        // right column grid
        private const float ColL = 762, ColR = 1088, ColW = 306;
        private const float Row1 = 168, Row2 = 470;

        private List<IPeripheral> devices = new();

        public override void Refresh(bool opened)
        {
            try { devices = PeripheralsProvider.AllPeripherals(); } catch { devices = new(); }
        }

        public override void Paint(NebulaCanvas c)
        {
            PaintHero(c);
            PaintCpu(c);
            PaintGpu(c);
            PaintFans(c);
            PaintMemory(c);
            PaintModes(c);
            PaintQuick(c);
        }

        // ---- hero + devices ----------------------------------------------------------------------
        private void PaintHero(NebulaCanvas c)
        {
            var th = c.Theme;
            var box = c.R(124, 150, 596, 330);
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

            c.Txt(AppConfig.GetModelShort(), 422, 528, c.F(30, FontStyle.Bold, true), th.Text, StringAlignment.Center);
            string cpu = PawnIO.CpuInfo.Name;
            string gpu = HardwareControl.GpuControl?.FullName ?? "";
            c.Txt(cpu, 422, 556, c.F(12), th.Muted, StringAlignment.Center);
            if (gpu.Length > 0)
            {
                string vram = "";
                try { var v = HardwareControl.GpuControl?.GetVramInfo(); if (v is { totalMb: > 0 }) vram = $" · {Math.Round(v.Value.totalMb / 1024.0)} GB"; } catch { }
                c.Txt(gpu + vram, 422, 578, c.F(12), th.Muted, StringAlignment.Center);
            }

            bool connected = Program.acpi?.IsConnected() ?? false;
            c.Txt("●  " + (connected ? NebulaText.Connected : NebulaText.NotConnected), 422, 606,
                  c.F(10, FontStyle.Bold), connected ? th.Accent : th.Warning, StringAlignment.Center);

            // devices strip
            string devTitle = NebulaText.T("Devices", "Устройства") + $" ({devices.Count})";
            c.Txt(devTitle, 124, 652, c.F(11, FontStyle.Bold), th.Faint);
            var link = c.F(11, FontStyle.Bold);
            string more = NebulaText.T("Open  ↗", "Открыть  ↗");
            c.Txt(more, 720, 652, link, c.IsHover("overview:devices") ? th.Text : th.Accent, StringAlignment.Far);
            c.Hit(c.R(720 - c.TextWidth(more, link) - 8, 634, c.TextWidth(more, link) + 16, 26), "overview:devices");

            float x = 124;
            if (devices.Count == 0)
            {
                c.Card(c.R(124, 664, 596, 46), 10, th.Card, th.Line);
                c.Txt(NebulaText.T("No ASUS peripherals connected.", "Периферия ASUS не подключена."), 144, 693, c.F(11), th.Muted);
            }
            for (int i = 0; i < devices.Count && i < 3; i++)
            {
                var d = devices[i];
                var r = c.R(x, 664, 192, 46);
                c.Card(r, 10, c.IsHover("overview:dev:" + i) ? th.Raised : th.Card, th.Line);
                var thumb = NebulaAssets.Scaled(NebulaAssets.DeviceRender(d.DeviceType(), d.GetDisplayName()), (int)c.S(40));
                if (thumb is not null) c.G.DrawImage(thumb, c.S(x + 6), c.S(667), c.S(40), c.S(40));
                else c.IconAt("mouse", x + 12, 675, 24, true);
                string name = d.GetDisplayName();
                if (name.Length > 18) name = name[..17] + "…";
                c.Txt(name, x + 46, 684, c.F(11, FontStyle.Bold), th.Text);
                string st = d.IsDeviceReady ? (d.HasBattery() ? d.Battery + "%" + (d.Charging ? " ⚡" : "") : NebulaText.T("connected", "подключено")) : NebulaText.T("not ready", "не готово");
                c.Txt(st, x + 46, 700, c.F(9), th.Muted);
                c.Hit(r, "overview:dev:" + i);
                x += 202;
            }
        }

        // ---- blocks --------------------------------------------------------------------------------
        private static void BlockTitle(NebulaCanvas c, float x, float y, string icon, string title)
        {
            c.IconAt(icon, x, y - 18, 24);
            c.Txt(title, x + 32, y, c.F(19, FontStyle.Bold, true), c.Theme.Text);
        }

        /// <summary>Label / value row with an optional bar underneath (bar = 0..1, null = no bar).</summary>
        private static float Row(NebulaCanvas c, float x, float y, string label, string value, float? bar, Color color)
        {
            var th = c.Theme;
            c.Txt(label, x, y, c.F(12), th.Muted);
            c.Txt(value, x + ColW, y, c.F(12, FontStyle.Bold), th.Text, StringAlignment.Far);
            if (bar is not null)
            {
                using (var b = new SolidBrush(th.Line)) c.G.FillRectangle(b, c.R(x, y + 8, ColW, 3));
                float w = ColW * Math.Clamp(bar.Value, 0, 1);
                if (w > 0)
                {
                    using var lg = new LinearGradientBrush(c.R(x, y + 8, ColW, 3), th.Accent, th.Blue, LinearGradientMode.Horizontal);
                    c.G.FillRectangle(lg, c.R(x, y + 8, w, 3));
                }
                return y + 36;
            }
            using (var pen = new Pen(Color.FromArgb(70, th.Line), 1f)) c.G.DrawLine(pen, c.S(x), c.S(y + 8), c.S(x + ColW), c.S(y + 8));
            return y + 27;
        }

        private static string Dash => "—";

        private void PaintCpu(NebulaCanvas c)
        {
            float y = Row1;
            BlockTitle(c, ColL, y + 22, "cpu", NebulaText.T("Processor", "Процессор"));
            y += 66;

            var mhz = NebulaSensors.CpuMhz;
            y = Row(c, ColL, y, NebulaText.T("Frequency", "Частота"), mhz is > 0 ? $"{mhz:N0} MHz" : Dash, mhz is > 0 ? Math.Clamp(mhz.Value / 5500f, 0, 1) : null, c.Theme.Accent);
            var use = HardwareControl.cpuUsage;
            y = Row(c, ColL, y, NebulaText.T("Usage", "Использование"), use is >= 0 ? use + " %" : Dash, use is >= 0 ? use.Value / 100f : null, c.Theme.Accent);
            var t = HardwareControl.cpuTemp;
            y = Row(c, ColL, y, NebulaText.T("Temperature", "Температура"), t is > 0 ? Math.Round(t.Value) + " °C" : Dash, null, c.Theme.Accent);
            var p = HardwareControl.cpuPower;
            y = Row(c, ColL, y, NebulaText.T("Power", "Мощность"), p is > 0 ? Math.Round(p.Value) + " " + NebulaText.Watt : Dash, null, c.Theme.Accent);
            if (NebulaSensors.HasIgpu && NebulaSensors.IgpuUse is >= 0)
                y = Row(c, ColL, y, "iGPU · " + NebulaText.T("usage", "загрузка"), NebulaSensors.IgpuUse + " %" + (NebulaSensors.IgpuPower is > 0 ? " · " + NebulaSensors.IgpuPower + " " + NebulaText.Watt : ""), null, c.Theme.Accent);

            c.Sparkline(c.R(ColL, y + 4, ColW, 40), NebulaSensors.CpuHistory.ToArray(), NebulaSensors.HistoryLength, c.Theme.Accent);
        }

        private void PaintGpu(NebulaCanvas c)
        {
            float y = Row1;
            BlockTitle(c, ColR, y + 22, "gpu", NebulaText.T("Graphics", "Графика"));
            y += 66;

            bool eco = AppConfig.Get("gpu_mode") == AsusACPI.GPUModeEco && !AppConfig.Is("gpu_auto");
            bool awake = HardwareControl.gpuTemp is > 0;
            string state = eco ? NebulaText.T("off (Eco)", "выкл (Eco)") : awake ? NebulaText.T("active", "активна") : NebulaText.Sleeping;
            y = Row(c, ColR, y, NebulaText.T("dGPU state", "Состояние dGPU"), state, null, c.Theme.Blue);
            var use = HardwareControl.gpuUsage;
            y = Row(c, ColR, y, NebulaText.T("Usage", "Использование"), awake && use is >= 0 ? use + " %" : Dash, awake && use is >= 0 ? use.Value / 100f : null, c.Theme.Blue);
            var t = HardwareControl.gpuTemp;
            y = Row(c, ColR, y, NebulaText.T("Temperature", "Температура"), t is > 0 ? Math.Round(t.Value) + " °C" : Dash, null, c.Theme.Blue);
            var p = NebulaSensors.DgpuPower;
            y = Row(c, ColR, y, NebulaText.T("Power", "Мощность"), p is > 0 ? Math.Round(p.Value) + " " + NebulaText.Watt : Dash, null, c.Theme.Blue);
            int gpuBase = AppConfig.Get("gpu_base", -1);
            string mode = AppConfig.Is("gpu_auto") ? NebulaText.Optimized : AppConfig.Get("gpu_mode") switch { AsusACPI.GPUModeEco => NebulaText.Eco, AsusACPI.GPUModeUltimate => NebulaText.Ultimate, _ => NebulaText.Standard };
            y = Row(c, ColR, y, NebulaText.T("Mode", "Режим"), mode, null, c.Theme.Blue);

            c.Sparkline(c.R(ColR, y + 4, ColW, 40), NebulaSensors.GpuHistory.ToArray(), NebulaSensors.HistoryLength, c.Theme.Blue);
        }

        private void PaintFans(NebulaCanvas c)
        {
            float y = Row2;
            BlockTitle(c, ColL, y + 22, "fan", NebulaText.T("Fans", "Вентиляторы"));
            var link = c.F(11, FontStyle.Bold);
            c.Txt(NebulaText.Configure, ColL + ColW, y + 22, link, c.IsHover("overview:fans") ? c.Theme.Text : c.Theme.Accent, StringAlignment.Far);
            c.Hit(c.R(ColL + ColW - 110, y, 110, 30), "overview:fans");
            y += 66;

            y = FanRow(c, y, NebulaText.T("CPU fan", "Вентилятор CPU"), HardwareControl.cpuFan, "fan_max_0");
            y = FanRow(c, y, NebulaText.T("GPU fan", "Вентилятор GPU"), HardwareControl.gpuFan, "fan_max_1");
            if (AppConfig.Is("mid_fan")) y = FanRow(c, y, NebulaText.T("System fan", "Системный вентилятор"), HardwareControl.midFan, "fan_max_2");

            bool custom = AppConfig.IsApplyFans();
            Row(c, ColL, y, NebulaText.T("Curve", "Кривая"), custom ? NebulaText.T("custom", "своя") : NebulaText.T("factory", "заводская"), null, c.Theme.Accent);
        }

        private float FanRow(NebulaCanvas c, float y, string label, string? reading, string maxKey)
        {
            if (string.IsNullOrEmpty(reading)) return Row(c, ColL, y, label, Dash, null, c.Theme.Accent);
            int rpm = ParseLeadingInt(reading);
            int pct = ParsePercent(reading);
            if (pct < 0)
            {
                int max = AppConfig.Get(maxKey, 0) * 100;
                if (max > 0 && rpm > 0) pct = Math.Clamp(rpm * 100 / max, 0, 100);
            }
            return Row(c, ColL, y, label, rpm > 0 ? $"{rpm:N0} " + NebulaText.Rpm : reading, pct >= 0 ? pct / 100f : null, c.Theme.Accent);
        }

        private void PaintMemory(NebulaCanvas c)
        {
            float y = Row2;
            BlockTitle(c, ColR, y + 22, "chart", NebulaText.T("Memory", "Память"));
            y += 66;

            var used = HardwareControl.ramUsedMb; var pct = HardwareControl.ramUsage;
            if (used is not null && pct is not null)
            {
                double totalGb = used.Value / 1024.0 / Math.Max(1, pct.Value) * 100.0;
                y = Row(c, ColR, y, NebulaText.T("RAM", "ОЗУ"), $"{used.Value / 1024.0:0.0} / {Math.Round(totalGb)} {NebulaText.Gb}", pct.Value / 100f, c.Theme.Blue);
            }
            else y = Row(c, ColR, y, NebulaText.T("RAM", "ОЗУ"), Dash, null, c.Theme.Blue);

            if (NebulaSensors.Disk is { } d && d.totalGb > 0)
                y = Row(c, ColR, y, NebulaText.T("System drive", "Накопитель"), $"{d.usedGb:N0} / {d.totalGb:N0} {NebulaText.Gb}", d.usedGb / (float)d.totalGb, c.Theme.Blue);

            try
            {
                var v = HardwareControl.GpuControl?.GetVramInfo();
                if (HardwareControl.gpuTemp is > 0 && v is { totalMb: > 0 })
                    y = Row(c, ColR, y, "VRAM", $"{v.Value.usedMb / 1024.0:0.0} / {Math.Round(v.Value.totalMb / 1024.0)} {NebulaText.Gb}", v.Value.usedMb / (float)v.Value.totalMb, c.Theme.Blue);
            }
            catch { }

            var b = HardwareControl.batteryCharge ?? "";
            if (b.Length > 0)
            {
                int ch = ParseLeadingInt(b);
                string rate = HardwareControl.batteryRate is { } r && r != 0 ? (r > 0 ? " · +" : " · −") + Math.Abs(Math.Round(r, 1)) + " " + NebulaText.Watt : "";
                y = Row(c, ColR, y, NebulaText.T("Battery", "Батарея"), b + rate, ch >= 0 ? ch / 100f : null, c.Theme.Blue);
            }
        }

        // ---- modes & quick access ------------------------------------------------------------------
        private static void PaintModes(NebulaCanvas c)
        {
            var th = c.Theme;
            c.Txt(NebulaText.Mode, 124, 760, c.F(11, FontStyle.Bold), th.Faint);

            var list = Modes.GetList();
            int current = Modes.GetCurrent();
            int n = Math.Min(list.Count, 6);
            float w = Math.Min(185, (1270 - 13 * (n - 1)) / n);
            float x = 124;
            for (int i = 0; i < n; i++)
            {
                int mode = list[i];
                bool active = mode == current;
                var r = c.R(x, 776, w, 72);
                c.Card(r, 12, active ? th.AccentBg : c.IsHover("mode:" + mode) ? th.Raised : th.Card, active ? th.Accent : th.Line);
                if (active) using (var b = new SolidBrush(th.Accent)) c.G.FillRectangle(b, c.R(x + 12, 846, w - 24, 2));
                c.IconAt(IconFor(mode), x + 15, 790, 24, active);
                c.Txt(Modes.GetName(mode), x + 49, 806, c.F(15, FontStyle.Bold), active ? th.Accent : th.Text);
                c.Txt(HintFor(mode), x + 15, 834, c.F(10), th.Muted);
                c.Hit(r, "mode:" + mode);
                x += w + 13;
            }
            if (list.Count > 6)
            {
                var r = c.R(x, 776, 1394 - x, 72);
                c.Card(r, 12, th.Card, th.Line);
                c.Txt(NebulaText.More, x + (1394 - x) / 2, 816, c.F(13, FontStyle.Bold), th.Text, StringAlignment.Center);
                c.Hit(r, "mode:more");
            }
        }

        private static string HintFor(int mode) => Modes.GetBase(mode) switch
        {
            2 => NebulaText.SilentHint,
            1 => NebulaText.TurboHint,
            _ => mode > 2 ? NebulaText.CustomHint : NebulaText.BalancedHint,
        };

        private static string IconFor(int mode) => Modes.GetBase(mode) switch { 2 => "leaf", 1 => "power", _ => "chart" };

        private static void PaintQuick(NebulaCanvas c)
        {
            c.Txt(NebulaText.QuickAccess, 124, 886, c.F(11, FontStyle.Bold), c.Theme.Faint);

            int gpuMode = AppConfig.Get("gpu_mode");
            bool gpuAuto = AppConfig.Is("gpu_auto");
            string gpuName = gpuMode switch { AsusACPI.GPUModeEco => NebulaText.Eco, AsusACPI.GPUModeUltimate => NebulaText.Ultimate, _ => NebulaText.Standard };
            QuickCard(c, 124, "quick:gpu", "gpu", gpuAuto ? NebulaText.Optimized : gpuName, gpuAuto ? NebulaText.GraphicsAuto + " · " + gpuName : NebulaText.GraphicsMode);

            int hz = AppConfig.Get("frequency", 0);
            bool auto = AppConfig.Is("screen_auto");
            QuickCard(c, 446, "quick:screen", "display", hz > 0 ? hz + " " + NebulaText.T("Hz", "Гц") : Dash, NebulaText.ScreenHint + (auto ? " · " + NebulaText.ScreenAuto : ""));

            int limit = AppConfig.Get("charge_limit", 100);
            QuickCard(c, 768, "quick:battery", "battery", limit + "%", NebulaText.BatteryHint);

            string aura = Dash;
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
            var r = c.R(x, 902, 304, 96);
            bool hv = c.IsHover(id);
            c.Card(r, 14, hv ? th.Raised : th.Card, hv ? th.Accent : th.Line);
            c.IconAt(icon, x + 20, 918, 24, hv);
            c.Txt(value, x + 56, 936, c.F(19, FontStyle.Bold, true), th.Text);
            c.Txt(hint, x + 20, 980, c.F(11), th.Muted);
            c.Hit(r, id);
        }

        // ---- helpers -------------------------------------------------------------------------------
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

        // ---- actions -------------------------------------------------------------------------------
        public override bool Click(string id, Point at, NebulaForm form)
        {
            if (id == "mode:more")
            {
                int current = Modes.GetCurrent();
                Menu(form, at, Modes.GetList().Skip(6).Select(m => (Modes.GetName(m), m == current, (Action)(() => Program.modeControl.SetPerformanceMode(m, true)))));
                return true;
            }
            if (id.StartsWith("mode:")) { Program.modeControl.SetPerformanceMode(int.Parse(id[5..]), true); return true; }
            if (id.StartsWith("overview:dev:")) { form.ShowPage("mouse"); return true; }

            switch (id)
            {
                case "overview:fans": form.ShowPage("fan"); return true;
                case "overview:devices": form.ShowPage("mouse"); return true;
                case "quick:gpu": form.ShowPage("gpu"); return true;
                case "quick:screen": form.ShowPage("display"); return true;
                case "quick:battery": form.ShowPage("battery"); return true;
                case "quick:light": form.ShowPage("light"); return true;
            }
            return false;
        }
    }
}
