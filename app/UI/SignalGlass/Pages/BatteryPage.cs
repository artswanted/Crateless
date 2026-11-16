using GHelper.Battery;

namespace GHelper.UI.SignalGlass.Pages
{
    /// <summary>
    /// 05 Battery: charge limit slider + numeric step controls, one-time 100% cycle,
    /// and read-only health/capacity/cycles/status telemetry.
    /// </summary>
    public sealed class BatteryPage : SignalPage
    {
        public override string Id => "battery";
        public override string GroupId => "tuning";
        public override string Title => SignalText.T("Battery", "Батарея");
        public override bool CanApply => dirty;
        public override bool CanReset => true;

        private int draftLimit = -1;
        private bool dirty => draftLimit >= 0 && draftLimit != CurrentConfirmedLimit;
        private float lastContentH = 600f;

        private static int CurrentConfirmedLimit => Math.Clamp(AppConfig.Get("charge_limit", 100), 40, 100);

        public override void OnEnter(SignalPaintContext? ctx)
        {
            if (!dirty) draftLimit = CurrentConfirmedLimit;
            Task.Run(HardwareControl.RefreshBatteryHealth);
        }

        public override void Reset()
        {
            draftLimit = CurrentConfirmedLimit;
        }

        public override void Apply()
        {
            if (draftLimit < 0) return;
            int target = draftLimit;
            BatteryControl.SetBatteryChargeLimit(target);
            draftLimit = CurrentConfirmedLimit;
        }

        public override float ContentHeightDip(SignalPaintContext ctx) => lastContentH;

        public override void Paint(SignalPaintContext ctx)
        {
            var body = ctx.Layout.WorkspaceBody;
            float x = body.X, w = body.Width, y = body.Y - ctx.ScrollDip;
            float pad = 24f, rx = x + pad, rw = w - pad * 2f;
            var s = ctx.Snapshot;

            if (draftLimit < 0) draftLimit = CurrentConfirmedLimit;
            int min = ctx.Caps.BatteryLimitMin;
            int max = ctx.Caps.BatteryLimitMax;

            const int rows = 7;
            float panelH = rows * 74f + 46f;
            ctx.DrawPanel(new RectangleF(x, y, w, panelH));
            float ry = y + 16f;

            // Charge limit row with slider & stepper
            float ctrlW = Math.Min(290f, rw * 0.42f);
            float textW = rw - ctrlW - 24f;
            var titleRect = ctx.Rtl ? new RectangleF(rx + ctrlW + 24f, ry + 6f, textW, 24f) : new RectangleF(rx, ry + 6f, textW, 24f);
            var subRect = ctx.Rtl ? new RectangleF(rx + ctrlW + 24f, ry + 32f, textW, 30f) : new RectangleF(rx, ry + 32f, textW, 30f);
            var fmt = ctx.Rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft;

            ctx.Painter.DrawText(ctx.G, SignalText.T("Charge limit", "Лимит заряда"), ctx.Px(titleRect),
                ctx.Painter.Font(15f, ctx.Scale, FontStyle.Bold), ctx.Theme.Text, fmt);
            ctx.Painter.DrawText(ctx.G, ctx.Caps.BatteryDiscrete6080
                ? SignalText.T("Supported values: 60%, 80%, 100%", "Поддерживаются значения: 60%, 80%, 100%")
                : SignalText.T("Allowed bounds are hardware-specific", "Допустимые границы заданы устройством"),
                ctx.Px(subRect), ctx.Painter.Font(12f, ctx.Scale), ctx.Theme.Muted, fmt);

            float sx = ctx.Rtl ? rx : (rx + rw - ctrlW);
            ctx.DrawButton("battery:dec", new RectangleF(sx, ry + 14f, 34f, 32f), "−", _ => StepLimit(-5, min, max), fontSize: 14f);
            ctx.DrawButton("battery:inc", new RectangleF(sx + ctrlW - 34f, ry + 14f, 34f, 32f), "+", _ => StepLimit(5, min, max), fontSize: 14f);

            float sliderX = sx + 42f;
            float sliderW = ctrlW - 148f;
            float t = (draftLimit - min) / (float)Math.Max(1, max - min);
            ctx.DrawSlider("battery:slider", new RectangleF(sliderX, ry + 20f, sliderW, 20f),
                SignalText.T("Charge limit", "Лимит заряда"), t, draftLimit + "%",
                (nt, _) =>
                {
                    int val = min + (int)Math.Round(nt * (max - min) / 5f) * 5;
                    draftLimit = SnapDiscrete(Math.Clamp(val, min, max), ctx.Caps.BatteryDiscrete6080);
                },
                delta => StepLimit(delta * 5, min, max));

            ctx.Painter.DrawText(ctx.G, draftLimit + "%",
                ctx.Px(sx + ctrlW - 98f, ry + 18f, 58f, 24f),
                ctx.Painter.Font(15f, ctx.Scale, FontStyle.Bold), ctx.Theme.Accent, GlassPainter.FmtRight);

            using (var pen = new Pen(Color.FromArgb(80, ctx.Theme.Line), Math.Max(1f, ctx.Scale)))
                ctx.G.DrawLine(pen, rx * ctx.Scale, (ry + 80f) * ctx.Scale, (rx + rw) * ctx.Scale, (ry + 80f) * ctx.Scale);
            ry += 84f;

            // One-time 100% charge
            bool full = BatteryControl.chargeFull;
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("One-time full charge", "Разовый заряд до 100%"),
                SignalText.T("Returns to the saved limit after the one-time cycle", "Возвращает обычный лимит после цикла"),
                full ? SignalText.T("Cancel", "Отменить") : SignalText.T("Charge to 100%", "Зарядить до 100%"),
                "battery:full",
                _ => { BatteryControl.ToggleBatteryLimitFull(); ctx.RequestRefresh(); });

