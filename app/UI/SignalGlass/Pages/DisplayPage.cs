using GHelper.Display;
using GHelper.UI.Nebula;

namespace GHelper.UI.SignalGlass.Pages
{
    /// <summary>
    /// 04 Display: refresh rate, AC/DC auto switching, Overdrive, Mini-LED, GameVisual,
    /// color gamut, and the OLED care drawer.
    /// </summary>
    public sealed class DisplayPage : SignalPage
    {
        public override string Id => "display";
        public override string GroupId => "tuning";
        public override string Title => SignalText.T("Display", "Экран");
        public override bool HasFooter => false;

        private bool showOledModal;
        private bool taskbarAutoHide, transparency, darkTheme;
        private int elmb = -1;
        private float lastContentH = 640f;

        public override void OnEnter(SignalPaintContext? ctx)
        {
            try { elmb = ScreenELMB.Get(); } catch { elmb = -1; }
            if (AppConfig.IsOLED())
            {
                try { taskbarAutoHide = OledCare.IsTaskbarAutoHide(); } catch { }
                try { transparency = OledCare.IsTransparencyEnabled(); } catch { }
                try { darkTheme = OledCare.IsDarkTheme(); } catch { }
            }
        }

        public override bool HandleEscape()
        {
            if (showOledModal) { showOledModal = false; return true; }
            return false;
        }

        public override float ContentHeightDip(SignalPaintContext ctx) => lastContentH;

        public override void Paint(SignalPaintContext ctx)
        {
            var body = ctx.Layout.WorkspaceBody;
            float x = body.X, w = body.Width, y = body.Y - ctx.ScrollDip;
            float pad = 24f, rx = x + pad, rw = w - pad * 2f;

            int freq = AppConfig.Get("frequency", -1);
            int max = AppConfig.Get("max_frequency", 0);
            int min = ScreenControl.MIN_RATE;
            bool auto = AppConfig.Is("screen_auto");
            bool oled = AppConfig.IsOLED();

            int rows = 7 + (elmb >= 0 ? 1 : 0);
            float panelH = rows * 74f + 32f;
            ctx.DrawPanel(new RectangleF(x, y, w, panelH));
            float ry = y + 16f;

            string rateVal = freq > 0 ? $"{freq} Hz" : "—";
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Refresh rate", "Частота обновления"),
                SignalText.T("Available rates come from the current display", "Доступные частоты определяются экраном"),
                rateVal,
                "display:rate",
                pt => ShowRateMenu(ctx, pt, min, max, freq, auto),
                enabled: freq > 0,
                showChevron: freq > 0);

            ry = ctx.DrawToggleRow(ry, rx, rw,
                SignalText.T("On battery", "От батареи") + $" ({min} Hz)",
                SignalText.T("Restore previous AC rate when power returns", "Возвращает частоту при подключении к сети"),
                "display:auto",
                auto,
                () => { ScreenControl.SetAutoRefresh(auto ? 0 : 1); ScreenControl.AutoScreen(); });

            bool odSupported = ctx.Caps.SupportsOverdrive;
            bool odOn = odSupported && !AppConfig.IsNoOverdrive();
            ry = ctx.DrawToggleRow(ry, rx, rw,
                "Overdrive",
                odSupported ? SignalText.T("Panel response boost at high refresh", "Ускорение отклика матрицы") : SignalText.T("Not supported on this panel", "Не поддерживается матрицей"),
                "display:overdrive",
                odOn,
                () => { AppConfig.Set("no_overdrive", AppConfig.IsNoOverdrive() ? 0 : 1); ScreenControl.AutoScreen(true); },
                enabled: odSupported);

            int miniled = AppConfig.Get("miniled", -1);
            bool miniledSup = miniled >= 0;
            ry = ctx.DrawSettingRow(ry, rx, rw,
                "Mini-LED",
                miniledSup ? SignalText.T("Local dimming zones", "Зоны локального затемнения") : SignalText.T("Unavailable on this model", "Недоступно на этой модели"),
                miniledSup ? (miniled == 1 ? SignalText.T("Multizone", "Мультизона") : SignalText.T("One zone", "Одна зона")) : SignalText.T("Not supported", "Не поддерживается"),
                "display:miniled",
                _ => ScreenControl.ToogleMiniled(),
                enabled: miniledSup,
                readOnly: !miniledSup);

