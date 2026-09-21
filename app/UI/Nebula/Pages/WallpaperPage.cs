using GHelper.USB;
using System.Diagnostics;

namespace GHelper.UI.Nebula.Pages
{
    /// <summary>Wallpaper page: links to the official ROG gallery, a file picker and the Aura wallpaper.</summary>
    public sealed class WallpaperPage : NebulaPage
    {
        public override string Id => "wallpaper";
        public override string Title => NebulaText.T("Wallpaper", "Обои");
        public override string Subtitle => NebulaText.T("ROG galleries, your own image, or a still wallpaper in your Aura colour.", "Галереи ROG, своя картинка или обои в цвете подсветки.");

        private Bitmap? preview;
        private (int color, int style) previewKey = (-1, -1);

        public override float ContentHeight => 139 + 170 + 16 + 372 + 20;

        public override void Paint(NebulaCanvas c)
        {
            var th = c.Theme;

            // ---- ROG galleries ----------------------------------------------------------------------
            c.Surface(236, 139, 1158, 170);
            c.Txt(NebulaText.T("Official ROG wallpapers", "Официальные обои ROG"), 256, 169, c.F(15, FontStyle.Bold), th.Text);
            c.Txt(NebulaText.T("Free downloads on rog.asus.com, desktop and phone sizes. Nothing is bundled; pick a file below once it is downloaded.",
                               "Бесплатные загрузки на rog.asus.com, размеры для рабочего стола и телефона. В приложение ничего не встроено, скачанный файл выберите ниже."), 256, 190, c.F(11), th.Muted);
            c.Button(256, 224, 260, 36, NebulaText.T("ROG gallery  ↗", "Галерея ROG  ↗"), "wp:gallery", primary: true);
            c.Button(528, 224, 260, 36, NebulaText.T("By product  ↗", "По устройствам  ↗"), "wp:products", primary: false);
            c.Button(800, 224, 260, 36, NebulaText.T("Choose a file…", "Выбрать файл…"), "wp:file", primary: false);
            c.Button(1072, 224, 302, 36, NebulaText.T("Windows background settings  ↗", "Параметры фона Windows  ↗"), "wp:windows", primary: false);
            c.Txt(NebulaText.T("Choosing a file sets it as the wallpaper right away.", "Выбранный файл сразу становится обоями."), 256, 288, c.F(10), th.Faint);

            // ---- Aura wallpaper --------------------------------------------------------------------
            float y = 139 + 170 + 16;
            c.Surface(236, y, 1158, 372);
            c.Txt(NebulaText.T("Aura wallpaper", "Aura-обои"), 256, y + 30, c.F(15, FontStyle.Bold), th.Text);
            c.Txt(NebulaText.T("A still image in the current keyboard colour. Redrawn only when the colour or effect changes, so nothing runs in the background.",
                               "Статичная картинка в текущем цвете клавиатуры. Перерисовывается только при смене цвета или эффекта, в фоне ничего не работает."), 256, y + 51, c.F(11), th.Muted);
            c.Toggle(1336, y + 18, AuraWallpaper.IsEnabled, "wp:aura");

            var style = AuraWallpaper.CurrentStyle;
            c.Txt(NebulaText.T("Style", "Стиль"), 256, y + 96, c.F(12, FontStyle.Bold), th.Text);
            c.Segment(256, y + 108, 150, 34, NebulaText.T("Gradient", "Градиент"), style == AuraWallpaper.Style.Gradient, "wp:style:0");
            c.Segment(414, y + 108, 150, 34, NebulaText.T("Wave", "Волна"), style == AuraWallpaper.Style.Wave, "wp:style:1");
            c.Segment(572, y + 108, 150, 34, NebulaText.T("Aurora", "Сияние"), style == AuraWallpaper.Style.Aurora, "wp:style:2");

            var mode = (AuraMode)AppConfig.Get("aura_mode", (int)AuraMode.AuraStatic);
            string modeName = "";
            try { if (Aura.GetModes().TryGetValue(mode, out var mn)) modeName = mn; } catch { }
            c.Txt(NebulaText.T("Colour source", "Источник цвета") + ": " + NebulaText.T("keyboard effect", "эффект клавиатуры") + (modeName.Length > 0 ? " · " + modeName : ""), 256, y + 172, c.F(11), th.Muted);
            c.Txt(NebulaText.T("Cycling effects rotate the hue slowly, one step every 20 seconds.", "Циклические эффекты медленно вращают оттенок, шаг раз в 20 секунд."), 256, y + 192, c.F(10), th.Faint);
            c.Txt(NebulaText.T("Turning it off restores the wallpaper you had before.", "При выключении возвращаются прежние обои."), 256, y + 212, c.F(10), th.Faint);
            c.Button(256, y + 240, 220, 34, NebulaText.T("Change colour", "Сменить цвет"), "wp:color", primary: false);
            c.Button(488, y + 240, 220, 34, NebulaText.T("Restore previous", "Вернуть прежние"), "wp:restore", primary: false, enabled: (AppConfig.GetString("aura_wallpaper_prev") ?? "").Length > 0);

            // preview
            var col = Aura.Color1;
            var key = (col.ToArgb(), (int)style);
            if (preview is null || previewKey != key)
            {
                preview?.Dispose();
                preview = AuraWallpaper.Render(col, style, 384, 240);
                previewKey = key;
            }
            var pr = c.R(770, y + 96, 604, 250);
            using (var clip = NebulaCanvas.Rounded(pr, c.S(10)))
            {
                var saved = c.G.Clip;
                c.G.SetClip(clip, System.Drawing.Drawing2D.CombineMode.Intersect);
                c.G.DrawImage(preview, pr);
                c.G.Clip = saved;
            }
            using (var pen = new Pen(th.Line, 1f)) using (var clip = NebulaCanvas.Rounded(pr, c.S(10))) c.G.DrawPath(pen, clip);
            c.Txt(NebulaText.T("Preview", "Предпросмотр"), 770, y + 366, c.F(10), th.Faint);
        }

        public override bool Click(string id, Point at, NebulaForm form)
        {
            if (id.StartsWith("wp:style:")) { AuraWallpaper.SetStyle((AuraWallpaper.Style)int.Parse(id[9..])); return true; }
            switch (id)
            {
                case "wp:gallery": Open("https://rog.asus.com/wallpapers/"); return true;
                case "wp:products": Open("https://rog.asus.com/wallpapers/products/"); return true;
                case "wp:windows": Open("ms-settings:personalization-background"); return true;
                case "wp:file":
                {
                    using var dlg = new OpenFileDialog
                    {
                        Title = NebulaText.T("Choose a wallpaper", "Выберите обои"),
                        Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.webp|All files|*.*",
                        CheckFileExists = true,
                    };
                    if (dlg.ShowDialog(form) == DialogResult.OK) AuraWallpaper.SetCustom(dlg.FileName);
                    return true;
                }
                case "wp:aura": AuraWallpaper.SetEnabled(!AuraWallpaper.IsEnabled); return true;
                case "wp:restore": AuraWallpaper.SetEnabled(false); return true;
                case "wp:color":
                {
                    var dlg = new RColorPicker(Aura.Color1);
                    dlg.ColorChanged += col => { AppConfig.Set("aura_color", col.ToArgb()); Program.settingsForm.SetAura(); form.Invalidate(); };
                    dlg.ShowDialog(form);
                    return true;
                }
            }
            return false;
        }

        private static void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { Logger.WriteLine(ex.Message); }
        }
    }
}
