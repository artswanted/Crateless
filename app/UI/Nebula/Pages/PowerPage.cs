using GHelper.Mode;
using PawnIO;
using System.Diagnostics;

namespace GHelper.UI.Nebula.Pages
{
    /// <summary>Power page (design kit screen "power"): mode, CPU limits (draft/apply), boost, Windows power mode, undervolt.</summary>
    public sealed class PowerPage : NebulaPage
    {
        public override string Id => "power";
        public override string Title => NebulaText.T("Power", "Мощность");
        public override string Subtitle => NebulaText.T("Choose how your laptop behaves.", "Выбери характер работы ноутбука.");

        private sealed class Limit
        {
            public string Key = "", Label = "", Unit = "W";
            public int Min, Max, Def, Step = 1;
            public int Draft;
            public bool Visible;
            public bool Temp;
        }

        private readonly Limit total = new() { Key = "limit_total" };
        private readonly Limit slow = new() { Key = "limit_slow" };
        private readonly Limit fast = new() { Key = "limit_fast" };
        private readonly Limit cpu = new() { Key = "limit_cpu" };
        private readonly Limit cross = new() { Key = "limit_crossload" };
        private readonly Limit gpucpu = new() { Key = "limit_gpucpu", Step = AsusACPI.StepGPUtoCPU };
        private readonly Limit cputemp = new() { Key = "limit_cputemp", Unit = "°C", Temp = true };
        private Limit[] All => new[] { total, slow, fast, cpu, cross, gpucpu, cputemp };

        private bool dirty;
        private int boost = -1;
        private string powerMode = "";
        private bool batterySaver;

        // undervolt
        private bool pawnInstalled, pawnAvailable;
        private int uv, uvIgpu, uvTemp;
        private bool uvDirty;
        private string uvResult = "";

        private static readonly string[] BoostNames =
        {
            "Disabled", "Enabled", "Aggressive", "Efficient Enabled", "Efficient Aggressive", "Aggressive at Guaranteed", "Efficient at Guaranteed",
        };

        public override void Refresh(bool opened)
        {
            if (!opened && dirty) return;
            LoadLimits();
            try { boost = PowerNative.GetCPUBoost(); } catch { boost = -1; }
            try { powerMode = PowerNative.GetPowerMode(); batterySaver = PowerNative.GetBatterySaverStatus(); } catch { }
            if (opened) LoadUv();
        }

        private void LoadLimits()
        {
            var acpi = Program.acpi;
            bool modeA = acpi.IsSupported(AsusACPI.PPT_APUA0) || CpuInfo.IsAMD;
            bool modeB0 = acpi.IsAllAmdPPT();
            bool modeC1 = acpi.IsSupported(AsusACPI.PPT_APUC1);

            total.Visible = modeA; total.Min = AsusACPI.MinTotal; total.Max = AsusACPI.MaxTotal; total.Def = AsusACPI.DefaultTotal;
            cpu.Visible = modeB0; cpu.Min = AsusACPI.MinCPU; cpu.Max = AsusACPI.MaxCPU; cpu.Def = AsusACPI.DefaultCPU;
            slow.Visible = modeA && !modeB0; slow.Min = AsusACPI.MinTotal; slow.Max = AsusACPI.MaxTotal;
            fast.Visible = modeA && !modeB0 && CpuInfo.IsAMD && modeC1; fast.Min = AsusACPI.MinTotal; fast.Max = AsusACPI.MaxTotal;
            cross.Visible = acpi.IsSupported(AsusACPI.PPT_CROSS9F); cross.Min = AsusACPI.MinCrossLoad; cross.Max = AsusACPI.MaxCrossLoad; cross.Def = AsusACPI.MaxCrossLoad;
            gpucpu.Visible = acpi.IsSupported(AsusACPI.PPT_GPUCPU9C); gpucpu.Min = AsusACPI.MinGPUtoCPU; gpucpu.Max = AsusACPI.MaxGPUtoCPU; gpucpu.Def = AsusACPI.MaxGPUtoCPU;
            cputemp.Visible = acpi.IsSupported(AsusACPI.PPT_TEMP9E); cputemp.Min = AsusACPI.MinCPUTemp; cputemp.Max = AsusACPI.MaxCPUTemp; cputemp.Def = AsusACPI.MaxCPUTemp;

            if (modeB0) { total.Label = "Platform (CPU + GPU)"; cpu.Label = "CPU"; }
            else if (CpuInfo.IsAMD) { total.Label = "SPL · " + NebulaText.T("sustained", "длительная"); slow.Label = "sPPT · " + NebulaText.T("long boost", "долгий буст"); fast.Label = "fPPT · " + NebulaText.T("short boost", "короткий буст"); }
            else { total.Label = "PL1 · " + NebulaText.T("sustained", "длительная"); slow.Label = "PL2 · " + NebulaText.T("boost", "буст"); }
            cross.Label = NebulaText.T("Cross-load (CPU under GPU load)", "Cross-load (CPU при нагрузке GPU)");
            gpucpu.Label = NebulaText.T("GPU → CPU power shift", "Перенос мощности GPU → CPU");
            cputemp.Label = NebulaText.T("CPU temperature limit", "Лимит температуры CPU");

            int lt = Math.Clamp(AppConfig.GetMode("limit_total", AsusACPI.DefaultTotal), total.Min, total.Max);
            total.Draft = lt;
            slow.Draft = Math.Clamp(AppConfig.GetMode("limit_slow", lt), slow.Min, slow.Max);
            fast.Draft = Math.Clamp(AppConfig.GetMode("limit_fast", lt), fast.Min, fast.Max);
            cpu.Draft = Math.Clamp(AppConfig.GetMode("limit_cpu", AsusACPI.DefaultCPU), cpu.Min, cpu.Max);
            cross.Draft = Math.Clamp(AppConfig.GetMode("limit_crossload", cross.Def), cross.Min, cross.Max);
            gpucpu.Draft = Math.Clamp(AppConfig.GetMode("limit_gpucpu", gpucpu.Def), gpucpu.Min, gpucpu.Max);
            cputemp.Draft = Math.Clamp(AppConfig.GetMode("limit_cputemp", cputemp.Def), cputemp.Min, cputemp.Max);
            dirty = false;
        }

