using GHelper.Fan;
using GHelper.Mode;
using GHelper.USB;
using PawnIO;
using System.Drawing.Drawing2D;

namespace GHelper.UI.SignalGlass.Pages
{
    /// <summary>
    /// 02 Cooling & modes: interactive fan curve editor (drag + keyboard + numeric steppers),
    /// performance modes, SPL/sPPT/fPPT limits, CPU boost, and AMD undervolt.
    /// </summary>
    public sealed class CoolingPage : SignalPage
    {
        public override string Id => "cooling";
        public override string GroupId => "tuning";
        public override string Title => SignalText.T("Cooling & modes", "Охлаждение и режимы");
        public override bool CanApply => dirty;
        public override bool CanReset => true;

        private sealed class PowerLimitDraft
        {
            public string Key = "", Label = "", Unit = "W";
            public int Min, Max, Def, Step = 1, Draft;
            public bool Visible, IsTemp;
        }

        private readonly List<AsusFan> fans = new();
        private readonly Dictionary<AsusFan, byte[]> curves = new();
        private AsusFan activeFan = AsusFan.CPU;
        private int selectedPoint = 0;
        private bool customCurveEnabled;
        private bool clamp = AppConfig.Is("fan_clamp");
        private int hystUp = -1, hystDown = -1;
        private bool hystSupported;

        private readonly PowerLimitDraft total = new() { Key = "limit_total" };
        private readonly PowerLimitDraft slow = new() { Key = "limit_slow" };
        private readonly PowerLimitDraft fast = new() { Key = "limit_fast" };
        private readonly PowerLimitDraft cpu = new() { Key = "limit_cpu" };
        private readonly PowerLimitDraft cross = new() { Key = "limit_crossload" };
        private readonly PowerLimitDraft gpucpu = new() { Key = "limit_gpucpu", Step = AsusACPI.StepGPUtoCPU };
        private readonly PowerLimitDraft cputemp = new() { Key = "limit_cputemp", Unit = "°C", IsTemp = true };
        private PowerLimitDraft[] AllLimits => new[] { total, slow, fast, cpu, cross, gpucpu, cputemp };

        private int boost = -1;
        private string powerMode = "";
        private bool batterySaver;
        private bool pawnAvailable, pawnInstalled;
        private int uv, uvIgpu, uvTemp;
        private bool dirty;
        private float lastContentH = 900f;

        private static readonly string[] BoostNames =
        {
            "Disabled", "Enabled", "Aggressive", "Efficient Enabled", "Efficient Aggressive", "Aggressive at Guaranteed", "Efficient at Guaranteed",
        };

        public override void OnEnter(SignalPaintContext? ctx)
        {
            if (!dirty) LoadFromBackend();
        }

