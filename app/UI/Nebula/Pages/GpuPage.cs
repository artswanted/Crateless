using GHelper.Gpu.NVidia;
using GHelper.Mode;

namespace GHelper.UI.Nebula.Pages
{
    /// <summary>Graphics page (design kit screen "gpu"): GPU modes, dGPU state, NVIDIA tuning.</summary>
    public sealed class GpuPage : NebulaPage
    {
        public override string Id => "gpu";
        public override string Title => NebulaText.T("Graphics", "Графика");
        public override string Subtitle => NebulaText.T("Performance when you need it.", "Производительность, когда она нужна.");

        private sealed class Tune
        {
            public string Key = "", Label = "", Suffix = "";
            public int Min, Max, Step = 1, Value;
            public bool Visible;
            public Func<int, string>? Format;
        }

        private readonly Tune core = new() { Key = "gpu_core", Suffix = " MHz", Step = 5 };
        private readonly Tune memory = new() { Key = "gpu_memory", Suffix = " MHz", Step = 5 };
        private readonly Tune clock = new() { Key = "gpu_clock_limit", Suffix = " MHz", Step = 5 };
        private readonly Tune boost = new() { Key = "gpu_boost", Suffix = " W" };
        private readonly Tune temp = new() { Key = "gpu_temp", Suffix = " °C" };
        private readonly Tune power = new() { Key = "gpu_power", Suffix = " W" };
        private Tune[] All => new[] { core, memory, clock, boost, temp, power };

        private sealed record Proc(int Pid, string Name, double Cpu, long MemMb);
        private List<Proc> procs = new();
        private readonly Dictionary<int, (TimeSpan cpu, long tick)> prevCpu = new();
        private int tick;
        private bool procsLoading;
        private bool procsSupported;

        private bool nvidia;
        private bool eco;
        private int gpuPowerBase;
        private string tuneNote = "";

