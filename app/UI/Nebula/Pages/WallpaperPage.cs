using GHelper.USB;
using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace GHelper.UI.Nebula.Pages
{
    /// <summary>Wallpaper page: the official ROG gallery loaded in-app with previews and download buttons, a file picker and the Aura wallpaper.</summary>
    public sealed class WallpaperPage : NebulaPage
    {
        public override string Id => "wallpaper";
        public override string Title => NebulaText.T("Wallpaper", "Обои");
        public override string Subtitle => NebulaText.T("Official ROG wallpapers straight from asus.com, your own image, or a still wallpaper in your Aura colour.", "Официальные обои ROG прямо с asus.com, своя картинка или обои в цвете подсветки.");

        private Bitmap? preview;
        private (int color, int style) previewKey = (-1, -1);
        private float bottom = 900;
        private string category = "";
        private List<string> categories = new();

        private const int Cols = 3;
        private const float CardW = 370, CardH = 330, Gap = 24, ThumbH = 222;

        public override float ContentHeight => bottom + 20;

        public override void Refresh(bool opened)
        {
            if (opened) RogWallpapers.Load(Repaint);
        }

        private static void Repaint()
        {
            try { Program.nebulaForm?.BeginInvoke(Program.nebulaForm.Invalidate); } catch { }
        }

        public override void Paint(NebulaCanvas c)
        {
            var th = c.Theme;
            float y = 139;

            // ---- header / tools ----------------------------------------------------------------------
            c.Surface(236, y, 1158, 96);
            c.Txt(NebulaText.T("Official ROG wallpapers", "Официальные обои ROG"), 256, y + 30, c.F(15, FontStyle.Bold), th.Text);
            List<RogWallpapers.Item> items;
            lock (RogWallpapers.Items) items = RogWallpapers.Items.ToList();
            string state = RogWallpapers.Loading ? NebulaText.T("Loading the gallery from asus.com…", "Загружаем галерею с asus.com…")
                         : RogWallpapers.Error.Length > 0 ? NebulaText.T("Could not reach the ASUS site: ", "Не удалось связаться с сайтом ASUS: ") + RogWallpapers.Error
                         : NebulaText.T($"{items.Count} wallpapers. Files are saved to Pictures\\ROG Wallpapers.", $"Обоев: {items.Count}. Файлы сохраняются в Изображения\\ROG Wallpapers.");
            c.Txt(state, 256, y + 52, c.F(11), RogWallpapers.Error.Length > 0 ? th.Danger : th.Muted);
            c.Txt(NebulaText.T("Nothing is bundled with the app; previews and files come from the ASUS CDN when this page is open.", "В приложение ничего не встроено, превью и файлы приходят с CDN ASUS при открытии страницы."), 256, y + 72, c.F(10), th.Faint);
            c.Button(900, y + 30, 110, 32, NebulaText.T("Reload", "Обновить"), "wp:reload", primary: false, enabled: !RogWallpapers.Loading);
            c.Button(1022, y + 30, 110, 32, NebulaText.T("Folder", "Папка"), "wp:folder", primary: false);
            c.Button(1144, y + 30, 230, 32, NebulaText.T("Choose a file…", "Выбрать файл…"), "wp:file", primary: false);
            y += 96 + 16;

            // ---- category filter (wrapping row of segments) -------------------------------------------
            categories = items.Select(i => i.Category).Where(s => s.Length > 0).Distinct().OrderBy(s => s).ToList();
            if (categories.Count > 1)
            {
                var sf = c.F(11, FontStyle.Bold);
                float x = 236, rowH = 32, gapX = 8;
                for (int i = -1; i < categories.Count; i++)
                {
                    string name = i < 0 ? NebulaText.T("All", "Все") : categories[i];
                    int count = i < 0 ? items.Count : items.Count(it => it.Category == categories[i]);
                    string label = $"{name}  {count}";
                    float w = c.TextWidth(label, sf) + 28;
                    if (x + w > 1394) { x = 236; y += rowH + 8; }
                    c.Segment(x, y, w, rowH, label, i < 0 ? category.Length == 0 : category == categories[i], "wp:cat:" + i);
                    x += w + gapX;
                }
                y += rowH + 16;
            }
            if (category.Length > 0) items = items.Where(i => i.Category == category).ToList();

            // ---- gallery grid ----------------------------------------------------------------------
            if (items.Count == 0)
            {
                c.Surface(236, y, 1158, 120);
                c.Txt(RogWallpapers.Loading ? NebulaText.T("Loading…", "Загружаем…") : NebulaText.T("Nothing to show. Press Reload to try again.", "Показать нечего. Нажмите «Обновить», чтобы повторить."), 256, y + 66, c.F(12), th.Muted);
                y += 120;
            }
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                float cx = 236 + (i % Cols) * (CardW + Gap), cy = y + (i / Cols) * (CardH + Gap);
                var card = c.R(cx, cy, CardW, CardH);
                // only fetch thumbnails for cards that are actually on screen
                if (c.G.VisibleClipBounds.IntersectsWith(card)) RogWallpapers.LoadThumb(it, Repaint);
                c.Card(card, 12, th.Card, th.Line);

                // thumbnail, cover-fit, clipped to the rounded top of the card
                var tr = c.R(cx, cy, CardW, ThumbH);
                using (var clip = NebulaCanvas.Rounded(card, c.S(12)))
                {
                    var saved = c.G.Clip;
                    c.G.SetClip(clip, CombineMode.Intersect);
                    c.G.SetClip(tr, CombineMode.Intersect);
                    if (it.ThumbImage is Image img)
                    {
                        float k = Math.Max(tr.Width / img.Width, tr.Height / img.Height);
                        float w = img.Width * k, h = img.Height * k;
                        c.G.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        c.G.DrawImage(img, new RectangleF(tr.X + (tr.Width - w) / 2, tr.Y + (tr.Height - h) / 2, w, h));
                    }
                    else
                    {
                        using var b = new SolidBrush(th.Raised);
                        c.G.FillRectangle(b, tr);
                        string ph = it.ThumbFailed ? NebulaText.T("No preview", "Нет превью") : NebulaText.T("Loading…", "Загружаем…");
                        c.Txt(ph, cx + CardW / 2, cy + ThumbH / 2 + 5, c.F(11), th.Faint, StringAlignment.Center);
                    }
                    c.G.Clip = saved;
                }
                if (it.Category.Length > 0) c.Pill(cx + 12, cy + 12, it.Category, Color.FromArgb(180, th.Bg), th.Text);

                c.Txt(Trim(it.Name, 36), cx + 16, cy + ThumbH + 26, c.F(13, FontStyle.Bold), th.Text);
                var best = RogWallpapers.Best(it);
                string meta = best is null ? "" : $"{best.Width}×{best.Height}";
                if (it.Desktop.Count > 1) meta += NebulaText.T($" · {it.Desktop.Count} sizes", $" · размеров: {it.Desktop.Count}");
                if (it.Phone.Count > 0) meta += NebulaText.T(" · phone", " · телефон");
                c.Txt(meta, cx + 16, cy + ThumbH + 46, c.F(10), th.Faint);
                c.Txt(NebulaText.T($"{it.Downloads:N0} downloads", $"{it.Downloads:N0} загрузок"), cx + CardW - 16, cy + ThumbH + 46, c.F(10), th.Faint, StringAlignment.Far);

                float by = cy + ThumbH + 60;
                bool have = it.LocalFile.Length > 0 && File.Exists(it.LocalFile);
                if (it.Downloading)
                    c.Txt(NebulaText.T("Downloading…", "Скачиваем…"), cx + 16, by + 21, c.F(11), th.Accent);
                else if (it.Error.Length > 0)
                    c.Txt(Trim(it.Error, 40), cx + 16, by + 21, c.F(10), th.Danger);
                else if (have)
                {
                    c.Txt("✓ " + NebulaText.T("Saved", "Скачано"), cx + 16, by + 21, c.F(11, FontStyle.Bold), th.Accent);
                    c.Button(cx + 118, by, 112, 32, NebulaText.T("Show", "Показать"), "wp:show:" + it.Id, primary: false);
                }
                else
                    c.Button(cx + 16, by, 214, 32, NebulaText.T("Download", "Скачать"), "wp:dl:" + it.Id, primary: false);
                c.Button(cx + 242, by, 112, 32, NebulaText.T("Apply", "Поставить"), "wp:set:" + it.Id, primary: true, enabled: !it.Downloading);
            }
            if (items.Count > 0) y += ((items.Count + Cols - 1) / Cols) * (CardH + Gap) - Gap;
            y += 16;

            // ---- Aura wallpaper --------------------------------------------------------------------
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
            c.Txt(NebulaText.T("Turning it off restores the wallpaper you had before. The original ASUS Aura packs only exist inside Armoury Crate and cannot be downloaded separately.",
                               "При выключении возвращаются прежние обои. Оригинальные Aura-паки ASUS существуют только внутри Armoury Crate и отдельно не скачиваются."), 256, y + 212, c.F(10), th.Faint);
            c.Button(256, y + 240, 220, 34, NebulaText.T("Change colour", "Сменить цвет"), "wp:color", primary: false);
            c.Button(488, y + 240, 220, 34, NebulaText.T("Restore previous", "Вернуть прежние"), "wp:restore", primary: false, enabled: (AppConfig.GetString("aura_wallpaper_prev") ?? "").Length > 0);
            c.Button(256, y + 286, 452, 34, NebulaText.T("Windows background settings  ↗", "Параметры фона Windows  ↗"), "wp:windows", primary: false);

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
                c.G.SetClip(clip, CombineMode.Intersect);
                c.G.DrawImage(preview, pr);
                c.G.Clip = saved;
            }
            using (var pen = new Pen(th.Line, 1f)) using (var clip = NebulaCanvas.Rounded(pr, c.S(10))) c.G.DrawPath(pen, clip);
            c.Txt(NebulaText.T("Preview", "Предпросмотр"), 770, y + 366, c.F(10), th.Faint);
            bottom = y + 372;
        }

        private static string Trim(string s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..(max - 1)] + "…";

        private static RogWallpapers.Item? Find(string id)
        {
            int n = int.Parse(id[(id.LastIndexOf(':') + 1)..]);
            lock (RogWallpapers.Items) return RogWallpapers.Items.FirstOrDefault(i => i.Id == n);
        }

        public override bool Click(string id, Point at, NebulaForm form)
        {
            if (id.StartsWith("wp:style:")) { AuraWallpaper.SetStyle((AuraWallpaper.Style)int.Parse(id[9..])); return true; }
            if (id.StartsWith("wp:cat:")) { int n = int.Parse(id[7..]); category = n < 0 || n >= categories.Count ? "" : categories[n]; form.ScrollTop(); return true; }
            if (id.StartsWith("wp:dl:")) { if (Find(id) is { } it) RogWallpapers.Download(it, false, Repaint); return true; }
            if (id.StartsWith("wp:set:"))
            {
                if (Find(id) is { } it)
                {
                    if (it.LocalFile.Length > 0 && File.Exists(it.LocalFile)) AuraWallpaper.SetCustom(it.LocalFile);
                    else RogWallpapers.Download(it, true, Repaint);
                }
                return true;
            }
            if (id.StartsWith("wp:show:"))
            {
                if (Find(id) is { LocalFile.Length: > 0 } it)
                    try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{it.LocalFile}\"") { UseShellExecute = true }); } catch (Exception ex) { Logger.WriteLine(ex.Message); }
                return true;
            }
            switch (id)
            {
                case "wp:reload": RogWallpapers.Load(Repaint, force: true); return true;
                case "wp:folder": RogWallpapers.OpenFolder(); return true;
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
