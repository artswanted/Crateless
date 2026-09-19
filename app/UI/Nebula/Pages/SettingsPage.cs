using GHelper.Helpers;
using System.Diagnostics;
using System.Globalization;

namespace GHelper.UI.Nebula.Pages
{
    /// <summary>Settings page (design kit screen "settings"): app, system & diagnostics, about.</summary>
    public sealed class SettingsPage : NebulaPage
    {
        public override string Id => "settings";
        public override string Title => NebulaText.T("Settings", "Настройки");
        public override string Subtitle => NebulaText.T("Crateless works the way you like.", "Crateless работает так, как удобно тебе.");

        private static readonly string[] Languages =
        {
            "", "en", "ar", "cs-CZ", "da", "de", "es", "fr", "hu", "id", "it", "ja", "ko", "lt", "pl",
            "pt-BR", "pt-PT", "ro", "tr", "uk", "vi", "zh-CN", "zh-TW",
        };

        private bool startup;
        private int services = -1;
        private bool admin;
        private bool servicesBusy;

        public override void Refresh(bool opened)
        {
            if (!opened) return;
            try { startup = Startup.IsScheduled(); } catch { startup = false; }
            admin = ProcessHelper.IsUserAdministrator();
            Task.Run(() => { try { services = AsusService.GetRunningCount(); } catch { services = -1; } });
        }

        private static string ThemeName()
        {
            string mode = AppConfig.GetString("ui_mode")?.ToLower() ?? "";
            return mode switch
            {
                "dark" => NebulaText.T("Dark", "Тёмная"),
                "light" => NebulaText.T("Light", "Светлая"),
                _ => NebulaText.T("System", "Системная"),
            };
        }

        private static string LanguageName(string code)
        {
            if (code.Length == 0) return NebulaText.T("System", "Системный");
            try { return CultureInfo.GetCultureInfo(code).NativeName; } catch { return code; }
        }

        public override void Paint(NebulaCanvas c)
        {
            var th = c.Theme;

            // ---- app -------------------------------------------------------------------------------
            c.Surface(236, 139, 818, 234);
            c.Txt(NebulaText.T("Application", "Приложение"), 256, 169, c.F(15, FontStyle.Bold), th.Text);
            c.ValueRow(256, 206, 778, NebulaText.T("Theme", "Тема"), ThemeName(), "settings:theme");
            c.ValueRow(256, 265, 778, NebulaText.T("Language", "Язык"), LanguageName(AppConfig.GetString("language") ?? ""), "settings:language");
            c.Txt(NebulaText.T("Start with Windows", "Запускать вместе с Windows"), 256, 341, c.F(13), th.Text);
            c.Toggle(996, 324, startup, "settings:startup");

            // ---- system & diagnostics ------------------------------------------------------------------
            c.Surface(236, 389, 399, 309);
            c.Txt(NebulaText.T("System and diagnostics", "Система и диагностика"), 256, 419, c.F(15, FontStyle.Bold), th.Text);

            string svc = servicesBusy ? "…" : services < 0 ? "—" : services == 0 ? NebulaText.T("stopped", "остановлены") : services + " " + NebulaText.T("running", "запущено");
            c.Txt(NebulaText.T("ASUS services", "Службы ASUS") + " · " + svc, 256, 456, c.F(13), th.Text);
            string svcAction = services > 0 ? NebulaText.T("Stop", "Остановить") : NebulaText.T("Start", "Запустить");
            c.Txt(svcAction + (admin ? "" : "  🛡"), 615, 457, c.F(12, FontStyle.Bold), c.IsHover("settings:services") ? th.Text : th.Accent, StringAlignment.Far);
            if (!servicesBusy && services >= 0) c.Hit(c.R(500, 437, 135, 30), "settings:services", admin ? "" : NebulaText.T("Needs administrator rights", "Нужны права администратора"));

            c.Txt(NebulaText.T("Application log", "Журнал приложения"), 256, 517, c.F(13), th.Text);
            c.Txt(NebulaText.T("Open", "Открыть"), 615, 518, c.F(12, FontStyle.Bold), c.IsHover("settings:log") ? th.Text : th.Accent, StringAlignment.Far);
            c.Hit(c.R(500, 498, 135, 30), "settings:log");

            c.Txt(NebulaText.T("BIOS and driver updates", "Обновления BIOS и драйверов"), 256, 578, c.F(13), th.Text);
            c.Txt(NebulaText.T("View", "Посмотреть"), 615, 579, c.F(12, FontStyle.Bold), c.IsHover("settings:updates") ? th.Text : th.Accent, StringAlignment.Far);
            c.Hit(c.R(500, 559, 135, 30), "settings:updates");

            c.Button(256, 639, 359, 36, NebulaText.T("Check for app updates", "Проверить обновления приложения"), "settings:appupdate", primary: false);

            // ---- about -------------------------------------------------------------------------------
            c.Surface(651, 389, 403, 309);
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            c.Txt("Crateless", 671, 419, c.F(15, FontStyle.Bold), th.Text);
            c.Txt($"{ver?.Major}.{ver?.Minor}.{ver?.Build}", 1034, 419, c.F(12), th.Faint, StringAlignment.Far);
            c.Txt(NebulaText.T("Free control.", "Свободный контроль."), 671, 465, c.F(23, FontStyle.Bold, true), th.Text);
            c.Txt(NebulaText.T("Free for everyone.", "Бесплатно для всех."), 671, 494, c.F(15), th.Muted);
            c.Txt(NebulaText.T("Based on G-Helper by seerge.", "Основано на G-Helper (seerge)."), 671, 535, c.F(12), th.Muted);
            c.Txt(NebulaText.T("Upstream authorship is preserved. GPL-3.0.", "Авторство upstream сохраняется. GPL-3.0."), 671, 558, c.F(12), th.Muted);
            c.Button(671, 590, 363, 36, NebulaText.T("Open repository", "Открыть репозиторий"), "settings:repo", primary: false);
            c.Txt(NebulaText.T("No account. No paid modes.", "Без аккаунта. Без платных режимов."), 671, 676, c.F(11), th.Faint);

            // ---- quit ---------------------------------------------------------------------------------
            var f = c.F(12, FontStyle.Bold);
            string quit = NebulaText.T("Quit Crateless", "Закрыть Crateless");
            c.Txt(quit, 1054, 740, f, c.IsHover("settings:quit") ? th.Danger : th.Faint, StringAlignment.Far);
            c.Hit(c.R(1054 - c.TextWidth(quit, f) - 8, 722, c.TextWidth(quit, f) + 16, 26), "settings:quit");
        }