        public override void Refresh(bool opened)
        {
            if (opened || tick++ % 5 == 0) LoadProcs();
            if (!opened) return;
            nvidia = false;
            eco = false;
            try { eco = Program.acpi.DeviceGet(AsusACPI.GPUEco) == 1; } catch { }
            core.Label = NebulaText.T("Core clock offset", "Смещение частоты ядра");
            memory.Label = NebulaText.T("Memory clock offset", "Смещение частоты памяти");
            clock.Label = NebulaText.T("Max clock", "Максимальная частота");
            boost.Label = NebulaText.T("Dynamic Boost", "Dynamic Boost");
            temp.Label = NebulaText.T("Temperature target", "Целевая температура");
            power.Label = NebulaText.T("GPU power (TGP)", "Мощность GPU (TGP)");

            Task.Run(() =>
            {
                try
                {
                    if (eco) { tuneNote = NebulaText.T("dGPU is off (Eco): tuning unavailable.", "dGPU выключена (Eco): тюнинг недоступен."); return; }
                    if (HardwareControl.GpuControl is null || !HardwareControl.GpuControl.IsValid) HardwareControl.RecreateGpuControl();
                    if (HardwareControl.GpuControl is not NvidiaGpuControl nv) { tuneNote = NebulaText.T("Tuning is available for NVIDIA dGPU only.", "Тюнинг доступен только для NVIDIA dGPU."); return; }

                    int gb = AppConfig.GetMode("gpu_boost"); if (gb < 0) gb = AsusACPI.MaxGPUBoost;
                    int gt = AppConfig.GetMode("gpu_temp"); if (gt < 0) gt = AsusACPI.MaxGPUTemp;
                    int c = AppConfig.GetMode("gpu_core"); if (c == -1) c = 0;
                    int m = AppConfig.GetMode("gpu_memory"); if (m == -1) m = 0;
                    int cl = AppConfig.GetMode("gpu_clock_limit"); if (cl == -1) cl = NvidiaGpuControl.MaxClockLimit;
                    if (nv.GetClocks(out int cc, out int cm)) { c = cc; m = cm; }
                    int maxClock = nv.GetMaxGPUCLock();
                    if (maxClock == 0) cl = NvidiaGpuControl.MaxClockLimit; else if (maxClock > 0) cl = maxClock;

                    core.Min = NvidiaGpuControl.MinCoreOffset; core.Max = NvidiaGpuControl.MaxCoreOffset; core.Value = Math.Clamp(c, core.Min, core.Max); core.Visible = true;
                    memory.Min = NvidiaGpuControl.MinMemoryOffset; memory.Max = NvidiaGpuControl.MaxMemoryOffset; memory.Value = Math.Clamp(m, memory.Min, memory.Max); memory.Visible = true;
                    clock.Min = NvidiaGpuControl.MinClockLimit; clock.Max = NvidiaGpuControl.MaxClockLimit; clock.Value = Math.Clamp(cl, clock.Min, clock.Max); clock.Visible = true;
                    clock.Format = v => v >= NvidiaGpuControl.MaxClockLimit ? "Default" : v + " MHz";
                    boost.Min = AsusACPI.MinGPUBoost; boost.Max = AsusACPI.MaxGPUBoost; boost.Value = Math.Clamp(gb, boost.Min, boost.Max); boost.Visible = Program.acpi.IsSupported(AsusACPI.PPT_GPUC0);
                    temp.Min = AsusACPI.MinGPUTemp; temp.Max = AsusACPI.MaxGPUTemp; temp.Value = Math.Clamp(gt, temp.Min, temp.Max); temp.Visible = Program.acpi.IsSupported(AsusACPI.PPT_GPUC2);

                    gpuPowerBase = Program.acpi.DeviceGet(AsusACPI.GPU_BASE);
                    power.Visible = gpuPowerBase > 0;
                    if (power.Visible)
                    {
                        int maxPower = NvidiaSmi.GetMaxGPUPower();
                        if (maxPower > 0) AsusACPI.MaxGPUPower = maxPower - gpuPowerBase - AsusACPI.MaxGPUBoost;
                        power.Min = AsusACPI.MinGPUPower; power.Max = AsusACPI.MaxGPUPower;
                        int pv = Program.acpi.DeviceGet(AsusACPI.GPU_POWER);
                        int gp = AppConfig.GetMode("gpu_power"); if (gp < 0) gp = pv >= 0 ? pv : AsusACPI.MaxGPUPower;
                        power.Value = Math.Clamp(gp, power.Min, power.Max);
                        int b = gpuPowerBase;
                        power.Format = v => (b + v) + " W";
                    }
                    nvidia = true;
                    tuneNote = "";
                }
                catch (Exception ex) { tuneNote = ex.Message; }
                try { Program.nebulaForm?.BeginInvoke(Program.nebulaForm.Invalidate); } catch { }
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
                        var list = new List<Proc>();
                        foreach (var p in nv.GetActiveApplications())
                        {
                            try
                            {
                                var t = p.TotalProcessorTime;
                                double cpu = 0;
                                if (prevCpu.TryGetValue(p.Id, out var prev) && now > prev.tick)
                                    cpu = (t - prev.cpu).TotalMilliseconds / (now - prev.tick) / Environment.ProcessorCount * 100.0;
                                prevCpu[p.Id] = (t, now);
                                list.Add(new Proc(p.Id, p.ProcessName, Math.Clamp(cpu, 0, 100), p.WorkingSet64 / (1024 * 1024)));
                            }
                            catch { }
                        }
                        foreach (var dead in prevCpu.Keys.Where(k => list.All(x => x.Pid != k)).ToList()) prevCpu.Remove(dead);
                        procs = list.OrderByDescending(x => x.Cpu).ThenByDescending(x => x.MemMb).ToList();
                    }
                    else { procsSupported = false; procs = new(); }
                }
                catch (Exception ex) { Logger.WriteLine("dGPU apps: " + ex.Message); }
                procsLoading = false;
                try { Program.nebulaForm?.BeginInvoke(Program.nebulaForm.Invalidate); } catch { }
            });
        }

        private float ProcCardH => 78 + Math.Max(1, Math.Min(procs.Count, 8)) * 30 + (procs.Count > 8 ? 22 : 0);

        public override void Paint(NebulaCanvas c)
        {
            var th = c.Theme;
            int mode = AppConfig.Get("gpu_mode");
            bool auto = AppConfig.Is("gpu_auto");
            bool mux = Program.settingsForm.isMuxGpu;

            Tile(c, 236, 139, "leaf", NebulaText.Eco, NebulaText.T("Integrated graphics only", "Только встроенная графика"), NebulaText.T("For battery life.", "Для работы от батареи."), !auto && mode == AsusACPI.GPUModeEco, "gpu:mode:eco");
            Tile(c, 653, 139, "gpu", NebulaText.Standard, NebulaText.T("Integrated + discrete", "Встроенная + дискретная"), NebulaText.T("Apps pick the GPU (MS Hybrid).", "Приложения выбирают GPU (MS Hybrid)."), !auto && mode == AsusACPI.GPUModeStandard, "gpu:mode:standard");
            if (mux)
                Tile(c, 236, 292, "power", NebulaText.Ultimate, NebulaText.T("Discrete graphics only", "Только дискретная графика"), NebulaText.T("May require a restart (MUX).", "Может требовать перезагрузки (MUX)."), !auto && mode == AsusACPI.GPUModeUltimate, "gpu:mode:ultimate");
            else
                Tile(c, 236, 292, "power", NebulaText.Ultimate, NebulaText.T("Not available", "Недоступно"), NebulaText.T("This model has no MUX switch.", "На этой модели нет MUX-переключателя."), false, "", enabled: false);
            Tile(c, 653, 292, "refresh", NebulaText.Optimized, NebulaText.T("Automatic switching", "Автоматическое переключение"), NebulaText.T("Eco on battery, Standard on AC.", "Eco от батареи, Standard от сети."), auto, "gpu:mode:optimized");

            // ---- state ------------------------------------------------------------------------------
            c.Surface(236, 461, 818, 119);
            bool awake = HardwareControl.gpuTemp is > 0;
            string name = HardwareControl.GpuControl?.FullName ?? "dGPU";
            c.Txt(eco ? NebulaText.T("Discrete GPU is off", "Дискретная видеокарта выключена")
                      : awake ? NebulaText.T("Discrete GPU is active", "Дискретная видеокарта активна")
                      : NebulaText.T("Discrete GPU is sleeping", "Дискретная видеокарта спит"), 256, 491, c.F(15, FontStyle.Bold), th.Text);
            string detail = name;
            if (awake)
            {
                detail += "  ·  " + Math.Round(HardwareControl.gpuTemp!.Value) + " °C";
                if (NebulaSensors.DgpuPower is > 0) detail += "  ·  " + Math.Round(NebulaSensors.DgpuPower.Value) + " " + NebulaText.Watt;
                if (HardwareControl.gpuUsage is >= 0) detail += "  ·  " + HardwareControl.gpuUsage + " %";
            }
            c.Txt(detail, 256, 512, c.F(11), th.Muted);
            c.Txt(NebulaText.T("Apps holding the dGPU keep it awake and block Eco.", "Приложения на dGPU не дают ей уснуть и мешают Eco."), 256, 553, c.F(12), th.Muted);
            c.Button(813, 516, 221, 36, NebulaText.T("Close dGPU apps", "Закрыть приложения dGPU"), "gpu:kill", primary: false, enabled: !eco);

            // ---- processes ------------------------------------------------------------------------------
            float py = 596;
            float ph = ProcCardH;
            c.Surface(236, py, 1158, ph);
            c.Txt(NebulaText.T("Processes on the dGPU", "Процессы на dGPU"), 256, py + 30, c.F(15, FontStyle.Bold), th.Text);
            c.Txt(NebulaText.T("Apps using the discrete GPU right now. Close them to let it sleep or to switch to Eco.",
                               "Приложения, которые сейчас используют дискретную видеокарту. Закрой их, чтобы она уснула или для перехода в Eco."), 256, py + 51, c.F(11), th.Faint);
            c.Button(1374 - 300, py + 18, 130, 32, NebulaText.T("Refresh", "Обновить"), "gpu:procs:refresh", primary: false);
            c.Button(1374 - 160, py + 18, 160, 32, NebulaText.T("Stop all", "Остановить все"), "gpu:procs:killall", primary: false, enabled: procs.Count > 0);

            float ry = py + 78;
            if (!procsSupported)
                c.Txt(eco ? NebulaText.T("dGPU is off.", "dGPU выключена.") : NebulaText.T("Process list is available for NVIDIA dGPU only.", "Список процессов доступен только для NVIDIA dGPU."), 256, ry + 12, c.F(12), th.Muted);
            else if (procs.Count == 0)
                c.Txt(procsLoading ? NebulaText.T("Reading…", "Читаем…") : NebulaText.T("No user processes on the dGPU.", "Пользовательских процессов на dGPU нет."), 256, ry + 12, c.F(12), th.Muted);
            else
            {
                c.Txt(NebulaText.T("Process", "Процесс"), 256, ry, c.F(10, FontStyle.Bold), th.Faint);
                c.Txt("PID", 760, ry, c.F(10, FontStyle.Bold), th.Faint, StringAlignment.Far);
                c.Txt(NebulaText.T("CPU", "Процессор"), 960, ry, c.F(10, FontStyle.Bold), th.Faint, StringAlignment.Far);
                c.Txt(NebulaText.T("Memory", "Память"), 1180, ry, c.F(10, FontStyle.Bold), th.Faint, StringAlignment.Far);
                ry += 8;
                foreach (var p in procs.Take(8))
                {
                    ry += 22;
                    c.Txt(p.Name.Length > 40 ? p.Name[..39] + "…" : p.Name, 256, ry, c.F(12), th.Text);
                    c.Txt(p.Pid.ToString(), 760, ry, c.F(11), th.Muted, StringAlignment.Far);
                    c.Txt(p.Cpu.ToString("0.0") + " %", 960, ry, c.F(11), th.Muted, StringAlignment.Far);
                    c.Txt(p.MemMb.ToString("N0") + " MB", 1180, ry, c.F(11), th.Muted, StringAlignment.Far);
                    var kr = c.R(1330, ry - 15, 44, 22);
                    c.Card(kr, 6, c.IsHover("gpu:procs:kill:" + p.Pid) ? th.Danger : th.Raised);
                    c.Txt("✕", 1352, ry, c.F(11, FontStyle.Bold), c.IsHover("gpu:procs:kill:" + p.Pid) ? th.Bg : th.Muted, StringAlignment.Center);
                    c.Hit(kr, "gpu:procs:kill:" + p.Pid, NebulaText.T("End process", "Завершить процесс"));
                    ry += 8;
                }
                if (procs.Count > 8)
                    c.Txt(NebulaText.T($"and {procs.Count - 8} more…", $"и ещё {procs.Count - 8}…"), 256, ry + 24, c.F(11), th.Faint);
            }

            // ---- tuning -------------------------------------------------------------------------------
            float ty0 = py + ph + 16;
            var visible = All.Where(t => t.Visible).ToList();
            int rowsN = (visible.Count + 1) / 2;
            float h = nvidia ? 110 + rowsN * 74 + 30 : 102;
            c.Surface(236, ty0, 1158, h);
            c.Txt(NebulaText.T("Advanced · NVIDIA tuning", "Расширенные · тюнинг NVIDIA"), 256, ty0 + 30, c.F(15, FontStyle.Bold), th.Text);
            if (!nvidia)
            {
                c.Txt(tuneNote.Length > 0 ? tuneNote : NebulaText.T("Reading GPU state…", "Читаем состояние GPU…"), 256, ty0 + 62, c.F(12), th.Muted);
                return;
            }
            c.Txt(NebulaText.T("Changes apply immediately for the current mode and are stored per mode.", "Изменения применяются сразу для текущего режима и хранятся по режимам."), 256, ty0 + 51, c.F(11), th.Faint);

            const float colW = 549;
            for (int i = 0; i < visible.Count; i++)
            {
                var t = visible[i];
                float x = 256 + (i % 2) * (colW + 40);
                float y = ty0 + 94 + (i / 2) * 74;
                c.Txt(t.Label, x, y, c.F(12), th.Muted);
                string v = t.Format is not null ? t.Format(t.Value) : t.Value + t.Suffix;
                c.Txt(v, x + colW, y, c.F(14, FontStyle.Bold), th.Text, StringAlignment.Far);
                c.Slider(x, y + 18, colW, (t.Value - t.Min) / (float)Math.Max(1, t.Max - t.Min), "slider:gpu:" + t.Key);
            }
            c.Button(1374 - 160, ty0 + h - 50, 160, 36, NebulaText.T("Reset tuning", "Сбросить тюнинг"), "gpu:reset", primary: false);
        }

        private static void Tile(NebulaCanvas c, float x, float y, string icon, string title, string line1, string line2, bool active, string id, bool enabled = true)
        {
            var th = c.Theme;
            var r = c.R(x, y, 401, 137);
            bool hv = enabled && c.IsHover(id);
            c.Card(r, 12, active ? th.AccentBg : hv ? th.Raised : th.Card, active ? th.Accent : th.Line);
            c.IconAt(icon, x + 20, y + 22, 24, active);
            c.Txt(title, x + 59, y + 40, c.F(19, FontStyle.Bold, true), !enabled ? th.Faint : active ? th.Accent : th.Text);
            c.Txt(line1, x + 20, y + 79, c.F(13), enabled ? th.Text : th.Faint);
            c.Txt(line2, x + 20, y + 109, c.F(11), th.Muted);
            if (enabled && id.Length > 0) c.Hit(r, id);
        }

        public override float ContentHeight => 596 + ProcCardH + 16 + (nvidia ? 110 + ((All.Count(t => t.Visible) + 1) / 2) * 74 + 50 : 122);

        public override void Drag(string id, float t, bool done)
        {
            var tune = All.FirstOrDefault(x => "slider:gpu:" + x.Key == id);
            if (tune is null) return;
            int v = tune.Min + (int)Math.Round(t * (tune.Max - tune.Min) / tune.Step) * tune.Step;
            tune.Value = Math.Clamp(v, tune.Min, tune.Max);
            if (!done) return;

            AppConfig.SetMode(tune.Key, tune.Value);
            Task.Run(() =>
            {
                try
                {
                    if (tune == core || tune == memory || tune == clock) Program.modeControl.SetGPUClocks(true);
                    else Program.modeControl.SetGPUPower();
                }
                catch (Exception ex) { Logger.WriteLine("GPU tune: " + ex.Message); }
            });
        }

        public override bool Click(string id, Point at, NebulaForm form)
        {
            if (id.StartsWith("gpu:procs:kill:") && int.TryParse(id[15..], out int pid))
            {
                Task.Run(() =>
                {
                    try { Helpers.ProcessHelper.KillByProcess(System.Diagnostics.Process.GetProcessById(pid)); } catch (Exception ex) { Logger.WriteLine("kill " + pid + ": " + ex.Message); }
                    System.Threading.Thread.Sleep(400);
                    LoadProcs();
                });
                return true;
            }
            switch (id)
            {
                case "gpu:mode:eco": Program.gpuControl.SetGPUMode(AsusACPI.GPUModeEco); return true;
                case "gpu:mode:standard": Program.gpuControl.SetGPUMode(AsusACPI.GPUModeStandard); return true;
                case "gpu:mode:ultimate": Program.gpuControl.SetGPUMode(AsusACPI.GPUModeUltimate); return true;
                case "gpu:mode:optimized":
                    AppConfig.Set("gpu_auto", AppConfig.Is("gpu_auto") ? 0 : 1);
                    Program.settingsForm.VisualiseGPUMode();
                    Program.gpuControl.AutoGPUMode(true);
                    return true;
                case "gpu:kill": Task.Run(() => { Program.gpuControl.KillGPUApps(); LoadProcs(); }); return true;
                case "gpu:procs:refresh": LoadProcs(); return true;
                case "gpu:procs:killall":
                    Task.Run(() =>
                    {
                        foreach (var p in procs.ToList())
                            try { Helpers.ProcessHelper.KillByProcess(System.Diagnostics.Process.GetProcessById(p.Pid)); } catch (Exception ex) { Logger.WriteLine("kill " + p.Name + ": " + ex.Message); }
                        System.Threading.Thread.Sleep(500);
                        LoadProcs();
                    });
                    return true;
                case "gpu:reset":
                    foreach (var t in All) AppConfig.RemoveMode(t.Key);
                    Task.Run(() =>
                    {
                        try { Program.modeControl.SetGPUClocks(true, true); Program.modeControl.SetGPUPower(); } catch { }
                        Refresh(true);
                    });
                    return true;
            }
            return false;
        }
    }
}
