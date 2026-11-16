using GHelper.Gpu.NVidia;
using GHelper.Helpers;
using PawnIO;

namespace GHelper.UI.SignalGlass.Pages
{
    /// <summary>
    /// 03 Graphics: Eco/Standard/Ultimate/Optimized GPU modes, NVIDIA clock/power tuning,
    /// iGPU memory allocation, and an explicit dGPU process viewer modal (no auto-termination).
    /// </summary>
    public sealed class GraphicsPage : SignalPage
    {
        public override string Id => "graphics";
        public override string GroupId => "tuning";
        public override string Title => SignalText.T("Graphics", "Графика");
        public override bool CanApply => dirty;
        public override bool CanReset => nvidia || vramDirty;

        private sealed class TuneDraft
        {
            public string Key = "", Label = "", Sub = "", Suffix = "";
            public int Min, Max, Step = 5, Draft, Confirmed;
            public bool Visible;
            public Func<int, string>? Format;
        }

        private readonly TuneDraft core = new() { Key = "gpu_core", Suffix = " MHz", Step = 5 };
        private readonly TuneDraft mem = new() { Key = "gpu_memory", Suffix = " MHz", Step = 5 };
        private readonly TuneDraft maxClk = new() { Key = "gpu_clock_limit", Suffix = " MHz", Step = 5 };
        private readonly TuneDraft boost = new() { Key = "gpu_boost", Suffix = " W", Step = 1 };
        private readonly TuneDraft temp = new() { Key = "gpu_temp", Suffix = " °C", Step = 1 };
        private readonly TuneDraft tgp = new() { Key = "gpu_power", Suffix = " W", Step = 1 };
        private TuneDraft[] Tunes => new[] { core, mem, maxClk, boost, temp, tgp };

        private sealed record ProcRow(int Pid, string Name, double Cpu, long MemMb);
        private List<ProcRow> procs = new();
        private readonly Dictionary<int, (TimeSpan cpu, long tick)> prevCpu = new();
        private bool procsLoading, procsSupported;
        private bool showProcessesModal;

        private int[] vramOptions = Array.Empty<int>();
        private int vramUnitMb;
        private bool apuMemMode, intelMode, vramLoaded;
        private static readonly int[] ApuMemGb = { 0, 2, 3, 4, 5, 7, 8, 9, 6 };
        private int vramCurrent = -1, vramDraft = -1;
        private bool vramDirty => vramDraft >= 0 && vramDraft != vramCurrent;

        private bool nvidia, eco, mux;
        private int gpuPowerBase;
        private bool dirty;
        private float lastContentH = 620f;
        private Action? requestRepaint;

        public override void OnEnter(SignalPaintContext? ctx)
        {
            requestRepaint = ctx?.RequestRefresh;
            LoadState();
            LoadProcs();
            if (!vramLoaded) LoadVram();
        }

        public override bool HandleEscape()
        {
            if (showProcessesModal) { showProcessesModal = false; return true; }
            return false;
        }

