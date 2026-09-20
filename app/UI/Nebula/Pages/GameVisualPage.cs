using GHelper.Display;

namespace GHelper.UI.Nebula.Pages
{
    /// <summary>GameVisual page: the panel's colour presets (ASUS Splendid), colour space and temperature.</summary>
    public sealed class GameVisualPage : NebulaPage
    {
        public override string Id => "visual";
        public override string Title => "GameVisual";
        public override string Subtitle => NebulaText.T("Colour presets of the panel. Fn+V cycles them.", "Цветовые пресеты панели. Fn+V переключает по кругу.");

        private static readonly (SplendidCommand cmd, string icon, string en, string ru)[] Tiles =
        {
            (SplendidCommand.Default, "overview", "Accurate colours for photos and the web.", "Точные цвета для фото и сайтов."),
            (SplendidCommand.Racing, "power", "Sharper, faster response for racing games.", "Резче и быстрее, для гонок."),
            (SplendidCommand.Scenery, "sun", "Brighter, more contrast and saturation.", "Ярче, контрастнее, насыщеннее."),
            (SplendidCommand.RTS, "chart", "Detail and colour for strategy and RPG.", "Детали и цвет для стратегий и RPG."),
            (SplendidCommand.FPS, "search", "Lifts dark scenes so enemies stand out.", "Осветляет тёмные сцены, чтобы видеть врагов."),
            (SplendidCommand.Cinema, "display", "Contrast and saturation for video.", "Контраст и насыщенность для видео."),
            (SplendidCommand.Vivid, "light", "Maximum saturation and brightness.", "Максимальная насыщенность и яркость."),
            (SplendidCommand.Eyecare, "heart", "Less blue light for long sessions.", "Меньше синего света для долгой работы."),
            (SplendidCommand.EReading, "moon", "Black and white, paper-like.", "Чёрно-белый режим, как бумага."),
            (SplendidCommand.VivoNormal, "overview", "Accurate colours for photos and the web.", "Точные цвета для фото и сайтов."),
            (SplendidCommand.VivoVivid, "light", "Maximum saturation and brightness.", "Максимальная насыщенность и яркость."),
            (SplendidCommand.VivoManual, "settings", "Manual colour settings.", "Ручные настройки цвета."),
            (SplendidCommand.VivoEycare, "heart", "Less blue light for long sessions.", "Меньше синего света для долгой работы."),
        };

        private float bottom = 700;
        public override float ContentHeight => bottom + 20;

        public override void Paint(NebulaCanvas c)
        {
            var th = c.Theme;
            Dictionary<SplendidCommand, string> modes;
            try { modes = VisualControl.GetVisualModes(); } catch { modes = new(); }
            if (modes.Count == 0)
            {
                c.Surface(236, 139, 1158, 120);
                c.Txt(NebulaText.T("This panel reports no GameVisual presets.", "Панель не сообщает пресетов GameVisual."), 256, 190, c.F(13), th.Muted);
                bottom = 259;
                return;
            }

            bool enabled = VisualControl.IsEnabled();
            var cur = (SplendidCommand)AppConfig.Get("visual", (int)VisualControl.GetDefaultVisualMode());
            int temp = AppConfig.Get("color_temp", VisualControl.DefaultColorTemp);
            var tiles = Tiles.Where(t => modes.ContainsKey(t.cmd)).ToList();
            int rows = (tiles.Count + 3) / 4;
            float h = 100 + rows * 128 + 40;

            c.Surface(236, 139, 1158, h);
            c.Txt(NebulaText.T("Presets", "Пресеты"), 256, 169, c.F(15, FontStyle.Bold), th.Text);
            c.Txt(NebulaText.T("Applied through ASUS Splendid. Off restores the panel's native output.", "Применяются через ASUS Splendid. Выключение возвращает родной вывод панели."), 256, 190, c.F(11), th.Muted);
            c.Txt(NebulaText.T("GameVisual", "GameVisual"), 1300, 175, c.F(12), th.Muted, StringAlignment.Far);
            c.Toggle(1336, 158, enabled && cur != SplendidCommand.Disabled, "visual:gv");

            float y = 218;
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                float x = 256 + (i % 4) * 284;
                float ty = y + (i / 4) * 128;
                bool active = enabled && cur == t.cmd;
                var r = c.R(x, ty, 268, 112);
                c.Card(r, 12, active ? th.AccentBg : c.IsHover("visual:set:" + (int)t.cmd) ? th.Raised : th.Card, active ? th.Accent : th.Line);
                c.IconAt(t.icon, x + 18, ty + 18, 24, active);
                c.Txt(modes[t.cmd], x + 54, ty + 36, c.F(15, FontStyle.Bold), active ? th.Accent : th.Text);
                c.Txt(NebulaText.T(t.en, t.ru), x + 18, ty + 72, c.F(11), th.Muted);
                if (active) using (var b = new SolidBrush(th.Accent)) c.G.FillRectangle(b, c.R(x + 12, ty + 110, 244, 2));
                c.Hit(r, "visual:set:" + (int)t.cmd);
            }