        public override bool Click(string id, Point at, NebulaForm form)
        {
            switch (id)
            {
                case "settings:theme":
                {
                    string cur = AppConfig.GetString("ui_mode")?.ToLower() ?? "";
                    var items = new (string text, string value)[]
                    {
                        (NebulaText.T("System", "Системная"), ""),
                        (NebulaText.T("Dark", "Тёмная"), "dark"),
                        (NebulaText.T("Light", "Светлая"), "light"),
                    };
                    Menu(form, at, items.Select(i => (i.text, cur == i.value, (Action)(() =>
                    {
                        AppConfig.Set("ui_mode", i.value);
                        Program.settingsForm.InitTheme(true);
                        form.ApplyTheme();
                    }))));
                    return true;
                }
                case "settings:language":
                {
                    string cur = AppConfig.GetString("language") ?? "";
                    Menu(form, at, Languages.Select(l => (LanguageName(l), cur == l, (Action)(() =>
                    {
                        AppConfig.Set("language", l);
                        MessageBox.Show(form, NebulaText.T("The language changes after Crateless restarts.", "Язык сменится после перезапуска Crateless."), "Crateless");
                    }))));
                    return true;
                }
                case "settings:startup":
                    if (startup) Startup.UnSchedule(); else Startup.Schedule();
                    startup = !startup;
                    return true;
                case "settings:services":
                    if (!admin) { ProcessHelper.RunAsAdmin("services"); return true; }
                    servicesBusy = true;
                    Task.Run(() =>
                    {
                        try
                        {
                            if (AsusService.GetRunningCount() > 0) { AsusService.StopAsusServices(); Program.inputDispatcher?.Init(); }
                            else AsusService.StartAsusServices();
                            services = AsusService.GetRunningCount();
                        }
                        catch (Exception ex) { Logger.WriteLine("Services: " + ex.Message); }
                        servicesBusy = false;
                        try { form.BeginInvoke(form.Invalidate); } catch { }
                    });
                    return true;
                case "settings:log":
                    try { Process.Start(new ProcessStartInfo(Logger.logFile) { UseShellExecute = true }); } catch { }
                    return true;
                case "settings:updates": Program.settingsForm.UpdatesToggle(); return true;
                case "settings:appupdate": Program.settingsForm.CheckAppUpdate(); return true;
                case "settings:repo":
                    try { Process.Start(new ProcessStartInfo("https://github.com/artswanted/Crateless") { UseShellExecute = true }); } catch { }
                    return true;
                case "settings:quit": Program.settingsForm.QuitApp(); return true;
            }
            return false;
        }
    }
}
