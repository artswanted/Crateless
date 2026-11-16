using GHelper.Helpers;
using GHelper.Input;
using GHelper.USB;
using System.Drawing.Drawing2D;

namespace GHelper.UI.SignalGlass.Pages
{
    /// <summary>
    /// 07 Lighting: Keyboard / Matrix / Slash targets, Aura effects, HSV + Hue + HEX color
    /// popup, brightness slider, AC/battery & sleep policies.
    /// </summary>
    public sealed class LightingPage : SignalPage
    {
        public override string Id => "lighting";
        public override string GroupId => "tuning";
        public override string Title => SignalText.T("Lighting", "Подсветка");
        public override bool CanApply => dirty;
        public override bool CanReset => true;

        private AuraMode draftMode;
        private AuraSpeed draftSpeed;
        private Color draftColor;
        private int draftBrightness;
        private bool draftSleep;
        private bool dirty;

        private bool showColorModal;
        private float modalHue, modalSat, modalVal;
        private Color modalPreview;
        private string modalHex = "";
        private float lastContentH = 660f;

        public override void OnEnter(SignalPaintContext? ctx)
        {
            if (!dirty) LoadFromBackend();
        }

        public override bool HandleEscape()
        {
            if (showColorModal) { showColorModal = false; return true; }
            return false;
        }

        private void LoadFromBackend()
        {
            draftMode = (AuraMode)AppConfig.Get("aura_mode", (int)AuraMode.AuraStatic);
            draftSpeed = (AuraSpeed)AppConfig.Get("aura_speed", (int)AuraSpeed.Normal);
            draftColor = Aura.Color1;
            int max = Math.Max(1, AppConfig.Get("max_brightness", 3));
            draftBrightness = Math.Clamp(InputDispatcher.GetBacklight(), 0, max);
            draftSleep = AppConfig.Is("keyboard_sleep");
            dirty = false;
        }

        public override void Reset()
        {
            LoadFromBackend();
        }

        public override void Apply()
        {
            AppConfig.Set("aura_mode", (int)draftMode);
            AppConfig.Set("aura_speed", (int)draftSpeed);
            AppConfig.Set("aura_color", draftColor.ToArgb());
            AppConfig.Set("keyboard_sleep", draftSleep ? 1 : 0);

            bool onBattery = SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Online;
            AppConfig.Set(onBattery ? "keyboard_brightness_ac" : "keyboard_brightness", draftBrightness);
            Aura.ApplyBrightness(draftBrightness, "SignalGlass");
            Program.settingsForm.SetAura();
            Task.Run(Aura.ApplyPower);
            dirty = false;
        }

        public override float ContentHeightDip(SignalPaintContext ctx) => lastContentH;

        public override void Paint(SignalPaintContext ctx)
        {
            var body = ctx.Layout.WorkspaceBody;
            float x = body.X, w = body.Width, y = body.Y - ctx.ScrollDip;
            float pad = 24f, rx = x + pad, rw = w - pad * 2f;

            var modes = SafeModes();
            var speeds = SafeSpeeds();
            string modeName = modes.TryGetValue(draftMode, out var mn) ? mn : "Static";
            string speedName = speeds.TryGetValue(draftSpeed, out var sn) ? sn : "Normal";
            int maxBr = Math.Max(1, AppConfig.Get("max_brightness", 3));

            bool hasSpeed = draftMode != AuraMode.AuraStatic && speeds.Count > 0;
            int kbRows = (ctx.Caps.IsDynamicLightingActive ? 1 : 0) + (hasSpeed ? 5 : 4);
            float kbPanelH = kbRows * 74f + 32f;
            ctx.DrawPanel(new RectangleF(x, y, w, kbPanelH));
            float ry = y + 16f;

            if (ctx.Caps.IsDynamicLightingActive)
            {
                ry = ctx.DrawSettingRow(ry, rx, rw,
                    "Windows Dynamic Lighting",
                    SignalText.T("Windows currently controls keyboard lighting", "Подсветкой сейчас управляет Windows Dynamic Lighting"),
                    SignalText.T("Open settings", "Открыть параметры"),
                    "lighting:dyn",
                    _ => DynamicLightingHelper.OpenSettings());
            }

            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Effect", "Эффект"),
                SignalText.T("Only effects reported by this device", "Только эффекты данного устройства"),
                modeName,
                "lighting:effect",
                pt => ctx.ShowMenu(pt, modes.Select(m => (m.Value, m.Key == draftMode, (string?)null, (Action)(() => { draftMode = m.Key; dirty = true; })))),
                enabled: modes.Count > 0,
                showChevron: modes.Count > 0);