            if (elmb >= 0)
            {
                ry = ctx.DrawToggleRow(ry, rx, rw,
                    "ELMB",
                    SignalText.T("Motion blur reduction", "Снижение смазывания в движении"),
                    "display:elmb",
                    elmb == 1,
                    () => { int v = elmb == 1 ? 0 : 1; AppConfig.Set("elmb", v); ScreenELMB.Set(v); elmb = v; });
            }

            Dictionary<SplendidCommand, string> gvModes;
            try { gvModes = VisualControl.GetVisualModes(); } catch { gvModes = new(); }
            var curGv = (SplendidCommand)AppConfig.Get("visual", (int)VisualControl.GetDefaultVisualMode());
            string gvName = gvModes.TryGetValue(curGv, out var gn) ? gn : (gvModes.Count > 0 ? "Default" : SignalText.T("Not supported", "Не поддерживается"));

            ry = ctx.DrawSettingRow(ry, rx, rw,
                "GameVisual",
                SignalText.T("Panel colour presets (Fn+V)", "Цветовые пресеты панели (Fn+V)"),
                gvName,
                "display:gv",
                pt =>
                {
                    int ctemp = AppConfig.Get("color_temp", VisualControl.DefaultColorTemp);
                    ctx.ShowMenu(pt, gvModes.Select(m => (m.Value, m.Key == curGv, (string?)null, (Action)(() =>
                    {
                        VisualControl.SetRegStatus(1);
                        VisualControl.SetVisual(m.Key, ctemp);
                        Program.settingsForm.InitVisual();
                    }))));
                },
                enabled: gvModes.Count > 0,
                readOnly: gvModes.Count == 0,
                showChevron: gvModes.Count > 0);

