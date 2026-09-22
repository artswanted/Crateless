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

        /// <summary>Every language the app ships, as culture codes. "" means "follow Windows".</summary>
        private static readonly string[] Languages =
        {
            "", "en", "ar", "cs-CZ", "da", "de", "es", "fr", "hu", "id", "it", "ja", "ko", "lt", "pl",
            "pt-BR", "pt-PT", "ro", "ru", "tr", "uk", "vi", "zh-CN", "zh-TW",
        };

        /// <summary>The entry that matches Windows, or null when this system's language is not one of ours.</summary>
        private static string? SystemLanguage()
        {
            var ui = CultureInfo.InstalledUICulture;
            foreach (var l in Languages)
                if (l.Length > 0 && string.Equals(l, ui.Name, StringComparison.OrdinalIgnoreCase)) return l;
            foreach (var l in Languages)
                if (l.Length > 0 && string.Equals(l.Split('-')[0], ui.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase)) return l;
            return null;
        }

        /// <summary>Menu order: follow Windows, then this system's language, then English, then the rest by name.</summary>
        private static List<string> MenuOrder()
        {
            var sys = SystemLanguage();
            var head = new List<string> { "" };
            if (sys is not null && sys != "en") head.Add(sys);
            head.Add("en");
            head.AddRange(Languages.Where(l => l.Length > 0 && !head.Contains(l)).OrderBy(LanguageName, StringComparer.InvariantCulture));
            return head;
        }

        private static string FlagName(string code) => code.Length == 0 ? "system" : code switch
        {
            "cs-CZ" => "cs",
            _ => code,
        };

        private static Image? Flag(NebulaCanvas c, string code, float designWidth = 20)
            => NebulaAssets.Scaled("flag-" + FlagName(code), Math.Max(8, (int)c.S(designWidth)));

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
            if (code.Length == 0)
            {
                string sys = SystemLanguage() is string s ? LanguageName(s) : "English";
                return NebulaText.T("System", "Системный") + " · " + sys;
            }
            try
            {
                var name = CultureInfo.GetCultureInfo(code).NativeName;
                if (name.Length > 0) name = char.ToUpper(name[0], CultureInfo.InvariantCulture) + name[1..];
                // "português (Brasil)" keeps the country because there are two of them; "čeština (Česko)" does not need one
                int bracket = name.IndexOf(" (", StringComparison.Ordinal);
                string two = code.Split('-')[0];
                bool severalVariants = Languages.Count(l => l.Length > 0 && l.Split('-')[0] == two) > 1;
                if (bracket > 0 && !severalVariants) name = name[..bracket];
                return name;
            }
            catch { return code; }
        }

        public override void Paint(NebulaCanvas c)
        {
            var th = c.Theme;

            // ---- app -------------------------------------------------------------------------------
            c.Surface(236, 139, 818, 250);
            c.Txt(NebulaText.T("Application", "Приложение"), 256, 169, c.F(15, FontStyle.Bold), th.Text);
            c.ValueRow(256, 206, 778, NebulaText.T("Theme", "Тема"), ThemeName(), "settings:theme");
            string langCode = AppConfig.GetString("language") ?? "";
            string langName = LanguageName(langCode);
            var vf = c.F(12, FontStyle.Bold);
            float lw = c.TextWidth(langName + "  ⌄", vf);
            var flag = Flag(c, langCode);
            if (flag is not null) c.G.DrawImage(flag, c.S(256 + 778 - lw - 28), c.S(265 - 11), flag.Width, flag.Height);
            c.ValueRow(256, 265, 778, NebulaText.T("Language", "Язык"), langName, "settings:language");
            c.Txt(NebulaText.T("Start with Windows", "Запускать вместе с Windows"), 256, 341, c.F(13), th.Text);
            c.Toggle(996, 324, startup, "settings:startup");

            bool hasAsus = NebulaAssets.HasAsusRender();
            c.Txt(NebulaText.T("Use the ASUS render found on this PC", "Использовать рендер ASUS, найденный на этом ПК"), 256, 366 + 0, c.F(11), hasAsus ? th.Muted : th.Faint);
            c.Toggle(996, 349, hasAsus && AppConfig.Is("nebula_asus_render"), "settings:asusrender", hasAsus);

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

            c.Txt(NebulaText.T("Updates", "Обновления"), 256, 578, c.F(13), th.Text);
            c.Txt(NebulaText.T("Open", "Открыть"), 615, 579, c.F(12, FontStyle.Bold), c.IsHover("settings:updates") ? th.Text : th.Accent, StringAlignment.Far);
            c.Hit(c.R(500, 559, 135, 30), "settings:updates");
            c.Txt(NebulaText.T("Crateless, BIOS and drivers in one place.", "Crateless, BIOS и драйверы в одном месте."), 256, 600, c.F(10), th.Faint);

            // ---- classic extras --------------------------------------------------------------------
            c.Surface(236, 714, 818, 70);
            c.Txt(NebulaText.T("Additional options (classic panel)", "Дополнительные параметры (классическая панель)"), 256, 745, c.F(13), th.Text);
            c.Txt(NebulaText.T("Backlight timeouts, VRAM/APU memory, cores, ASPM, boot sound, clamshell, ACPI debug…", "Таймауты подсветки, память VRAM/APU, ядра, ASPM, звук при загрузке, крышка, ACPI-отладка…"), 256, 766, c.F(10), th.Faint);
            c.Button(873, 731, 161, 36, NebulaText.T("Open", "Открыть"), "settings:extra", primary: false);

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
            c.Txt(quit, 1054, 830, f, c.IsHover("settings:quit") ? th.Danger : th.Faint, StringAlignment.Far);
            c.Hit(c.R(1054 - c.TextWidth(quit, f) - 8, 812, c.TextWidth(quit, f) + 16, 26), "settings:quit");
        }

        public override float ContentHeight => 850;

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
                    Menu(form, at, MenuOrder().Select(l => (LanguageName(l), cur == l, NebulaAssets.Scaled("flag-" + FlagName(l), 20), (Action)(() =>
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