            if (hasSpeed)
            {
                ry = ctx.DrawSettingRow(ry, rx, rw,
                    SignalText.T("Speed", "Скорость"),
                    SignalText.T("Effect animation cadence", "Скорость анимации эффекта"),
                    speedName,
                    "lighting:speed",
                    pt => ctx.ShowMenu(pt, speeds.Select(s => (s.Value, s.Key == draftSpeed, (string?)null, (Action)(() => { draftSpeed = s.Key; dirty = true; })))),
                    showChevron: true);
            }

            string hex = $"#{draftColor.R:X2}{draftColor.G:X2}{draftColor.B:X2}";
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Color", "Цвет"),
                SignalText.T("Accessible picker (HSV, Hue, HEX)", "Палитра (HSV, тон, HEX)"),
                hex,
                "lighting:color",
                _ => OpenColorModal());

            string brTxt = draftBrightness == 0 ? SignalText.T("Off", "Выкл") : $"{Math.Round(draftBrightness * 100.0 / maxBr)}%";
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Brightness", "Яркость"),
                $"{draftBrightness} / {maxBr}",
                brTxt,
                "lighting:bright",
                pt =>
                {
                    var items = Enumerable.Range(0, maxBr + 1).Select(i =>
                        (i == 0 ? SignalText.T("Off", "Выкл") : $"{i} / {maxBr} ({Math.Round(i * 100.0 / maxBr)}%)",
                         i == draftBrightness, (string?)null, (Action)(() => { draftBrightness = i; dirty = true; })));
                    ctx.ShowMenu(pt, items);
                },
                showChevron: true);

            ctx.DrawToggleRow(ry, rx, rw,
                SignalText.T("During sleep", "Во время сна"),
                SignalText.T("Keep keyboard lit when sleeping", "Оставлять подсветку во сне"),
                "lighting:sleep",
                draftSleep,
                () => { draftSleep = !draftSleep; dirty = true; },
                drawDivider: false);

            y += kbPanelH + 18f;

            // ---- Slash Lighting / AniMe Matrix section --------------------------------------------
            bool hasAux = ctx.Caps.SupportsMatrixOrSlash || LidLightingHelper.IsSupported;
            if (hasAux)
            {
                bool isSlash = ctx.Caps.IsSlash || LidLightingHelper.IsSlash;
                string secTitle = isSlash ? "Slash Lighting" : Properties.Strings.AnimeMatrix;
                var brLabels = LidLightingHelper.BrightnessLabels;
                int brIdx = LidLightingHelper.CurrentBrightnessIndex();
                var runModes = LidLightingHelper.ModeLabels(isSlash);
                int runIdx = Math.Clamp(AppConfig.Get("matrix_running", 0), 0, Math.Max(0, runModes.Count - 1));

                float auxH = 44f + 3 * 74f + 16f;
                ctx.DrawPanel(new RectangleF(x, y, w, auxH));
                var hdrFmt = ctx.Rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft;
                ctx.Painter.DrawText(ctx.G, secTitle, ctx.Px(rx, y + 18f, rw, 26f),
                    ctx.Painter.Font(16.5f, ctx.Scale, FontStyle.Bold), ctx.Theme.Text, hdrFmt);

                float ay = y + 50f;
                ay = ctx.DrawSettingRow(ay, rx, rw,
                    isSlash ? SignalText.T("Strip brightness", "Яркость полосы") : SignalText.T("Matrix brightness", "Яркость матрицы"),
                    SignalText.T("Configure auxiliary lid lighting", "Настройка дополнительной подсветки крышки"),
                    brLabels[brIdx],
                    "lighting:slash:bright",
                    pt => ctx.ShowMenu(pt, brLabels.Select((lbl, idx) => (lbl, idx == brIdx, (string?)null, (Action)(() => LidLightingHelper.SetBrightness(idx))))),
                    showChevron: true);

                string modeCur = runModes.Count > 0 ? runModes[Math.Clamp(runIdx, 0, runModes.Count - 1)] : "—";
                ay = ctx.DrawSettingRow(ay, rx, rw,
                    isSlash ? SignalText.T("Strip mode", "Режим полосы") : SignalText.T("Matrix mode", "Режим матрицы"),
                    secTitle,
                    modeCur,
                    "lighting:slash:mode",
                    pt => ctx.ShowMenu(pt, runModes.Select((lbl, idx) => (lbl, idx == runIdx, (string?)null, (Action)(() => LidLightingHelper.SetMode(idx))))),
                    enabled: runModes.Count > 0,
                    showChevron: runModes.Count > 0);

                ctx.DrawSettingRow(ay, rx, rw,
                    SignalText.T("Advanced panel", "Дополнительно"),
                    SignalText.T("Animation, audio & GIF settings", "Настройки анимации, аудио и параметров"),
                    isSlash ? "Slash" : "Matrix",
                    "lighting:matrix",
                    _ => LidLightingHelper.OpenDevicePanel(),
                    drawDivider: false);

                lastContentH = kbPanelH + 18f + auxH + 20f;
            }
            else
            {
                float unavH = 74f + 24f;
                ctx.DrawPanel(new RectangleF(x, y, w, unavH));
                ctx.DrawSettingRow(y + 12f, rx, rw,
                    "AniMe Matrix / Slash",
                    SignalText.T("Unavailable on this model", "Недоступно на этой модели"),
                    SignalText.T("Not available", "Недоступно"),
                    "lighting:matrix",
                    null,
                    enabled: false,
                    readOnly: true,
                    drawDivider: false);
                lastContentH = kbPanelH + 18f + unavH + 20f;
            }

            if (showColorModal)
                PaintColorModal(ctx);
        }

        private void OpenColorModal()
        {
            ColorToHsv(draftColor, out modalHue, out modalSat, out modalVal);
            modalPreview = draftColor;
            modalHex = $"#{draftColor.R:X2}{draftColor.G:X2}{draftColor.B:X2}";
            showColorModal = true;
        }

        private void PaintColorModal(SignalPaintContext ctx)
        {
            var th = ctx.Theme;
            float sc = ctx.Scale;

            using (var dim = new SolidBrush(Color.FromArgb(145, 0, 0, 0)))
                ctx.G.FillRectangle(dim, 0, 0, ctx.Layout.WindowW * sc, ctx.Layout.WindowH * sc);

            float mw = 480f, mh = 430f;
            var modal = new RectangleF((ctx.Layout.WindowW - mw) / 2f, (ctx.Layout.WindowH - mh) / 2f, mw, mh);
            ctx.DrawPanel(modal);

            float pad = 24f;
            ctx.Painter.DrawText(ctx.G, SignalText.T("Pick Aura color", "Выбор цвета Aura"),
                ctx.Px(modal.X + pad, modal.Y + 20f, mw - 48f, 28f),
                ctx.Painter.Font(19f, sc, FontStyle.Bold), th.Text, GlassPainter.FmtLeft);

            // HSV Saturation/Value square
            var svDip = new RectangleF(modal.X + pad, modal.Y + 60f, mw - pad * 2f, 180f);
            var svPx = ctx.Px(svDip);
            Color hueColor = HsvToColor(modalHue, 1f, 1f);
            using (var hGrad = new LinearGradientBrush(svPx, Color.White, hueColor, 0f))
                ctx.G.FillRectangle(hGrad, svPx);
            using (var vGrad = new LinearGradientBrush(svPx, Color.FromArgb(0, 0, 0, 0), Color.FromArgb(255, 0, 0, 0), 90f))
                ctx.G.FillRectangle(vGrad, svPx);
            using (var pen = new Pen(th.Line, Math.Max(1f, sc)))
                ctx.G.DrawRectangle(pen, svPx.X, svPx.Y, svPx.Width, svPx.Height);

            float cx = svPx.X + modalSat * svPx.Width;
            float cy = svPx.Y + (1f - modalVal) * svPx.Height;
            using (var cpen = new Pen(Color.White, 2f * sc))
                ctx.G.DrawEllipse(cpen, cx - 6f * sc, cy - 6f * sc, 12f * sc, 12f * sc);

            ctx.Register(new UiElement
            {
                Id = "lighting:hsv",
                BoundsPx = svPx,
                Name = "Saturation and brightness",
                Value = $"S {Math.Round(modalSat * 100)}%, V {Math.Round(modalVal * 100)}%",
                Role = AccessibleRole.Slider,
                IsSlider = true,
                SliderTrackPx = svPx,
                OnStep = d => { modalVal = Math.Clamp(modalVal + d * 0.05f, 0f, 1f); UpdateModalColor(); },
                OnActivate = _ => ctx.StartSliderDrag(("lighting:hsv", (tx, ty, _) =>
                {
                    modalSat = Math.Clamp(tx, 0f, 1f);
                    modalVal = Math.Clamp(1f - ty, 0f, 1f);
                    UpdateModalColor();
                }, svPx)),
            });

            // Hue slider
            var hueDip = new RectangleF(modal.X + pad, svDip.Bottom + 16f, mw - pad * 2f, 20f);
            var huePx = ctx.Px(hueDip);
            using (var hb = new LinearGradientBrush(huePx, Color.Red, Color.Red, 0f))
            {
                hb.InterpolationColors = new ColorBlend
                {
                    Colors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red },
                    Positions = new[] { 0f, 1 / 6f, 2 / 6f, 3 / 6f, 4 / 6f, 5 / 6f, 1f },
                };
                ctx.G.FillRectangle(hb, huePx);
            }
            float hx = huePx.X + (modalHue / 360f) * huePx.Width;
            using (var hpen = new Pen(Color.White, 2f * sc))
                ctx.G.DrawRectangle(hpen, hx - 3f * sc, huePx.Y - 2f * sc, 6f * sc, huePx.Height + 4f * sc);

            ctx.Register(new UiElement
            {
                Id = "lighting:hue",
                BoundsPx = huePx,
                Name = "Hue",
                Value = $"{Math.Round(modalHue)}°",
                Role = AccessibleRole.Slider,
                IsSlider = true,
                SliderTrackPx = huePx,
                OnStep = d => { modalHue = (modalHue + d * 10f + 360f) % 360f; UpdateModalColor(); },
                OnActivate = _ => ctx.StartSliderDrag(("lighting:hue", (tx, _, _) =>
                {
                    modalHue = Math.Clamp(tx, 0f, 1f) * 360f;
                    UpdateModalColor();
                }, huePx)),
            });

            // Preview + Presets + HEX
            float py = hueDip.Bottom + 20f;
            using (var swBrush = new SolidBrush(modalPreview))
                ctx.G.FillEllipse(swBrush, (modal.X + pad) * sc, py * sc, 36f * sc, 36f * sc);
            ctx.Painter.DrawText(ctx.G, "HEX " + modalHex, ctx.Px(modal.X + pad + 48f, py + 7f, 130f, 24f),
                ctx.Painter.Font(14f, sc, FontStyle.Bold), th.Text, GlassPainter.FmtLeft);

            // Quick Signal Glass & Aura preset swatches
            Color[] swatches = { th.Accent, th.Gpu, Color.White, Color.CornflowerBlue, Color.HotPink, Color.Gold };
            float swx = modal.Right - pad - swatches.Length * 32f;
            for (int i = 0; i < swatches.Length; i++)
            {
                var c = swatches[i];
                var r = new RectangleF(swx + i * 32f, py + 4f, 26f, 26f);
                var rPx = ctx.Px(r);
                using (var b = new SolidBrush(c)) ctx.G.FillEllipse(b, rPx);
                using (var p = new Pen(th.Line, sc)) ctx.G.DrawEllipse(p, rPx);
                ctx.Register(new UiElement
                {
                    Id = "lighting:sw:" + i,
                    BoundsPx = rPx,
                    Name = $"Preset #{c.R:X2}{c.G:X2}{c.B:X2}",
                    OnActivate = _ => { ColorToHsv(c, out modalHue, out modalSat, out modalVal); UpdateModalColor(); },
                });
            }

            // Footer Cancel / Apply
            ctx.DrawButton("lighting:color:cancel", new RectangleF(modal.Right - pad - 216f, modal.Bottom - 58f, 100f, 38f),
                SignalText.T("Cancel", "Отмена"), _ => showColorModal = false);
            ctx.DrawButton("lighting:color:apply", new RectangleF(modal.Right - pad - 104f, modal.Bottom - 58f, 104f, 38f),
                SignalText.T("Apply", "Применить"), _ => { draftColor = modalPreview; dirty = true; showColorModal = false; }, selected: true);
        }

        private void UpdateModalColor()
        {
            modalPreview = HsvToColor(modalHue, modalSat, modalVal);
            modalHex = $"#{modalPreview.R:X2}{modalPreview.G:X2}{modalPreview.B:X2}";
        }

        private static Dictionary<AuraMode, string> SafeModes()
        {
            try { return Aura.GetModes(); } catch { return new(); }
        }

        private static Dictionary<AuraSpeed, string> SafeSpeeds()
        {
            try { return Aura.GetSpeeds(); } catch { return new(); }
        }

        private static void ColorToHsv(Color c, out float h, out float s, out float v)
        {
            int max = Math.Max(c.R, Math.Max(c.G, c.B));
            int min = Math.Min(c.R, Math.Min(c.G, c.B));
            h = c.GetHue();
            s = max == 0 ? 0 : 1f - (1f * min / max);
            v = max / 255f;
        }

        private static Color HsvToColor(float h, float s, float v)
        {
            int hi = Convert.ToInt32(Math.Floor(h / 60)) % 6;
            double f = h / 60 - Math.Floor(h / 60);
            v *= 255;
            int v0 = Convert.ToInt32(v);
            int p = Convert.ToInt32(v * (1 - s));
            int q = Convert.ToInt32(v * (1 - f * s));
            int t = Convert.ToInt32(v * (1 - (1 - f) * s));
            return hi switch
            {
                0 => Color.FromArgb(255, v0, t, p),
                1 => Color.FromArgb(255, q, v0, p),
                2 => Color.FromArgb(255, p, v0, t),
                3 => Color.FromArgb(255, p, q, v0),
                4 => Color.FromArgb(255, t, p, v0),
                _ => Color.FromArgb(255, v0, p, q),
            };
        }
    }
}
