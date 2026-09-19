using GHelper.Helpers;
using GHelper.Input;
using GHelper.USB;
using System.Drawing.Drawing2D;

namespace GHelper.UI.Nebula.Pages
{
    /// <summary>Lighting page (design kit screen "light"): keyboard Aura effect, colour, brightness, sleep, AniMe/Slash.</summary>
    public sealed class LightPage : NebulaPage
    {
        public override string Id => "light";
        public override string Title => NebulaText.T("Lighting", "Персонализация");
        public override string Subtitle => NebulaText.T("Technology with your character.", "Технологии с твоим характером.");

        private static readonly Color[] Presets =
        {
            ColorTranslator.FromHtml("#BDABFF"), ColorTranslator.FromHtml("#8DCBFF"), ColorTranslator.FromHtml("#FDA2CF"),
            ColorTranslator.FromHtml("#F8C883"), ColorTranslator.FromHtml("#F6F5FF"), ColorTranslator.FromHtml("#85DCCB"),
        };

        private bool dynamicLightingConflict;

        public override void Refresh(bool opened)
        {
            if (!opened) return;
            try { dynamicLightingConflict = AppConfig.IsDynamicLighting() && DynamicLightingHelper.IsEnabled(); }
            catch { dynamicLightingConflict = false; }
        }

