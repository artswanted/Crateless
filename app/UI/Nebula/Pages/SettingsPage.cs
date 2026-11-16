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

        private static string ShellName() => UI.UiShell.Current switch
        {
            UI.UiShellMode.SignalGlass => "Signal Glass",
            UI.UiShellMode.Nebula => "Nebula",
            _ => NebulaText.T("Classic", "Классический"),
        };

        public override void Paint(NebulaCanvas c)
        {
            var th = c.Theme;

            // ---- app -------------------------------------------------------------------------------
            c.Surface(236, 139, 818, 310);
            c.Txt(NebulaText.T("Application", "Приложение"), 256, 169, c.F(15, FontStyle.Bold), th.Text);
            c.ValueRow(256, 206, 778, NebulaText.T("Interface", "Интерфейс"), ShellName(), "settings:shell");
            c.ValueRow(256, 262, 778, NebulaText.T("Theme", "Тема"), ThemeName(), "settings:theme");
            string langCode = NebulaLang.Current;
            string langName = NebulaLang.Name(langCode);
            var vf = c.F(12, FontStyle.Bold);
            float lw = c.TextWidth(langName + "  ⌄", vf);
            var flag = NebulaLang.Flag(langCode, (int)c.S(20));
            if (flag is not null) c.G.DrawImage(flag, c.S(256 + 778 - lw - 28), c.S(321 - 11), flag.Width, flag.Height);
            c.ValueRow(256, 321, 778, NebulaText.T("Language", "Язык"), langName, "settings:language");
            c.Txt(NebulaText.T("Start with Windows", "Запускать вместе с Windows"), 256, 397, c.F(13), th.Text);
            c.Toggle(996, 380, startup, "settings:startup");

            bool hasAsus = NebulaAssets.HasAsusRender();
            c.Txt(NebulaText.T("Use the ASUS render found on this PC", "Использовать рендер ASUS, найденный на этом ПК"), 256, 422, c.F(11), hasAsus ? th.Muted : th.Faint);
            c.Toggle(996, 405, hasAsus && AppConfig.Is("nebula_asus_render"), "settings:asusrender", hasAsus);

            // ---- system & diagnostics ------------------------------------------------------------------
            c.Surface(236, 465, 399, 309);
            c.Txt(NebulaText.T("System and diagnostics", "Система и диагностика"), 256, 495, c.F(15, FontStyle.Bold), th.Text);

            string svc = servicesBusy ? "…" : services < 0 ? "—" : services == 0 ? NebulaText.T("stopped", "остановлены") : services + " " + NebulaText.T("running", "запущено");
            c.Txt(NebulaText.T("ASUS services", "Службы ASUS") + " · " + svc, 256, 532, c.F(13), th.Text);
            string svcAction = services > 0 ? NebulaText.T("Stop", "Остановить") : NebulaText.T("Start", "Запустить");
            c.Txt(svcAction + (admin ? "" : "  🛡"), 615, 533, c.F(12, FontStyle.Bold), c.IsHover("settings:services") ? th.Text : th.Accent, StringAlignment.Far);
            if (!servicesBusy && services >= 0) c.Hit(c.R(500, 513, 135, 30), "settings:services", admin ? "" : NebulaText.T("Needs administrator rights", "Нужны права администратора"));

            c.Txt(NebulaText.T("Application log", "Журнал приложения"), 256, 593, c.F(13), th.Text);
            c.Txt(NebulaText.T("Open", "Открыть"), 615, 594, c.F(12, FontStyle.Bold), c.IsHover("settings:log") ? th.Text : th.Accent, StringAlignment.Far);
            c.Hit(c.R(500, 574, 135, 30), "settings:log");

            c.Txt(NebulaText.T("Updates", "Обновления"), 256, 654, c.F(13), th.Text);
            c.Txt(NebulaText.T("Open", "Открыть"), 615, 655, c.F(12, FontStyle.Bold), c.IsHover("settings:updates") ? th.Text : th.Accent, StringAlignment.Far);
            c.Hit(c.R(500, 635, 135, 30), "settings:updates");
            c.Txt(NebulaText.T("Crateless, BIOS and drivers in one place.", "Crateless, BIOS и драйверы в одном месте."), 256, 676, c.F(10), th.Faint);

            // ---- classic extras --------------------------------------------------------------------
            c.Surface(236, 790, 818, 70);
            c.Txt(NebulaText.T("Additional options (classic panel)", "Дополнительные параметры (классическая панель)"), 256, 821, c.F(13), th.Text);
            c.Txt(NebulaText.T("Backlight timeouts, VRAM/APU memory, cores, ASPM, boot sound, clamshell, ACPI debug…", "Таймауты подсветки, память VRAM/APU, ядра, ASPM, звук при загрузке, крышка, ACPI-отладка…"), 256, 842, c.F(10), th.Faint);
            c.Button(873, 807, 161, 36, NebulaText.T("Open", "Открыть"), "settings:extra", primary: false);

            // ---- about -------------------------------------------------------------------------------
            c.Surface(651, 465, 403, 309);
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            c.Txt("Crateless", 671, 495, c.F(15, FontStyle.Bold), th.Text);
            c.Txt($"{ver?.Major}.{ver?.Minor}.{ver?.Build}", 1034, 495, c.F(12), th.Faint, StringAlignment.Far);
            c.Txt(NebulaText.T("Free control.", "Свободный контроль."), 671, 541, c.F(23, FontStyle.Bold, true), th.Text);
            c.Txt(NebulaText.T("Free for everyone.", "Бесплатно для всех."), 671, 570, c.F(15), th.Muted);
            c.Txt(NebulaText.T("Based on G-Helper by seerge.", "Основано на G-Helper (seerge)."), 671, 611, c.F(12), th.Muted);
            c.Txt(NebulaText.T("Upstream authorship is preserved. GPL-3.0.", "Авторство upstream сохраняется. GPL-3.0."), 671, 634, c.F(12), th.Muted);
            c.Button(671, 666, 363, 36, NebulaText.T("Open repository", "Открыть репозиторий"), "settings:repo", primary: false);
            c.Txt(NebulaText.T("No account. No paid modes.", "Без аккаунта. Без платных режимов."), 671, 752, c.F(11), th.Faint);

            // ---- quit ---------------------------------------------------------------------------------
            var f = c.F(12, FontStyle.Bold);
            string quit = NebulaText.T("Quit Crateless", "Закрыть Crateless");
            c.Txt(quit, 1054, 906, f, c.IsHover("settings:quit") ? th.Danger : th.Faint, StringAlignment.Far);
            c.Hit(c.R(1054 - c.TextWidth(quit, f) - 8, 888, c.TextWidth(quit, f) + 16, 26), "settings:quit");
        }

        public override float ContentHeight => 926;

        public override bool Click(string id, Point at, NebulaForm form)
        {
            switch (id)
            {
                case "settings:shell":
                {
                    var cur = UI.UiShell.Current;
                    var items = new (string text, UI.UiShellMode mode)[]
                    {
                        ("Signal Glass", UI.UiShellMode.SignalGlass),
                        ("Nebula", UI.UiShellMode.Nebula),
                        (NebulaText.T("Classic", "Классический"), UI.UiShellMode.Classic),
                    };
                    Menu(form, at, items.Select(i => (i.text, cur == i.mode, (Action)(() => Program.SwitchUiShell(i.mode)))));
                    return true;
                }
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
                    Menu(form, at, NebulaLang.MenuItems(() => form.Invalidate()));
                    return true;
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
                case "settings:updates": form.ShowPage("updates"); return true;
                case "settings:repo":
                    try { Process.Start(new ProcessStartInfo("https://github.com/artswanted/Crateless") { UseShellExecute = true }); } catch { }
                    return true;
                case "settings:asusrender":
                    AppConfig.Set("nebula_asus_render", AppConfig.Is("nebula_asus_render") ? 0 : 1);
                    NebulaAssets.ResetHero();
                    return true;
                case "settings:extra": Program.settingsForm.ExtraWindow(); return true;
                case "settings:quit": Program.settingsForm.QuitApp(); return true;
            }
            return false;
        }
    }
}