        private void LoadState()
        {
            eco = AppConfig.Get("gpu_mode") == AsusACPI.GPUModeEco && !AppConfig.Is("gpu_auto");
            mux = Program.settingsForm?.isMuxGpu ?? false;
            core.Label = SignalText.T("Core clock offset", "Смещение частоты ядра");
            core.Sub = SignalText.T("Use hardware-provided limits", "В аппаратных пределах");
            mem.Label = SignalText.T("Memory clock offset", "Смещение частоты памяти");
            mem.Sub = SignalText.T("Pending change applies on Apply", "Применяется по кнопке Apply");
            maxClk.Label = SignalText.T("Max clock", "Максимальная частота");
            maxClk.Sub = SignalText.T("Upper GPU frequency ceiling", "Верхний предел частоты GPU");
            boost.Label = "Dynamic Boost";
            boost.Sub = SignalText.T("Only where supported", "Только при поддержке");
            temp.Label = SignalText.T("Temperature target", "Целевая температура");
            temp.Sub = SignalText.T("Thermal limit for dGPU", "Тепловой предел dGPU");
            tgp.Label = SignalText.T("Total graphics power", "Мощность графики (TGP)");
            tgp.Sub = SignalText.T("Read-only if firmware does not expose control", "Только чтение без поддержки прошивки");

            Task.Run(() =>
            {
                try
                {
                    if (eco) { nvidia = false; requestRepaint?.Invoke(); return; }
                    if (HardwareControl.GpuControl is null || !HardwareControl.GpuControl.IsValid)
                        HardwareControl.RecreateGpuControl();

                    if (HardwareControl.GpuControl is not NvidiaGpuControl nv)
                    {
                        nvidia = false;
                        requestRepaint?.Invoke();
                        return;
                    }

                    int gb = AppConfig.GetMode("gpu_boost"); if (gb < 0) gb = AsusACPI.MaxGPUBoost;
                    int gt = AppConfig.GetMode("gpu_temp"); if (gt < 0) gt = AsusACPI.MaxGPUTemp;
                    int c = AppConfig.GetMode("gpu_core"); if (c == -1) c = 0;
                    int m = AppConfig.GetMode("gpu_memory"); if (m == -1) m = 0;
                    int cl = AppConfig.GetMode("gpu_clock_limit"); if (cl == -1) cl = NvidiaGpuControl.MaxClockLimit;
                    if (nv.GetClocks(out int cc, out int cm)) { c = cc; m = cm; }
                    int maxC = nv.GetMaxGPUCLock();
                    if (maxC == 0) cl = NvidiaGpuControl.MaxClockLimit; else if (maxC > 0) cl = maxC;

                    core.Min = NvidiaGpuControl.MinCoreOffset; core.Max = NvidiaGpuControl.MaxCoreOffset; core.Confirmed = core.Draft = Math.Clamp(c, core.Min, core.Max); core.Visible = true;
                    mem.Min = NvidiaGpuControl.MinMemoryOffset; mem.Max = NvidiaGpuControl.MaxMemoryOffset; mem.Confirmed = mem.Draft = Math.Clamp(m, mem.Min, mem.Max); mem.Visible = true;
                    maxClk.Min = NvidiaGpuControl.MinClockLimit; maxClk.Max = NvidiaGpuControl.MaxClockLimit; maxClk.Confirmed = maxClk.Draft = Math.Clamp(cl, maxClk.Min, maxClk.Max); maxClk.Visible = true;
                    maxClk.Format = v => v >= NvidiaGpuControl.MaxClockLimit ? "Default" : v + " MHz";
                    boost.Min = AsusACPI.MinGPUBoost; boost.Max = AsusACPI.MaxGPUBoost; boost.Confirmed = boost.Draft = Math.Clamp(gb, boost.Min, boost.Max); boost.Visible = Program.acpi.IsSupported(AsusACPI.PPT_GPUC0);
                    temp.Min = AsusACPI.MinGPUTemp; temp.Max = AsusACPI.MaxGPUTemp; temp.Confirmed = temp.Draft = Math.Clamp(gt, temp.Min, temp.Max); temp.Visible = Program.acpi.IsSupported(AsusACPI.PPT_GPUC2);

                    gpuPowerBase = Program.acpi.DeviceGet(AsusACPI.GPU_BASE);
                    tgp.Visible = gpuPowerBase > 0;
                    if (tgp.Visible)
                    {
                        int maxP = NvidiaSmi.GetMaxGPUPower();
                        if (maxP > 0) AsusACPI.MaxGPUPower = maxP - gpuPowerBase - AsusACPI.MaxGPUBoost;
                        tgp.Min = AsusACPI.MinGPUPower; tgp.Max = AsusACPI.MaxGPUPower;
                        int pv = Program.acpi.DeviceGet(AsusACPI.GPU_POWER);
                        int gp = AppConfig.GetMode("gpu_power"); if (gp < 0) gp = pv >= 0 ? pv : AsusACPI.MaxGPUPower;
                        tgp.Confirmed = tgp.Draft = Math.Clamp(gp, tgp.Min, tgp.Max);
                        int b = gpuPowerBase;
                        tgp.Format = v => (b + v) + " W";
                    }
                    nvidia = true;
                }
                catch { nvidia = false; }
                requestRepaint?.Invoke();
            });
        }

