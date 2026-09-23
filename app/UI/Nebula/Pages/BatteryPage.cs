using GHelper.Battery;
using GHelper.Mode;
using System.Diagnostics;

namespace GHelper.UI.Nebula.Pages
{
    /// <summary>Power page (design kit screen "battery"): charge, idle timeouts and power-source rules.</summary>
    public sealed class BatteryPage : NebulaPage
    {
        public override string Id => "battery";
        public override string Title => NebulaText.T("Power & battery", "Электропитание");
        public override string Subtitle => NebulaText.T("Charge, sleep and what happens when the charger comes off.",
                                                        "Заряд, сон и что делать при отключении от сети.");

        private static readonly bool discrete = AppConfig.IsChargeLimit6080();
        private static readonly int[] discreteValues = { 60, 80, 100 };

        /// <summary>Idle timeouts offered in the menus, in seconds. 0 is "never".</summary>
        private static readonly int[] spans = { 0, 60, 120, 180, 300, 600, 900, 1200, 1800, 2700, 3600, 7200, 10800, 14400, 18000 };

        private int draft = -1;          // unsaved limit
        private long healthRequested;

        // the power plan is read through PowrProf, so it is kept between paints and re-read on a change
        private string planName = "";
        private readonly Dictionary<(PowerPlan.Idle, bool), int> idle = new();
        private float bottom = 1013;

        public override void Refresh(bool opened)
        {
            if (!opened) return;

            draft = -1;
            ReadPlan();

            long now = Environment.TickCount64;
            if (now - healthRequested > 15 * 60_000)
            {
                healthRequested = now;
                Task.Run(HardwareControl.RefreshBatteryHealth);
            }
        }

        private void ReadPlan()
        {
            try
            {
                planName = PowerPlan.PlanName();
                idle.Clear();
                foreach (PowerPlan.Idle what in Enum.GetValues<PowerPlan.Idle>())
                    foreach (bool ac in new[] { true, false })
                        idle[(what, ac)] = PowerPlan.Get(what, ac);
            }
            catch (Exception ex) { Logger.WriteLine("Power plan: " + ex.Message); }
        }

        private int Idle(PowerPlan.Idle what, bool ac) => idle.TryGetValue((what, ac), out var v) ? v : -1;

        private static int CurrentLimit => Math.Clamp(AppConfig.Get("charge_limit", 100), 40, 100);
        private int Draft => draft < 0 ? CurrentLimit : draft;

