using GHelper.Fan;
using GHelper.Mode;
using GHelper.USB;
using System.Drawing.Drawing2D;

namespace GHelper.UI.Nebula.Pages
{
    /// <summary>Cooling page (design kit screen "fan"): BIOS fan curve editor per fan, hysteresis, apply/factory.</summary>
    public sealed class FanPage : NebulaPage
    {
        public override string Id => "fan";
        public override string Title => NebulaText.T("Cooling", "Охлаждение");
        public override string Subtitle => NebulaText.T("Silence and performance, balanced your way.", "Тишина и производительность — в твоём балансе.");

        private const int TempMin = 20, TempMax = 110, FanMax = 100;
        private const int Points = 8;

        // plot area (design px)
        private const float PlotX = 190, PlotY = 400, PlotW = 790, PlotH = 330;

        private readonly List<AsusFan> fans = new();
        private readonly Dictionary<AsusFan, byte[]> curves = new();   // draft: 8 temps + 8 percents
        private AsusFan current = AsusFan.CPU;
        private int selected = -1;
        private bool dirty;
        private bool clamp = AppConfig.Is("fan_clamp");

        private int hystUp = -1, hystDown = -1;
        private bool hystSupported;
        private static readonly string[] HystNames = { Properties.Strings.VeryLow, Properties.Strings.Low, Properties.Strings.Medium, Properties.Strings.High, Properties.Strings.VeryHigh };

        public override void Refresh(bool opened)
        {
            if (!opened) return;
            fans.Clear();
            fans.Add(AsusFan.CPU);
            fans.Add(AsusFan.GPU);
            try
            {
                if (!AsusACPI.IsEmptyCurve(Program.acpi.GetFanCurve(AsusFan.Mid)) || Program.acpi.IsMidFanSupported()) { AppConfig.Set("mid_fan", 1); fans.Add(AsusFan.Mid); }
                else AppConfig.Set("mid_fan", 0);
                if (Program.acpi.IsXGConnected() || XGM.IsConnected()) { AppConfig.Set("xgm_fan", 1); fans.Add(AsusFan.XGM); }
                else AppConfig.Set("xgm_fan", 0);
            }
            catch { }
            if (!fans.Contains(current)) current = AsusFan.CPU;

            if (!dirty)
            {
                curves.Clear();
                foreach (var f in fans) curves[f] = Load(f, false);
            }

            try
            {
                var d = Program.acpi.GetFanHysteresis();
                hystSupported = d.up >= 0 && d.down >= 0;
                if (hystSupported)
                {
                    int up = AppConfig.GetMode("hysteresis_up");
                    int down = AppConfig.GetMode("hysteresis_down");
                    hystUp = Math.Clamp(up < 0 ? (d.up > 0 ? d.up : 3) : up, 1, 5);
                    hystDown = Math.Clamp(down < 0 ? (d.down > 0 ? d.down : 3) : down, 1, 5);
                }
            }
            catch { hystSupported = false; }
        }

        /// <summary>Same rules as Fans.LoadProfile: stored config, otherwise the BIOS curve for the current mode.</summary>
        private static byte[] Load(AsusFan device, bool reset)
        {
            byte[] curve = AppConfig.GetFanConfig(device);
            if (reset || AsusACPI.IsInvalidCurve(curve))
            {
                curve = Program.acpi.GetFanCurve(device, Modes.GetCurrentBase());
                if (AsusACPI.IsInvalidCurve(curve)) curve = AppConfig.GetDefaultCurve(device);
                curve = AsusACPI.FixFanCurve(curve);
            }
            var c = (byte[])curve.Clone();
            byte old = 0;
            for (int i = 0; i < Points; i++) { if (c[i] == old) c[i]++; old = c[i]; }
            return c;
        }

        private static string FanName(AsusFan f) => f switch
        {
            AsusFan.CPU => "CPU", AsusFan.GPU => "GPU", AsusFan.Mid => NebulaText.T("System", "Система"), _ => "XG Mobile",
        };