        private void LoadVram()
        {
            vramLoaded = true;
            Task.Run(() =>
            {
                try
                {
                    if (CpuInfo.IsAMD) vramOptions = Program.acpi.GetVramOptions(out vramUnitMb);
                    if (vramOptions.Length > 0)
                    {
                        int cur = Program.acpi.GetVramMem();
                        if (cur == 0) cur = AppConfig.Get("vram_mem", 0);
                        vramCurrent = Math.Max(0, Array.IndexOf(vramOptions, cur));
                    }
                    else if (AppConfig.IsAlly())
                    {
                        int apu = Program.acpi.GetAPUMem();
                        if (apu >= 0) { apuMemMode = true; vramCurrent = apu; }
                    }
                    else if (!CpuInfo.IsAMD && Gpu.IntelVram.Init())
                    {
                        intelMode = true;
                        vramCurrent = Gpu.IntelVram.Index;
                    }
                    vramDraft = vramCurrent;
                }
                catch { }
                requestRepaint?.Invoke();
            });
        }

        private void LoadProcs()
        {
            if (procsLoading) return;
            procsLoading = true;
            Task.Run(() =>
            {
                try
                {
                    if (HardwareControl.GpuControl is NvidiaGpuControl nv && !eco)
                    {
                        procsSupported = true;
                        long now = Environment.TickCount64;
                        var list = new List<ProcRow>();
                        foreach (var p in nv.GetActiveApplications())
                        {
                            try
                            {
                                var t = p.TotalProcessorTime;
                                double cpu = 0;
                                if (prevCpu.TryGetValue(p.Id, out var prev) && now > prev.tick)
                                    cpu = (t - prev.cpu).TotalMilliseconds / (now - prev.tick) / Environment.ProcessorCount * 100.0;
                                prevCpu[p.Id] = (t, now);
                                list.Add(new ProcRow(p.Id, p.ProcessName, Math.Clamp(cpu, 0, 100), p.WorkingSet64 / (1024 * 1024)));
                            }
                            catch { }
                        }
                        procs = list.OrderByDescending(x => x.Cpu).ThenByDescending(x => x.MemMb).ToList();
                    }
                    else { procsSupported = false; procs = new(); }
                }
                catch { }
                procsLoading = false;
                requestRepaint?.Invoke();
            });
        }

        public override void Reset()
        {
            foreach (var t in Tunes) t.Draft = t.Confirmed;
            vramDraft = vramCurrent;
            dirty = false;
        }

        public override void Apply()
        {
            if (nvidia)
            {
                foreach (var t in Tunes.Where(x => x.Visible))
                    AppConfig.SetMode(t.Key, t.Draft);

                Task.Run(() =>
                {
                    try
                    {
                        Program.modeControl.SetGPUClocks(true);
                        Program.modeControl.SetGPUPower();
                        foreach (var t in Tunes) t.Confirmed = t.Draft;
                    }
                    catch (Exception ex) { Logger.WriteLine("GPU apply: " + ex.Message); }
                    requestRepaint?.Invoke();
                });
            }
            dirty = false;
        }

        public override float ContentHeightDip(SignalPaintContext ctx) => lastContentH;

        public override void Paint(SignalPaintContext ctx)
        {
            requestRepaint = ctx.RequestRefresh;
            var body = ctx.Layout.WorkspaceBody;
            float x = body.X, w = body.Width, y = body.Y - ctx.ScrollDip;
            float pad = 24f;
            float rx = x + pad, rw = w - pad * 2f;

            var visTunes = Tunes.Where(t => t.Visible).ToList();
            bool vramSupported = vramCurrent >= 0 && (vramOptions.Length > 0 || apuMemMode || intelMode);
            int totalRows = 1 + (nvidia ? visTunes.Count : 1) + (vramSupported ? 1 : 0) + 1;
            float panelH = totalRows * 74f + 32f;

            ctx.DrawPanel(new RectangleF(x, y, w, panelH));
            float ry = y + 16f;

            // GPU mode
            string gpuLabel = CurrentGpuModeLabel(ctx.Snapshot);
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("GPU mode", "Режим GPU"),
                "Eco / Standard / Ultimate / Optimized",
                gpuLabel,
                "graphics:mode",
                pt => ShowGpuModeMenu(ctx, pt),
                enabled: ctx.Caps.SupportsGpuControl,
                showChevron: ctx.Caps.SupportsGpuControl);