        private void LoadUv()
        {
            pawnAvailable = ModeControl.IsPawnAvailable();
            pawnInstalled = pawnAvailable || ModeControl.IsPawnInstalled();
            uv = Math.Clamp(AppConfig.GetMode("cpu_uv", 0), CpuInfo.MinCPUUV, CpuInfo.MaxCPUUV);
            uvIgpu = Math.Clamp(AppConfig.GetMode("igpu_uv", 0), CpuInfo.MinIGPUUV, CpuInfo.MaxIGPUUV);
            int t = AppConfig.GetMode("cpu_temp");
            uvTemp = (t < CpuInfo.MinTemp || t > CpuInfo.DefaultTemp) ? CpuInfo.DefaultTemp : t;
            uvDirty = false;
        }

        private bool AnyLimit => All.Any(l => l.Visible);

        public override void Paint(NebulaCanvas c)
        {
            var th = c.Theme;

            // ---- mode ------------------------------------------------------------------------------
            c.Surface(236, 139, 818, 144);
            c.Txt(NebulaText.T("Performance mode", "Режим работы"), 256, 169, c.F(15, FontStyle.Bold), th.Text);
            c.Txt(NebulaText.T("Limits are stored separately for each mode.", "Параметры сохраняются отдельно для каждого режима."), 256, 190, c.F(11), th.Muted);
            var list = Modes.GetList();
            int current = Modes.GetCurrent();
            float mx = 256; float mw = Math.Min(145, (778 - 12 * (list.Count - 1)) / Math.Max(1, list.Count));
            foreach (int m in list)
            {
                c.Segment(mx, 211, mw, 36, Modes.GetName(m), m == current, "power:mode:" + m);
                mx += mw + 12;
            }
            c.Button(1034 - 36, 211, 36, 36, "+", "power:mode:add", primary: false);

            // ---- limits ----------------------------------------------------------------------------
            var visible = All.Where(l => l.Visible).ToList();
            float rows = Math.Max(1, visible.Count);
            float limitsH = 120 + rows * 74 + 40;
            c.Surface(236, 299, 504, limitsH);
            c.Txt(NebulaText.T("CPU limits", "Лимиты процессора"), 256, 329, c.F(15, FontStyle.Bold), th.Text);
            string state = !AnyLimit ? NebulaText.T("Not supported on this model.", "На этой модели недоступно.")
                         : dirty ? NebulaText.T("Draft · changes not applied yet", "Черновик · изменения ещё не применены")
                         : AppConfig.IsApplyPower() ? NebulaText.T("Applied with the mode", "Применяются вместе с режимом")
                         : NebulaText.T("Firmware defaults (custom limits off)", "Заводские значения (свои лимиты выключены)");
            c.Txt(state, 256, 350, c.F(11), dirty ? th.Warning : th.Muted);

            float y = 390;
            foreach (var l in visible)
            {
                c.Txt(l.Label, 256, y, c.F(12), th.Muted);
                string v = l.Temp ? l.Draft + " °C" : l.Draft + " " + NebulaText.Watt;
                c.Txt(v, 720, y, c.F(14, FontStyle.Bold), th.Text, StringAlignment.Far);
                c.Slider(256, y + 18, 464, (l.Draft - l.Min) / (float)Math.Max(1, l.Max - l.Min), "slider:power:" + l.Key);
                y += 74;
            }

            float footY = 299 + limitsH - 52;
            c.Txt(NebulaText.T("Apply automatically", "Применять автоматически"), 256, footY + 22, c.F(12), th.Muted);
            c.Toggle(430, footY + 5, AppConfig.IsApplyPower(), "power:auto", AnyLimit);
            c.Button(505, footY, 100, 36, NebulaText.T("Reset", "Сбросить"), "power:reset", primary: false, enabled: AnyLimit);
            c.Button(617, footY, 103, 36, NebulaText.T("Apply", "Применить"), "power:apply", primary: true, enabled: dirty);

            // ---- windows -----------------------------------------------------------------------------
            c.Surface(756, 299, 298, limitsH);
            c.Txt(NebulaText.T("Windows and boost", "Windows и boost"), 776, 329, c.F(15, FontStyle.Bold), th.Text);

            c.Txt("CPU Boost", 778, 372, c.F(12), th.Muted);
            string boostName = boost >= 0 && boost < BoostNames.Length ? BoostNames[boost] : "—";
            DropRow(c, 778, 384, 254, boostName, "power:boost", boost >= 0);
            c.Txt(NebulaText.T("Values and names depend on the platform.", "Значения и названия зависят от платформы."), 778, 444, c.F(10), th.Faint);

            c.Txt(NebulaText.T("Windows power mode", "Режим питания Windows"), 778, 490, c.F(12), th.Muted);
            string pm = PowerNative.powerModes.TryGetValue(powerMode, out var pmn) ? pmn : "—";
            if (batterySaver) pm = NebulaText.T("Battery saver", "Экономия заряда");
            DropRow(c, 778, 502, 254, pm, "power:powermode", !batterySaver);
            string def = PowerNative.powerModes.TryGetValue(PowerNative.GetDefaultPowerMode(Modes.GetCurrentBase()), out var dn) ? dn : "";
            c.Txt(NebulaText.T("Default for this mode: ", "По умолчанию для режима: ") + def, 778, 562, c.F(10), th.Faint);

            bool onAc = Gpu.GPUModeControl.IsPlugged();
            c.Txt(NebulaText.T("Auto-switch on power source", "Автопереключение по питанию"), 778, 608, c.F(12), th.Muted);
            c.Txt(onAc ? NebulaText.T("Plugged in", "От сети") : NebulaText.T("On battery", "От батареи"), 1032, 608, c.F(12, FontStyle.Bold), th.Text, StringAlignment.Far);
            c.Txt(NebulaText.T("Modes per source are set from the tray menu.", "Режим для каждого источника задаётся из меню трея."), 778, 628, c.F(10), th.Faint);

            // ---- undervolt (AMD) --------------------------------------------------------------------------
            if (!CpuInfo.IsAMD) return;
            float uy = 299 + limitsH + 16;
            c.Surface(236, uy, 818, 236);
            c.Txt(NebulaText.T("Advanced · undervolt and temperature", "Расширенные · андервольт и температура"), 256, uy + 30, c.F(15, FontStyle.Bold), th.Text);

            if (!pawnInstalled)
            {
                c.Txt(NebulaText.T("Needs the PawnIO driver for SMU access.", "Нужен драйвер PawnIO для доступа к SMU."), 256, uy + 60, c.F(12), th.Muted);
                c.Button(256, uy + 80, 220, 36, NebulaText.T("Get PawnIO  ↗", "Скачать PawnIO  ↗"), "power:pawnio", primary: false);
                return;
            }

            bool uvSupported = CpuInfo.IsSupportedUV();
            if (!uvSupported)
            {
                c.Txt(NebulaText.T("This CPU is not in the supported undervolt list.", "Этот CPU не входит в список поддержки андервольта."), 256, uy + 60, c.F(12), th.Muted);
            }
            else
            {
                float ry = uy + 70;
                c.Txt(NebulaText.T("CPU undervolt", "Андервольт CPU"), 256, ry, c.F(12), th.Muted);
                c.Txt(uv.ToString(), 720, ry, c.F(14, FontStyle.Bold), th.Text, StringAlignment.Far);
                c.Slider(256, ry + 18, 464, (uv - CpuInfo.MinCPUUV) / (float)Math.Max(1, CpuInfo.MaxCPUUV - CpuInfo.MinCPUUV), "slider:power:uv");
                ry += 70;
                if (CpuInfo.IsSupportedUViGPU())
                {
                    c.Txt(NebulaText.T("iGPU undervolt", "Андервольт iGPU"), 256, ry, c.F(12), th.Muted);
                    c.Txt(uvIgpu.ToString(), 720, ry, c.F(14, FontStyle.Bold), th.Text, StringAlignment.Far);
                    c.Slider(256, ry + 18, 464, (uvIgpu - CpuInfo.MinIGPUUV) / (float)Math.Max(1, CpuInfo.MaxIGPUUV - CpuInfo.MinIGPUUV), "slider:power:uvigpu");
                    ry += 70;
                }
                c.Txt(NebulaText.T("Temperature limit", "Лимит температуры"), 256, ry, c.F(12), th.Muted);
                c.Txt(uvTemp >= CpuInfo.DefaultTemp ? "Default" : uvTemp + " °C", 720, ry, c.F(14, FontStyle.Bold), th.Text, StringAlignment.Far);
                c.Slider(256, ry + 18, 464, (uvTemp - CpuInfo.MinTemp) / (float)Math.Max(1, CpuInfo.DefaultTemp - CpuInfo.MinTemp), "slider:power:uvtemp");
            }

            c.Txt(NebulaText.T("Apply with mode", "Применять с режимом"), 778, uy + 70, c.F(12), th.Muted);
            c.Toggle(996, uy + 53, AppConfig.IsApplyUV(), "power:uvauto", uvSupported && !uvDirty);
            c.Button(778, uy + 100, 254, 36, NebulaText.T("Apply now", "Применить сейчас"), "power:uvapply", primary: true, enabled: uvSupported);
            c.Button(778, uy + 146, 254, 36, NebulaText.T("Read current limits", "Прочитать текущие лимиты"), "power:uvread", primary: false);
            if (uvResult.Length > 0) c.Txt(uvResult, 256, uy + 220, c.F(10), th.Warning);
        }