        private void LoadFromBackend()
        {
            fans.Clear();
            fans.Add(AsusFan.CPU);
            fans.Add(AsusFan.GPU);
            try
            {
                if (!AsusACPI.IsEmptyCurve(Program.acpi.GetFanCurve(AsusFan.Mid)) || Program.acpi.IsMidFanSupported())
                {
                    AppConfig.Set("mid_fan", 1);
                    fans.Add(AsusFan.Mid);
                }
                if (Program.acpi.IsXGConnected() || XGM.IsConnected())
                {
                    AppConfig.Set("xgm_fan", 1);
                    fans.Add(AsusFan.XGM);
                }
            }
            catch { }
            if (!fans.Contains(activeFan)) activeFan = AsusFan.CPU;

            curves.Clear();
            foreach (var f in fans) curves[f] = LoadCurve(f, factoryReset: false);
            customCurveEnabled = AppConfig.IsApplyFans();

            try
            {
                var d = Program.acpi.GetFanHysteresis();
                hystSupported = d.up >= 0 && d.down >= 0;
                if (hystSupported)
                {
                    int up = AppConfig.GetMode("hysteresis_up");
                    int down = AppConfig.GetMode("hysteresis_down");
                    hystUp = Math.Clamp(up < 0 ? (d.up > 0 ? d.up : 3) : up, 1, 5);
                    hystDown = Math.Clamp(down < 0 ? (d.down > 0 ? d.down : 3) : down, 1, 5);
                }
            }
            catch { hystSupported = false; }

            var acpi = Program.acpi;
            bool modeA = (acpi?.IsSupported(AsusACPI.PPT_APUA0) ?? false) || CpuInfo.IsAMD;
            bool modeB0 = acpi?.IsAllAmdPPT() ?? false;
            bool modeC1 = acpi?.IsSupported(AsusACPI.PPT_APUC1) ?? false;

            total.Visible = modeA; total.Min = AsusACPI.MinTotal; total.Max = AsusACPI.MaxTotal; total.Def = AsusACPI.DefaultTotal;
            cpu.Visible = modeB0; cpu.Min = AsusACPI.MinCPU; cpu.Max = AsusACPI.MaxCPU; cpu.Def = AsusACPI.DefaultCPU;
            slow.Visible = modeA && !modeB0; slow.Min = AsusACPI.MinTotal; slow.Max = AsusACPI.MaxTotal;
            fast.Visible = modeA && !modeB0 && CpuInfo.IsAMD && modeC1; fast.Min = AsusACPI.MinTotal; fast.Max = AsusACPI.MaxTotal;
            cross.Visible = acpi?.IsSupported(AsusACPI.PPT_CROSS9F) ?? false; cross.Min = AsusACPI.MinCrossLoad; cross.Max = AsusACPI.MaxCrossLoad; cross.Def = AsusACPI.MaxCrossLoad;
            gpucpu.Visible = acpi?.IsSupported(AsusACPI.PPT_GPUCPU9C) ?? false; gpucpu.Min = AsusACPI.MinGPUtoCPU; gpucpu.Max = AsusACPI.MaxGPUtoCPU; gpucpu.Def = AsusACPI.MaxGPUtoCPU;
            cputemp.Visible = acpi?.IsSupported(AsusACPI.PPT_TEMP9E) ?? false; cputemp.Min = AsusACPI.MinCPUTemp; cputemp.Max = AsusACPI.MaxCPUTemp; cputemp.Def = AsusACPI.MaxCPUTemp;

            if (modeB0) { total.Label = "Platform (CPU + GPU)"; cpu.Label = "CPU"; }
            else if (CpuInfo.IsAMD) { total.Label = "Sustained power · SPL"; slow.Label = "Slow boost · sPPT"; fast.Label = "Fast boost · fPPT"; }
            else { total.Label = "Sustained power · PL1"; slow.Label = "Boost · PL2"; }
            cross.Label = SignalText.T("Cross-load", "Cross-load");
            gpucpu.Label = SignalText.T("GPU → CPU shift", "Сдвиг GPU → CPU");
            cputemp.Label = SignalText.T("CPU temp limit", "Лимит температуры CPU");

            int lt = Math.Clamp(AppConfig.GetMode("limit_total", AsusACPI.DefaultTotal), total.Min, total.Max);
            total.Draft = lt;
            slow.Draft = Math.Clamp(AppConfig.GetMode("limit_slow", lt), slow.Min, slow.Max);
            fast.Draft = Math.Clamp(AppConfig.GetMode("limit_fast", lt), fast.Min, fast.Max);
            cpu.Draft = Math.Clamp(AppConfig.GetMode("limit_cpu", AsusACPI.DefaultCPU), cpu.Min, cpu.Max);
            cross.Draft = Math.Clamp(AppConfig.GetMode("limit_crossload", cross.Def), cross.Min, cross.Max);
            gpucpu.Draft = Math.Clamp(AppConfig.GetMode("limit_gpucpu", gpucpu.Def), gpucpu.Min, gpucpu.Max);
            cputemp.Draft = Math.Clamp(AppConfig.GetMode("limit_cputemp", cputemp.Def), cputemp.Min, cputemp.Max);

            try { boost = PowerNative.GetCPUBoost(); } catch { boost = -1; }
            try { powerMode = PowerNative.GetPowerMode(); batterySaver = PowerNative.GetBatterySaverStatus(); } catch { }

            try
            {
                pawnAvailable = ModeControl.IsPawnAvailable();
                pawnInstalled = pawnAvailable || ModeControl.IsPawnInstalled();
                uv = Math.Clamp(AppConfig.GetMode("cpu_uv", 0), CpuInfo.MinCPUUV, CpuInfo.MaxCPUUV);
                uvIgpu = Math.Clamp(AppConfig.GetMode("igpu_uv", 0), CpuInfo.MinIGPUUV, CpuInfo.MaxIGPUUV);
                int t = AppConfig.GetMode("cpu_temp");
                uvTemp = (t < CpuInfo.MinTemp || t > CpuInfo.DefaultTemp) ? CpuInfo.DefaultTemp : t;
            }
            catch { }

            dirty = false;
        }

