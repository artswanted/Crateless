using GHelper.UI.Nebula;
using GHelper.USB;
using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace GHelper.UI.SignalGlass.Pages
{
    /// <summary>
    /// 10 Wallpapers: live previews for built-in Signal Glass art, live Aura wallpaper,
    /// custom local image picker, and the official ROG online gallery grid with thumbnails,
    /// category filtering, download, and one-click apply.
    /// </summary>
    public sealed class WallpapersPage : SignalPage
    {
        public override string Id => "wallpapers";
        public override string GroupId => "devices";
        public override string Title => SignalText.T("Wallpapers", "Обои");
        public override bool HasFooter => false;

        private int selectedMode; // 0 = Signal Glass, 1 = Aura tint, 2 = Custom image
        private string customPath = "";
        private string statusNote = "";
        private string category = "";
        private List<string> categories = new();

        private Bitmap? auraPreview;
        private (int color, int style) auraPreviewKey = (-1, -1);
        private Bitmap? customPreview;
        private string customPreviewPath = "";

        private float lastContentH = 920f;
        private Action? requestRepaint;
        private Action<string, string, bool>? notifyToast;

        public override void OnEnter(SignalPaintContext? ctx)
        {
            requestRepaint = ctx?.RequestRefresh ?? requestRepaint;
            selectedMode = AuraWallpaper.IsEnabled ? 1 : (!string.IsNullOrEmpty(customPath) ? 2 : 0);
            RogWallpapers.Load(Repaint);
        }

        private void Repaint()
        {
            try { requestRepaint?.Invoke(); } catch { }
            try { Program.signalForm?.BeginInvoke(Program.signalForm.Invalidate); } catch { }
        }

        public override float ContentHeightDip(SignalPaintContext ctx) => lastContentH;

        public override void Paint(SignalPaintContext ctx)
        {
            requestRepaint = ctx.RequestRefresh;
            notifyToast = ctx.Toast;

            var body = ctx.Layout.WorkspaceBody;
            float viewTop = body.Y;
            float viewBottom = ctx.Layout.Workspace.Bottom;
            float x = body.X, w = body.Width, y = body.Y - ctx.ScrollDip;
            float pad = 20f;
            bool rtl = ctx.Rtl;
            var th = ctx.Theme;
            float sc = ctx.Scale;

            EnsureAuraPreview();
            EnsureCustomPreview();

            // ---- 1) Top 3 live wallpaper source cards (Signal Glass / Aura tint / Custom) ---------
            float cardGap = 16f;
            float topCardW = (w - cardGap * 2f) / 3f;
            float topThumbH = 118f;
            float topCardH = topThumbH + 66f;

            var sources = new[]
            {
                (idx: 0, title: "Signal Glass",
                    btnText: SignalText.T("Apply", "Применить"),
                    img: SignalAssets.Background(ctx.Dark, false),
                    fallbackCol: th.Plot,
                    onCard: (Action)ApplySignalGlass,
                    enabled: true),
                (idx: 1, title: SignalText.T("Aura tint", "Цвет Aura"),
                    btnText: AuraWallpaper.IsEnabled ? SignalText.T("On", "Вкл") : SignalText.T("Apply", "Применить"),
                    img: (Image?)auraPreview,
                    fallbackCol: ControlPaint.Dark(Aura.Color1, 0.3f),
                    onCard: (Action)(() => { AuraWallpaper.SetEnabled(true); selectedMode = 1; notifyToast?.Invoke(SignalText.T("Wallpaper applied", "Обои установлены"), SignalText.T("Aura tint", "Цвет Aura"), false); }),
                    enabled: true),
                (idx: 2, title: SignalText.T("Custom image", "Своя картинка"),
                    btnText: SignalText.T("Choose file…", "Выбрать файл…"),
                    img: (Image?)customPreview,
                    fallbackCol: th.Raised,
                    onCard: (Action)PickAndApplyCustomFile,
                    enabled: true),
            };

            for (int i = 0; i < sources.Length; i++)
            {
                var src = sources[i];
                int slot = rtl ? (sources.Length - 1 - i) : i;
                float cx = x + slot * (topCardW + cardGap);
                var cardRect = new RectangleF(cx, y, topCardW, topCardH);
                bool sel = selectedMode == src.idx || (src.idx == 1 && AuraWallpaper.IsEnabled);

                if (cardRect.Bottom >= viewTop - 20f && cardRect.Y <= viewBottom + 20f)
                {
                    ctx.DrawPanel(cardRect);
                    var thumbDip = new RectangleF(cx + 12f, y + 12f, topCardW - 24f, topThumbH);
                    var thumbPx = ctx.Px(thumbDip);

                    DrawThumbBox(ctx, thumbPx, src.img, src.fallbackCol, sel ? th.Accent : null);
                    if (src.img is null && src.idx == 2)
                    {
                        var ic = SignalAssets.IconScaled("wallpaper", ctx.Dark, (int)Math.Round(26f * sc), th.Muted);
                        if (ic is not null)
                            ctx.G.DrawImage(ic, thumbPx.X + (thumbPx.Width - ic.Width) / 2f, thumbPx.Y + (thumbPx.Height - ic.Height) / 2f, ic.Width, ic.Height);
                    }

                    var fmtText = rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft;
                    float btnW = Math.Clamp(topCardW * 0.42f, 92f, 134f);
                    float titleW = topCardW - 24f - btnW - 8f;
                    float titleX = rtl ? (cx + 12f + btnW + 8f) : (cx + 14f);
                    float btnX = rtl ? (cx + 12f) : (cx + topCardW - 12f - btnW);

                    ctx.Painter.DrawText(ctx.G, src.title, ctx.Px(titleX, y + topThumbH + 22f, titleW, 24f),
                        ctx.Painter.Font(14f, sc, FontStyle.Bold), sel ? th.Accent : th.Text, fmtText);

                    ctx.DrawButton("wallpapers:src:" + src.idx, new RectangleF(btnX, y + topThumbH + 17f, btnW, 34f),
                        src.btnText, _ => { selectedMode = src.idx; src.onCard(); }, selected: sel, fontSize: 12f);
                }
            }
            y += topCardH + 18f;

            // ---- 2) Aura & Windows tools strip ----------------------------------------------------
            float toolsH = 158f;
            var toolsRect = new RectangleF(x, y, w, toolsH);
            if (toolsRect.Bottom >= viewTop - 20f && toolsRect.Y <= viewBottom + 20f)
            {
                ctx.DrawPanel(toolsRect);
                float rx = x + pad, rw = w - pad * 2f;
                var fmt = rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft;

                // Row 1: Use Aura color toggle
                var style = AuraWallpaper.CurrentStyle;
                string row1Title = SignalText.T("Use Aura color", "Обои в цвете Aura");
                float row1Y = y + 14f;
                float tX = rtl ? rx : (rx + rw - 44f);
                ctx.Painter.DrawText(ctx.G, row1Title, ctx.Px(rtl ? rx + 54f : rx, row1Y + 4f, rw - 60f, 24f),
                    ctx.Painter.Font(13.5f, sc, FontStyle.Bold), th.Text, fmt);
                ctx.DrawToggle("wallpapers:aura", new RectangleF(tX, row1Y + 2f, 44f, 24f), row1Title,
                    AuraWallpaper.IsEnabled, () => { AuraWallpaper.SetEnabled(!AuraWallpaper.IsEnabled); selectedMode = AuraWallpaper.IsEnabled ? 1 : 0; });

                // Row 2: Aura styles + Change colour
                float row2Y = y + 54f;
                float segW = 88f, segGap = 6f, colW = 116f;
                var styles = Enum.GetValues<AuraWallpaper.Style>();
                float segsW = styles.Length * segW + (styles.Length - 1) * segGap;
                float styleLblW = Math.Max(80f, rw - segsW - colW - 18f);
                ctx.Painter.DrawText(ctx.G, SignalText.T("Aura style", "Стиль Aura"),
                    ctx.Px(rtl ? (rx + rw - styleLblW) : rx, row2Y + 6f, styleLblW, 22f),
                    ctx.Painter.Font(12.5f, sc), th.Muted, fmt);

                float colBtnX = rtl ? rx : (rx + rw - colW);
                float segStart = rtl ? (rx + colW + 8f) : (colBtnX - 8f - segsW);
                for (int i = 0; i < styles.Length; i++)
                {
                    var st = styles[i];
                    int sSlot = rtl ? (styles.Length - 1 - i) : i;
                    string sLabel = st switch
                    {
                        AuraWallpaper.Style.Gradient => SignalText.T("Gradient", "Градиент"),
                        AuraWallpaper.Style.Wave => SignalText.T("Wave", "Волна"),
                        _ => SignalText.T("Aurora", "Сияние"),
                    };
                    float sx = segStart + sSlot * (segW + segGap);
                    ctx.DrawButton("wallpapers:style:" + (int)st, new RectangleF(sx, row2Y, segW, 30f),
                        sLabel, _ => { AuraWallpaper.SetStyle(st); selectedMode = 1; }, selected: st == style, fontSize: 11.5f);
                }

                ctx.DrawButton("wallpapers:color", new RectangleF(colBtnX, row2Y, colW, 30f),
                    SignalText.T("Change colour", "Сменить цвет"), _ => PickAuraColor(), fontSize: 11.5f);

                using (var pen = new Pen(Color.FromArgb(75, th.Line), 1f))
                    ctx.G.DrawLine(pen, rx * sc, (y + 96f) * sc, (rx + rw) * sc, (y + 96f) * sc);

                // Row 3: Status + Restore previous + Windows background settings
                float row3Y = y + 109f;
                string note = statusNote.Length > 0 ? statusNote : SignalText.T("Static image updated only when Aura changes", "Статичное изображение, без фоновой нагрузки");
                ctx.Painter.DrawText(ctx.G, note, ctx.Px(rtl ? rx + 340f : rx, row3Y + 7f, Math.Max(140f, rw - 350f), 22f),
                    ctx.Painter.Font(12f, sc), th.Muted, fmt);

                bool canRestore = !string.IsNullOrEmpty(AppConfig.GetString("aura_wallpaper_prev"));
                float wBtnX = rtl ? rx : (rx + rw - 172f);
                float rBtnX = rtl ? (rx + 180f) : (wBtnX - 166f);
                ctx.DrawButton("wallpapers:restore", new RectangleF(rBtnX, row3Y, 156f, 32f),
                    SignalText.T("Restore previous", "Вернуть прежние"), _ => { AuraWallpaper.SetEnabled(false); selectedMode = 0; }, enabled: canRestore, fontSize: 12f);
                ctx.DrawButton("wallpapers:windows", new RectangleF(wBtnX, row3Y, 172f, 32f),
                    SignalText.T("Windows background", "Фон Windows") + " ↗", _ => OpenUrl("ms-settings:personalization-background"), fontSize: 12f);
            }
            y += toolsH + 18f;

            // ---- 3) Official ROG Wallpapers Gallery Header & Category Pills -----------------------
            List<RogWallpapers.Item> allItems;
            lock (RogWallpapers.Items) allItems = RogWallpapers.Items.ToList();

            float galHdrH = 84f;
            var galHdrRect = new RectangleF(x, y, w, galHdrH);
            if (galHdrRect.Bottom >= viewTop - 20f && galHdrRect.Y <= viewBottom + 20f)
            {
                ctx.DrawPanel(galHdrRect);
                float rx = x + pad, rw = w - pad * 2f;
                var fmt = rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft;

                ctx.Painter.DrawText(ctx.G, SignalText.T("Official ROG wallpapers", "Официальные обои ROG"),
                    ctx.Px(rtl ? rx + 240f : rx, y + 16f, Math.Max(160f, rw - 250f), 24f),
                    ctx.Painter.Font(15.5f, sc, FontStyle.Bold), th.Text, fmt);

                string galState = RogWallpapers.Loading
                    ? SignalText.T("Loading from asus.com…", "Загружаем галерею с asus.com…")
                    : RogWallpapers.Error.Length > 0
                        ? RogWallpapers.Error
                        : SignalText.Tf("{0} wallpapers · Pictures\\ROG Wallpapers", "Обоев: {0} · Изображения\\ROG Wallpapers", allItems.Count);
                ctx.Painter.DrawText(ctx.G, galState,
                    ctx.Px(rtl ? rx + 240f : rx, y + 44f, Math.Max(160f, rw - 250f), 20f),
                    ctx.Painter.Font(12f, sc), RogWallpapers.Error.Length > 0 ? th.Danger : th.Muted, fmt);

                float fldX = rtl ? rx : (rx + rw - 104f);
                float relX = rtl ? (rx + 114f) : (fldX - 118f);
                ctx.DrawButton("wallpapers:reload", new RectangleF(relX, y + 24f, 108f, 34f),
                    SignalText.T("Reload", "Обновить"), _ => RogWallpapers.Load(Repaint, force: true), enabled: !RogWallpapers.Loading, fontSize: 12f);
                ctx.DrawButton("wallpapers:folder", new RectangleF(fldX, y + 24f, 104f, 34f),
                    SignalText.T("Folder", "Папка"), _ => RogWallpapers.OpenFolder(), fontSize: 12f);
            }
            y += galHdrH + 14f;

            // Category pills
            categories = allItems.Select(i => i.Category).Where(s => s.Length > 0).Distinct().OrderBy(s => s).ToList();
            if (categories.Count > 1)
            {
                float px = rtl ? (x + w) : x;
                float py = y;
                const float pillH = 30f, pGap = 8f;
                var pillFont = ctx.Painter.Font(12f, sc, FontStyle.Bold);

                for (int i = -1; i < categories.Count; i++)
                {
                    string catName = i < 0 ? SignalText.T("All", "Все") : categories[i];
                    int cnt = i < 0 ? allItems.Count : allItems.Count(it => it.Category == categories[i]);
                    string label = $"{catName}  {cnt}";
                    float pw = Math.Max(64f, ctx.G.MeasureString(label, pillFont, 400, GlassPainter.FmtLeft).Width / sc + 26f);

                    if (!rtl && px + pw > x + w) { px = x; py += pillH + 8f; }
                    if (rtl && px - pw < x) { px = x + w; py += pillH + 8f; }

                    float drawX = rtl ? (px - pw) : px;
                    bool isSel = i < 0 ? category.Length == 0 : category == categories[i];
                    int catIdx = i;
                    if (py + pillH >= viewTop - 20f && py <= viewBottom + 20f)
                    {
                        ctx.DrawButton("wallpapers:cat:" + i, new RectangleF(drawX, py, pw, pillH), label,
                            _ => category = catIdx < 0 ? "" : categories[catIdx], selected: isSel, radiusDip: 15f, fontSize: 11.5f);
                    }
                    px += rtl ? -(pw + pGap) : (pw + pGap);
                }
                y = py + pillH + 14f;
            }

            // ---- 4) Official ROG Wallpapers Thumbnail Card Grid ------------------------------------
            var filtered = category.Length > 0 ? allItems.Where(i => i.Category == category).ToList() : allItems;

            if (filtered.Count == 0)
            {
                float emptyH = 92f;
                if (y + emptyH >= viewTop - 20f && y <= viewBottom + 20f)
                {
                    ctx.DrawPanel(new RectangleF(x, y, w, emptyH));
                    string msg = RogWallpapers.Loading
                        ? SignalText.T("Loading from asus.com…", "Загружаем галерею с asus.com…")
                        : SignalText.T("Nothing to display. Click Reload to try again.", "Пока пусто. Нажмите «Обновить», чтобы загрузить с asus.com.");
                    ctx.Painter.DrawText(ctx.G, msg, ctx.Px(x + pad, y + 34f, w - pad * 2f, 24f),
                        ctx.Painter.Font(13f, sc), th.Muted, rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft);
                }
                y += emptyH;
            }
            else
            {
                int cols = w >= 860f ? 3 : 2;
                const float gGap = 16f;
                float gCardW = (w - gGap * (cols - 1)) / cols;
                float gThumbH = MathF.Round(gCardW * 0.56f);
                float gCardH = gThumbH + 114f;

                for (int i = 0; i < filtered.Count; i++)
                {
                    var it = filtered[i];
                    int colIdx = i % cols;
                    if (rtl) colIdx = cols - 1 - colIdx;
                    float cx = x + colIdx * (gCardW + gGap);
                    float cy = y + (i / cols) * (gCardH + gGap);
                    var cardRect = new RectangleF(cx, cy, gCardW, gCardH);

                    bool visible = cardRect.Bottom >= viewTop - 32f && cardRect.Y <= viewBottom + 32f;
                    if (!visible) continue;

                    RogWallpapers.LoadThumb(it, Repaint);
                    ctx.DrawPanel(cardRect);

                    var thumbDip = new RectangleF(cx + 10f, cy + 10f, gCardW - 20f, gThumbH);
                    var thumbPx = ctx.Px(thumbDip);
                    DrawThumbBox(ctx, thumbPx, it.ThumbImage, th.Plot, null);

                    if (it.ThumbImage is null)
                    {
                        string ph = it.ThumbFailed ? SignalText.T("No preview", "Нет превью") : SignalText.T("Loading…", "Загрузка…");
                        ctx.Painter.DrawText(ctx.G, ph, thumbPx, ctx.Painter.Font(12f, sc), th.Muted, GlassPainter.FmtMiddleCenter);
                    }

                    // Category badge over thumbnail
                    if (it.Category.Length > 0)
                    {
                        var bf = ctx.Painter.Font(10.5f, sc, FontStyle.Bold);
                        float bw = ctx.G.MeasureString(it.Category, bf, 300, GlassPainter.FmtLeft).Width + 18f * sc;
                        float bh = 20f * sc;
                        float bx = rtl ? (thumbPx.Right - 8f * sc - bw) : (thumbPx.X + 8f * sc);
                        var pillPx = new RectangleF(bx, thumbPx.Y + 8f * sc, bw, bh);
                        using (var pb = new SolidBrush(Color.FromArgb(190, 14, 22, 26)))
                        using (var pp = GlassPainter.Rounded(pillPx, 10f * sc))
                            ctx.G.FillPath(pb, pp);
                        ctx.Painter.DrawText(ctx.G, it.Category, pillPx, bf, Color.FromArgb(243, 247, 248), GlassPainter.FmtMiddleCenter);
                    }

                    // Title & metadata
                    float ty = cy + gThumbH + 20f;
                    var tFmt = rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft;
                    ctx.Painter.DrawText(ctx.G, it.Name, ctx.Px(cx + 12f, ty, gCardW - 24f, 22f),
                        ctx.Painter.Font(13.5f, sc, FontStyle.Bold), th.Text, tFmt);

                    var best = RogWallpapers.Best(it);
                    string meta = best is null ? "" : $"{best.Width}×{best.Height}";
                    if (it.Desktop.Count > 1) meta += " · " + SignalText.Tf("{0} sizes", "размеров: {0}", it.Desktop.Count);
                    ctx.Painter.DrawText(ctx.G, meta, ctx.Px(cx + 12f, ty + 23f, gCardW - 24f, 18f),
                        ctx.Painter.Font(11f, sc), th.Muted, tFmt);

                    // Action bar
                    float by = cy + gCardH - 44f;
                    if (!string.IsNullOrEmpty(it.LocalFile)) it.LocalExists = File.Exists(it.LocalFile);
                    bool have = !string.IsNullOrEmpty(it.LocalFile) && it.LocalExists;

                    float applyW = 96f;
                    float applyX = rtl ? (cx + 12f) : (cx + gCardW - 12f - applyW);
                    float leftX = rtl ? (applyX + applyW + 8f) : (cx + 12f);
                    float leftW = Math.Max(80f, gCardW - 24f - applyW - 8f);

                    if (it.Downloading)
                    {
                        ctx.Painter.DrawText(ctx.G, SignalText.T("Downloading…", "Скачиваем…"),
                            ctx.Px(leftX, by + 7f, leftW, 20f), ctx.Painter.Font(12f, sc, FontStyle.Bold), th.Accent, tFmt);
                    }
                    else if (it.Error.Length > 0)
                    {
                        ctx.Painter.DrawText(ctx.G, it.Error,
                            ctx.Px(leftX, by + 7f, leftW, 20f), ctx.Painter.Font(11f, sc), th.Danger, tFmt);
                    }
                    else if (have)
                    {
                        ctx.DrawButton("wallpapers:show:" + it.Id, new RectangleF(leftX, by, leftW, 32f),
                            "✓ " + SignalText.T("Show", "Показать"),
                            _ => RevealFile(it.LocalFile), fontSize: 11.5f);
                    }
                    else
                    {
                        ctx.DrawButton("wallpapers:dl:" + it.Id, new RectangleF(leftX, by, leftW, 32f),
                            SignalText.T("Download", "Скачать"),
                            _ => RogWallpapers.Download(it, false, Repaint), fontSize: 11.5f);
                    }

                    ctx.DrawButton("wallpapers:set:" + it.Id, new RectangleF(applyX, by, applyW, 32f),
                        SignalText.T("Apply", "Поставить"),
                        _ => ApplyRogWallpaper(it), selected: true, enabled: !it.Downloading, fontSize: 12f);
                }

                int rowsCount = (filtered.Count + cols - 1) / cols;
                y += rowsCount * (gCardH + gGap) - gGap;
            }

            lastContentH = (y + ctx.ScrollDip - body.Y) + 20f;
        }

        private static void DrawThumbBox(SignalPaintContext ctx, RectangleF thumbPx, Image? img, Color fallbackBg, Color? borderColor)
        {
            float rad = 12f * ctx.Scale;
            using (var tp = GlassPainter.Rounded(thumbPx, rad))
            using (var tb = new SolidBrush(fallbackBg))
                ctx.G.FillPath(tb, tp);

            if (img is not null && img.Width > 0 && img.Height > 0)
            {
                var saved = ctx.G.Clip;
                using var tp = GlassPainter.Rounded(thumbPx, rad);
                ctx.G.SetClip(tp, CombineMode.Intersect);
                float k = Math.Max(thumbPx.Width / img.Width, thumbPx.Height / img.Height);
                float dw = img.Width * k, dh = img.Height * k;
                ctx.G.DrawImage(img, thumbPx.X + (thumbPx.Width - dw) / 2f, thumbPx.Y + (thumbPx.Height - dh) / 2f, dw, dh);
                ctx.G.Clip = saved;
            }

            if (borderColor is { } bc)
            {
                using var pen = new Pen(bc, Math.Max(2f, 2f * ctx.Scale));
                using var tp = GlassPainter.Rounded(thumbPx, rad);
                ctx.G.DrawPath(pen, tp);
            }
        }

        private void EnsureAuraPreview()
        {
            var col = Aura.Color1;
            var style = AuraWallpaper.CurrentStyle;
            var key = (col.ToArgb(), (int)style);
            if (auraPreview is not null && auraPreviewKey == key) return;
            auraPreview?.Dispose();
            auraPreview = AuraWallpaper.Render(col, style, 360, 210);
            auraPreviewKey = key;
        }

        private void EnsureCustomPreview()
        {
            if (string.IsNullOrEmpty(customPath) || !File.Exists(customPath))
            {
                if (customPreview is not null) { customPreview.Dispose(); customPreview = null; customPreviewPath = ""; }
                return;
            }
            if (customPreview is not null && string.Equals(customPreviewPath, customPath, StringComparison.OrdinalIgnoreCase))
                return;
            customPreview?.Dispose();
            customPreview = null;
            customPreviewPath = customPath;
            try
            {
                using var src = Image.FromFile(customPath);
                float k = Math.Min(1f, Math.Min(640f / src.Width, 360f / src.Height));
                int w = Math.Max(1, (int)(src.Width * k)), h = Math.Max(1, (int)(src.Height * k));
                var bmp = new Bitmap(w, h);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(src, 0, 0, w, h);
                }
                customPreview = bmp;
            }
            catch
            {
                statusNote = SignalText.T("Unsupported image type.", "Неподдерживаемый формат файла.");
            }
        }

        private void ApplySignalGlass()
        {
            try
            {
                string target = Path.Combine(Logger.appPath, "signal-glass-wallpaper.png");
                Directory.CreateDirectory(Logger.appPath);
                var src = SignalAssets.Background(true, true);
                if (src is not null)
                {
                    src.Save(target, System.Drawing.Imaging.ImageFormat.Png);
                    AuraWallpaper.SetCustom(target);
                    statusNote = SignalText.T("Signal Glass", "Signal Glass");
                    notifyToast?.Invoke(SignalText.T("Wallpaper applied", "Обои установлены"), "Signal Glass", false);
                }
            }
            catch (Exception ex)
            {
                statusNote = ex.Message;
            }
        }

        private void PickAndApplyCustomFile()
        {
            using var dlg = new OpenFileDialog
            {
                Title = SignalText.T("Choose a wallpaper", "Выберите обои"),
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.webp",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            var fi = new FileInfo(dlg.FileName);
            string ext = fi.Extension.ToLowerInvariant();
            if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp"))
            {
                statusNote = SignalText.T("Unsupported image type.", "Неподдерживаемый формат файла.");
                return;
            }
            if (fi.Length == 0 || fi.Length > 50L * 1024 * 1024)
            {
                statusNote = SignalText.T("Image size must be under 50 MB.", "Размер файла должен быть до 50 МБ.");
                return;
            }

            customPath = dlg.FileName;
            selectedMode = 2;
            statusNote = fi.Name;
            EnsureCustomPreview();
            AuraWallpaper.SetCustom(customPath);
            notifyToast?.Invoke(SignalText.T("Wallpaper applied", "Обои установлены"), fi.Name, false);
            Repaint();
        }

        private void ApplyRogWallpaper(RogWallpapers.Item it)
        {
            selectedMode = 2;
            if (!string.IsNullOrEmpty(it.LocalFile) && File.Exists(it.LocalFile))
            {
                customPath = it.LocalFile;
                statusNote = it.Name;
                AuraWallpaper.SetCustom(it.LocalFile);
                EnsureCustomPreview();
                notifyToast?.Invoke(SignalText.T("Wallpaper applied", "Обои установлены"), it.Name, false);
            }
            else
            {
                statusNote = it.Name;
                RogWallpapers.Download(it, true, () =>
                {
                    if (!string.IsNullOrEmpty(it.LocalFile) && File.Exists(it.LocalFile))
                    {
                        customPath = it.LocalFile;
                        EnsureCustomPreview();
                    }
                    Repaint();
                });
            }
        }

        private void PickAuraColor()
        {
            try
            {
                using var dlg = new RColorPicker(Aura.Color1);
                dlg.ColorChanged += col =>
                {
                    AppConfig.Set("aura_color", col.ToArgb());
                    Program.settingsForm?.SetAura();
                    Repaint();
                };
                dlg.ShowDialog(Program.signalForm);
            }
            catch { }
        }

        private static void RevealFile(string path)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
            catch (Exception ex) { Logger.WriteLine(ex.Message); }
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { Logger.WriteLine(ex.Message); }
        }
    }
}