        private static string? Reading(AsusFan f) => f switch
        {
            AsusFan.CPU => HardwareControl.cpuFan, AsusFan.GPU => HardwareControl.gpuFan, AsusFan.Mid => HardwareControl.midFan, _ => null,
        };

        private static float? Temp(AsusFan f) => f == AsusFan.GPU || f == AsusFan.XGM ? HardwareControl.gpuTemp : HardwareControl.cpuTemp;

        private static string Level(int pct, AsusFan device)
        {
            if (pct == 0) return "OFF";
            if (!FanSensorControl.fanRpm) return pct + "%";
            int min = FanSensorControl.GetFanMin(device), max = FanSensorControl.GetFanMax(device);
            return (200 * Math.Floor((float)(min * 100 + (max - min) * pct) / 200)).ToString("N0") + " " + NebulaText.Rpm;
        }

        // ---- painting ------------------------------------------------------------------------------
        public override void Paint(NebulaCanvas c)
        {
            var th = c.Theme;
            bool custom = AppConfig.IsApplyFans();

            c.Txt(NebulaText.T("PROFILE", "ПРОФИЛЬ") + " / " + Modes.GetCurrentName().ToUpperInvariant(), 124, 182, c.F(11, FontStyle.Bold), th.Faint);
            c.Txt(NebulaText.T("Cool head.", "Холодная голова."), 124, 228, c.F(35, FontStyle.Bold, true), th.Text);
            c.Txt(NebulaText.T("BIOS drives the fans by default. Your own curve is optional.", "Заводской BIOS управляет вентиляторами. Своя кривая — по желанию."), 124, 257, c.F(13), th.Muted);

            // ---- chart card ------------------------------------------------------------------------
            c.Surface(124, 300, 882, 559);
            c.Txt(NebulaText.T("Fan curve", "Кривая вентилятора"), 150, 337, c.F(19, FontStyle.Bold, true), th.Text);
            c.Txt(NebulaText.T("Temperature → speed. Drag a point; Shift moves all.", "Температура → скорость. Тяни точку; Shift двигает все."), 150, 364, c.F(12), th.Muted);

            float tx = 1006 - 20 - fans.Count * 92;
            foreach (var f in fans)
            {
                c.Segment(tx, 320, 84, 36, FanName(f), f == current, "fan:tab:" + (int)f);
                tx += 92;
            }

            PaintPlot(c, custom);

            // hysteresis + options row
            float oy = 800;
            if (hystSupported)
            {
                c.Txt(NebulaText.T("Hysteresis ↑", "Гистерезис ↑"), 150, oy + 24, c.F(11), th.Muted);
                float hx = 250;
                for (int i = 1; i <= 5; i++) { c.Segment(hx, oy + 6, 40, 30, i.ToString(), hystUp == i, "fan:hup:" + i); hx += 44; }
                c.Txt(NebulaText.T("↓", "↓"), hx + 10, oy + 24, c.F(11), th.Muted);
                hx += 30;
                for (int i = 1; i <= 5; i++) { c.Segment(hx, oy + 6, 40, 30, i.ToString(), hystDown == i, "fan:hdown:" + i); hx += 44; }
                c.Txt(HystNames[Math.Clamp(hystUp, 1, 5) - 1] + " / " + HystNames[Math.Clamp(hystDown, 1, 5) - 1], hx + 8, oy + 24, c.F(10), th.Faint);
            }
            c.Txt(NebulaText.T("Show RPM", "Показывать об/мин"), 830, oy + 24, c.F(11), th.Muted, StringAlignment.Far);
            c.Toggle(842, oy + 8, FanSensorControl.fanRpm, "fan:rpm");
            c.Txt(NebulaText.T("Clamp", "Сетка"), 934, oy + 24, c.F(11), th.Muted, StringAlignment.Far);
            c.Toggle(946, oy + 8, clamp, "fan:clamp");

            // ---- side cards ------------------------------------------------------------------------
            c.Surface(1030, 300, 364, 190);
            c.Txt(FanName(current), 1095, 342, c.F(12, FontStyle.Bold), th.Faint);
            c.IconAt(current == AsusFan.GPU ? "gpu" : "cpu", 1055, 322, 24);
            var t = Temp(current);
            c.Txt(t is > 0 ? Math.Round(t.Value).ToString() : "—", 1055, 416, c.F(48, FontStyle.Bold, true), th.Text);
            c.Txt("°C  /  " + NebulaText.T("temperature", "температура"), 1055, 453, c.F(11), th.Muted);

            c.Surface(1030, 510, 364, 190);
            c.IconAt("fan", 1055, 532, 24);
            c.Txt(FanName(current).ToUpperInvariant() + " FAN", 1095, 552, c.F(12, FontStyle.Bold), th.Faint);
            string r = Reading(current) ?? "—";
            int rpm = ParseLeadingInt(r);
            c.Txt(rpm > 0 ? rpm.ToString("N0") : r, 1055, 626, c.F(48, FontStyle.Bold, true), th.Text);
            c.Txt(NebulaText.Rpm + "  /  " + NebulaText.T("current speed", "текущая скорость"), 1055, 663, c.F(11), th.Muted);

            c.Surface(1030, 720, 364, 139);
            c.Txt(NebulaText.T("Custom curve", "Своя кривая"), 1055, 756, c.F(16, FontStyle.Bold), th.Text);
            c.Txt(custom ? NebulaText.T("Now: your curve", "Сейчас: своя кривая") : NebulaText.T("Now: factory profile", "Сейчас: заводской профиль"), 1055, 799, c.F(12), th.Muted);
            c.Toggle(1330, 737, custom, "fan:custom");

            // ---- bottom bar --------------------------------------------------------------------------
            if (dirty) c.Txt("●  " + NebulaText.T("Unsaved changes", "Есть несохранённые изменения"), 124, 922, c.F(12), th.Warning);
            else if (selected >= 0 && curves.TryGetValue(current, out var cur))
                c.Txt(NebulaText.T("Point", "Точка") + $" {selected + 1}: {cur[selected]} °C → " + Level(cur[selected + Points], current), 124, 922, c.F(12), th.Muted);
            c.Button(966, 894, 196, 36, NebulaText.T("Factory profile", "Заводской профиль"), "fan:factory", primary: false);
            c.Button(1180, 894, 214, 36, NebulaText.T("Apply curve", "Применить кривую"), "fan:apply", primary: true, enabled: dirty || !custom);
            c.Txt(NebulaText.T("The curve is handed to BIOS. Direct RPM control is a separate feature and is not part of this editor.",
                               "Кривая передаётся BIOS. Прямое управление оборотами — отдельная функция и в этот редактор не входит."), 124, 976, c.F(12), th.Faint);
        }