            // Read-only telemetry rows (never styled as buttons!)
            string healthTxt = s.BatteryHealthPct is >= 0 ? SignalText.FormatNumber((double)s.BatteryHealthPct.Value, "0.#") + "%" : "—";
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Health", "Здоровье"),
                SignalText.T("Read-only estimate, not a diagnosis", "Только чтение, оценочное значение"),
                healthTxt, "battery:health", null, readOnly: true);

            string designTxt = s.DesignCapacityWh is > 0 ? SignalText.FormatNumber((double)s.DesignCapacityWh.Value, "0.#") + " Wh" : "—";
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Design capacity", "Проектная ёмкость"),
                SignalText.T("Factory specification", "Заводская характеристика"),
                designTxt, "battery:design", null, readOnly: true);

            string fullTxt = s.FullCapacityWh is > 0 ? SignalText.FormatNumber((double)s.FullCapacityWh.Value, "0.#") + " Wh" : "—";
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Full charge capacity", "Полная ёмкость"),
                SignalText.T("Current measured full capacity", "Текущая полная ёмкость"),
                fullTxt, "battery:fullcap", null, readOnly: true);

            string cyclesTxt = s.CycleCount is > 0 ? s.CycleCount.Value.ToString() : "—";
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Cycle count", "Циклы зарядки"),
                SignalText.T("Shown as — when not reported by the controller", "Показывается —, если контроллер не сообщает"),
                cyclesTxt, "battery:cycles", null, readOnly: true);

            string statusTxt = s.BatteryStatus switch
            {
                BatteryStatusKind.LimitReached => SignalText.T("Limit reached", "Лимит достигнут"),
                BatteryStatusKind.Charging => SignalText.T("Charging", "Зарядка"),
                BatteryStatusKind.Discharging => SignalText.T("Discharging", "Разряд"),
                BatteryStatusKind.PluggedIdle => SignalText.T("Plugged in", "От сети"),
                BatteryStatusKind.OnBattery => SignalText.T("On battery", "От батареи"),
                _ => "—",
            };
            ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Current status", "Текущее состояние"),
                s.ChargePct is >= 0 ? SignalText.Tf("Current charge: {0}%", "Текущий заряд: {0}%", s.ChargePct.Value) : "",
                statusTxt, "battery:status", null, readOnly: true, drawDivider: false);

            lastContentH = panelH + 16f;
        }

        private void StepLimit(int delta, int min, int max)
        {
            int next = Math.Clamp(draftLimit + delta, min, max);
            draftLimit = SnapDiscrete(next, AppConfig.IsChargeLimit6080());
        }

        private static int SnapDiscrete(int val, bool discrete)
        {
            if (!discrete) return val;
            if (val <= 70) return 60;
            if (val <= 90) return 80;
            return 100;
        }
    }
}