            Dictionary<SplendidGamut, string> gamuts;
            try { gamuts = VisualControl.GetGamutModes(); } catch { gamuts = new(); }
            var curGamut = (SplendidGamut)AppConfig.Get("gamut", (int)VisualControl.GetDefaultGamut());
            string gamutName = gamuts.TryGetValue(curGamut, out var gmn) ? gmn.Replace("Gamut:", "").Trim() : (gamuts.Count > 0 ? "Native" : SignalText.T("Not supported", "Не поддерживается"));

            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Color gamut", "Цветовой охват"),
                SignalText.T("Supported presets only", "Только поддерживаемые профили"),
                gamutName,
                "display:gamut",
                pt => ctx.ShowMenu(pt, gamuts.Select(g => (g.Value.Replace("Gamut:", "").Trim(), g.Key == curGamut, (string?)null, (Action)(() => VisualControl.SetGamut((int)g.Key))))),
                enabled: gamuts.Count > 0,
                readOnly: gamuts.Count == 0,
                showChevron: gamuts.Count > 0);

            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("OLED care", "Защита OLED"),
                oled ? SignalText.T("Pixel shift · dimming · focus · taskbar", "Сдвиг пикселей · затемнение · фокус · панель задач") : SignalText.T("Available on OLED panels", "Доступно для OLED-панелей"),
                oled ? SignalText.T("Open settings", "Открыть параметры") : SignalText.T("Not supported", "Не поддерживается"),
                "display:oled",
                _ => showOledModal = true,
                enabled: oled,
                readOnly: !oled,
                drawDivider: false);

            lastContentH = panelH + 16f;

            if (showOledModal && oled)
                PaintOledModal(ctx);
        }

        private void PaintOledModal(SignalPaintContext ctx)
        {
            var th = ctx.Theme;
            float sc = ctx.Scale;

            using (var dim = new SolidBrush(Color.FromArgb(145, 0, 0, 0)))
                ctx.G.FillRectangle(dim, 0, 0, ctx.Layout.WindowW * sc, ctx.Layout.WindowH * sc);

            float mw = Math.Min(640f, ctx.Layout.WindowW - 48f);
            float mh = 490f;
            var modal = new RectangleF((ctx.Layout.WindowW - mw) / 2f, (ctx.Layout.WindowH - mh) / 2f, mw, mh);
            ctx.DrawPanel(modal);

            float pad = 24f;
            ctx.Painter.DrawText(ctx.G, SignalText.T("OLED care", "Защита OLED"),
                ctx.Px(modal.X + pad, modal.Y + 20f, mw - 140f, 28f),
                ctx.Painter.Font(19f, sc, FontStyle.Bold), th.Text, GlassPainter.FmtLeft);
            ctx.DrawButton("display:oled:close", new RectangleF(modal.Right - pad - 88f, modal.Y + 16f, 88f, 34f),
                SignalText.T("Close", "Закрыть"), _ => showOledModal = false);

            float ry = modal.Y + 66f;
            float rx = modal.X + pad, rw = mw - pad * 2f;

            ry = ctx.DrawToggleRow(ry, rx, rw,
                SignalText.T("Auto-hide taskbar", "Автоскрытие панели задач"),
                SignalText.T("Reduces static elements along the bottom edge", "Снижает статичную нагрузку у нижнего края"),
                "display:oled:taskbar", taskbarAutoHide, () => { taskbarAutoHide = !taskbarAutoHide; OledCare.SetTaskbarAutoHide(taskbarAutoHide); });

            ry = ctx.DrawToggleRow(ry, rx, rw,
                SignalText.T("Taskbar transparency", "Прозрачность панели задач"),
                SignalText.T("Windows transparency switch", "Переключатель прозрачности Windows"),
                "display:oled:transp", transparency, () => { transparency = !transparency; OledCare.SetTransparency(transparency); });

            ry = ctx.DrawToggleRow(ry, rx, rw,
                SignalText.T("Focus mode", "Целевой режим"),
                SignalText.T("Dims everything except the active window", "Затемняет всё кроме активного окна"),
                "display:oled:focus", OledCare.IsFocusDim, () => OledCare.SetFocusDim(!OledCare.IsFocusDim));

            ry = ctx.DrawToggleRow(ry, rx, rw,
                SignalText.T("Dim when idle", "Затемнение при простое"),
                SignalText.Tf("After {0} min", "Через {0} мин", OledCare.IdleMinutes),
                "display:oled:idle", OledCare.IsIdleDim, () => OledCare.SetIdleDim(!OledCare.IsIdleDim));

            ry = ctx.DrawToggleRow(ry, rx, rw,
                SignalText.T("Pixel shift", "Сдвиг пикселей"),
                OledCare.PixelShiftUnsupported ? SignalText.T("Not supported", "Не поддерживается") : SignalText.Tf("Every {0} s", "Каждые {0} с", OledCare.ShiftSeconds),
                "display:oled:shift", OledCare.IsPixelShift, () => OledCare.SetPixelShift(!OledCare.IsPixelShift),
                enabled: !OledCare.PixelShiftUnsupported, drawDivider: false);
        }

        private static void ShowRateMenu(SignalPaintContext ctx, Point pt, int min, int max, int cur, bool auto)
        {
            var items = new List<(string, bool, string?, Action)>
            {
                (SignalText.T("Auto", "Авто"), auto, null, () => { ScreenControl.SetAutoRefresh(1); ScreenControl.AutoScreen(); }),
                ($"{min} Hz", !auto && cur == min, null, () => { ScreenControl.SetAutoRefresh(0); ScreenControl.SetScreen(min, 0); }),
            };
            if (max > min)
                items.Add(($"{max} Hz", !auto && cur > min, null, () => { ScreenControl.SetAutoRefresh(0); ScreenControl.SetScreen(ScreenControl.MAX_REFRESH, 1); }));
            ctx.ShowMenu(pt, items);
        }
    }
}