        public override void Paint(NebulaCanvas c)
        {
            var th = c.Theme;

            // ---- current charge -----------------------------------------------------------------
            c.Surface(236, 139, 330, 292);
            c.Txt(NebulaText.T("Current charge", "Текущий заряд"), 256, 169, c.F(15, FontStyle.Bold), th.Text);

            int charge = ParseLeadingInt(HardwareControl.batteryCharge ?? "");
            bool plugged = Gpu.GPUModeControl.IsPlugged();
            decimal rate = HardwareControl.batteryRate ?? 0;
            int limit = CurrentLimit;

            c.Ring(401, 300, 82, charge >= 0 ? charge / 100f : 0, th.Accent, 10);
            c.Txt(charge >= 0 ? charge + "%" : "—", 401, 312, c.F(38, FontStyle.Bold, true), th.Text, StringAlignment.Center);
            c.Txt(plugged ? NebulaText.OnAc : NebulaText.OnBattery, 401, 336, c.F(10, FontStyle.Bold), th.Faint, StringAlignment.Center);

            string status;
            if (charge < 0) status = NebulaText.T("No battery data", "Нет данных о батарее");
            else if (rate > 0) status = NebulaText.T("Charging", "Идёт зарядка") + " · " + Math.Round(rate, 1) + " " + NebulaText.Watt;
            else if (rate < 0) status = NebulaText.T("Discharging", "Разряд") + " · " + Math.Round(-rate, 1) + " " + NebulaText.Watt;
            else if (plugged && charge >= limit) status = NebulaText.T("Limit reached · charging stopped", "Лимит достигнут · зарядка остановлена");
            else if (plugged) status = NebulaText.T("Plugged in · not charging", "От сети · без зарядки");
            else status = NebulaText.T("On battery", "От батареи");
            c.Txt(status, 401, 410, c.F(10), th.Muted, StringAlignment.Center);

            // ---- charge limit -------------------------------------------------------------------
            c.Surface(582, 139, 472, 292);
            c.Txt(NebulaText.T("Charge limit", "Лимит заряда"), 602, 169, c.F(15, FontStyle.Bold), th.Text);
            c.Txt(NebulaText.T("Pick a ceiling for everyday use.", "Выбери предел для ежедневного использования."), 602, 190, c.F(11), th.Muted);

            c.Txt(NebulaText.T("Charge up to", "Заряжать до"), 602, 244, c.F(12), th.Muted);
            c.Txt(Draft + "%", 1034, 244, c.F(14, FontStyle.Bold), th.Text, StringAlignment.Far);

            if (!discrete)
                c.Slider(602, 262, 432, (Draft - 40) / 60f, "slider:battery");

            float segX = 602;
            foreach (int v in discreteValues)
            {
                float w = v == 100 ? 144 : 132;
                c.Segment(segX, 292, w, 36, v + "%", Draft == v, "battery:set:" + v);
                segX += w + 12;
            }

            c.Txt(discrete
                    ? NebulaText.T("This model accepts 60, 80 and 100%.", "Эта модель принимает 60, 80 и 100%.")
                    : NebulaText.T("Supported range is reported by the device (40–100%).", "Диапазон сообщает устройство (40–100%)."),
                  602, 365, c.F(11), th.Faint);

            bool dirty = draft >= 0 && draft != limit;
            c.Button(810, 382, 224, 36, dirty ? NebulaText.T("Apply limit", "Применить лимит") : NebulaText.T("Limit applied", "Лимит применён"),
                     "battery:apply", primary: true, enabled: dirty);

            // ---- one-time full charge (exists in the fork: charge_full) --------------------------
            c.Surface(236, 447, 818, 251);
            bool full = BatteryControl.chargeFull;
            c.Txt(NebulaText.T("Before a trip", "Перед поездкой"), 256, 477, c.F(15, FontStyle.Bold), th.Text);
            c.Txt(NebulaText.T("Charge to 100% once", "Разово зарядить до 100%"), 256, 507, c.F(16, FontStyle.Bold), th.Text);
            c.Txt(NebulaText.Tf("Then return to the usual {0}% limit.", "Затем вернуть обычный лимит {0}%.", limit), 256, 536, c.F(12), th.Muted);
            c.Pill(256, 557, full ? NebulaText.T("ACTIVE", "ВКЛЮЧЕНО") : NebulaText.T("OFF", "ВЫКЛЮЧЕНО"), full ? th.AccentBg : th.Raised, full ? th.Accent : th.Muted);
            c.Button(810, 490, 224, 36, full ? NebulaText.T("Cancel", "Отменить") : NebulaText.T("Enable", "Включить"), "battery:full", primary: !full);

            // ---- health -------------------------------------------------------------------------
            c.Txt(NebulaText.T("Battery health", "Состояние батареи"), 256, 648, c.F(12, FontStyle.Bold), th.Text);
            string health;
            if (HardwareControl.batteryHealth >= 0)
            {
                // batteryHealth is full/design capacity in percent (100 = new battery)
                decimal h = HardwareControl.batteryHealth;
                health = NebulaText.T("Health", "Здоровье") + ": " + Math.Round(h, 1) + "%  ·  " + NebulaText.T("wear", "износ") + " " + Math.Round(100 - h, 1) + "%";
                if (HardwareControl.fullCapacity is > 0 && HardwareControl.designCapacity is > 0)
                    health += $"  ·  {Math.Round(HardwareControl.fullCapacity.Value / 1000m, 1)} / {Math.Round(HardwareControl.designCapacity.Value / 1000m, 1)} " + NebulaText.T("Wh", "Вт·ч");
            }
            else health = NebulaText.T("Reading…", "Читаем…");
            c.Txt(health, 256, 674, c.F(11), th.Muted);

            var link = c.F(11, FontStyle.Bold);
            string rep = NebulaText.T("Windows battery report  ↗", "Отчёт Windows о батарее  ↗");
            c.Txt(rep, 1034, 674, link, c.IsHover("battery:report") ? th.Text : th.Accent, StringAlignment.Far);
            c.Hit(c.R(1034 - c.TextWidth(rep, link), 656, c.TextWidth(rep, link) + 8, 26), "battery:report");

            float y = PaintIdle(c, 718);
            bottom = PaintRules(c, y + 22);
        }

