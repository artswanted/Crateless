namespace GHelper.UI.SignalGlass.Pages
{
    /// <summary>
    /// 01 Overview: two 60-second telemetry plots (CPU & GPU) on top and three compact
    /// summary cards (Cooling, Memory, Battery) below — designed to fit down to 900×640 without scroll.
    /// </summary>
    public sealed class OverviewPage : SignalPage
    {
        public override string Id => "overview";
        public override string GroupId => "overview";
        public override bool HasFooter => false;

        public override string Title
        {
            get
            {
                string m = AppConfig.GetModelShort();
                return string.IsNullOrWhiteSpace(m) ? "Crateless" : m;
            }
        }

        public override string Subtitle
        {
            get
            {
                var snap = lastSnapshot;
                string baseSub = SignalText.T("System monitoring · last 60 seconds", "Мониторинг · последние 60 секунд");
                if (snap is null || snap.Timestamp == DateTimeOffset.MinValue)
                    return baseSub + " · " + SignalText.T("Reading sensors…", "Чтение датчиков…");
                if (snap.Status == SampleStatus.Stale)
                    return baseSub + " · " + SignalText.Tf("Stale ({0})", "Устарело ({0})", SignalText.FormatTime(snap.Timestamp));
                return baseSub + " · " + SignalText.FormatTime(snap.Timestamp);
            }
        }

        private SensorSnapshot? lastSnapshot;

        public override void Paint(SignalPaintContext ctx)
        {
            lastSnapshot = ctx.Snapshot;
            var s = ctx.Snapshot;
            var l = ctx.Layout;

            PaintTelemetryPanel(
                ctx,
                l.CpuPanel,
                l.CpuMetrics,
                l.CpuPlot,
                "CPU",
                ctx.Theme.Accent,
                s.CpuTempC is > 0 ? Math.Round(s.CpuTempC.Value) + "°C" : "—",
                new[]
                {
                    s.CpuUsagePct is >= 0 ? s.CpuUsagePct + "% " + SignalText.T("load", "нагр.") : SignalText.T("No data", "Нет данных"),
                    s.CpuClockMhz is > 0 ? SignalText.FormatNumber(s.CpuClockMhz.Value / 1000.0, "0.00") + " GHz" : "—",
                    s.CpuPowerW is > 0 ? SignalText.FormatNumber(s.CpuPowerW.Value, "0.#") + " W" : "—",
                },
                ctx.CpuHistory);

            string gpuBig = s.GpuTempC is > 0 ? Math.Round(s.GpuTempC.Value) + "°C" : "—";
            string gpuStateLine = s.DgpuState switch
            {
                DgpuActivityState.OffEco => SignalText.T("Off (Eco)", "Выкл (Eco)"),
                DgpuActivityState.Sleeping => SignalText.T("Sleeping", "Спит"),
                DgpuActivityState.Unsupported => SignalText.T("No data", "Нет данных"),
                _ => s.GpuUsagePct is >= 0 ? s.GpuUsagePct + "% " + SignalText.T("load", "нагр.") : "—",
            };

            PaintTelemetryPanel(
                ctx,
                l.GpuPanel,
                l.GpuMetrics,
                l.GpuPlot,
                "GPU",
                ctx.Theme.Gpu,
                gpuBig,
                new[]
                {
                    gpuStateLine,
                    s.DgpuState == DgpuActivityState.Active && s.GpuClockMhz is > 0
                        ? SignalText.FormatNumber(s.GpuClockMhz.Value / 1000.0, "0.00") + " GHz"
                        : "—",
                    s.DgpuState == DgpuActivityState.Active && s.GpuPowerW is > 0
                        ? SignalText.FormatNumber(s.GpuPowerW.Value, "0.#") + " W"
                        : "—",
                },
                ctx.GpuHistory);

            PaintCoolingCard(ctx, l.BottomCooling, s);
            PaintMemoryCard(ctx, l.BottomMemory, s);
            PaintBatteryCard(ctx, l.BottomBattery, s);
        }

        private static void PaintTelemetryPanel(
            SignalPaintContext ctx,
            RectangleF panelDip,
            RectangleF metricDip,
            RectangleF plotDip,
            string title,
            Color accentColor,
            string bigTemp,
            string[] details,
            RingHistoryBuffer history)
        {
            ctx.DrawPanel(panelDip);
            var th = ctx.Theme;
            float sc = ctx.Scale;
            bool compact = panelDip.Height < 185f;

            var fmt = ctx.Rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft;
            var mPx = ctx.Px(metricDip);

            float y = metricDip.Y + (compact ? 4f : 12f);
            ctx.Painter.DrawText(ctx.G, title, new RectangleF(mPx.X, y * sc, mPx.Width, 24f * sc),
                ctx.Painter.Font(17f, sc, FontStyle.Bold), accentColor, fmt);
            y += compact ? 24f : 34f;

            float bigSize = compact ? 30f : 38f;
            ctx.Painter.DrawText(ctx.G, bigTemp, new RectangleF(mPx.X, y * sc, mPx.Width, (bigSize + 8f) * sc),
                ctx.Painter.Font(bigSize, sc, FontStyle.Bold, true), th.Text, fmt);
            y += compact ? 38f : 54f;

            float step = compact ? 20f : 27f;
            var detFont = ctx.Painter.Font(compact ? 12.5f : 14.5f, sc);
            foreach (string line in details)
            {
                if (y + step > metricDip.Bottom + 4f) break;
                ctx.Painter.DrawText(ctx.G, line, new RectangleF(mPx.X, y * sc, mPx.Width, step * sc), detFont, th.Muted, fmt);
                y += step;
            }

            var plotPx = ctx.Px(plotDip);
            ctx.Painter.DrawHistoryChart(
                ctx.G,
                plotPx,
                sc,
                th,
                history.Snapshot(),
                history.Capacity,
                accentColor,
                "°C",
                ctx.MouseClient,
                out string hoverTip);

            ctx.Register(new UiElement
            {
                Id = "overview:chart:" + title.ToLowerInvariant(),
                BoundsPx = ctx.Px(panelDip),
                Name = title,
                Value = $"{bigTemp}, {string.Join(", ", details)}",
                Tooltip = hoverTip.Length > 0 ? hoverTip : $"{title}: {bigTemp}",
                Role = AccessibleRole.StaticText,
                States = AccessibleStates.ReadOnly,
                Focusable = false,
            });
        }

        private static void PaintCoolingCard(SignalPaintContext ctx, RectangleF cardDip, SensorSnapshot s)
        {
            var rows = new List<(string label, string val)>
            {
                (SignalText.T("CPU fan", "Вентилятор CPU"), FormatFan(s.CpuFanRpm, s.CpuFanRaw)),
                (SignalText.T("GPU fan", "Вентилятор GPU"), FormatFan(s.GpuFanRpm, s.GpuFanRaw)),
            };
            if (s.HasMidFan)
                rows.Add((SignalText.T("System fan", "Вентилятор системы"), FormatFan(s.MidFanRpm, s.MidFanRaw)));

            PaintSummaryCard(ctx, cardDip, SignalText.T("Cooling", "Охлаждение"), "overview:card:cooling", () => ctx.Navigate("cooling"), rows);
        }

        private static void PaintMemoryCard(SignalPaintContext ctx, RectangleF cardDip, SensorSnapshot s)
        {
            string used = s.RamUsedGb is > 0 ? SignalText.FormatNumber(s.RamUsedGb.Value, "0.0") + " GB" : "—";
            string total = s.RamTotalGb is > 0 ? SignalText.FormatNumber(s.RamTotalGb.Value, "0") + " GB" : "—";
            string pct = s.RamUsagePct is >= 0 ? s.RamUsagePct + "%" : "—";

            var rows = new List<(string, string)>
            {
                (SignalText.T("Used", "Занято"), used),
                (SignalText.T("Available total", "Доступно всего"), total),
                (SignalText.T("Usage", "Загрузка"), pct),
            };
            PaintSummaryCard(ctx, cardDip, SignalText.T("Memory", "Память"), "overview:card:memory", null, rows);
        }

        private static void PaintBatteryCard(SignalPaintContext ctx, RectangleF cardDip, SensorSnapshot s)
        {
            string charge = s.ChargePct is >= 0 ? s.ChargePct + "%" : "—";
            string limit = s.ChargeLimitPct + "%";
            string status = s.BatteryStatus switch
            {
                BatteryStatusKind.LimitReached => SignalText.T("Limit reached", "Лимит достигнут"),
                BatteryStatusKind.Charging => SignalText.T("Charging", "Зарядка") + (s.BatteryRateW is > 0 ? $" · {SignalText.FormatNumber((double)s.BatteryRateW.Value, "0.0")} W" : ""),
                BatteryStatusKind.Discharging => SignalText.T("Discharging", "Разряд") + (s.BatteryRateW is < 0 ? $" · {SignalText.FormatNumber((double)(-s.BatteryRateW.Value), "0.0")} W" : ""),
                BatteryStatusKind.PluggedIdle => SignalText.T("Plugged in", "От сети"),
                BatteryStatusKind.OnBattery => SignalText.T("On battery", "От батареи"),
                _ => "—",
            };

            var rows = new List<(string, string)>
            {
                (SignalText.T("Charge", "Заряд"), charge),
                (SignalText.T("Charge limit", "Лимит заряда"), limit),
                (SignalText.T("Status", "Состояние"), status),
            };
            PaintSummaryCard(ctx, cardDip, SignalText.T("Battery", "Батарея"), "overview:card:battery", () => ctx.Navigate("battery"), rows);
        }

        private static void PaintSummaryCard(SignalPaintContext ctx, RectangleF cardDip, string title, string id,
            Action? onClick, IReadOnlyList<(string label, string value)> rows)
        {
            ctx.DrawPanel(cardDip);
            var th = ctx.Theme;
            float sc = ctx.Scale;
            bool compact = cardDip.Height < 175f;
            float padX = 20f;
            float y = cardDip.Y + (compact ? 14f : 20f);
            float innerW = cardDip.Width - padX * 2f;

            var titleFmt = ctx.Rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft;
            ctx.Painter.DrawText(ctx.G, title, ctx.Px(cardDip.X + padX, y, innerW, 24f),
                ctx.Painter.Font(compact ? 15.5f : 17.5f, sc, FontStyle.Bold), th.Text, titleFmt);

            y += compact ? 32f : 44f;
            float avail = Math.Max(60f, cardDip.Bottom - y - (compact ? 10f : 14f));
            float step = Math.Min(44f, avail / Math.Max(1, rows.Count));

            var lblFont = ctx.Painter.Font(compact ? 12f : 13f, sc);
            var valFont = ctx.Painter.Font(compact ? 13f : 14.5f, sc, FontStyle.Bold);

            foreach (var (label, value) in rows)
            {
                float half = innerW * 0.56f;
                var lRect = ctx.Rtl
                    ? ctx.Px(cardDip.X + padX + (innerW - half), y, half, step)
                    : ctx.Px(cardDip.X + padX, y, half, step);
                var vRect = ctx.Rtl
                    ? ctx.Px(cardDip.X + padX, y, innerW - half - 6f, step)
                    : ctx.Px(cardDip.X + padX + half + 6f, y, innerW - half - 6f, step);

                ctx.Painter.DrawText(ctx.G, label, lRect, lblFont, th.Muted, ctx.Rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft);
                ctx.Painter.DrawText(ctx.G, value, vRect, valFont, th.Text, ctx.Rtl ? GlassPainter.FmtLeft : GlassPainter.FmtRight);
                y += step;
            }

            if (ctx.IsFocused(id))
                GlassPainter.DrawFocusRing(ctx.G, ctx.Px(cardDip), sc, th, false, SignalTheme.RadiusPanel);

            ctx.Register(new UiElement
            {
                Id = id,
                BoundsPx = ctx.Px(cardDip),
                Name = title,
                Value = string.Join(", ", rows.Select(r => $"{r.label}: {r.value}")),
                Role = onClick is not null ? AccessibleRole.PushButton : AccessibleRole.Grouping,
                States = onClick is not null ? AccessibleStates.Focusable : AccessibleStates.ReadOnly,
                Focusable = onClick is not null,
                Enabled = true,
                RadiusDip = SignalTheme.RadiusPanel,
                OnActivate = onClick is not null ? _ => onClick() : null,
            });
        }

        private static string FormatFan(int? rpm, string? raw)
        {
            if (rpm is > 0) return rpm.Value + " rpm";
            if (!string.IsNullOrWhiteSpace(raw)) return raw;
            return "—";
        }
    }
}