        private void PaintPlot(NebulaCanvas c, bool custom)
        {
            var th = c.Theme;
            var plot = c.R(PlotX, PlotY, PlotW, PlotH);
            c.Card(plot, 8, th.Bg);

            using (var grid = new Pen(Color.FromArgb(90, th.Line), 1f))
            {
                for (int tt = 30; tt <= 100; tt += 10)
                {
                    float x = X(c, tt);
                    c.G.DrawLine(grid, x, plot.Y, x, plot.Bottom);
                }
            }
            // axis labels (draw after grid so text is crisp)
            for (int tt = 30; tt <= 100; tt += 10)
                c.Txt(tt + "°", PlotX + PlotW * (tt - TempMin) / (TempMax - TempMin), PlotY + PlotH + 18, c.F(10), th.Faint, StringAlignment.Center);
            for (int p = 0; p <= 100; p += 25)
            {
                float y = Y(c, p);
                using var grid = new Pen(Color.FromArgb(90, th.Line), 1f);
                c.G.DrawLine(grid, plot.X, y, plot.Right, y);
                c.Txt(p.ToString(), PlotX - 10, p == 0 ? PlotY + PlotH + 4 : PlotY + PlotH * (100 - p) / 100f + 4, c.F(10), th.Faint, StringAlignment.Far);
            }

            if (!curves.TryGetValue(current, out var cur)) return;
            Color line = current == AsusFan.GPU ? th.Blue : current == AsusFan.Mid ? th.Warning : th.Accent;
            if (!custom) line = Color.FromArgb(160, line);

            var pts = new PointF[Points];
            for (int i = 0; i < Points; i++) pts[i] = new PointF(X(c, cur[i]), Y(c, cur[i + Points]));

            using (var fill = new GraphicsPath())
            {
                fill.AddLine(pts[0].X, plot.Bottom, pts[0].X, pts[0].Y);
                fill.AddLines(pts);
                fill.AddLine(pts[^1].X, pts[^1].Y, pts[^1].X, plot.Bottom);
                fill.CloseFigure();
                using var lg = new LinearGradientBrush(plot, Color.FromArgb(60, line), Color.FromArgb(0, line), LinearGradientMode.Vertical);
                c.G.FillPath(lg, fill);
            }
            using (var pen = new Pen(line, Math.Max(1.5f, 2.2f * c.K)) { LineJoin = LineJoin.Round })
                c.G.DrawLines(pen, pts);

            float rad = c.S(7);
            for (int i = 0; i < Points; i++)
            {
                bool sel = i == selected;
                var pr = new RectangleF(pts[i].X - rad, pts[i].Y - rad, rad * 2, rad * 2);
                using (var b = new SolidBrush(sel ? th.Text : line)) c.G.FillEllipse(b, pr);
                using (var pen = new Pen(th.Bg, Math.Max(1f, 2f * c.K))) c.G.DrawEllipse(pen, pr);
                c.Hit(new RectangleF(pr.X - rad, pr.Y - rad, rad * 4, rad * 4), "slider:fan:pt:" + i);
            }
            if (selected >= 0)
            {
                string tip = $"{cur[selected]} °C  ·  {Level(cur[selected + Points], current)}";
                var f = c.F(11, FontStyle.Bold);
                float w = c.TextWidth(tip, f) + 16;
                float bx = Math.Clamp(pts[selected].X / c.K - w / 2, PlotX, PlotX + PlotW - w);
                float by = Math.Max(PlotY + 4, pts[selected].Y / c.K - 34);
                c.Card(c.R(bx, by, w, 22), 6, th.Raised, th.Line);
                c.Txt(tip, bx + 8, by + 15, f, th.Text);
            }

            // whole-plot hit for click-to-select nearest point
            c.Hit(plot, "fan:plot");
        }