            if (!nvidia)
            {
                string why = eco
                    ? SignalText.T("dGPU is off in Eco mode", "Дискретная GPU выключена в Eco")
                    : SignalText.T("Unavailable on this GPU", "Недоступно для данного GPU");
                ry = ctx.DrawSettingRow(ry, rx, rw,
                    SignalText.T("NVIDIA tuning", "Тюнинг NVIDIA"),
                    why,
                    SignalText.T("Not available", "Недоступно"),
                    "graphics:nvidia:ro",
                    null,
                    enabled: false,
                    readOnly: true);
            }
            else
            {
                foreach (var t in visTunes)
                {
                    var td = t;
                    string txt = td.Format is not null ? td.Format(td.Draft) : $"{(td.Draft > 0 && (td == core || td == mem) ? "+" : "")}{td.Draft}{td.Suffix}";
                    ry = ctx.DrawSettingRow(ry, rx, rw,
                        td.Label,
                        td.Sub,
                        txt,
                        "graphics:" + td.Key,
                        pt => ShowTuneMenu(ctx, pt, td),
                        showChevron: true);
                }
            }

            if (vramSupported)
            {
                ry = ctx.DrawSettingRow(ry, rx, rw,
                    SignalText.T("iGPU memory", "Память iGPU"),
                    SignalText.T("Restart may be required", "Может потребоваться перезагрузка"),
                    VramLabel(vramDraft),
                    "graphics:vram",
                    pt => ShowVramMenu(ctx, pt),
                    showChevron: true);
            }

            string procText = !procsSupported
                ? (eco ? SignalText.T("dGPU off", "dGPU выкл") : SignalText.T("Not available", "Недоступно"))
                : procsLoading ? SignalText.T("Reading…", "Чтение…")
                : procs.Count == 0 ? SignalText.T("0 processes", "0 процессов")
                : SignalText.Tf("View {0} processes", "Показать ({0})", procs.Count);

            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Processes using dGPU", "Процессы на dGPU"),
                SignalText.T("Do not terminate automatically", "Не завершаются автоматически"),
                procText,
                "graphics:procs",
                _ => { LoadProcs(); showProcessesModal = true; },
                enabled: procsSupported,
                readOnly: !procsSupported,
                drawDivider: false);

            lastContentH = panelH + 16f;