        private static void DropRow(NebulaCanvas c, float x, float y, float w, string value, string id, bool enabled)
        {
            var th = c.Theme;
            var r = c.R(x, y, w, 36);
            c.Card(r, 8, enabled && c.IsHover(id) ? th.Line : th.Raised);
            c.Txt(value, x + 12, y + 22.5f, c.F(12, FontStyle.Bold), enabled ? th.Text : th.Faint);
            c.Txt("⌄", x + w - 16, y + 22, c.F(12, FontStyle.Bold), th.Muted, StringAlignment.Center);
            if (enabled) c.Hit(r, id);
        }

        public override void Drag(string id, float t, bool done)
        {
            switch (id)
            {
                case "slider:power:uv": uv = Round(CpuInfo.MinCPUUV + t * (CpuInfo.MaxCPUUV - CpuInfo.MinCPUUV), 1); uvDirty = true; SaveUvDraft(); return;
                case "slider:power:uvigpu": uvIgpu = Round(CpuInfo.MinIGPUUV + t * (CpuInfo.MaxIGPUUV - CpuInfo.MinIGPUUV), 1); uvDirty = true; SaveUvDraft(); return;
                case "slider:power:uvtemp": uvTemp = Round(CpuInfo.MinTemp + t * (CpuInfo.DefaultTemp - CpuInfo.MinTemp), 1); uvDirty = true; SaveUvDraft(); return;
            }
            var l = All.FirstOrDefault(x => "slider:power:" + x.Key == id);
            if (l is null) return;
            l.Draft = Math.Clamp(Round(l.Min + t * (l.Max - l.Min), l.Step), l.Min, l.Max);
            dirty = true;

            // keep the same ordering rules as the classic form
            if (l == total) { if (total.Draft > slow.Draft) slow.Draft = total.Draft; if (total.Draft > fast.Draft) fast.Draft = total.Draft; if (total.Draft < cpu.Draft) cpu.Draft = total.Draft; }
            if (l == slow) { if (slow.Draft < total.Draft) total.Draft = slow.Draft; if (slow.Draft > fast.Draft) fast.Draft = slow.Draft; }
            if (l == fast) { if (fast.Draft < slow.Draft) slow.Draft = fast.Draft; if (fast.Draft < total.Draft) total.Draft = fast.Draft; }
            if (l == cpu) { if (cpu.Draft > total.Draft) total.Draft = cpu.Draft; }
        }

