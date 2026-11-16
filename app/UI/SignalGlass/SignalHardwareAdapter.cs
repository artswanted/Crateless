using GHelper.Battery;
using GHelper.Display;
using GHelper.Fan;
using GHelper.Gpu.NVidia;
using GHelper.Helpers;
using GHelper.Mode;
using GHelper.Peripherals;
using GHelper.USB;
using PawnIO;
using System.Management;

namespace GHelper.UI.SignalGlass
{
    /// <summary>
    /// Adapts existing G-Helper / Crateless controllers to Signal Glass contracts
    /// without duplicating or modifying upstream hardware logic.
    /// </summary>
    public sealed class SignalHardwareAdapter : ISignalBackend
    {
        private NvAPIWrapper.GPU.PhysicalGPU? nvGpu;
        private bool nvFailed;
        private bool ramRead;
        private int? ramSpeedMTs;
        private bool cycleReadFailed;
        private int? batteryCycles;
        private long lastCyclesReadTick;
        private long lastHealthTick;

        private List<(string label, string[] roots)>? diskLayout;
        private List<DiskSummary> cachedDisks = new();
        private int diskTick;
        private const long Gb = 1024L * 1024 * 1024;

        private System.Diagnostics.PerformanceCounter? cpuPerf;
        private bool cpuPerfFailed;
        private static readonly Lazy<int> cpuBaseMhz = new(() =>
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return key?.GetValue("~MHz") is int v ? v : 0;
            }
            catch { return 0; }
        });

        public Task<CapabilitySet> GetCapabilitiesAsync(CancellationToken ct)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                var acpi = Program.acpi;
                bool connected = acpi?.IsConnected() ?? false;
                bool noGpu = AppConfig.NoGpu();
                bool eco = !noGpu && acpi is not null && acpi.DeviceGet(AsusACPI.GPUEco) >= 0;
                bool mux = !noGpu && (Program.settingsForm?.isMuxGpu ?? false);

                bool modeA = acpi is not null && (acpi.IsSupported(AsusACPI.PPT_APUA0) || CpuInfo.IsAMD);
                bool modeB0 = acpi is not null && acpi.IsAllAmdPPT();
                bool modeC1 = acpi is not null && acpi.IsSupported(AsusACPI.PPT_APUC1);
                bool supportsPowerLimits = modeA || modeB0 || modeC1 ||
                    (acpi is not null && (acpi.IsSupported(AsusACPI.PPT_CROSS9F) || acpi.IsSupported(AsusACPI.PPT_GPUCPU9C) || acpi.IsSupported(AsusACPI.PPT_TEMP9E)));

                bool hyst = false;
                try
                {
                    if (acpi is not null)
                    {
                        var d = acpi.GetFanHysteresis();
                        hyst = d.up >= 0 && d.down >= 0;
                    }
                }
                catch { }

                bool midFan = AppConfig.Is("mid_fan");
                try
                {
                    if (acpi is not null)
                        midFan = !AsusACPI.IsEmptyCurve(acpi.GetFanCurve(AsusFan.Mid)) || acpi.IsMidFanSupported();
                }
                catch { }

                bool xgmFan = false;
                try { xgmFan = (acpi?.IsXGConnected() ?? false) || XGM.IsConnected(); } catch { }

                bool overdrive = false;
                try { overdrive = acpi?.IsOverdriveSupported() ?? false; } catch { }

                int miniled = AppConfig.Get("miniled", -1);
                int elmb = -1;
                try { elmb = ScreenELMB.Get(); } catch { }

                bool gv = false, gamut = false;
                try { gv = VisualControl.GetVisualModes().Count > 0; } catch { }
                try { gamut = VisualControl.GetGamutModes().Count > 1; } catch { }

                int[] rates = Array.Empty<int>();
                try
                {
                    var scr = ScreenNative.FindLaptopScreen(true);
                    if (!string.IsNullOrEmpty(scr))
                    {
                        int max = ScreenControl.GetMaxRate(scr);
                        int min = ScreenControl.MIN_RATE;
                        var set = new SortedSet<int>();
                        if (min > 0) set.Add(min);
                        if (max > 0) set.Add(max);
                        int cur = ScreenNative.GetRefreshRate(scr);
                        if (cur > 0) set.Add(cur);
                        rates = set.ToArray();
                    }
                }
                catch { }

                bool hibernate = false;
                try
                {
                    hibernate = PowerPlan.Get(PowerPlan.Idle.Hibernate, true) >= 0 || PowerPlan.Get(PowerPlan.Idle.Hibernate, false) >= 0;
                }
                catch { }

                bool pawnInstalled = false, pawnAvailable = false;
                try { pawnAvailable = ModeControl.IsPawnAvailable(); pawnInstalled = pawnAvailable || ModeControl.IsPawnInstalled(); } catch { }

                bool dynLight = false;
                try { dynLight = AppConfig.IsDynamicLighting() && DynamicLightingHelper.IsEnabled(); } catch { }

                return new CapabilitySet
                {
                    AcpiConnected = connected,
                    SupportsEco = eco,
                    SupportsMuxUltimate = mux,
                    SupportsGpuControl = !noGpu,
                    SupportsNvidiaTuning = !noGpu && HardwareControl.GpuControl is NvidiaGpuControl,
                    SupportsIGpuMemory = CpuInfo.IsAMD || AppConfig.IsAlly() || !CpuInfo.IsAMD,
                    SupportsGpuProcesses = HardwareControl.GpuControl is NvidiaGpuControl,
                    SupportsPowerLimits = supportsPowerLimits,
                    SupportsCpuBoost = true,
                    SupportsUndervolt = CpuInfo.IsAMD && CpuInfo.IsSupportedUV(),
                    PawnInstalled = pawnInstalled,
                    PawnAvailable = pawnAvailable,
                    SupportsFanHysteresis = hyst,
                    SupportsMidFan = midFan,
                    SupportsXgmFan = xgmFan,
                    SupportsOverdrive = overdrive,
                    SupportsMiniLed = miniled >= 0,
                    SupportsElmb = elmb >= 0,
                    SupportsGameVisual = gv,
                    SupportsGamut = gamut,
                    IsOled = AppConfig.IsOLED(),
                    AvailableRefreshRates = rates,
                    BatteryLimitMin = AppConfig.IsChargeLimit6080() ? 60 : 40,
                    BatteryLimitMax = 100,
                    BatteryDiscrete6080 = AppConfig.IsChargeLimit6080(),
                    SupportsHibernate = hibernate,
                    SupportsMatrixOrSlash = Program.settingsForm.matrixControl?.IsValid ?? false,
                    IsSlash = Program.settingsForm.matrixControl?.IsSlash ?? false,
                    IsDynamicLightingActive = dynLight,
                };
            }, ct);
        }

        public Task<SensorSnapshot> ReadSensorsAsync(CancellationToken ct)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    HardwareControl.ReadSensors();
                    HardwareControl.cpuUsage = HardwareControl.GetCPUUsage();
                    HardwareControl.InitCPUPowerAsync();
                    HardwareControl.cpuPower = HardwareControl.GetCPUPower();

                    long nowTick = Environment.TickCount64;
                    if (nowTick - lastHealthTick > 15 * 60_000 || HardwareControl.batteryHealth < 0)
                    {
                        lastHealthTick = nowTick;
                        try { HardwareControl.RefreshBatteryHealth(); } catch { }
                    }

                    if (!cycleReadFailed && (nowTick - lastCyclesReadTick > 15 * 60_000 || lastCyclesReadTick == 0))
                    {
                        lastCyclesReadTick = nowTick;
                        batteryCycles = TryReadBatteryCycles();
                    }

                    // CPU frequency
                    int? cpuMhz = null;
                    if (!cpuPerfFailed)
                    {
                        try
                        {
                            cpuPerf ??= new System.Diagnostics.PerformanceCounter("Processor Information", "% Processor Performance", "_Total", true);
                            float perf = cpuPerf.NextValue();
                            int baseMhz = cpuBaseMhz.Value;
                            cpuMhz = perf > 0 && baseMhz > 0 ? (int)Math.Round(baseMhz * perf / 100f) : null;
                        }
                        catch { cpuPerfFailed = true; }
                    }

                    // dGPU state (never wake a sleeping dGPU)
                    bool eco = AppConfig.Get("gpu_mode") == AsusACPI.GPUModeEco && !AppConfig.Is("gpu_auto");
                    bool awake = HardwareControl.gpuTemp is > 0;
                    int? gpuUse = null, gpuMhz = null, gpuMemMhz = null, gpuMv = null;
                    float? dgpuPower = null;
                    try
                    {
                        var gc = HardwareControl.GpuControl;
                        if (awake && gc is not null)
                        {
                            gpuUse = gc.GetGpuUse();
                            dgpuPower = gc.GetGpuPower();
                            HardwareControl.gpuUsage = gpuUse;
                        }
                        else
                        {
                            HardwareControl.gpuUsage = null;
                        }
                    }
                    catch { }

                    if (!nvFailed && awake && HardwareControl.GpuControl?.IsNvidia == true)
                    {
                        try
                        {
                            nvGpu ??= NvAPIWrapper.GPU.PhysicalGPU.GetPhysicalGPUs()
                                .FirstOrDefault(g => g.SystemType == NvAPIWrapper.Native.GPU.SystemType.Laptop);
                            var clocks = nvGpu?.CurrentClockFrequencies;
                            if (clocks is not null)
                            {
                                gpuMhz = (int)(clocks.GraphicsClock.Frequency / 1000);
                                gpuMemMhz = (int)(clocks.MemoryClock.Frequency / 1000);
                            }
                            try
                            {
                                var ps = nvGpu?.PerformanceStatesInfo;
                                var v = ps?.CurrentPerformanceState?.Voltages?.FirstOrDefault();
                                if (v is not null && v.CurrentVoltageInMicroVolt > 0)
                                    gpuMv = (int)(v.CurrentVoltageInMicroVolt / 1000);
                            }
                            catch { }
                        }
                        catch { nvFailed = true; }
                    }

                    // iGPU
                    int? igpuUse = null, igpuTemp = null, igpuPower = null;
                    if (AppConfig.IsAMDiGPU() || CpuInfo.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var ig = HardwareControl.AmdApu().GetiGpuSensors();
                            igpuUse = ig.use; igpuTemp = ig.temp; igpuPower = ig.gfxPower;
                        }
                        catch { }
                    }

                    // RAM & VRAM & Disks
                    var ram = HardwareControl.GetRAMInfo();
                    HardwareControl.ramUsage = ram?.percent;
                    HardwareControl.ramUsedMb = ram?.usedMb;

                    double? ramUsedGb = null, ramTotalGb = null;
                    if (ram is { usedMb: > 0, percent: > 0 })
                    {
                        ramUsedGb = ram.Value.usedMb / 1024.0;
                        ramTotalGb = Math.Round(ramUsedGb.Value / ram.Value.percent * 100.0);
                    }

                    if (!ramRead)
                    {
                        ramRead = true;
                        ramSpeedMTs = TryReadRamSpeed();
                    }

                    int? vramUsed = null, vramTotal = null;
                    try
                    {
                        var v = HardwareControl.GpuControl?.GetVramInfo();
                        if (v is { totalMb: > 0 }) { vramUsed = (int)v.Value.usedMb; vramTotal = (int)v.Value.totalMb; }
                    }
                    catch { }

                    if (diskTick % 30 == 0)
                    {
                        if (diskTick % 600 == 0) diskLayout = null;
                        cachedDisks = ReadDisks();
                    }
                    diskTick++;

                    // Battery status (never report Charging when limit reached!)
                    int? chargePct = ParseLeadingIntNullable(HardwareControl.batteryCharge);
                    int limit = Math.Clamp(AppConfig.Get("charge_limit", 100), 40, 100);
                    bool plugged = Gpu.GPUModeControl.IsPlugged();
                    decimal rate = HardwareControl.batteryRate ?? 0m;
                    BatteryStatusKind bStatus =
                        chargePct is null ? BatteryStatusKind.Unavailable :
                        rate > 0 ? BatteryStatusKind.Charging :
                        rate < 0 ? BatteryStatusKind.Discharging :
                        plugged && chargePct >= limit ? BatteryStatusKind.LimitReached :
                        plugged ? BatteryStatusKind.PluggedIdle :
                        BatteryStatusKind.OnBattery;

                    var dgpuState = AppConfig.NoGpu() ? DgpuActivityState.Unsupported
                        : eco ? DgpuActivityState.OffEco
                        : awake ? DgpuActivityState.Active
                        : DgpuActivityState.Sleeping;

                    var devs = new List<IPeripheral>();
                    try { devs = PeripheralsProvider.AllPeripherals(); } catch { }
                    string devSummary = devs.Count == 0
                        ? ""
                        : devs.Count == 1
                            ? SafePeripheralName(devs[0])
                            : $"{SafePeripheralName(devs[0])} (+{devs.Count - 1})";

                    return new SensorSnapshot
                    {
                        Timestamp = DateTimeOffset.Now,
                        Status = SampleStatus.Ready,
                        CpuTempC = HardwareControl.cpuTemp is > 0 ? HardwareControl.cpuTemp : null,
                        CpuUsagePct = HardwareControl.cpuUsage is >= 0 ? HardwareControl.cpuUsage : null,
                        CpuClockMhz = cpuMhz,
                        CpuPowerW = HardwareControl.cpuPower is > 0 ? HardwareControl.cpuPower : null,
                        IgpuUsePct = igpuUse,
                        IgpuTempC = igpuTemp,
                        IgpuPowerW = igpuPower,

                        DgpuState = dgpuState,
                        GpuTempC = HardwareControl.gpuTemp is > 0 ? HardwareControl.gpuTemp : null,
                        GpuUsagePct = awake ? gpuUse : null,
                        GpuClockMhz = awake ? gpuMhz : null,
                        GpuMemClockMhz = awake ? gpuMemMhz : null,
                        GpuPowerW = awake ? dgpuPower : null,
                        GpuMv = awake ? gpuMv : null,
                        GpuName = HardwareControl.GpuControl?.FullName ?? "",

                        CpuFanRpm = ParseFanRpm(HardwareControl.cpuFan),
                        CpuFanRaw = HardwareControl.cpuFan,
                        GpuFanRpm = ParseFanRpm(HardwareControl.gpuFan),
                        GpuFanRaw = HardwareControl.gpuFan,
                        MidFanRpm = ParseFanRpm(HardwareControl.midFan),
                        MidFanRaw = HardwareControl.midFan,
                        HasMidFan = AppConfig.Is("mid_fan") || HardwareControl.midFan is not null,
                        CustomFanCurve = AppConfig.IsApplyFans(),

                        RamUsedGb = ramUsedGb,
                        RamTotalGb = ramTotalGb,
                        RamUsagePct = ram?.percent,
                        RamSpeedMTs = ramSpeedMTs,
                        VramUsedMb = vramUsed,
                        VramTotalMb = vramTotal,
                        Disks = cachedDisks,

                        ChargePct = chargePct,
                        ChargeLimitPct = limit,
                        OneTimeFullCharge = BatteryControl.chargeFull,
                        IsPlugged = plugged,
                        BatteryRateW = HardwareControl.batteryRate,
                        BatteryHealthPct = HardwareControl.batteryHealth >= 0 ? HardwareControl.batteryHealth : null,
                        DesignCapacityWh = HardwareControl.designCapacity is > 0 ? HardwareControl.designCapacity / 1000m : null,
                        FullCapacityWh = HardwareControl.fullCapacity is > 0 ? HardwareControl.fullCapacity / 1000m : null,
                        CycleCount = batteryCycles,
                        BatteryStatus = bStatus,

                        ModelName = AppConfig.GetModelShort(),
                        PerformanceMode = Modes.GetCurrent(),
                        PerformanceModeName = Modes.GetCurrentName(),
                        GpuMode = AppConfig.Get("gpu_mode"),
                        GpuAuto = AppConfig.Is("gpu_auto"),
                        RefreshRateHz = AppConfig.Get("frequency", 0),
                        ScreenAuto = AppConfig.Is("screen_auto"),
                        ConnectedDevicesCount = devs.Count,
                        PrimaryDeviceSummary = devSummary,
                        AcpiConnected = Program.acpi?.IsConnected() ?? false,
                    };
                }
                catch (Exception ex)
                {
                    Logger.WriteLine("Signal sensors: " + ex.Message);
                    return new SensorSnapshot
                    {
                        Timestamp = DateTimeOffset.Now,
                        Status = SampleStatus.Error,
                        ErrorMessage = ex.Message,
                    };
                }
            }, ct);
        }

        public Task<CommandResult> ExecuteAsync(HardwareCommand command, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    switch (command.CapabilityId)
                    {
                        case "performance_mode":
                        {
                            if (!int.TryParse(command.Value, out int mode))
                                return Fail(command, "INVALID_MODE", "Invalid performance mode");
                            Program.modeControl.SetPerformanceMode(mode, true);
                            return Ok(command, Modes.GetCurrent().ToString());
                        }
                        case "charge_limit":
                        {
                            if (!int.TryParse(command.Value, out int lim))
                                return Fail(command, "INVALID_LIMIT", "Invalid charge limit");
                            BatteryControl.SetBatteryChargeLimit(lim);
                            return Ok(command, AppConfig.Get("charge_limit", lim).ToString());
                        }
                        case "charge_full":
                        {
                            BatteryControl.ToggleBatteryLimitFull();
                            return Ok(command, BatteryControl.chargeFull ? "1" : "0");
                        }
                        case "gpu_mode":
                        {
                            return ExecuteGpuMode(command);
                        }
                        case "refresh_rate":
                        {
                            if (command.Value == "auto")
                            {
                                ScreenControl.SetAutoRefresh(1);
                                ScreenControl.AutoScreen();
                                return Ok(command, "auto");
                            }
                            if (int.TryParse(command.Value, out int hz))
                            {
                                ScreenControl.SetAutoRefresh(0);
                                ScreenControl.SetScreen(hz, hz > ScreenControl.MIN_RATE ? 1 : 0);
                                return Ok(command, AppConfig.Get("frequency", hz).ToString());
                            }
                            return Fail(command, "INVALID_RATE", "Invalid refresh rate");
                        }
                        default:
                            return Fail(command, "UNKNOWN_CAP", $"Unknown capability: {command.CapabilityId}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.WriteLine($"Signal command {command.CapabilityId}: {ex.Message}");
                    return Fail(command, "EXEC_FAILED", ex.Message);
                }
            }, ct);
        }

        private static CommandResult ExecuteGpuMode(HardwareCommand command)
        {
            int targetMode = command.Value switch
            {
                "eco" => AsusACPI.GPUModeEco,
                "standard" => AsusACPI.GPUModeStandard,
                "ultimate" => AsusACPI.GPUModeUltimate,
                "optimized" => -2,
                _ => -99,
            };
            if (targetMode == -99) return Fail(command, "INVALID_GPU_MODE", "Unsupported GPU mode");

            if (targetMode == -2)
            {
                bool nextAuto = !AppConfig.Is("gpu_auto");
                AppConfig.Set("gpu_auto", nextAuto ? 1 : 0);
                Program.settingsForm.VisualiseGPUMode();
                Program.gpuControl.AutoGPUMode(true);
                return Ok(command, nextAuto ? "optimized" : ModeSlug(AppConfig.Get("gpu_mode")));
            }

            if (targetMode == AsusACPI.GPUModeUltimate || AppConfig.Get("gpu_mode") == AsusACPI.GPUModeUltimate)
            {
                // Ultimate MUX switching goes through GPUModeControl which prompts & restarts if confirmed.
                Program.settingsForm.BeginInvoke(() => Program.gpuControl.SetGPUMode(targetMode));
                return new CommandResult
                {
                    RequestId = command.RequestId,
                    CapabilityId = command.CapabilityId,
                    Success = true,
                    RestartRequired = true,
                    ConfirmedValue = command.Value,
                };
            }

            Program.gpuControl.SetGPUMode(targetMode);
            return Ok(command, ModeSlug(targetMode));
        }

        private static string ModeSlug(int mode) => mode switch
        {
            AsusACPI.GPUModeEco => "eco",
            AsusACPI.GPUModeUltimate => "ultimate",
            _ => "standard",
        };

        private static CommandResult Ok(HardwareCommand cmd, string confirmed) => new()
        {
            RequestId = cmd.RequestId,
            CapabilityId = cmd.CapabilityId,
            Success = true,
            ConfirmedValue = confirmed,
        };

        private static CommandResult Fail(HardwareCommand cmd, string code, string msg) => new()
        {
            RequestId = cmd.RequestId,
            CapabilityId = cmd.CapabilityId,
            Success = false,
            ErrorCode = code,
            ErrorMessage = msg,
        };

        private static string SafePeripheralName(IPeripheral p)
        {
            try { return p.GetDisplayName(); } catch { return "ASUS device"; }
        }

        private static int? TryReadRamSpeed()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");
                foreach (ManagementObject mo in searcher.Get())
                {
                    using (mo)
                    {
                        int v = Convert.ToInt32(mo["ConfiguredClockSpeed"] ?? mo["Speed"] ?? 0);
                        if (v > 0) return v;
                    }
                }
            }
            catch { }
            return null;
        }

        private int? TryReadBatteryCycles()
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT CycleCount FROM BatteryCycleCount");
                foreach (ManagementObject mo in searcher.Get())
                {
                    using (mo)
                    {
                        int v = Convert.ToInt32(mo["CycleCount"] ?? 0);
                        if (v > 0) return v;
                    }
                }
            }
            catch { cycleReadFailed = true; }
            return null;
        }

        private List<DiskSummary> ReadDisks()
        {
            var list = new List<DiskSummary>();
            try
            {
                diskLayout ??= ReadDiskLayout();
                foreach (var (label, roots) in diskLayout)
                {
                    long total = 0, used = 0;
                    foreach (string root in roots)
                    {
                        try
                        {
                            var d = new DriveInfo(root);
                            if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                            total += d.TotalSize;
                            used += d.TotalSize - d.TotalFreeSpace;
                        }
                        catch { }
                    }
                    if (total > 0) list.Add(new DiskSummary(label, used / Gb, total / Gb));
                }
            }
            catch { }
            return list;
        }

        private static List<(string label, string[] roots)> ReadDiskLayout()
        {
            var result = new List<(string, string[])>();
            try
            {
                var groups = new Dictionary<uint, List<string>>();
                using var searcher = new ManagementObjectSearcher("SELECT DeviceID, DiskIndex FROM Win32_DiskPartition");
                foreach (ManagementObject p in searcher.Get())
                {
                    using (p)
                    {
                        uint disk = Convert.ToUInt32(p["DiskIndex"] ?? uint.MaxValue);
                        foreach (ManagementObject ld in p.GetRelated("Win32_LogicalDisk"))
                        {
                            using (ld)
                            {
                                string? id = ld["DeviceID"]?.ToString();
                                if (string.IsNullOrEmpty(id)) continue;
                                if (!groups.TryGetValue(disk, out var roots)) groups[disk] = roots = new();
                                roots.Add(id + "\\");
                            }
                        }
                    }
                }
                foreach (var kv in groups.OrderBy(k => k.Key))
                {
                    var letters = kv.Value.Select(r => r.TrimEnd('\\')).OrderBy(x => x).ToArray();
                    if (letters.Length > 0) result.Add((string.Join(" ", letters), kv.Value.ToArray()));
                }
            }
            catch { }
            if (result.Count == 0)
            {
                var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
                result.Add((root.TrimEnd('\\'), new[] { root }));
            }
            return result;
        }

        private static int? ParseLeadingIntNullable(string? s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            int i = 0; while (i < s.Length && !char.IsDigit(s[i])) i++;
            int j = i; while (j < s.Length && char.IsDigit(s[j])) j++;
            return j > i && int.TryParse(s[i..j], out int v) ? v : null;
        }

        private static int? ParseFanRpm(string? raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            if (raw.Contains('%')) return null;
            return ParseLeadingIntNullable(raw);
        }
    }
}