        // ---- screen off, sleep, hibernate -----------------------------------------------------------
        private float PaintIdle(NebulaCanvas c, float top)
        {
            var th = c.Theme;
            var rows = IdleRows();
            float height = 110 + rows.Count * 62 + 44;
            c.Surface(236, top, 818, height);

            c.Txt(NebulaText.T("Screen and sleep", "Экран и сон"), 256, top + 30, c.F(15, FontStyle.Bold), th.Text);
            c.Txt(planName.Length > 0
                    ? NebulaText.Tf("Windows power plan: {0}", "Схема электропитания Windows: {0}", planName)
                    : NebulaText.T("Windows did not report a power plan.", "Windows не сообщила схему электропитания."),
                  256, top + 51, c.F(11), th.Muted);

            c.Txt(NebulaText.OnAc, 640, top + 92, c.F(10, FontStyle.Bold), th.Faint);
            c.Txt(NebulaText.OnBattery, 844, top + 92, c.F(10, FontStyle.Bold), th.Faint);

            float y = top + 110;
            foreach (var (what, label, hint) in rows)
            {
                c.Txt(label, 256, y + 16, c.F(13), th.Text);
                c.Txt(hint, 256, y + 36, c.F(11), th.Faint);
                Choice(c, 640, y - 4, 190, Span(Idle(what, true)), "battery:idle:" + what + ":ac", Idle(what, true) >= 0);
                Choice(c, 844, y - 4, 190, Span(Idle(what, false)), "battery:idle:" + what + ":dc", Idle(what, false) >= 0);
                y += 62;
            }

            // the screen going dark after the machine has already slept would never be seen
            bool odd = false;
            foreach (bool ac in new[] { true, false })
            {
                int screen = Idle(PowerPlan.Idle.ScreenOff, ac), sleep = Idle(PowerPlan.Idle.Sleep, ac);
                if (screen > 0 && sleep > 0 && sleep < screen) odd = true;
            }
            c.Txt(odd
                    ? "⚠ " + NebulaText.T("Sleep comes before the screen turns off, so the screen timeout never fires.",
                                          "Сон наступает раньше, чем гаснет экран, поэтому таймер экрана не сработает.")
                    : NebulaText.T("Written to the plan Windows is using right now; the same values as in Control Panel.",
                                   "Пишется в схему, которой Windows пользуется сейчас; те же значения, что в панели управления."),
                  256, y + 22, c.F(11), odd ? th.Warning : th.Faint);

            var link = c.F(11, FontStyle.Bold);
            string more = NebulaText.T("Windows power settings  ↗", "Параметры питания Windows  ↗");
            c.Txt(more, 1034, y + 22, link, c.IsHover("battery:winpower") ? th.Text : th.Accent, StringAlignment.Far);
            c.Hit(c.R(1034 - c.TextWidth(more, link), y + 4, c.TextWidth(more, link) + 8, 26), "battery:winpower");

            return top + height;
        }