        private static int Round(float v, int step) => step <= 1 ? (int)Math.Round(v) : (int)Math.Round(v / step) * step;

        private void SaveUvDraft()
        {
            AppConfig.SetMode("auto_uv", 0);
            AppConfig.SetMode("cpu_temp", uvTemp);
            AppConfig.SetMode("cpu_uv", uv);
            AppConfig.SetMode("igpu_uv", uvIgpu);
        }

        public override bool Click(string id, Point at, NebulaForm form)
        {
            if (id.StartsWith("power:mode:"))
            {
                if (id == "power:mode:add")
                {
                    int mode = Modes.Add();
                    Program.settingsForm.SetContextMenu();
                    Program.modeControl.SetPerformanceMode(mode);
                }
                else Program.modeControl.SetPerformanceMode(int.Parse(id[11..]));
                LoadLimits();
                return true;
            }

            switch (id)
            {
                case "power:apply":
                    AppConfig.SetMode("limit_total", total.Draft);
                    AppConfig.SetMode("limit_slow", slow.Draft);
                    AppConfig.SetMode("limit_cpu", cpu.Draft);
                    AppConfig.SetMode("limit_fast", fast.Draft);
                    if (cross.Visible) AppConfig.SetMode("limit_crossload", cross.Draft);
                    if (gpucpu.Visible) AppConfig.SetMode("limit_gpucpu", gpucpu.Draft);
                    if (cputemp.Visible) AppConfig.SetMode("limit_cputemp", cputemp.Draft);
                    AppConfig.SetMode("auto_apply_power", 1);
                    dirty = false;
                    Task.Run(() => Program.modeControl.AutoPower(true));
                    return true;
                case "power:reset":
                    foreach (var l in All) AppConfig.RemoveMode(l.Key);
                    AppConfig.SetMode("auto_apply_power", 0);
                    Program.modeControl.ResetPerformanceMode();
                    LoadLimits();
                    return true;
                case "power:auto":
                    AppConfig.SetMode("auto_apply_power", AppConfig.IsApplyPower() ? 0 : 1);
                    Program.modeControl.SetPerformanceMode();
                    return true;
                case "power:boost":
                    Menu(form, at, BoostNames.Select((n, i) => (n, i == boost, (Action)(() =>
                    {
                        if (AppConfig.GetMode("auto_boost") != i) PowerNative.SetCPUBoost(i);
                        AppConfig.SetMode("auto_boost", i);
                        boost = i;
                    }))));
                    return true;
                case "power:powermode":
                    Menu(form, at, PowerNative.powerModes.Select(p => (p.Value, p.Key == powerMode, (Action)(() =>
                    {
                        PowerNative.SetPowerMode(p.Key);
                        if (PowerNative.GetDefaultPowerMode(Modes.GetCurrentBase()) != p.Key) AppConfig.SetMode("powermode", p.Key);
                        else AppConfig.RemoveMode("powermode");
                        powerMode = p.Key;
                    }))));
                    return true;
                case "power:pawnio":
                    try { Process.Start(new ProcessStartInfo("https://pawnio.eu/") { UseShellExecute = true }); } catch { }
                    return true;
                case "power:uvapply":
                    uvResult = Program.modeControl.SetRyzen(true) ?? "";
                    uvDirty = false;
                    return true;
                case "power:uvread":
                    uvResult = Program.modeControl.ReadRyzenLimits() ?? "";
                    return true;
                case "power:uvauto":
                    AppConfig.SetMode("auto_uv", AppConfig.IsApplyUV() ? 0 : 1);
                    return true;
            }
            return false;
        }
    }
}