        private static float X(NebulaCanvas c, float temp) => c.S(PlotX + PlotW * (temp - TempMin) / (TempMax - TempMin));
        private static float Y(NebulaCanvas c, float pct) => c.S(PlotY + PlotH * (100 - pct) / 100f);

        // ---- editing ----------------------------------------------------------------------------------
        /// <summary>Called by the form with plot-relative coordinates while dragging a point.</summary>
        public void DragPoint(int index, float tx, float ty, bool shift)
        {
            if (!curves.TryGetValue(current, out var cur) || index < 0 || index >= Points) return;
            selected = index;

            double dx = Math.Clamp(TempMin + tx * (TempMax - TempMin), TempMin, TempMax);
            double dy = Math.Clamp((1 - ty) * FanMax, 0, FanMax);
            double dymin = (dx - 70) * 1.2;
            if (dy < dymin) dy = dymin;

            if (clamp)
            {
                double minX = 30 + index * 10, maxX = minX + 9;
                dx = Math.Max(minX, Math.Min(maxX, dx));
            }

            if (shift)
            {
                double deltaY = dy - cur[index + Points];
                for (int i = 0; i < Points; i++) cur[i + Points] = (byte)Math.Clamp(cur[i + Points] + deltaY, 0, 100);
            }
            else
            {
                cur[index] = (byte)Math.Round(dx);
                cur[index + Points] = (byte)Math.Round(dy);
                AdjustLevels(cur, index);
            }
            dirty = true;
        }

