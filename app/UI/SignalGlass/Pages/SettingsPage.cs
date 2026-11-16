using GHelper.AutoUpdate;
using GHelper.Helpers;
using System.Diagnostics;

namespace GHelper.UI.SignalGlass.Pages
{
    /// <summary>
    /// 12 Settings: Interface switcher (Signal Glass / Nebula / Classic), language with autonyms
    /// (no flags), appearance, glass intensity, reduced motion, startup, polling interval, and system actions.
    /// </summary>
    public sealed class SettingsPage : SignalPage
    {
        public override string Id => "settings";
        public override string GroupId => "system";
        public override string Title => SignalText.T("Settings", "Настройки");
        public override bool HasFooter => false;

        private bool startup;
        private int services = -1;
        private bool admin;
        private bool servicesBusy;
        private float lastContentH = 820f;
        private Action? requestRepaint;

        public override void OnEnter(SignalPaintContext? ctx)
        {
            requestRepaint = ctx?.RequestRefresh;
            try { startup = Startup.IsScheduled(); } catch { startup = false; }
            admin = ProcessHelper.IsUserAdministrator();
            Task.Run(() =>
            {
                try { services = AsusService.GetRunningCount(); } catch { services = -1; }
                requestRepaint?.Invoke();
            });
        }

        public override float ContentHeightDip(SignalPaintContext ctx) => lastContentH;

        public override void Paint(SignalPaintContext ctx)
        {
            requestRepaint = ctx.RequestRefresh;
            var body = ctx.Layout.WorkspaceBody;
            float x = body.X, w = body.Width, y = body.Y - ctx.ScrollDip;
            float pad = 24f, rx = x + pad, rw = w - pad * 2f;

            const int rows = 10;
            float panelH = rows * 74f + 32f;
            ctx.DrawPanel(new RectangleF(x, y, w, panelH));
            float ry = y + 16f;

            // 1) Interface / UI shell switcher (Signal Glass / Nebula / Classic)
            string shellName = UiShell.Current switch
            {
                UiShellMode.SignalGlass => "Signal Glass",
                UiShellMode.Nebula => "Nebula",
                _ => SignalText.T("Classic (G-Helper)", "Классический (G-Helper)"),
            };
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Interface", "Интерфейс"),
                SignalText.T("Stored as the theme flag in config.json", "Сохраняется в config.json как флаг theme"),
                shellName,
                "settings:shell",
                pt => ctx.ShowMenu(pt, new (string, bool, string?, Action)[]
                {
                    ("Signal Glass", UiShell.Current == UiShellMode.SignalGlass, null, () => Program.SwitchUiShell(UiShellMode.SignalGlass)),
                    ("Nebula", UiShell.Current == UiShellMode.Nebula, null, () => Program.SwitchUiShell(UiShellMode.Nebula)),
                    (SignalText.T("Classic (G-Helper)", "Классический (G-Helper)"), UiShell.Current == UiShellMode.Classic, null, () => Program.SwitchUiShell(UiShellMode.Classic)),
                }),
                showChevron: true);

            // 2) Language (autonyms + rounded code badge, zero country flags)
            string curLang = SignalLang.Current;
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Language", "Язык"),
                SignalText.T("Repository locale inventory with autonyms", "Языки приложения с оригинальными названиями"),
                $"{SignalLang.Badge(curLang)} · {SignalLang.Autonym(curLang)}",
                "settings:language",
                pt => ctx.ShowMenu(pt, SignalLang.Entries().Select(e => ($"[{e.badge}]  {e.autonym}", e.isCurrent, (string?)null, (Action)(() => SignalLang.Apply(e.code))))),
                showChevron: true);