        public override void Paint(NebulaCanvas c)
        {
            var th = c.Theme;
            var modes = SafeModes();
            var mode = (AuraMode)AppConfig.Get("aura_mode", (int)AuraMode.AuraStatic);
            string modeName = modes.TryGetValue(mode, out var mn) ? mn : "—";

            // ---- hero ------------------------------------------------------------------------------
            c.Txt("AURA / " + NebulaText.T("LIGHTING", "ПОДСВЕТКА"), 124, 190, c.F(11, FontStyle.Bold), th.Faint);
            c.Txt(NebulaText.T("Your light.", "Свой свет."), 124, 247, c.F(49, FontStyle.Bold, true), th.Text);
            c.Txt(NebulaText.T("Pick the mood of your laptop.", "Выбери настроение своего ноутбука."), 124, 280, c.F(16), th.Muted);

            var box = c.R(124, 300, 780, 490);
            int topW = (int)Math.Min(box.Width, box.Height);
            var top = NebulaAssets.Scaled("hero-laptop-top", topW);
            if (top is not null)
            {
                var dst = new RectangleF(box.X + (box.Width - top.Width) / 2, box.Y + (box.Height - top.Height) / 2, top.Width, top.Height);
                c.G.DrawImage(top, dst);
                // keyboard glow layer follows the chosen colour (white when the effect has no fixed colour)
                bool colourless = mode is AuraMode.AuraRainbow or AuraMode.AuraColorCycle or AuraMode.HEATMAP or AuraMode.GPUMODE or AuraMode.AMBIENT or AuraMode.BATTERY or AuraMode.AUDIO or AuraMode.AUDIOPULSE;
                var glow = NebulaAssets.TintedGlow(colourless ? th.Accent : Aura.Color1, topW);
                if (glow is not null && InputDispatcher.GetBacklight() > 0)
                {
                    int levels = Math.Max(1, AppConfig.Get("max_brightness", 3));
                    float alpha = 0.35f + 0.65f * Math.Clamp(InputDispatcher.GetBacklight(), 0, levels) / levels;
                    var cm = new System.Drawing.Imaging.ColorMatrix { Matrix33 = alpha };
                    using var ia = new System.Drawing.Imaging.ImageAttributes();
                    ia.SetColorMatrix(cm);
                    c.G.DrawImage(glow, Rectangle.Round(dst), 0, 0, glow.Width, glow.Height, GraphicsUnit.Pixel, ia);
                }
            }
            c.Pill(132, 806, modeName.ToUpperInvariant(), th.AccentBg, th.Accent);

            if (dynamicLightingConflict)
            {
                var link = c.F(11, FontStyle.Bold);
                string warn = NebulaText.T("Windows Dynamic Lighting controls the keyboard. Open Windows settings  ↗",
                                           "Клавиатурой управляет Windows Dynamic Lighting. Открыть параметры Windows  ↗");
                c.Txt(warn, 124, 860, link, c.IsHover("light:dynamic") ? th.Text : th.Warning);
                c.Hit(c.R(124, 842, c.TextWidth(warn, link) + 8, 26), "light:dynamic");
            }

            // ---- effect panel ----------------------------------------------------------------------
            c.Surface(934, 161, 460, 649);
            c.Txt(NebulaText.T("Effect settings", "Настройка эффекта"), 962, 200, c.F(21, FontStyle.Bold, true), th.Text);

            c.Txt(NebulaText.T("EFFECT", "ЭФФЕКТ"), 962, 250, c.F(11, FontStyle.Bold), th.Faint);
            DropRow(c, 962, 270, 404, modeName, "light:mode");

            float y = 351;
            var speeds = SafeSpeeds();
            bool hasSpeed = speeds.Count > 0 && mode != AuraMode.AuraStatic;
            if (hasSpeed)
            {
                c.Txt(NebulaText.T("SPEED", "СКОРОСТЬ"), 962, y, c.F(11, FontStyle.Bold), th.Faint);
                var cur = (AuraSpeed)AppConfig.Get("aura_speed", (int)AuraSpeed.Normal);
                float sx = 962; float sw = (404 - 12 * (speeds.Count - 1)) / speeds.Count;
                foreach (var s in speeds)
                {
                    c.Segment(sx, y + 19, sw, 36, s.Value, s.Key == cur, "light:speed:" + (int)s.Key);
                    sx += sw + 12;
                }
                y += 85;
            }

            c.Txt(NebulaText.T("COLOR", "ЦВЕТ"), 962, y, c.F(11, FontStyle.Bold), th.Faint);
            float cx = 962;
            for (int i = 0; i < Presets.Length; i++)
            {
                var p = Presets[i];
                bool sel = Near(p, Aura.Color1);
                c.Card(c.R(cx, y + 23, 47, 47), 23.5f, p, sel ? th.Text : th.Line, sel ? 2.5f : 1f);
                c.Hit(c.R(cx, y + 23, 47, 47), "light:preset:" + i);
                cx += 68;
            }
            c.Txt("HEX  #" + Aura.Color1.R.ToString("X2") + Aura.Color1.G.ToString("X2") + Aura.Color1.B.ToString("X2"), 962, y + 105, c.F(13), th.Muted);
            c.Card(c.R(1160, y + 90, 22, 22), 11, Aura.Color1, th.Line);
            c.Button(1215, y + 83, 151, 36, NebulaText.T("Custom color", "Свой цвет"), "light:color", primary: false);
            if (Aura.HasSecondColor())
            {
                c.Card(c.R(1190, y + 90, 22, 22), 11, Aura.Color2, th.Line);
                c.Hit(c.R(1186, y + 86, 30, 30), "light:color2", NebulaText.T("Second color", "Второй цвет"));
            }
            y += 178;

            // brightness 0..max (default 3)
            int max = Math.Max(1, AppConfig.Get("max_brightness", 3));
            int b = InputDispatcher.GetBacklight();
            c.Txt(NebulaText.T("Brightness", "Яркость"), 962, y, c.F(12), th.Muted);
            c.Txt(b == 0 ? NebulaText.T("off", "выкл") : $"{b} / {max}", 1366, y, c.F(14, FontStyle.Bold), th.Text, StringAlignment.Far);
            float bx = 962; float bw = (404 - 12 * max) / (max + 1);
            for (int i = 0; i <= max; i++)
            {
                c.Segment(bx, y + 19, bw, 36, i == 0 ? NebulaText.T("Off", "Выкл") : i.ToString(), i == b, "light:bright:" + i);
                bx += bw + 12;
            }
            y += 88;

            c.Txt(NebulaText.T("Keep lit in sleep", "Подсветка во сне"), 962, y, c.F(14), th.Text);
            c.Toggle(1326, y - 16, AppConfig.Is("keyboard_sleep"), "light:sleep");
            y += 79;

            bool matrix = Program.settingsForm.matrixControl?.IsValid ?? false;
            if (matrix)
            {
                bool slash = Program.settingsForm.matrixControl!.IsSlash;
                c.Txt(slash ? "Slash Lighting" : "AniMe Matrix", 962, y, c.F(15, FontStyle.Bold), th.Text);
                c.Button(962, y + 32, 404, 36, NebulaText.T("Open device panel", "Открыть панель устройства"), "light:matrix", primary: false);
            }

            // ---- devices bar ---------------------------------------------------------------------
            c.Txt(NebulaText.T("ONE PALETTE. EVERY DEVICE.", "ОДНА ПАЛИТРА. ВСЕ УСТРОЙСТВА."), 124, 899, c.F(11, FontStyle.Bold), th.Faint);
            c.Surface(124, 921, 1270, 70);
            c.IconAt("keyboard", 148, 944, 24, true);
            c.Txt(NebulaText.T("Laptop keyboard", "Клавиатура ноутбука"), 193, 963, c.F(15, FontStyle.Bold), th.Text);
            c.Txt(NebulaText.T("External keyboard and mouse follow device support", "Внешняя клавиатура и мышь — по поддержке устройства"), 550, 963, c.F(12), th.Muted);
            c.Button(1180, 938, 188, 36, NebulaText.T("Devices", "Устройства"), "light:devices", primary: false);
        }

