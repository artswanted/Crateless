using GHelper.Input;

namespace GHelper.UI.Nebula.Pages
{
    /// <summary>Keys page (design kit screen "keyboard"): hotkey actions, Fn lock, extras bridge.</summary>
    public sealed class KeysPage : NebulaPage
    {
        public override string Id => "keyboard";
        public override string Title => NebulaText.T("Keys", "Клавиши");
        public override string Subtitle => NebulaText.T("Familiar actions in one press.", "Привычные действия — одним нажатием.");

        private sealed record KeyDef(string Name, string Label, string DefaultAction);

        private readonly List<KeyDef> keys = new();

        public override void Refresh(bool opened)
        {
            if (!opened) return;
            keys.Clear();

            // Mirrors Extra.cs visibility rules per model.
            string m1 = "M1", m2 = "M2", m3 = "M3", m4 = "M4", fnf4 = "Fn+F4";
            bool showM1 = true, showM2 = true, showM3 = true, showM4 = true, showFnF4 = true, showFnC = true, showFnV = !AppConfig.IsNoFNV(), showFnE = AppConfig.IsTUF();
            string m3Name = "m3", fnf4Name = "fnf4";

            if (AppConfig.IsARCNM()) { m3 = "FN+F6"; showM1 = showM2 = showM4 = showFnF4 = false; }
            if (AppConfig.NoMKeys()) { m1 = "FN+F2"; m2 = "FN+F3"; m3 = "FN+F4"; showM4 = AppConfig.IsM4Button(); showFnF4 = false; }
            if (AppConfig.IsVivoZenPro()) { showM1 = showM2 = showM3 = showFnF4 = false; m4 = "FN+F12"; }
            if (AppConfig.MediaKeys()) showFnF4 = false;
            if (AppConfig.IsAlly())
            {
                showM1 = showM2 = false; showFnC = showFnV = false;
                m3 = "Cmd Center"; m3Name = "cc"; m4 = "ROG"; fnf4 = "Back Paddles"; fnf4Name = "paddle"; showFnF4 = true;
            }

            if (showM1) keys.Add(new("m1", m1, "volume_down"));
            if (showM2) keys.Add(new("m2", m2, "volume_up"));
            if (showM3) keys.Add(new(m3Name, m3, m3Name == "cc" ? "" : "micmute"));
            if (showM4) keys.Add(new("m4", m4, "ghelper"));
            if (showFnF4) keys.Add(new(fnf4Name, fnf4, fnf4Name == "paddle" ? "" : "aura"));
            if (showFnC) keys.Add(new("fnc", "Fn+C", "fnlock"));
            if (showFnV) keys.Add(new("fnv", "Fn+V", "visual"));
            if (showFnE) keys.Add(new("fne", "Fn+Numpad", "calculator"));
        }

        private static Dictionary<string, string> Actions()
        {
            var a = new Dictionary<string, string>
            {
                { "volume_down", Properties.Strings.VolumeDown },
                { "volume_up", Properties.Strings.VolumeUp },
                { "backlight_down", Properties.Strings.BacklightDown },
                { "backlight_up", Properties.Strings.BacklightUp },
                { "mute", Properties.Strings.VolumeMute },
                { "screenshot", Properties.Strings.PrintScreen },
                { "play", Properties.Strings.PlayPause },
                { "aura", Properties.Strings.ToggleAura },
                { "performance", Properties.Strings.PerformanceMode },
                { "screen", Properties.Strings.ToggleScreen },
                { "lock", Properties.Strings.LockScreen },
                { "miniled", Properties.Strings.ToggleMiniled },
                { "fnlock", Properties.Strings.ToggleFnLock },
                { "brightness_down", Properties.Strings.BrightnessDown },
                { "brightness_up", Properties.Strings.BrightnessUp },
                { "visual", Properties.Strings.VisualMode },
                { "touchscreen", Properties.Strings.ToggleTouchscreen },
                { "micmute", Properties.Strings.MuteMic },
                { "ghelper", Properties.Strings.OpenGHelper },
                { "overlay", Properties.Strings.Overlay },
                { "custom", Properties.Strings.Custom },
            };
            if (AppConfig.IsDUO()) { a["screenpad_down"] = Properties.Strings.ScreenPadDown; a["screenpad_up"] = Properties.Strings.ScreenPadUp; }
            if (AppConfig.IsAlly()) a["controller"] = "Controller Mode";
            return a;
        }

        private static string ActionName(string code, KeyDef k)
        {
            if (code.Length == 0) code = k.DefaultAction;
            if (code == "calculator") return NebulaText.T("Calculator", "Калькулятор");
            if (code.Length == 0) return NebulaText.T("Nothing", "Ничего");
            return Actions().TryGetValue(code, out var n) ? n : code;
        }