            float cy = 139 + h + 16;
            c.Surface(236, cy, 1158, 170);
            c.Txt(NebulaText.T("Colour", "Цвет"), 256, cy + 30, c.F(15, FontStyle.Bold), th.Text);

            Dictionary<SplendidGamut, string>? gamuts = null;
            try { gamuts = VisualControl.GetGamutModes(); } catch { }
            float ry = cy + 72;
            if (gamuts is { Count: > 0 })
            {
                string g = "—";
                var cg = (SplendidGamut)AppConfig.Get("gamut", (int)VisualControl.GetDefaultGamut());
                if (gamuts.TryGetValue(cg, out var n)) g = n.Replace("Gamut:", "").Trim();
                c.ValueRow(256, ry, 1118, NebulaText.T("Colour space", "Цветовое пространство"), g, "visual:gamut");
                ry += 46;
            }
            var temps = VisualControl.GetTemperatures();
            string tn = temps.TryGetValue(temp, out var tname) ? tname : temp.ToString();
            c.ValueRow(256, ry, 1118, NebulaText.T("Colour temperature", "Цветовая температура"), tn, "visual:temp", enabled);
            c.Txt(NebulaText.T("Colour profiles (ICC) for the panel are installed from the Windows colour settings if missing.",
                               "ICC-профили панели при их отсутствии ставятся из параметров цвета Windows."), 256, cy + 150, c.F(10), th.Faint);
            bottom = cy + 170;
        }

        public override bool Click(string id, Point at, NebulaForm form)
        {
            int temp = AppConfig.Get("color_temp", VisualControl.DefaultColorTemp);
            var cur = (SplendidCommand)AppConfig.Get("visual", (int)VisualControl.GetDefaultVisualMode());

            if (id.StartsWith("visual:set:") && int.TryParse(id[11..], out int gv))
            {
                VisualControl.SetRegStatus(1);
                VisualControl.SetVisual((SplendidCommand)gv, temp);
                Program.settingsForm.InitVisual();
                return true;
            }
            switch (id)
            {
                case "visual:gv":
                {
                    bool on = VisualControl.IsEnabled() && cur != SplendidCommand.Disabled;
                    if (on) VisualControl.SetVisual(SplendidCommand.Disabled, temp);
                    else { VisualControl.SetRegStatus(1); VisualControl.SetVisual(VisualControl.GetDefaultVisualMode(), temp); }
                    Program.settingsForm.InitVisual();
                    return true;
                }
                case "visual:temp":
                    Menu(form, at, VisualControl.GetTemperatures().Select(t => (t.Value, t.Key == temp, (Action)(() =>
                    {
                        VisualControl.SetVisual(cur, t.Key);
                        Program.settingsForm.InitVisual();
                    }))));
                    return true;
                case "visual:gamut":
                {
                    var gamuts = VisualControl.GetGamutModes();
                    var cg = (SplendidGamut)AppConfig.Get("gamut", (int)VisualControl.GetDefaultGamut());
                    Menu(form, at, gamuts.Select(g => (g.Value, g.Key == cg, (Action)(() => VisualControl.SetGamut((int)g.Key)))));
                    return true;
                }
            }
            return false;
        }
    }
}