        private static byte[] LoadCurve(AsusFan device, bool factoryReset)
        {
            byte[] curve = AppConfig.GetFanConfig(device);
            if (factoryReset || AsusACPI.IsInvalidCurve(curve))
            {
                curve = Program.acpi?.GetFanCurve(device, Modes.GetCurrentBase()) ?? Array.Empty<byte>();
                if (AsusACPI.IsInvalidCurve(curve)) curve = AppConfig.GetDefaultCurve(device);
                curve = AsusACPI.FixFanCurve(curve);
            }
            return FanCurveValidator.Normalize(curve);
        }

        public override void Reset()
        {
            // Reversible before Apply: reload factory curves & reset draft limits
            foreach (var f in fans) curves[f] = LoadCurve(f, factoryReset: true);
            customCurveEnabled = false;
            foreach (var l in AllLimits) l.Draft = l.Def > 0 ? Math.Clamp(l.Def, l.Min, l.Max) : AsusACPI.DefaultTotal;
            dirty = true;
        }

        public override void Apply()
        {
            foreach (var f in fans)
            {
                if (curves.TryGetValue(f, out var cv) && FanCurveValidator.IsValidMonotonic(cv))
                    AppConfig.SetFanConfig(f, cv);
            }
            AppConfig.SetMode("auto_apply", customCurveEnabled ? 1 : 0);

            if (AllLimits.Any(l => l.Visible))
            {
                AppConfig.SetMode("limit_total", total.Draft);
                AppConfig.SetMode("limit_slow", slow.Draft);
                AppConfig.SetMode("limit_cpu", cpu.Draft);
                AppConfig.SetMode("limit_fast", fast.Draft);
                if (cross.Visible) AppConfig.SetMode("limit_crossload", cross.Draft);
                if (gpucpu.Visible) AppConfig.SetMode("limit_gpucpu", gpucpu.Draft);
                if (cputemp.Visible) AppConfig.SetMode("limit_cputemp", cputemp.Draft);
                AppConfig.SetMode("auto_apply_power", 1);
            }

            if (hystSupported)
            {
                AppConfig.SetMode("hysteresis_up", hystUp);
                AppConfig.SetMode("hysteresis_down", hystDown);
            }

            dirty = false;
            Task.Run(() =>
            {
                Program.modeControl.AutoFans(force: customCurveEnabled);
                Program.modeControl.AutoPower(true);
                if (hystSupported) Program.acpi.SetFanHysteresis(hystUp, hystDown);
            });
        }

        public override float ContentHeightDip(SignalPaintContext ctx) => lastContentH;

