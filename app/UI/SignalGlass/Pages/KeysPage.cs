using GHelper.Input;

namespace GHelper.UI.SignalGlass.Pages
{
    /// <summary>
    /// 08 Keys & shortcuts: M1–M4 / Fn bindings, conflict detection, Fn lock,
    /// and classic Extra window link.
    /// </summary>
    public sealed class KeysPage : SignalPage
    {
        public override string Id => "keys";
        public override string GroupId => "tuning";
        public override string Title => SignalText.T("Keys & shortcuts", "Клавиши и сочетания");
        public override bool CanApply => false;
        public override bool CanReset => true;

        private sealed record KeyDef(string Name, string Label, string DefaultAction);
        private readonly List<KeyDef> keys = new();
        private float lastContentH = 600f;

        public override void OnEnter(SignalPaintContext? ctx)
        {
            keys.Clear();
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

        public override void Reset()
        {
            foreach (var k in keys) { AppConfig.Set(k.Name, ""); AppConfig.Set(k.Name + "_custom", ""); }
            MKeyControl.Reset();
            Program.inputDispatcher?.RegisterKeys();
        }

        public override float ContentHeightDip(SignalPaintContext ctx) => lastContentH;

        public override void Paint(SignalPaintContext ctx)
        {
            var body = ctx.Layout.WorkspaceBody;
            float x = body.X, w = body.Width, y = body.Y - ctx.ScrollDip;
            float pad = 24f, rx = x + pad, rw = w - pad * 2f;

            var conflicts = FindConflicts();
            int extraRows = 2 + (AppConfig.IsDUO() ? 1 : 1);
            float panelH = Math.Max(1, keys.Count + extraRows) * 74f + 32f;
            ctx.DrawPanel(new RectangleF(x, y, w, panelH));
            float ry = y + 16f;

            foreach (var k in keys)
            {
                var kd = k;
                string code = AppConfig.GetString(kd.Name) ?? "";
                string resolved = code.Length == 0 ? kd.DefaultAction : code;
                string sub = code == "custom"
                    ? (AppConfig.GetString(kd.Name + "_custom") ?? SignalText.T("No program chosen", "Программа не выбрана"))
                    : code.Length == 0 ? SignalText.T("Default action", "Действие по умолчанию") : SignalText.T("Custom assignment", "Своё назначение");

                ry = ctx.DrawSettingRow(ry, rx, rw,
                    kd.Label, sub, ActionName(resolved),
                    "keys:bind:" + kd.Name,
                    pt => ShowBindMenu(ctx, pt, kd),
                    showChevron: true);
            }

            ry = ctx.DrawToggleRow(ry, rx, rw,
                "Fn Lock",
                SignalText.T("F1–F12 act as function keys without Fn", "F1–F12 работают как функциональные без Fn"),
                "keys:fnlock",
                AppConfig.Is("fn_lock"),
                () => InputDispatcher.ToggleFnLock());

            if (AppConfig.IsDUO())
            {
                ry = ctx.DrawToggleRow(ry, rx, rw,
                    SignalText.T("Arrow lock", "Блокировка стрелок"),
                    SignalText.T("DUO keyboard arrow lock", "Блокировка стрелок DUO"),
                    "keys:arrowlock",
                    AppConfig.Is("arrow_lock"),
                    () => InputDispatcher.ToggleArrowLock());
            }

            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Conflicts", "Конфликты"),
                conflicts.Count == 0
                    ? SignalText.T("Never overwrite an existing binding silently", "Конфликты не перезаписываются скрытно")
                    : string.Join(", ", conflicts),
                conflicts.Count == 0 ? SignalText.T("No conflicts", "Нет конфликтов") : SignalText.Tf("{0} conflicts", "Конфликтов: {0}", conflicts.Count),
                "keys:conflicts",
                null,
                readOnly: true);

            ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("More keyboard options", "Дополнительные параметры клавиатуры"),
                SignalText.T("Backlight timeouts, touchpad, ASPM, boot sound", "Таймауты подсветки, тачпад, ASPM, звук загрузки"),
                SignalText.T("Open", "Открыть"),
                "keys:extra",
                _ => Program.settingsForm.ExtraWindow(),
                drawDivider: false);

            lastContentH = panelH + 16f;
        }

        private List<string> FindConflicts()
        {
            var seen = new Dictionary<string, string>();
            var dupes = new List<string>();
            foreach (var k in keys)
            {
                string c = AppConfig.GetString(k.Name) ?? "";
                string eff = c.Length == 0 ? k.DefaultAction : c;
                if (string.IsNullOrEmpty(eff) || eff == "custom") continue;
                if (seen.TryGetValue(eff, out var prev))
                    dupes.Add($"{prev} / {k.Label}");
                else
                    seen[eff] = k.Label;
            }
            return dupes;
        }

        private void ShowBindMenu(SignalPaintContext ctx, Point pt, KeyDef kd)
        {
            string cur = AppConfig.GetString(kd.Name) ?? "";
            var actions = Actions();
            var items = new List<(string, bool, string?, Action)>
            {
                (SignalText.T("Default", "По умолчанию") + " · " + ActionName(kd.DefaultAction), cur.Length == 0, null, () => Assign(kd.Name, "")),
            };

            foreach (var kv in actions)
            {
                if (kv.Key == kd.DefaultAction) continue;
                string code = kv.Key;
                items.Add((kv.Value, cur == code, null, () =>
                {
                    if (code == "custom")
                    {
                        using var dlg = new OpenFileDialog
                        {
                            Title = SignalText.T("Choose a program", "Выбрать программу"),
                            Filter = "Programs|*.exe;*.lnk;*.bat;*.cmd|All files|*.*",
                            CheckFileExists = true,
                        };
                        if (dlg.ShowDialog() != DialogResult.OK) return;
                        AppConfig.Set(kd.Name + "_custom", dlg.FileName);
                    }
                    Assign(kd.Name, code);
                }));
            }
            ctx.ShowMenu(pt, items);
        }

        private static void Assign(string name, string code)
        {
            AppConfig.Set(name, code);
            if (name is "m1" or "m2" or "m3" or "m4" or "m5")
            {
                MKeyControl.ApplyAll();
                Program.inputDispatcher?.RegisterKeys();
            }
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

        private static string ActionName(string code)
        {
            if (string.IsNullOrEmpty(code)) return SignalText.T("Nothing", "Ничего");
            if (code == "calculator") return SignalText.T("Calculator", "Калькулятор");
            return Actions().TryGetValue(code, out var n) ? n : code;
        }
    }
}
