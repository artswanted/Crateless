using GHelper.UI.SignalGlass;

namespace Crateless.Tests
{
    internal sealed class MockSignalBackend : ISignalBackend
    {
        public CapabilitySet Capabilities { get; set; } = CreateFullCapabilities();
        public SensorSnapshot NextSnapshot { get; set; } = CreateReadySnapshot();
        public bool FailNextCommand { get; set; }
        public bool NextRestartRequired { get; set; }
        public string LastConfirmedMode { get; set; } = "0";
        public string LastConfirmedChargeLimit { get; set; } = "80";

        public TaskCompletionSource<CommandResult>? PendingManualResult { get; set; }

        public Task<CapabilitySet> GetCapabilitiesAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Capabilities);
        }

        public Task<SensorSnapshot> ReadSensorsAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(NextSnapshot);
        }

        public Task<CommandResult> ExecuteAsync(HardwareCommand command, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (PendingManualResult is not null)
                return PendingManualResult.Task;

            if (FailNextCommand)
            {
                FailNextCommand = false;
                return Task.FromResult(new CommandResult
                {
                    RequestId = command.RequestId,
                    CapabilityId = command.CapabilityId,
                    Success = false,
                    ConfirmedValue = command.CapabilityId == "charge_limit" ? LastConfirmedChargeLimit : LastConfirmedMode,
                    ErrorCode = "HARDWARE_BUSY",
                    ErrorMessage = "Simulated EC write failure",
                });
            }

            if (command.CapabilityId == "performance_mode") LastConfirmedMode = command.Value;
            if (command.CapabilityId == "charge_limit") LastConfirmedChargeLimit = command.Value;

            return Task.FromResult(new CommandResult
            {
                RequestId = command.RequestId,
                CapabilityId = command.CapabilityId,
                Success = true,
                RestartRequired = NextRestartRequired || (command.CapabilityId == "gpu_mode" && command.Value == "ultimate"),
                ConfirmedValue = command.Value,
            });
        }

        public static CapabilitySet CreateFullCapabilities() => new()
        {
            AcpiConnected = true,
            SupportsEco = true,
            SupportsMuxUltimate = true,
            SupportsGpuControl = true,
            SupportsNvidiaTuning = true,
            SupportsIGpuMemory = true,
            SupportsGpuProcesses = true,
            SupportsPowerLimits = true,
            SupportsCpuBoost = true,
            SupportsUndervolt = true,
            PawnInstalled = true,
            PawnAvailable = true,
            SupportsFanHysteresis = true,
            SupportsMidFan = true,
            SupportsXgmFan = false,
            SupportsOverdrive = true,
            SupportsMiniLed = true,
            SupportsElmb = false,
            SupportsGameVisual = true,
            SupportsGamut = true,
            IsOled = true,
            SupportsMatrixOrSlash = true,
            IsSlash = true,
            AvailableRefreshRates = new[] { 60, 120, 240 },
            BatteryLimitMin = 40,
            BatteryLimitMax = 100,
            BatteryDiscrete6080 = false,
        };

        public static CapabilitySet CreateUnsupportedCapabilities() => new()
        {
            AcpiConnected = false,
            SupportsEco = false,
            SupportsMuxUltimate = false,
            SupportsGpuControl = false,
            SupportsNvidiaTuning = false,
            SupportsIGpuMemory = false,
            SupportsGpuProcesses = false,
            SupportsPowerLimits = false,
            SupportsCpuBoost = false,
            SupportsUndervolt = false,
            SupportsFanHysteresis = false,
            SupportsMidFan = false,
            SupportsXgmFan = false,
            SupportsOverdrive = false,
            SupportsMiniLed = false,
            SupportsElmb = false,
            SupportsGameVisual = false,
            SupportsGamut = false,
            IsOled = false,
            AvailableRefreshRates = Array.Empty<int>(),
        };

        public static SensorSnapshot CreateReadySnapshot() => new()
        {
            Timestamp = DateTimeOffset.UtcNow,
            Status = SampleStatus.Ready,
            CpuTempC = 64.2f,
            CpuUsagePct = 38,
            CpuClockMhz = 4120,
            CpuPowerW = 28.4f,
            DgpuState = DgpuActivityState.Active,
            GpuTempC = 57.6f,
            GpuUsagePct = 52,
            GpuClockMhz = 1890,
            GpuMemClockMhz = 7000,
            GpuPowerW = 64.0f,
            GpuMv = 825,
            GpuName = "NVIDIA GeForce RTX 4070 Laptop GPU",
            CpuFanRpm = 2800,
            CpuFanRaw = "2800 RPM",
            GpuFanRpm = 2600,
            GpuFanRaw = "2600 RPM",
            MidFanRpm = 2100,
            MidFanRaw = "2100 RPM",
            HasMidFan = true,
            RamUsedGb = 14.6,
            RamTotalGb = 32.0,
            RamUsagePct = 46,
            RamSpeedMTs = 6400,
            VramUsedMb = 2480,
            VramTotalMb = 8192,
            Disks = new[] { new DiskSummary("C:", 412, 954) },
            ChargePct = 80,
            ChargeLimitPct = 80,
            IsPlugged = true,
            BatteryRateW = 0m,
            BatteryHealthPct = 96.4m,
            DesignCapacityWh = 73.0m,
            FullCapacityWh = 70.4m,
            BatteryStatus = BatteryStatusKind.LimitReached,
            PerformanceMode = 0,
            PerformanceModeName = "Balanced",
            GpuMode = AsusACPI.GPUModeStandard,
            GpuAuto = false,
            RefreshRateHz = 240,
            ScreenAuto = false,
            ModelName = "ROG Zephyrus G16 GU605",
            ConnectedDevicesCount = 2,
            PrimaryDeviceSummary = "ROG Keris II Ace · 78%",
        };

        public static SensorSnapshot CreateLoadingSnapshot() => new()
        {
            Timestamp = DateTimeOffset.MinValue,
            Status = SampleStatus.Loading,
            ModelName = "ROG Zephyrus G16",
        };

        public static SensorSnapshot CreateStaleSnapshot() => CreateReadySnapshot() with
        {
            Timestamp = DateTimeOffset.UtcNow.AddSeconds(-15),
            Status = SampleStatus.Stale,
        };

        public static SensorSnapshot CreateErrorSnapshot() => new()
        {
            Timestamp = DateTimeOffset.MinValue,
            Status = SampleStatus.Error,
            ErrorMessage = "ACPI endpoint did not respond",
            ModelName = "ROG Zephyrus G16",
        };

        public static SensorSnapshot CreateDisconnectedSnapshot() => new()
        {
            Timestamp = DateTimeOffset.MinValue,
            Status = SampleStatus.Disconnected,
            ErrorMessage = "ASUS WMI interface unavailable",
            ModelName = "ROG Zephyrus G16",
        };
    }
}