        public override void Paint(SignalPaintContext ctx)
        {
            var body = ctx.Layout.WorkspaceBody;
            float y = body.Y - ctx.ScrollDip;
            float x = body.X;
            float w = body.Width;

            // ---- Top Panel: Fan Curve Editor ---------------------------------------------------
            float curvePanelH = 344f;
            var curvePanel = new RectangleF(x, y, w, curvePanelH);
            ctx.DrawPanel(curvePanel);

            float pad = 24f;
            string curveTitle = SignalText.T("Fan curve", "Кривая вентилятора") + (customCurveEnabled ? " · Custom" : "");
            ctx.Painter.DrawText(ctx.G, curveTitle,
                ctx.Px(x + pad, y + 16f, w * 0.45f, 26f),
                ctx.Painter.Font(18f, ctx.Scale, FontStyle.Bold),
                ctx.Theme.Text,
                ctx.Rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft);

            // Fan tabs (CPU / GPU / System)
            float tabW = 96f;
            float tabGap = 8f;
            float tabsTotalW = fans.Count * tabW + Math.Max(0, fans.Count - 1) * tabGap;
            float tabX = ctx.Rtl ? (x + pad) : (x + w - pad - tabsTotalW);
            for (int i = 0; i < fans.Count; i++)
            {
                var f = fans[i];
                string fname = f switch
                {
                    AsusFan.CPU => "CPU",
                    AsusFan.GPU => "GPU",
                    AsusFan.Mid => SignalText.T("System", "Система"),
                    _ => "XGM",
                };
                ctx.DrawButton("cooling:tab:" + (int)f, new RectangleF(tabX, y + 12f, tabW, 34f), fname,
                    _ => { activeFan = f; selectedPoint = 0; }, selected: activeFan == f);
                tabX += tabW + tabGap;
            }

            var plotDip = new RectangleF(x + pad, y + 58f, w - pad * 2f, 214f);
            PaintCurvePlot(ctx, plotDip);

            // Numeric point editor bar & custom curve toggle
            float barY = plotDip.Bottom + 14f;
            if (curves.TryGetValue(activeFan, out var cur) && selectedPoint >= 0 && selectedPoint < FanCurveValidator.PointCount)
            {
                int ptT = cur[selectedPoint];
                int ptP = cur[selectedPoint + FanCurveValidator.PointCount];
                string ptLabel = $"{SignalText.T("Point", "Точка")} {selectedPoint + 1}: {ptT}°C · {ptP}%";
                ctx.Painter.DrawText(ctx.G, ptLabel, ctx.Px(x + pad, barY + 6f, 220f, 24f),
                    ctx.Painter.Font(13f, ctx.Scale, FontStyle.Bold), ctx.Theme.Accent, GlassPainter.FmtLeft);

                float sx = x + pad + 210f;
                ctx.DrawButton("cooling:pt:tminus", new RectangleF(sx, barY, 44f, 32f), "T−", _ => StepSelectedPoint(-1, 0), fontSize: 12f);
                ctx.DrawButton("cooling:pt:tplus", new RectangleF(sx + 50f, barY, 44f, 32f), "T+", _ => StepSelectedPoint(1, 0), fontSize: 12f);
                ctx.DrawButton("cooling:pt:pminus", new RectangleF(sx + 106f, barY, 52f, 32f), "%−", _ => StepSelectedPoint(0, -5), fontSize: 12f);
                ctx.DrawButton("cooling:pt:pplus", new RectangleF(sx + 164f, barY, 52f, 32f), "%+", _ => StepSelectedPoint(0, 5), fontSize: 12f);
            }

            ctx.DrawButton("cooling:rpm", new RectangleF(x + w - pad - 246f, barY, 108f, 32f),
                FanSensorControl.fanRpm ? "RPM" : "%", _ => FanSensorControl.fanRpm = !FanSensorControl.fanRpm, selected: FanSensorControl.fanRpm, fontSize: 12f);
            ctx.DrawButton("cooling:custom", new RectangleF(x + w - pad - 130f, barY, 130f, 32f),
                customCurveEnabled ? "Custom: " + SignalText.T("On", "Вкл") : "Custom: " + SignalText.T("Off", "Выкл"),
                _ => { customCurveEnabled = !customCurveEnabled; dirty = true; }, selected: customCurveEnabled, fontSize: 12f);

            y += curvePanelH + 20f;

            // ---- Bottom Panel: Modes, Power Limits, Boost, Undervolt ---------------------------
            var visibleLimits = AllLimits.Where(l => l.Visible).ToList();
            int rowCount = 3 + visibleLimits.Count + (CpuInfo.IsAMD ? 1 : 0) + (RecommendedProfiles.IsAvailable ? 1 : 0);
            float settingsPanelH = rowCount * 74f + 32f;
            ctx.DrawPanel(new RectangleF(x, y, w, settingsPanelH));

            float ry = y + 16f;
            float rx = x + pad;
            float rw = w - pad * 2f;

            // Active performance mode
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Performance mode", "Режим работы"),
                "Silent / Balanced / Turbo / Custom",
                Modes.GetCurrentName(),
                "cooling:mode",
                pt => ctx.ShowMenu(pt, Modes.GetList().Select(m => (Modes.GetName(m), m == Modes.GetCurrent(), (string?)null, (Action)(() =>
                {
                    Program.modeControl.SetPerformanceMode(m, true);
                    LoadFromBackend();
                })))),
                showChevron: true);