        private List<(PowerPlan.Idle what, string label, string hint)> IdleRows()
        {
            var rows = new List<(PowerPlan.Idle, string, string)>
            {
                (PowerPlan.Idle.ScreenOff, NebulaText.T("Turn the screen off after", "Отключать экран через"),
                                           NebulaText.T("The panel goes dark, the machine keeps running.", "Панель гаснет, устройство продолжает работать.")),
                (PowerPlan.Idle.Sleep,     NebulaText.T("Sleep after", "Переводить в спящий режим через"),
                                           NebulaText.T("Everything stops, memory stays powered.", "Всё останавливается, память остаётся под питанием.")),
            };

            if (Idle(PowerPlan.Idle.Hibernate, true) >= 0 || Idle(PowerPlan.Idle.Hibernate, false) >= 0)
                rows.Add((PowerPlan.Idle.Hibernate, NebulaText.T("Hibernate after", "Гибернация через"),
                                                    NebulaText.T("Memory is written to disk and power is cut.", "Память пишется на диск, питание снимается.")));
            return rows;
        }

        // ---- what to do when the charger comes off or goes back on -----------------------------------
        private float PaintRules(NebulaCanvas c, float top)
        {
            var th = c.Theme;
            bool on = PowerRules.Enabled;
            var modes = Modes.GetList();
            float height = on ? 132 + modes.Count * 58 + 46 : 132;
            c.Surface(236, top, 818, height);

            c.Txt(NebulaText.T("When the power source changes", "При смене источника питания"), 256, top + 30, c.F(15, FontStyle.Bold), th.Text);
            c.Txt(NebulaText.T("A rule per mode instead of one blanket setting.", "Правило для каждого режима, а не одна общая настройка."),
                  256, top + 51, c.F(11), th.Muted);
            c.Toggle(996, top + 18, on, "battery:rules");

            if (!on)
            {
                c.Txt(NebulaText.T("Off: the mode you used last on that power source comes back, as before.",
                                   "Выключено: возвращается режим, который использовался на этом питании последним."),
                      256, top + 96, c.F(11), th.Faint);
                return top + height;
            }

            c.Txt(NebulaText.T("WHEN UNPLUGGED", "ПРИ ОТКЛЮЧЕНИИ ОТ СЕТИ"), 640, top + 92, c.F(10, FontStyle.Bold), th.Faint);
            c.Txt(NebulaText.T("WHEN PLUGGED BACK IN", "ПРИ ПОДКЛЮЧЕНИИ К СЕТИ"), 844, top + 92, c.F(10, FontStyle.Bold), th.Faint);

            int current = Modes.GetCurrent();
            float y = top + 110;
            foreach (int mode in modes)
            {
                bool active = mode == current;
                c.TxtFit(Modes.GetName(mode), 256, y + 22, 360, 13, active ? FontStyle.Bold : FontStyle.Regular, active ? th.Accent : th.Text);
                Choice(c, 640, y, 190, RuleName(PowerRules.OnBattery(mode), false), "battery:rule:bat:" + mode);
                Choice(c, 844, y, 190, RuleName(PowerRules.OnAc(mode), true), "battery:rule:ac:" + mode);
                y += 58;
            }

            int back = AppConfig.Get("power_rule_return", -1);
            string note = NebulaText.Tf("Now in {0}.", "Сейчас {0}.", Modes.GetName(current));
            int next = PowerRules.OnBattery(current);
            note += "  " + (next == PowerRules.Keep
                ? NebulaText.T("Unplugging changes nothing.", "Отключение от сети ничего не изменит.")
                : NebulaText.Tf("Unplugging switches to {0}.", "Отключение от сети переключит в {0}.", Modes.GetName(next)));
            if (Modes.Exists(back)) note += "  " + NebulaText.Tf("Mode to return to: {0}.", "Режим для возврата: {0}.", Modes.GetName(back));
            c.Txt(note, 256, y + 24, c.F(11), th.Faint);

            return top + height;
        }

        private static string RuleName(int target, bool ac)
        {
            if (target == PowerRules.Restore && ac) return NebulaText.T("Previous mode", "Прежний режим");
            if (target < 0 || !Modes.Exists(target)) return NebulaText.T("Stay as is", "Не менять");
            return Modes.GetName(target);
        }