        public override void Paint(NebulaCanvas c)
        {
            var th = c.Theme;
            float cardH = Math.Max(160, 110 + keys.Count * 73);
            c.Surface(236, 139, 818, cardH);
            c.Txt(NebulaText.T("Hotkeys", "Быстрые клавиши"), 256, 169, c.F(15, FontStyle.Bold), th.Text);
            c.Txt(NebulaText.T("Labels and available keys depend on the laptop model.", "Подписи и набор клавиш определяет модель ноутбука."), 256, 190, c.F(11), th.Muted);

            float y = 218;
            foreach (var k in keys)
            {
                string code = AppConfig.GetString(k.Name) ?? "";
                bool isDefault = code.Length == 0;
                c.Card(c.R(256, y, 94, 43), 7, th.Raised);
                c.Txt(k.Label, 303, y + 27, c.F(12, FontStyle.Bold), th.Text, StringAlignment.Center);
                c.Txt(ActionName(code, k), 374, y + 16, c.F(14, FontStyle.Bold), th.Text);
                string sub = code == "custom"
                    ? (AppConfig.GetString(k.Name + "_custom") ?? "") is { Length: > 0 } p ? p : NebulaText.T("Custom: no program chosen", "Своя команда: программа не выбрана")
                    : isDefault ? NebulaText.T("Default action", "Действие по умолчанию") : NebulaText.T("Custom assignment", "Своё назначение");
                c.Txt(sub, 374, y + 39, c.F(10), th.Muted);
                c.Button(873, y + 2, 161, 36, NebulaText.T("Change", "Изменить"), "keys:set:" + k.Name, primary: false);
                y += 73;
            }
            if (keys.Count == 0)
                c.Txt(NebulaText.T("No programmable keys detected on this model.", "На этой модели программируемых клавиш не найдено."), 256, 240, c.F(12), th.Muted);

            var link = c.F(11, FontStyle.Bold);
            string reset = NebulaText.T("Reset all bindings", "Сбросить все привязки");
            c.Txt(reset, 1034, 139 + cardH - 18, link, c.IsHover("keys:reset") ? th.Text : th.Accent, StringAlignment.Far);
            c.Hit(c.R(1034 - c.TextWidth(reset, link) - 8, 139 + cardH - 36, c.TextWidth(reset, link) + 16, 26), "keys:reset");

            // ---- behaviour ---------------------------------------------------------------------------
            float by = 139 + cardH + 16;
            c.Surface(236, by, 818, 148);
            c.Txt(NebulaText.T("Keyboard behaviour", "Поведение клавиатуры"), 256, by + 30, c.F(15, FontStyle.Bold), th.Text);

            c.Txt("Fn Lock", 256, by + 70, c.F(13), th.Text);
            c.Txt(NebulaText.T("F1–F12 act as function keys without Fn.", "F1–F12 работают без Fn."), 256, by + 90, c.F(10), th.Faint);
            c.Toggle(569, by + 53, AppConfig.Is("fn_lock"), "keys:fnlock");

            if (AppConfig.IsDUO())
            {
                c.Txt(NebulaText.T("Arrow lock", "Блокировка стрелок"), 670, by + 70, c.F(13), th.Text);
                c.Toggle(996, by + 53, AppConfig.Is("arrow_lock"), "keys:arrowlock");
            }
            else
            {
                c.Txt(NebulaText.T("More keyboard options", "Другие параметры клавиатуры"), 670, by + 70, c.F(13), th.Text);
                c.Txt(NebulaText.T("Backlight timeouts, touchpad, Numpad, boot sound…", "Таймауты подсветки, тачпад, Numpad, звук при загрузке…"), 670, by + 90, c.F(10), th.Faint);
                c.Txt(NebulaText.T("Open  ↗", "Открыть  ↗"), 1034, by + 70, c.F(12, FontStyle.Bold), c.IsHover("keys:extra") ? th.Text : th.Accent, StringAlignment.Far);
                c.Hit(c.R(960, by + 50, 80, 30), "keys:extra");
            }

            c.Txt(NebulaText.T("Conflicts with ASUS services are explained next to the unavailable setting.", "Конфликт со службой ASUS объясняем рядом с недоступной настройкой."), 256, by + 130, c.F(11), th.Faint);
        }

        public override float ContentHeight => 139 + Math.Max(160, 110 + keys.Count * 73) + 16 + 148 + 20;

        public override bool Click(string id, Point at, NebulaForm form)
        {
            if (id.StartsWith("keys:set:"))
            {
                string name = id[9..];
                var k = keys.FirstOrDefault(x => x.Name == name);
                if (k is null) return false;
                string cur = AppConfig.GetString(name) ?? "";
                var actions = Actions();
                var items = new List<(string, bool, Action)>
                {
                    (NebulaText.T("Default", "По умолчанию") + " · " + ActionName("", k), cur.Length == 0, () => SetAction(name, "")),
                };
                foreach (var a in actions)
                {
                    if (a.Key == k.DefaultAction) continue;
                    string code = a.Key;
                    items.Add((a.Value, cur == code, () =>
                    {
                        if (code == "custom") PickCustom(form, name);
                        SetAction(name, code);
                    }));
                }
                Menu(form, at, items);
                return true;
            }

            switch (id)
            {
                case "keys:reset":
                    foreach (var k in keys) { AppConfig.Set(k.Name, ""); AppConfig.Set(k.Name + "_custom", ""); }
                    MKeyControl.Reset();
                    Program.inputDispatcher?.RegisterKeys();
                    return true;
                case "keys:fnlock": InputDispatcher.ToggleFnLock(); return true;
                case "keys:arrowlock": InputDispatcher.ToggleArrowLock(); return true;
                case "keys:extra": form.ShowPage("extra"); return true;
            }
            return false;
        }

        private static void SetAction(string name, string code)
        {
            AppConfig.Set(name, code);
            if (name is "m1" or "m2" or "m3" or "m4" or "m5")
            {
                MKeyControl.ApplyAll();
                Program.inputDispatcher?.RegisterKeys();
            }
        }

        private static void PickCustom(NebulaForm form, string name)
        {
            using var dlg = new OpenFileDialog
            {
                Title = NebulaText.T("Choose a program or shortcut", "Выбери программу или ярлык"),
                Filter = "Programs|*.exe;*.lnk;*.bat;*.cmd|All files|*.*",
                CheckFileExists = true,
            };
            if (dlg.ShowDialog(form) == DialogResult.OK) AppConfig.Set(name + "_custom", dlg.FileName);
        }
    }
}