            if (showProcessesModal)
                PaintProcessesModal(ctx);
        }

        private void PaintProcessesModal(SignalPaintContext ctx)
        {
            var th = ctx.Theme;
            float sc = ctx.Scale;

            using (var dim = new SolidBrush(Color.FromArgb(145, 0, 0, 0)))
                ctx.G.FillRectangle(dim, 0, 0, ctx.Layout.WindowW * sc, ctx.Layout.WindowH * sc);

            float mw = Math.Min(640f, ctx.Layout.WindowW - 48f);
            float mh = Math.Min(480f, ctx.Layout.WindowH - 48f);
            var modal = new RectangleF((ctx.Layout.WindowW - mw) / 2f, (ctx.Layout.WindowH - mh) / 2f, mw, mh);
            ctx.DrawPanel(modal);

            float pad = 24f;
            ctx.Painter.DrawText(ctx.G, SignalText.T("Processes using dGPU", "Процессы на dGPU"),
                ctx.Px(modal.X + pad, modal.Y + 20f, mw - 180f, 28f),
                ctx.Painter.Font(19f, sc, FontStyle.Bold), th.Text, GlassPainter.FmtLeft);

            ctx.DrawButton("graphics:procs:refresh", new RectangleF(modal.Right - pad - 184f, modal.Y + 16f, 88f, 34f),
                SignalText.T("Refresh", "Обновить"), _ => LoadProcs());
            ctx.DrawButton("graphics:procs:close", new RectangleF(modal.Right - pad - 88f, modal.Y + 16f, 88f, 34f),
                SignalText.T("Close", "Закрыть"), _ => showProcessesModal = false);

            float y = modal.Y + 68f;
            if (procs.Count == 0)
            {
                ctx.Painter.DrawText(ctx.G, SignalText.T("No processes currently holding dGPU.", "Нет процессов на dGPU."),
                    ctx.Px(modal.X + pad, y + 40f, mw - pad * 2f, 28f),
                    ctx.Painter.Font(14f, sc), th.Muted, GlassPainter.FmtCenter);
            }
            else
            {
                foreach (var p in procs.Take(7))
                {
                    string name = p.Name.Length > 30 ? p.Name[..29] + "…" : p.Name;
                    ctx.Painter.DrawText(ctx.G, $"{name} (PID {p.Pid})", ctx.Px(modal.X + pad, y + 6f, mw - 270f, 24f),
                        ctx.Painter.Font(13f, sc, FontStyle.Bold), th.Text, GlassPainter.FmtLeft);
                    ctx.Painter.DrawText(ctx.G, $"{p.Cpu:0.0}% · {p.MemMb} MB", ctx.Px(modal.Right - pad - 210f, y + 6f, 110f, 24f),
                        ctx.Painter.Font(12f, sc), th.Muted, GlassPainter.FmtRight);

                    var proc = p;
                    ctx.DrawButton("graphics:procs:end:" + p.Pid, new RectangleF(modal.Right - pad - 88f, y, 88f, 32f),
                        SignalText.T("End", "Закрыть"), _ => ConfirmKillProcess(ctx, proc), fontSize: 12f);
                    y += 44f;
                }
            }

            if (procs.Count > 0)
            {
                ctx.DrawButton("graphics:procs:stopall", new RectangleF(modal.Right - pad - 140f, modal.Bottom - 54f, 140f, 36f),
                    SignalText.T("Stop all", "Остановить все"), _ => ConfirmKillAll(ctx));
            }
        }

        private void ConfirmKillProcess(SignalPaintContext ctx, ProcRow proc)
        {
            ctx.Confirm(new SignalConfirmState
            {
                Title = SignalText.T("End process?", "Завершить процесс?"),
                Message = $"{proc.Name} (PID {proc.Pid})",
                ConfirmLabel = SignalText.T("Confirm", "Подтвердить"),
                CancelLabel = SignalText.T("Cancel", "Отмена"),
                OnConfirm = () =>
                {
                    Task.Run(() =>
                    {
                        try { ProcessHelper.KillByProcess(System.Diagnostics.Process.GetProcessById(proc.Pid)); }
                        catch (Exception ex) { Logger.WriteLine("Kill GPU proc: " + ex.Message); }
                        Thread.Sleep(350);
                        LoadProcs();
                    });
                },
            });
        }

        private void ConfirmKillAll(SignalPaintContext ctx)
        {
            ctx.Confirm(new SignalConfirmState
            {
                Title = SignalText.T("Confirm closing GPU apps", "Закрыть приложения на dGPU?"),
                Message = SignalText.Tf("{0} active processes will be terminated.", "Будет завершено процессов: {0}.", procs.Count),
                ConfirmLabel = SignalText.T("Confirm", "Подтвердить"),
                CancelLabel = SignalText.T("Cancel", "Отмена"),
                OnConfirm = () =>
                {
                    var snapshot = procs.ToList();
                    Task.Run(() =>
                    {
                        foreach (var p in snapshot)
                            try { ProcessHelper.KillByProcess(System.Diagnostics.Process.GetProcessById(p.Pid)); } catch { }
                        Thread.Sleep(450);
                        LoadProcs();
                    });
                },
            });
        }

        private string CurrentGpuModeLabel(SensorSnapshot s)
        {
            if (s.GpuAuto) return "Optimized";
            return s.GpuMode switch
            {
                AsusACPI.GPUModeEco => "Eco",
                AsusACPI.GPUModeUltimate => "Ultimate",
                _ => "Standard",
            };
        }

        private void ShowGpuModeMenu(SignalPaintContext ctx, Point pt)
        {
            int cur = AppConfig.Get("gpu_mode");
            bool auto = AppConfig.Is("gpu_auto");
            var items = new List<(string, bool, string?, Action)>();

            if (ctx.Caps.SupportsEco)
                items.Add(("Eco", !auto && cur == AsusACPI.GPUModeEco, null, () => _ = ctx.Commands.DispatchAsync(ctx.Backend, "gpu_mode", "eco")));
            items.Add(("Standard", !auto && cur == AsusACPI.GPUModeStandard, null, () => _ = ctx.Commands.DispatchAsync(ctx.Backend, "gpu_mode", "standard")));
            if (mux)
            {
                items.Add(("Ultimate", !auto && cur == AsusACPI.GPUModeUltimate, null, () =>
                {
                    ctx.Confirm(new SignalConfirmState
                    {
                        Title = SignalText.T("Switch to Ultimate mode?", "Переключить в Ultimate?"),
                        Message = SignalText.T("A restart may be required. Save your work first.", "Может потребоваться перезагрузка. Сначала сохраните работу."),
                        ConfirmLabel = SignalText.T("Confirm", "Подтвердить"),
                        CancelLabel = SignalText.T("Cancel", "Отмена"),
                        OnConfirm = () => Program.gpuControl.SetGPUMode(AsusACPI.GPUModeUltimate),
                    });
                }));
            }
            if (ctx.Caps.SupportsEco)
                items.Add(("Optimized", auto, null, () =>
                {
                    AppConfig.Set("gpu_auto", auto ? 0 : 1);
                    Program.settingsForm.VisualiseGPUMode();
                    Program.gpuControl.AutoGPUMode(true);
                }));

            ctx.ShowMenu(pt, items);
        }

        private void ShowTuneMenu(SignalPaintContext ctx, Point pt, TuneDraft td)
        {
            var vals = new List<int>();
            int step = Math.Max(td.Step, (td.Max - td.Min) / 10);
            for (int v = td.Min; v <= td.Max; v += step) vals.Add(v);
            if (!vals.Contains(td.Draft)) vals.Add(td.Draft);
            if (!vals.Contains(td.Max)) vals.Add(td.Max);
            vals.Sort();

            ctx.ShowMenu(pt, vals.Select(v =>
            {
                string lbl = td.Format is not null ? td.Format(v) : $"{(v > 0 && (td == core || td == mem) ? "+" : "")}{v}{td.Suffix}";
                return (lbl, v == td.Draft, (string?)null, (Action)(() => { td.Draft = v; dirty = true; }));
            }));
        }

        private void ShowVramMenu(SignalPaintContext ctx, Point pt)
        {
            int count = apuMemMode ? ApuMemGb.Length : intelMode ? Gpu.IntelVram.Sizes.Length + 1 : vramOptions.Length;
            var items = Enumerable.Range(0, count).Select(i => (VramLabel(i), i == vramDraft, (string?)null, (Action)(() =>
            {
                int chosen = i;
                ctx.Confirm(new SignalConfirmState
                {
                    Title = SignalText.T("Change iGPU memory?", "Изменить память iGPU?"),
                    Message = SignalText.T("A restart may be required. Save your work first.", "Может потребоваться перезагрузка. Сначала сохраните работу."),
                    ConfirmLabel = SignalText.T("Confirm", "Подтвердить"),
                    CancelLabel = SignalText.T("Cancel", "Отмена"),
                    OnConfirm = () =>
                    {
                        if (intelMode && !ProcessHelper.IsUserAdministrator()) { ProcessHelper.RunAsAdmin(); return; }
                        if (intelMode) Gpu.IntelVram.SetIndex(chosen);
                        else if (apuMemMode) Program.acpi.SetAPUMem(chosen);
                        else { Program.acpi.SetVramMem(vramOptions[chosen]); AppConfig.Set("vram_mem", vramOptions[chosen]); }
                        vramCurrent = vramDraft = chosen;
                    },
                });
            })));
            ctx.ShowMenu(pt, items);
        }

        private string VramLabel(int i)
        {
            if (i <= 0) return SignalText.T("Automatic", "Авто");
            if (apuMemMode) return ApuMemGb[i] + " GB";
            if (intelMode) return Gpu.IntelVram.Sizes[i - 1] + " GB";
            return ((double)vramOptions[i] * vramUnitMb / 1024).ToString("0.#") + " GB";
        }
    }
}