        /// <summary>A value that opens a menu: reads as a field, not as a button.</summary>
        private static void Choice(NebulaCanvas c, float x, float y, float w, string text, string id, bool enabled = true)
        {
            var th = c.Theme;
            bool hv = enabled && c.IsHover(id);
            var r = c.R(x, y, w, 34);
            c.Card(r, 8, hv ? th.Raised : th.Bg, hv ? th.Accent : th.Line);
            c.TxtFit(text, x + 12, y + 22, w - 34, 12, FontStyle.Bold, enabled ? th.Text : th.Faint);
            c.Txt("⌄", x + w - 20, y + 21, c.F(11), enabled ? th.Muted : th.Faint);
            if (enabled) c.Hit(r, id);
        }

        private static string Span(int seconds)
        {
            if (seconds < 0) return "—";
            if (seconds == 0) return NebulaText.T("Never", "Никогда");
            if (seconds >= 3600 && seconds % 3600 == 0) return NebulaText.Tf("{0} h", "{0} ч", seconds / 3600);
            return NebulaText.Tf("{0} min", "{0} мин", seconds / 60);
        }

        public override float ContentHeight => bottom + 20;

        public override bool Click(string id, Point at, NebulaForm form)
        {
            switch (id)
            {
                case "battery:apply":
                    if (draft >= 0) { int v = draft; draft = -1; BatteryControl.SetBatteryChargeLimit(v); }
                    return true;
                case "battery:full":
                    BatteryControl.ToggleBatteryLimitFull();
                    return true;
                case "battery:report":
                    Task.Run(BatteryControl.BatteryReport);
                    return true;
                case "battery:rules":
                    PowerRules.SetEnabled(!PowerRules.Enabled);
                    return true;
                case "battery:winpower":
                    try { Process.Start(new ProcessStartInfo("ms-settings:powersleep") { UseShellExecute = true }); }
                    catch (Exception ex) { Logger.WriteLine("Power settings: " + ex.Message); }
                    return true;
            }

            if (id.StartsWith("battery:set:"))
            {
                draft = int.Parse(id[12..]);
                return true;
            }

            if (id.StartsWith("battery:idle:"))
            {
                var parts = id.Split(':');
                var what = Enum.Parse<PowerPlan.Idle>(parts[2]);
                bool ac = parts[3] == "ac";
                int now = Idle(what, ac);
                Menu(form, at, spans.Select(s => (Span(s), s == now, (Action)(() =>
                {
                    if (PowerPlan.Set(what, ac, s)) ReadPlan();
                }))));
                return true;
            }

            if (id.StartsWith("battery:rule:"))
            {
                var parts = id.Split(':');
                bool ac = parts[2] == "ac";
                int mode = int.Parse(parts[3]);
                int now = ac ? PowerRules.OnAc(mode) : PowerRules.OnBattery(mode);

                var items = new List<(string, bool, Action)>
                {
                    (NebulaText.T("Stay as is", "Не менять"), now == PowerRules.Keep, () => Assign(ac, mode, PowerRules.Keep)),
                };
                if (ac) items.Add((NebulaText.T("Previous mode", "Прежний режим"), now == PowerRules.Restore, () => Assign(ac, mode, PowerRules.Restore)));
                foreach (int target in Modes.GetList())
                {
                    if (target == mode) continue;
                    int t = target;
                    items.Add((Modes.GetName(t), now == t, () => Assign(ac, mode, t)));
                }
                Menu(form, at, items);
                return true;
            }

            return false;
        }

        private static void Assign(bool ac, int mode, int target)
        {
            if (ac) PowerRules.SetOnAc(mode, target); else PowerRules.SetOnBattery(mode, target);
        }

        public override void Drag(string id, float t, bool done)
        {
            if (id != "slider:battery") return;
            int v = 40 + (int)Math.Round(t * 60 / 5) * 5;
            draft = Math.Clamp(v, 40, 100);
        }

        private static int ParseLeadingInt(string s)
        {
            int i = 0; while (i < s.Length && !char.IsDigit(s[i])) i++;
            int j = i; while (j < s.Length && char.IsDigit(s[j])) j++;
            return j > i && int.TryParse(s[i..j], out var v) ? v : -1;
        }
    }
}