            // 3) Appearance (Dark / Light / System)
            string curUi = (AppConfig.GetString("ui_mode") ?? "").ToLowerInvariant();
            string appLabel = curUi switch
            {
                "dark" => SignalText.T("Dark", "Тёмная"),
                "light" => SignalText.T("Light", "Светлая"),
                _ => SignalText.T("System", "Системная"),
            };
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Appearance", "Оформление"),
                "Dark / Light / System",
                appLabel,
                "settings:appearance",
                pt => ctx.ShowMenu(pt, new (string, bool, string?, Action)[]
                {
                    (SignalText.T("System", "Системная"), curUi is "" or "windows", null, () => ApplyAppearance("")),
                    (SignalText.T("Dark", "Тёмная"), curUi == "dark", null, () => ApplyAppearance("dark")),
                    (SignalText.T("Light", "Светлая"), curUi == "light", null, () => ApplyAppearance("light")),
                }),
                showChevron: true);

            // 4) Glass intensity
            var glass = SignalTheme.GlassIntensity;
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Glass intensity", "Интенсивность стекла"),
                SignalText.T("Standard translucent panels or solid opaque fallback", "Стандартное стекло или непрозрачная подложка"),
                glass == GlassIntensityMode.Opaque ? SignalText.T("Opaque", "Непрозрачная") : SignalText.T("Standard", "Стандартная"),
                "settings:glass",
                pt => ctx.ShowMenu(pt, new (string, bool, string?, Action)[]
                {
                    (SignalText.T("Standard", "Стандартная"), glass == GlassIntensityMode.Standard, null, () => { SignalTheme.GlassIntensity = GlassIntensityMode.Standard; Program.signalForm?.OnThemeChanged(); }),
                    (SignalText.T("Opaque", "Непрозрачная"), glass == GlassIntensityMode.Opaque, null, () => { SignalTheme.GlassIntensity = GlassIntensityMode.Opaque; Program.signalForm?.OnThemeChanged(); }),
                }),
                showChevron: true);

            // 5) Reduced motion
            var motion = SignalTheme.ReducedMotion;
            string motionLbl = motion switch
            {
                ReducedMotionMode.On => SignalText.T("On", "Вкл"),
                ReducedMotionMode.Off => SignalText.T("Off", "Выкл"),
                _ => SignalText.T("Follow Windows", "Как в Windows"),
            };
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Reduced motion", "Уменьшение анимации"),
                SignalText.T("Disables UI transitions when enabled", "Отключает переходы и анимации интерфейса"),
                motionLbl,
                "settings:motion",
                pt => ctx.ShowMenu(pt, new (string, bool, string?, Action)[]
                {
                    (SignalText.T("Follow Windows", "Как в Windows"), motion == ReducedMotionMode.FollowWindows, null, () => SignalTheme.ReducedMotion = ReducedMotionMode.FollowWindows),
                    (SignalText.T("On", "Вкл"), motion == ReducedMotionMode.On, null, () => SignalTheme.ReducedMotion = ReducedMotionMode.On),
                    (SignalText.T("Off", "Выкл"), motion == ReducedMotionMode.Off, null, () => SignalTheme.ReducedMotion = ReducedMotionMode.Off),
                }),
                showChevron: true);

            // 6) Run at sign-in
            ry = ctx.DrawToggleRow(ry, rx, rw,
                SignalText.T("Run at sign-in", "Запуск при входе"),
                SignalText.T("Existing Windows Task Scheduler startup task", "Существующая задача планировщика Windows"),
                "settings:startup",
                startup,
                () => { if (startup) Startup.UnSchedule(); else Startup.Schedule(); startup = !startup; });

            // 7) Sensor interval
            int pollMs = Math.Clamp(AppConfig.Get("sensor_timer", 1000), 500, 5000);
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Sensor interval", "Интервал опроса"),
                SignalText.T("Background telemetry polling cadence", "Частота фонового чтения датчиков"),
                pollMs >= 1000 ? $"{pollMs / 1000.0:0.#} s" : $"{pollMs} ms",
                "settings:polling",
                pt => ctx.ShowMenu(pt, new[] { 500, 1000, 2000, 5000 }.Select(ms =>
                    (ms >= 1000 ? $"{ms / 1000.0:0.#} s" : $"{ms} ms", ms == pollMs, (string?)null, (Action)(() =>
                    {
                        AppConfig.Set("sensor_timer", ms);
                        Program.signalForm?.UpdatePollingInterval(ms);
                    })))),
                showChevron: true);

            // 8) ASUS services
            string svcTxt = servicesBusy ? "…" : services < 0 ? "—" : services == 0 ? SignalText.T("Stopped", "Остановлены") : $"{services} running";
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("ASUS services", "Службы ASUS"),
                SignalText.T("Background Armoury/MyASUS services status", "Состояние фоновых служб ASUS"),
                svcTxt + (admin ? "" : " · UAC"),
                "settings:services",
                _ => ToggleServices(),
                enabled: !servicesBusy && services >= 0);

            // 9) Diagnostics & About
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Application log & Extra", "Журнал и доп. параметры"),
                $"Crateless {AutoUpdateControl.AppVersion} · GPL-3.0 · based on G-Helper",
                SignalText.T("Open log", "Открыть лог"),
                "settings:log",
                _ => { try { Process.Start(new ProcessStartInfo(Logger.logFile) { UseShellExecute = true }); } catch { } });

            // 10) Quit
            ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Quit Crateless", "Закрыть Crateless"),
                SignalText.T("Exits background tray controller", "Полностью закрыть приложение из трея"),
                SignalText.T("Quit", "Выйти"),
                "settings:quit",
                _ => Program.settingsForm.QuitApp(),
                drawDivider: false);

            lastContentH = panelH + 16f;
        }

        private static void ApplyAppearance(string mode)
        {
            AppConfig.Set("ui_mode", mode);
            Program.settingsForm.InitTheme(true);
            Program.signalForm?.OnThemeChanged();
            Program.signalFlyout?.ApplyTheme();
        }

        private void ToggleServices()
        {
            if (!admin) { ProcessHelper.RunAsAdmin("services"); return; }
            servicesBusy = true;
            Task.Run(() =>
            {
                try
                {
                    if (AsusService.GetRunningCount() > 0) { AsusService.StopAsusServices(); Program.inputDispatcher?.Init(); }
                    else AsusService.StartAsusServices();
                    services = AsusService.GetRunningCount();
                }
                catch { }
                servicesBusy = false;
                requestRepaint?.Invoke();
            });
        }
    }
}
