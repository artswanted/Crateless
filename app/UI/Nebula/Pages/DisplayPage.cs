using GHelper.Display;
using System.Diagnostics;

namespace GHelper.UI.Nebula.Pages
{
    /// <summary>Display page (design kit screen "display").</summary>
    public sealed class DisplayPage : NebulaPage
    {
        public override string Id => "display";
        public override string Title => NebulaText.T("Display", "Экран");
        public override string Subtitle => NebulaText.T("Your screen. Your settings.", "Твой экран. Твои настройки.");

        private static readonly bool oled = AppConfig.IsOLED();

        private int brightnessDraft = -1;
        private bool external;
        private int elmb = -1;
        private bool hdr;
        private bool overdriveSupported;

        public override void Refresh(bool opened)
        {
            if (!opened) return;
            brightnessDraft = -1;
            try { external = ScreenNative.IsExternalDisplayConnected(); } catch { external = false; }
            try { elmb = ScreenELMB.Get(); } catch { elmb = -1; }
            try { hdr = ScreenCCD.GetHDRStatus(out _); } catch { hdr = false; }
            try { overdriveSupported = Program.acpi.IsOverdriveSupported(); } catch { overdriveSupported = false; }
        }

        public override void Paint(NebulaCanvas c)
        {
            var th = c.Theme;

            int frequency = AppConfig.Get("frequency", -1);
            int max = AppConfig.Get("max_frequency", 0);
            int min = ScreenControl.MIN_RATE;
            bool auto = AppConfig.Is("screen_auto");
            bool overdrive = AppConfig.Is("overdrive");
            bool noOverdrive = AppConfig.IsNoOverdrive();
            bool enabled = frequency > 0;

            // ---- refresh rate -------------------------------------------------------------------
            c.Surface(236, 139, 818, 182);
            c.Txt(NebulaText.T("Built-in display", "Встроенный дисплей"), 256, 169, c.F(15, FontStyle.Bold), th.Text);

            string panel = AppConfig.GetString("internal_display")?.Trim() ?? "";
            string sub = (oled ? "OLED · " : "") + (panel.Length > 0 ? panel + " · " : "") +
                         (max > 0 ? NebulaText.T($"up to {max} Hz", $"до {max} Гц") : "") +
                         (enabled ? NebulaText.T($"  ·  now {frequency} Hz", $"  ·  сейчас {frequency} Гц") : NebulaText.T("  ·  turned off", "  ·  выключен"));
            c.Txt(sub, 256, 190, c.F(11), th.Muted);

            c.Segment(256, 235, 210, 36, NebulaText.T("Auto", "Авто"), auto, "display:auto", enabled);
            c.Segment(478, 235, 210, 36, min + " " + NebulaText.T("Hz", "Гц"), !auto && frequency == min, "display:min", enabled);
            string hi = (max > min ? max : frequency) + " " + NebulaText.T("Hz", "Гц") + (overdriveSupported && !noOverdrive ? " + OD" : "");
            c.Segment(700, 235, 210, 36, hi, !auto && frequency > min, "display:max", enabled && max > min);

            c.Txt(NebulaText.T($"Auto: {max} Hz on AC, {min} Hz on battery.", $"Авто: {max} Гц от сети, {min} Гц от батареи."), 256, 301, c.F(11), th.Faint);

            // ---- picture ------------------------------------------------------------------------
            c.Surface(236, 337, 399, 361);
            c.Txt(NebulaText.T("Picture", "Изображение"), 256, 367, c.F(15, FontStyle.Bold), th.Text);

            float row = 421;
            if (oled)
            {
                int b = brightnessDraft >= 0 ? brightnessDraft : VisualControl.GetBrightness();
                c.Txt(NebulaText.T("Brightness (flicker-free)", "Яркость (без мерцания)"), 256, row, c.F(12), th.Muted);
                c.Txt(b + "%", 615, row, c.F(14, FontStyle.Bold), th.Text, StringAlignment.Far);
                c.Slider(256, row + 18, 359, b / 100f, "display:brightness");
                row += 70;
            }

            bool visual = false;
            try { visual = VisualControl.IsEnabled() && VisualControl.GetVisualModes().Count > 0; } catch { }
            if (visual)
            {
                string mode = "—";
                try
                {
                    var modes = VisualControl.GetVisualModes();
                    var cur = (SplendidCommand)AppConfig.Get("visual", (int)VisualControl.GetDefaultVisualMode());
                    if (modes.TryGetValue(cur, out var n)) mode = n;
                }
                catch { }
                c.ValueRow(256, row, 359, NebulaText.T("Visual mode", "Визуальный режим"), mode, "display:visual");
                row += 60;

                Dictionary<SplendidGamut, string>? gamuts = null;
                try { gamuts = VisualControl.GetGamutModes(); } catch { }
                if (gamuts is { Count: > 0 })
                {
                    string g = "—";
                    var cur = (SplendidGamut)AppConfig.Get("gamut", (int)VisualControl.GetDefaultGamut());
                    if (gamuts.TryGetValue(cur, out var n)) g = n;
                    c.ValueRow(256, row, 359, NebulaText.T("Color gamut", "Цветовой охват"), g, "display:gamut");
                    row += 60;
                }
            }

            if (overdriveSupported)
            {
                c.Txt("Overdrive", 256, row + 30, c.F(13), th.Text);
                c.Txt(NebulaText.T("Panel response boost at high refresh.", "Ускорение отклика на высокой частоте."), 256, row + 50, c.F(11), th.Faint);
                c.Toggle(577, row + 12, !noOverdrive, "display:overdrive");
            }
            else if (!visual && !oled)
            {
                c.Txt(NebulaText.T("No picture controls reported by this panel.", "Панель не сообщает настроек изображения."), 256, row, c.F(11), th.Faint);
            }

            // ---- panel & behaviour ----------------------------------------------------------------
            c.Surface(651, 337, 403, 361);
            c.Txt(NebulaText.T("Panel and behaviour", "Панель и поведение"), 671, 367, c.F(15, FontStyle.Bold), th.Text);

            float y = 413;
            int miniled = AppConfig.Get("miniled", -1);
            if (miniled >= 0)
            {
                c.Txt("Mini-LED " + NebulaText.T("multizone", "мультизона"), 671, y, c.F(13), th.Text);
                c.Txt(hdr ? NebulaText.T("HDR is on: zones stay active.", "HDR включён: зоны активны.") : NebulaText.T("Local dimming zones.", "Зоны локального затемнения."), 671, y + 31, c.F(11), th.Faint);
                c.Toggle(996, y - 17, miniled == 1 || hdr, "display:miniled", !hdr);
                y += 85;
            }

            if (elmb >= 0)
            {
                c.Txt("ELMB", 671, y, c.F(13), th.Text);
                c.Txt(NebulaText.T("Motion blur reduction.", "Снижение смазывания движения."), 671, y + 31, c.F(11), th.Faint);
                c.Toggle(996, y - 17, elmb == 1, "display:elmb");
                y += 85;
            }

            c.Txt("HDR", 671, y, c.F(13), th.Text);
            c.Txt(hdr ? NebulaText.T("On in Windows.", "Включён в Windows.") : NebulaText.T("Off. Managed by Windows.", "Выключен. Управляется Windows."), 671, y + 31, c.F(11), th.Faint);
            y += 85;

            var link = c.F(12, FontStyle.Bold);
            c.Txt(NebulaText.T("Windows display settings", "Параметры экрана Windows"), 671, y, c.F(13), th.Text);
            c.Txt(NebulaText.T("Open  ↗", "Открыть  ↗"), 1034, y, link, c.IsHover("display:windows") ? th.Text : th.Accent, StringAlignment.Far);
            c.Hit(c.R(671, y - 20, 363, 30), "display:windows");

            if (external)
                c.Txt(NebulaText.T("External displays are detected separately; these settings apply to the built-in panel.", "Внешние экраны определяются отдельно; настройки относятся к встроенной панели."), 671, 653, c.F(11), th.Faint);
        }