        private static void DropRow(NebulaCanvas c, float x, float y, float w, string value, string id)
        {
            var th = c.Theme;
            var r = c.R(x, y, w, 36);
            c.Card(r, 8, c.IsHover(id) ? th.Line : th.Raised);
            c.Txt(value, x + w / 2, y + 22.5f, c.F(12, FontStyle.Bold), th.Text, StringAlignment.Center);
            c.Txt("⌄", x + w - 16, y + 22, c.F(12, FontStyle.Bold), th.Muted, StringAlignment.Center);
            c.Hit(r, id);
        }

        private static bool Near(Color a, Color b) =>
            Math.Abs(a.R - b.R) < 6 && Math.Abs(a.G - b.G) < 6 && Math.Abs(a.B - b.B) < 6;

        private static Dictionary<AuraMode, string> SafeModes()
        {
            try { return Aura.GetModes(); } catch { return new Dictionary<AuraMode, string>(); }
        }

        private static Dictionary<AuraSpeed, string> SafeSpeeds()
        {
            try { return Aura.GetSpeeds(); } catch { return new Dictionary<AuraSpeed, string>(); }
        }

        public override bool Click(string id, Point at, NebulaForm form)
        {
            if (id.StartsWith("light:speed:"))
            {
                AppConfig.Set("aura_speed", int.Parse(id[12..]));
                Task.Run(Aura.ApplyAura);
                return true;
            }
            if (id.StartsWith("light:preset:"))
            {
                var p = Presets[int.Parse(id[13..])];
                AppConfig.Set("aura_color", p.ToArgb());
                Program.settingsForm.SetAura();
                return true;
            }
            if (id.StartsWith("light:bright:"))
            {
                int v = int.Parse(id[13..]);
                bool onBattery = SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online;
                AppConfig.Set(onBattery ? "keyboard_brightness_ac" : "keyboard_brightness", v);
                Aura.ApplyBrightness(v, "Nebula");
                Program.settingsForm.extraForm?.VisualiseBacklight(v);
                return true;
            }

            switch (id)
            {
                case "light:mode":
                {
                    var modes = SafeModes();
                    var cur = (AuraMode)AppConfig.Get("aura_mode", (int)AuraMode.AuraStatic);
                    Menu(form, at, modes.Select(m => (m.Value, m.Key == cur, (Action)(() =>
                    {
                        AppConfig.Set("aura_mode", (int)m.Key);
                        Program.settingsForm.SetAura();
                    }))));
                    return true;
                }
                case "light:color": PickColor(form, "aura_color", Aura.Color1, Aura.HasRandomColor()); return true;
                case "light:color2": PickColor(form, "aura_color2", Aura.Color2, false); return true;
                case "light:sleep":
                    AppConfig.Set("keyboard_sleep", AppConfig.Is("keyboard_sleep") ? 0 : 1);
                    Task.Run(Aura.ApplyPower);
                    return true;
                case "light:matrix": Program.settingsForm.MatrixToggle(); return true;
                case "light:devices": form.ShowPage("mouse"); return true;
                case "light:dynamic": DynamicLightingHelper.OpenSettings(); return true;
            }
            return false;
        }

        private static void PickColor(NebulaForm form, string key, Color initial, bool allowRandom)
        {
            var dlg = new RColorPicker(initial, allowRandom);
            dlg.ColorChanged += col =>
            {
                AppConfig.Set(key, col.ToArgb());
                Program.settingsForm.SetAura();
                form.Invalidate();
            };
            dlg.ShowDialog(form);
        }
    }
}