        /// <summary>Same neighbour rules as Fans.AdjustAllLevels: keep the curve monotonic.</summary>
        private static void AdjustLevels(byte[] cur, int index)
        {
            int x = cur[index], y = cur[index + Points];
            for (int i = index + 1; i < Points; i++)
            {
                if (cur[i + Points] < y) cur[i + Points] = (byte)y;
                if (cur[i] < x) cur[i] = (byte)x;
            }
            for (int i = index - 1; i >= 0; i--)
            {
                if (cur[i + Points] > y) cur[i + Points] = (byte)y;
                if (cur[i] > x) cur[i] = (byte)x;
            }
        }

        public override void Drag(string id, float t, bool done)
        {
        }

        public override bool Click(string id, Point at, NebulaForm form)
        {
            if (id.StartsWith("fan:tab:")) { current = (AsusFan)int.Parse(id[8..]); selected = -1; return true; }
            if (id.StartsWith("fan:hup:")) { hystUp = int.Parse(id[8..]); SaveHysteresis(); return true; }
            if (id.StartsWith("fan:hdown:")) { hystDown = int.Parse(id[10..]); SaveHysteresis(); return true; }
            if (id.StartsWith("slider:fan:pt:")) { selected = int.Parse(id[14..]); return true; }

            switch (id)
            {
                case "fan:plot": return true;
                case "fan:rpm": FanSensorControl.fanRpm = !FanSensorControl.fanRpm; return true;
                case "fan:clamp": clamp = !clamp; AppConfig.Set("fan_clamp", clamp ? 1 : 0); return true;
                case "fan:custom":
                    AppConfig.SetMode("auto_apply", AppConfig.IsApplyFans() ? 0 : 1);
                    Program.modeControl.SetPerformanceMode();
                    return true;
                case "fan:apply":
                    foreach (var f in fans) if (curves.TryGetValue(f, out var cv)) AppConfig.SetFanConfig(f, cv);
                    AppConfig.SetMode("auto_apply", 1);
                    dirty = false;
                    Task.Run(() => Program.modeControl.AutoFans());
                    return true;
                case "fan:factory":
                    foreach (var f in fans) { curves[f] = Load(f, true); AppConfig.SetFanConfig(f, curves[f]); }
                    AppConfig.SetMode("auto_apply", 0);
                    if (hystSupported) { AppConfig.RemoveMode("hysteresis_up"); AppConfig.RemoveMode("hysteresis_down"); }
                    dirty = false;
                    selected = -1;
                    Program.modeControl.SetPerformanceMode();
                    Refresh(true);
                    return true;
            }
            return false;
        }

        private void SaveHysteresis()
        {
            AppConfig.SetMode("hysteresis_up", hystUp);
            AppConfig.SetMode("hysteresis_down", hystDown);
            Task.Run(() => Program.acpi.SetFanHysteresis(hystUp, hystDown));
        }

        /// <summary>Plot rectangle in design px, for the form's drag math.</summary>
        public static RectangleF PlotRect => new(PlotX, PlotY, PlotW, PlotH);

        private static int ParseLeadingInt(string s)
        {
            int i = 0; while (i < s.Length && !char.IsDigit(s[i])) i++;
            int j = i; while (j < s.Length && char.IsDigit(s[j])) j++;
            return j > i && int.TryParse(s[i..j], out var v) ? v : -1;
        }
    }
}