        public override bool Click(string id, Point at, NebulaForm form)
        {
            switch (id)
            {
                case "display:auto":
                    ScreenControl.SetAutoRefresh(1);
                    ScreenControl.AutoScreen();
                    return true;
                case "display:min":
                    ScreenControl.SetAutoRefresh(0);
                    ScreenControl.SetScreen(ScreenControl.MIN_RATE, 0);
                    return true;
                case "display:max":
                    ScreenControl.SetAutoRefresh(0);
                    ScreenControl.SetScreen(ScreenControl.MAX_REFRESH, 1);
                    return true;
                case "display:overdrive":
                    AppConfig.Set("no_overdrive", AppConfig.IsNoOverdrive() ? 0 : 1);
                    ScreenControl.AutoScreen(true);
                    return true;
                case "display:miniled":
                    ScreenControl.ToogleMiniled();
                    return true;
                case "display:elmb":
                {
                    int v = elmb == 1 ? 0 : 1;
                    AppConfig.Set("elmb", v);
                    ScreenELMB.Set(v);
                    elmb = v;
                    return true;
                }
                case "display:windows":
                    try { Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true }); } catch { }
                    return true;
                case "display:visual":
                {
                    var modes = VisualControl.GetVisualModes();
                    var cur = (SplendidCommand)AppConfig.Get("visual", (int)VisualControl.GetDefaultVisualMode());
                    int temp = AppConfig.Get("color_temp", VisualControl.DefaultColorTemp);
                    Menu(form, at, modes.Select(m => (m.Value, m.Key == cur, (Action)(() =>
                    {
                        VisualControl.SetVisual(m.Key, temp);
                        Program.settingsForm.InitVisual();
                    }))));
                    return true;
                }
                case "display:gamut":
                {
                    var gamuts = VisualControl.GetGamutModes();
                    var cur = (SplendidGamut)AppConfig.Get("gamut", (int)VisualControl.GetDefaultGamut());
                    Menu(form, at, gamuts.Select(g => (g.Value, g.Key == cur, (Action)(() => VisualControl.SetGamut((int)g.Key)))));
                    return true;
                }
            }
            return false;
        }

        public override void Drag(string id, float t, bool done)
        {
            if (id != "display:brightness") return;
            brightnessDraft = (int)Math.Round(t * 100);
            if (done)
            {
                VisualControl.SetBrightness(brightnessDraft);
                Program.settingsForm.VisualiseBrightness();
                brightnessDraft = -1;
            }
        }
    }
}