            if (RecommendedProfiles.IsAvailable)
            {
                ry = ctx.DrawSettingRow(ry, rx, rw,
                    SignalText.T("Recommended profiles", "Рекомендуемые профили"),
                    SignalText.T("Tuned limits and curves for this model", "Готовые лимиты и кривые для модели"),
                    RecommendedProfiles.HasSaved ? SignalText.T("Saved", "Сохранённые") : SignalText.T("Built-in", "Встроенные"),
                    "cooling:recommended",
                    pt => ctx.ShowMenu(pt, BuildRecommendedMenu()),
                    showChevron: true);
            }

            foreach (var lim in visibleLimits)
            {
                var l = lim;
                string v = l.Draft + " " + l.Unit;
                ry = ctx.DrawSettingRow(ry, rx, rw,
                    l.Label,
                    $"{l.Min}–{l.Max} {l.Unit}",
                    v,
                    "cooling:lim:" + l.Key,
                    pt => ShowLimitStepMenu(ctx, pt, l),
                    showChevron: true);
            }

            string boostName = boost >= 0 && boost < BoostNames.Length ? BoostNames[boost] : SignalText.T("Not supported", "Не поддерживается");
            ry = ctx.DrawSettingRow(ry, rx, rw,
                "CPU boost",
                SignalText.T("Processor boost policy", "Политика турбобуста процессора"),
                boostName,
                "cooling:boost",
                pt => ctx.ShowMenu(pt, BoostNames.Select((n, idx) => (n, idx == boost, (string?)null, (Action)(() =>
                {
                    PowerNative.SetCPUBoost(idx);
                    AppConfig.SetMode("auto_boost", idx);
                    boost = idx;
                })))),
                enabled: boost >= 0,
                readOnly: boost < 0,
                showChevron: boost >= 0);

            string pmText = batterySaver
                ? SignalText.T("Battery saver", "Экономия заряда")
                : PowerNative.powerModes.TryGetValue(powerMode, out var pmn) ? pmn : "—";
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Windows power mode", "Режим питания Windows"),
                SignalText.T("Overlay applied with the active mode", "Применяется при активации режима"),
                pmText,
                "cooling:winpower",
                pt => ctx.ShowMenu(pt, PowerNative.powerModes.Select(kv => (kv.Value, kv.Key == powerMode, (string?)null, (Action)(() =>
                {
                    PowerNative.SetPowerMode(kv.Key);
                    if (PowerNative.GetDefaultPowerMode(Modes.GetCurrentBase()) != kv.Key) AppConfig.SetMode("powermode", kv.Key);
                    else AppConfig.RemoveMode("powermode");
                    powerMode = kv.Key;
                })))),
                enabled: !batterySaver,
                showChevron: !batterySaver,
                drawDivider: CpuInfo.IsAMD);

            if (CpuInfo.IsAMD)
            {
                bool uvOk = pawnAvailable && CpuInfo.IsSupportedUV();
                string uvVal = !pawnInstalled
                    ? SignalText.T("PawnIO missing", "Нет PawnIO")
                    : !uvOk ? SignalText.T("Not supported", "Не поддерживается")
                    : $"{uv} CO";
                ry = ctx.DrawSettingRow(ry, rx, rw,
                    SignalText.T("Undervolt", "Андервольт"),
                    uvOk ? $"{CpuInfo.MinCPUUV}..{CpuInfo.MaxCPUUV}" : SignalText.T("Unavailable on this CPU", "Недоступно на этом процессоре"),
                    uvVal,
                    "cooling:uv",
                    uvOk ? pt => ShowUvMenu(ctx, pt) : null,
                    enabled: uvOk,
                    readOnly: !uvOk,
                    showChevron: uvOk,
                    drawDivider: false);
            }

