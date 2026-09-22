using GHelper.Battery;

namespace GHelper.UI.Nebula.Pages
{
    /// <summary>Battery page (design kit screen "battery").</summary>
    public sealed class BatteryPage : NebulaPage
    {
        public override string Id => "battery";
        public override string Title => NebulaText.T("Battery", "Батарея");
        public override string Subtitle => NebulaText.T("Charge control without extra steps.", "Контроль заряда без лишних действий.");

        private static readonly bool discrete = AppConfig.IsChargeLimit6080();
        private static readonly int[] discreteValues = { 60, 80, 100 };

        private int draft = -1;          // unsaved limit
        private long healthRequested;

        public override void Refresh(bool opened)
        {
            if (opened)
            {
                draft = -1;
                long now = Environment.TickCount64;
                if (now - healthRequested > 15 * 60_000)
                {
                    healthRequested = now;
                    Task.Run(HardwareControl.RefreshBatteryHealth);
                }
            }
        }

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
        }

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
            }
            if (id.StartsWith("battery:set:"))
            {
                draft = int.Parse(id[12..]);
                return true;
            }
            return false;
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