            lastContentH = (y + ctx.ScrollDip + settingsPanelH + 16f) - body.Y;
        }

        private void PaintCurvePlot(SignalPaintContext ctx, RectangleF plotDip)
        {
            var th = ctx.Theme;
            float sc = ctx.Scale;
            var plotPx = ctx.Px(plotDip);

            using (var bgPath = GlassPainter.Rounded(plotPx, SignalTheme.RadiusPlot * sc))
            using (var bgBrush = new SolidBrush(th.Plot))
                ctx.G.FillPath(bgBrush, bgPath);

            float padL = 42f * sc, padR = 16f * sc, padT = 14f * sc, padB = 26f * sc;
            var inner = new RectangleF(plotPx.X + padL, plotPx.Y + padT,
                Math.Max(40f, plotPx.Width - padL - padR), Math.Max(40f, plotPx.Height - padT - padB));

            var axFont = ctx.Painter.Font(10.5f, sc);
            using var axBrush = new SolidBrush(th.Muted);
            using var gridPen = new Pen(Color.FromArgb(75, th.Line), Math.Max(1f, sc));

            for (int p = 0; p <= 100; p += 20)
            {
                float yy = inner.Bottom - inner.Height * p / 100f;
                ctx.G.DrawLine(gridPen, inner.Left, yy, inner.Right, yy);
                ctx.G.DrawString(p.ToString(), axFont, axBrush, inner.Left - 8f * sc, yy - axFont.Height / 2f, GlassPainter.FmtRight);
            }
            for (int t = 20; t <= 110; t += 10)
            {
                float xx = inner.Left + inner.Width * (t - 20f) / 90f;
                ctx.G.DrawLine(gridPen, xx, inner.Top, xx, inner.Bottom);
                ctx.G.DrawString(t + "°C", axFont, axBrush, xx, inner.Bottom + 5f * sc, GlassPainter.FmtCenter);
            }

            if (!curves.TryGetValue(activeFan, out var cur)) return;
            Color lineCol = activeFan == AsusFan.GPU ? th.Gpu : th.Accent;

            var pts = new PointF[FanCurveValidator.PointCount];
            for (int i = 0; i < FanCurveValidator.PointCount; i++)
            {
                float xx = inner.Left + inner.Width * (cur[i] - 20f) / 90f;
                float yy = inner.Bottom - inner.Height * cur[i + FanCurveValidator.PointCount] / 100f;
                pts[i] = new PointF(xx, yy);
            }

            using (var fill = new GraphicsPath())
            {
                fill.AddLine(pts[0].X, inner.Bottom, pts[0].X, pts[0].Y);
                fill.AddLines(pts);
                fill.AddLine(pts[^1].X, pts[^1].Y, pts[^1].X, inner.Bottom);
                fill.CloseFigure();
                using var fb = new SolidBrush(Color.FromArgb(28, lineCol));
                ctx.G.FillPath(fb, fill);
            }
            using (var pen = new Pen(lineCol, Math.Max(2f, 2.5f * sc)) { LineJoin = LineJoin.Round })
                ctx.G.DrawLines(pen, pts);

            float nodeR = 6.5f * sc;
            for (int i = 0; i < FanCurveValidator.PointCount; i++)
            {
                int idx = i;
                bool sel = idx == selectedPoint;
                string ptId = "cooling:pt:" + idx;
                var nr = new RectangleF(pts[i].X - nodeR, pts[i].Y - nodeR, nodeR * 2f, nodeR * 2f);
                using (var nb = new SolidBrush(sel ? th.Text : th.Surface)) ctx.G.FillEllipse(nb, nr);
                using (var np = new Pen(lineCol, Math.Max(2f, 2.2f * sc))) ctx.G.DrawEllipse(np, nr);

                var hit = new RectangleF(pts[i].X - 12f * sc, pts[i].Y - 12f * sc, 24f * sc, 24f * sc);
                if (ctx.IsFocused(ptId)) GlassPainter.DrawFocusRing(ctx.G, hit, sc, th, false, 12f);

                ctx.Register(new UiElement
                {
                    Id = ptId,
                    BoundsPx = hit,
                    Name = $"{SignalText.T("Point", "Точка")} {idx + 1}",
                    Value = $"{cur[idx]}°C, {cur[idx + FanCurveValidator.PointCount]}%",
                    Role = AccessibleRole.Slider,
                    IsSlider = true,
                    SliderTrackPx = inner,
                    RadiusDip = 12f,
                    OnStep = delta => { selectedPoint = idx; StepSelectedPoint(0, delta * 5); },
                    OnActivate = pt =>
                    {
                        selectedPoint = idx;
                        ctx.StartSliderDrag((ptId, (tx, ty, _) =>
                        {
                            int targetPwm = (int)Math.Round(Math.Clamp(1f - ty, 0f, 1f) * 100f);
                            int targetT = (int)Math.Round(20f + tx * 90f);
                            if (curves.TryGetValue(activeFan, out var arr))
                            {
                                FanCurveValidator.SetPoint(arr, idx, targetT, targetPwm, clamp);
                                dirty = true;
                            }
                        }, inner));
                    },
                });
            }
        }

        private void StepSelectedPoint(int deltaT, int deltaPwm)
        {
            if (!curves.TryGetValue(activeFan, out var arr) || selectedPoint < 0 || selectedPoint >= FanCurveValidator.PointCount)
                return;
            int t = arr[selectedPoint] + deltaT;
            int p = arr[selectedPoint + FanCurveValidator.PointCount] + deltaPwm;
            FanCurveValidator.SetPoint(arr, selectedPoint, t, p, clamp);
            dirty = true;
        }

        private void ShowLimitStepMenu(SignalPaintContext ctx, Point pt, PowerLimitDraft l)
        {
            var values = new List<int>();
            int step = Math.Max(5, (l.Max - l.Min) / 10);
            for (int v = l.Min; v <= l.Max; v += step) values.Add(v);
            if (!values.Contains(l.Draft)) values.Add(l.Draft);
            if (!values.Contains(l.Max)) values.Add(l.Max);
            values.Sort();

            ctx.ShowMenu(pt, values.Select(v => ($"{v} {l.Unit}", v == l.Draft, (string?)null, (Action)(() =>
            {
                l.Draft = v;
                dirty = true;
            }))));
        }

        private void ShowUvMenu(SignalPaintContext ctx, Point pt)
        {
            var items = new List<(string, bool, string?, Action)>();
            for (int v = CpuInfo.MaxCPUUV; v >= CpuInfo.MinCPUUV; v -= 5)
            {
                int val = v;
                items.Add(($"{val} CO", val == uv, null, () =>
                {
                    uv = val;
                    AppConfig.SetMode("cpu_uv", val);
                    Program.modeControl.SetRyzen(true);
                }));
            }
            ctx.ShowMenu(pt, items);
        }

        private IEnumerable<(string, bool, string?, Action)> BuildRecommendedMenu()
        {
            yield return (RecommendedProfiles.HasSaved
                ? SignalText.T("Apply recommended (saved)", "Применить рекомендуемые (сохранённые)")
                : SignalText.T("Apply recommended", "Применить рекомендуемые"),
                false, null, () => { RecommendedProfiles.Apply(); LoadFromBackend(); });
            yield return (SignalText.T("Save current as recommended", "Сохранить текущие как рекомендуемые"),
                false, null, RecommendedProfiles.SaveCurrent);
            if (RecommendedProfiles.HasSaved)
                yield return (SignalText.T("Back to built-in recommended", "Вернуть встроенные рекомендуемые"),
                    false, null, RecommendedProfiles.ForgetSaved);
        }
    }
}
