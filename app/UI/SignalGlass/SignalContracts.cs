namespace GHelper.UI.SignalGlass
{
    public enum SampleStatus
    {
        Ready,
        Missing,
        Stale,
        Error,
        Unsupported,
        Loading,
        Disconnected,
    }

    public enum DgpuActivityState
    {
        OffEco,
        Sleeping,
        Active,
        Unsupported,
    }

    public enum BatteryStatusKind
    {
        Unavailable,
        LimitReached,
        Charging,
        Discharging,
        PluggedIdle,
        OnBattery,
    }

    public sealed record DiskSummary(string Label, long UsedGb, long TotalGb);

    public sealed class SensorValue
    {
        public string Id { get; init; } = "";
        public double? Value { get; init; }
        public string Unit { get; init; } = "";
        public SampleStatus Status { get; init; }
        public DateTimeOffset Timestamp { get; init; }
    }

    /// <summary>
    /// Immutable telemetry snapshot captured on a worker thread.
    /// Null numeric fields stay null so the UI renders "—" or chart gaps instead of fabricated zeros.
    /// </summary>
    public sealed record SensorSnapshot
    {
        public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.MinValue;
        public SampleStatus Status { get; init; } = SampleStatus.Loading;
        public string ErrorMessage { get; init; } = "";
        public bool IsStale => Status == SampleStatus.Stale;

        // CPU & iGPU
        public float? CpuTempC { get; init; }
        public int? CpuUsagePct { get; init; }
        public int? CpuClockMhz { get; init; }
        public float? CpuPowerW { get; init; }
        public int? IgpuUsePct { get; init; }
        public int? IgpuTempC { get; init; }
        public int? IgpuPowerW { get; init; }

        // dGPU
        public DgpuActivityState DgpuState { get; init; } = DgpuActivityState.Unsupported;
        public float? GpuTempC { get; init; }
        public int? GpuUsagePct { get; init; }
        public int? GpuClockMhz { get; init; }
        public int? GpuMemClockMhz { get; init; }
        public float? GpuPowerW { get; init; }
        public int? GpuMv { get; init; }
        public string GpuName { get; init; } = "";

        // Fans
        public int? CpuFanRpm { get; init; }
        public string? CpuFanRaw { get; init; }
        public int? GpuFanRpm { get; init; }
        public string? GpuFanRaw { get; init; }
        public int? MidFanRpm { get; init; }
        public string? MidFanRaw { get; init; }
        public bool HasMidFan { get; init; }
        public bool CustomFanCurve { get; init; }

        // Memory & Storage
        public double? RamUsedGb { get; init; }
        public double? RamTotalGb { get; init; }
        public int? RamUsagePct { get; init; }
        public int? RamSpeedMTs { get; init; }
        public int? VramUsedMb { get; init; }
        public int? VramTotalMb { get; init; }
        public IReadOnlyList<DiskSummary> Disks { get; init; } = Array.Empty<DiskSummary>();

        // Battery & Power
        public int? ChargePct { get; init; }
        public int ChargeLimitPct { get; init; } = 100;
        public bool OneTimeFullCharge { get; init; }
        public bool IsPlugged { get; init; }
        public decimal? BatteryRateW { get; init; }
        public decimal? BatteryHealthPct { get; init; }
        public decimal? DesignCapacityWh { get; init; }
        public decimal? FullCapacityWh { get; init; }
        public int? CycleCount { get; init; }
        public BatteryStatusKind BatteryStatus { get; init; } = BatteryStatusKind.Unavailable;

        // Active Controls Summary
        public string ModelName { get; init; } = "";
        public int PerformanceMode { get; init; }
        public string PerformanceModeName { get; init; } = "";
        public int GpuMode { get; init; }
        public bool GpuAuto { get; init; }
        public int RefreshRateHz { get; init; }
        public bool ScreenAuto { get; init; }
        public int ConnectedDevicesCount { get; init; }
        public string PrimaryDeviceSummary { get; init; } = "";
        public bool AcpiConnected { get; init; } = true;
    }

    /// <summary>
    /// Fixed-capacity ring buffer for 60-second telemetry plots.
    /// Stores null for missing samples so charts show breaks instead of dropping to zero.
    /// </summary>
    public sealed class RingHistoryBuffer
    {
        private readonly float?[] slots;
        private int head;
        private int count;
        private readonly object sync = new();

        public int Capacity { get; }

        public RingHistoryBuffer(int capacity = 60)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
            slots = new float?[capacity];
        }

        public int Count
        {
            get { lock (sync) return count; }
        }

        public int ValidCount
        {
            get
            {
                lock (sync)
                {
                    int valid = 0;
                    for (int i = 0; i < count; i++)
                    {
                        int idx = (head - count + i + Capacity * 2) % Capacity;
                        if (slots[idx].HasValue) valid++;
                    }
                    return valid;
                }
            }
        }

        public float? Latest
        {
            get
            {
                lock (sync)
                {
                    if (count == 0) return null;
                    int idx = (head - 1 + Capacity) % Capacity;
                    return slots[idx];
                }
            }
        }

        public void Append(float? sample)
        {
            lock (sync)
            {
                slots[head] = sample;
                head = (head + 1) % Capacity;
                if (count < Capacity) count++;
            }
        }

        public void Clear()
        {
            lock (sync)
            {
                Array.Clear(slots, 0, slots.Length);
                head = 0;
                count = 0;
            }
        }

        /// <summary>Returns samples ordered oldest-to-newest, left-aligned or right-aligned to the 60s window.</summary>
        public float?[] Snapshot()
        {
            lock (sync)
            {
                var result = new float?[count];
                int start = (head - count + Capacity) % Capacity;
                for (int i = 0; i < count; i++)
                {
                    result[i] = slots[(start + i) % Capacity];
                }
                return result;
            }
        }
    }

    public sealed class Capability
    {
        public string Id { get; init; } = "";
        public bool Supported { get; init; }
        public double? Minimum { get; init; }
        public double? Maximum { get; init; }
        public double? Step { get; init; }
        public string[] Choices { get; init; } = Array.Empty<string>();
        public string UnavailableReason { get; init; } = "";
    }

    public sealed class CapabilitySet
    {
        public bool AcpiConnected { get; init; }
        public bool SupportsEco { get; init; }
        public bool SupportsMuxUltimate { get; init; }
        public bool SupportsGpuControl { get; init; }
        public bool SupportsNvidiaTuning { get; init; }
        public bool SupportsIGpuMemory { get; init; }
        public bool SupportsGpuProcesses { get; init; }

        public bool SupportsPowerLimits { get; init; }
        public bool SupportsCpuBoost { get; init; }
        public bool SupportsUndervolt { get; init; }
        public bool PawnInstalled { get; init; }
        public bool PawnAvailable { get; init; }

        public bool SupportsFanHysteresis { get; init; }
        public bool SupportsMidFan { get; init; }
        public bool SupportsXgmFan { get; init; }

        public bool SupportsOverdrive { get; init; }
        public bool SupportsMiniLed { get; init; }
        public bool SupportsElmb { get; init; }
        public bool SupportsGameVisual { get; init; }
        public bool SupportsGamut { get; init; }
        public bool IsOled { get; init; }
        public int[] AvailableRefreshRates { get; init; } = Array.Empty<int>();

        public int BatteryLimitMin { get; init; } = 40;
        public int BatteryLimitMax { get; init; } = 100;
        public bool BatteryDiscrete6080 { get; init; }

        public bool SupportsHibernate { get; init; }
        public bool SupportsMatrixOrSlash { get; init; }
        public bool IsSlash { get; init; }
        public bool IsDynamicLightingActive { get; init; }

        public IReadOnlyDictionary<string, Capability> Items { get; init; } = new Dictionary<string, Capability>();
    }

    public sealed class HardwareCommand
    {
        public Guid RequestId { get; init; } = Guid.NewGuid();
        public string CapabilityId { get; init; } = "";
        public string Value { get; init; } = "";
    }

    public sealed class CommandResult
    {
        public Guid RequestId { get; init; }
        public string CapabilityId { get; init; } = "";
        public bool Success { get; init; }
        public bool RestartRequired { get; init; }
        public string ConfirmedValue { get; init; } = "";
        public string ErrorCode { get; init; } = "";
        public string ErrorMessage { get; init; } = "";
    }

    public interface ISignalBackend
    {
        Task<CapabilitySet> GetCapabilitiesAsync(CancellationToken ct);
        Task<SensorSnapshot> ReadSensorsAsync(CancellationToken ct);
        Task<CommandResult> ExecuteAsync(HardwareCommand command, CancellationToken ct);
    }

    /// <summary>
    /// Coordinates Observed / Draft / Pending parameter state so at most one request
    /// per capability is active at a time, late stale responses never overwrite newer
    /// confirmed values, and failed commands roll back cleanly to the last confirmed value.
    /// </summary>
    public sealed class CommandCoordinator
    {
        private readonly object sync = new();
        private readonly Dictionary<string, Guid> activeRequests = new();
        private readonly Dictionary<string, string> confirmed = new();
        private readonly Dictionary<string, string> draft = new();
        private readonly Dictionary<string, string> errors = new();

        public event Action<string, CommandResult>? CommandCompleted;

        public void SeedObserved(string capabilityId, string confirmedValue)
        {
            lock (sync)
            {
                confirmed[capabilityId] = confirmedValue;
                if (!activeRequests.ContainsKey(capabilityId) && !IsDirtyLocked(capabilityId))
                {
                    draft[capabilityId] = confirmedValue;
                }
            }
        }

        public void SetDraft(string capabilityId, string draftValue)
        {
            lock (sync)
            {
                draft[capabilityId] = draftValue;
                errors.Remove(capabilityId);
            }
        }

        public void ResetDraft(string capabilityId)
        {
            lock (sync)
            {
                if (confirmed.TryGetValue(capabilityId, out var v)) draft[capabilityId] = v;
                else draft.Remove(capabilityId);
                errors.Remove(capabilityId);
            }
        }

        public bool IsPending(string capabilityId)
        {
            lock (sync) return activeRequests.ContainsKey(capabilityId);
        }

        public bool IsDirty(string capabilityId)
        {
            lock (sync) return IsDirtyLocked(capabilityId);
        }

        private bool IsDirtyLocked(string capabilityId)
        {
            return draft.TryGetValue(capabilityId, out var d)
                && (!confirmed.TryGetValue(capabilityId, out var c) || d != c);
        }

        public string GetVisibleValue(string capabilityId, string fallback = "")
        {
            lock (sync)
            {
                if (draft.TryGetValue(capabilityId, out var d)) return d;
                if (confirmed.TryGetValue(capabilityId, out var c)) return c;
                return fallback;
            }
        }

        public string GetConfirmedValue(string capabilityId, string fallback = "")
        {
            lock (sync) return confirmed.TryGetValue(capabilityId, out var c) ? c : fallback;
        }

        public string GetError(string capabilityId)
        {
            lock (sync) return errors.TryGetValue(capabilityId, out var e) ? e : "";
        }

        /// <summary>Registers a new command as the single active request for its capability.</summary>
        public HardwareCommand CreateCommand(string capabilityId, string value)
        {
            var cmd = new HardwareCommand
            {
                RequestId = Guid.NewGuid(),
                CapabilityId = capabilityId,
                Value = value,
            };
            lock (sync)
            {
                activeRequests[capabilityId] = cmd.RequestId;
                errors.Remove(capabilityId);
            }
            return cmd;
        }

        /// <summary>
        /// Applies a command response only if its RequestId still matches the active request
        /// for that capability. Stale out-of-order responses return false and are ignored.
        /// </summary>
        public bool TryComplete(CommandResult result)
        {
            lock (sync)
            {
                if (!activeRequests.TryGetValue(result.CapabilityId, out var expectedId) || expectedId != result.RequestId)
                {
                    return false;
                }
                activeRequests.Remove(result.CapabilityId);

                if (result.Success)
                {
                    confirmed[result.CapabilityId] = result.ConfirmedValue;
                    draft[result.CapabilityId] = result.ConfirmedValue;
                    errors.Remove(result.CapabilityId);
                }
                else
                {
                    // Roll back visible draft to last confirmed value
                    if (confirmed.TryGetValue(result.CapabilityId, out var lastConfirmed))
                        draft[result.CapabilityId] = lastConfirmed;
                    errors[result.CapabilityId] = string.IsNullOrEmpty(result.ErrorMessage) ? result.ErrorCode : result.ErrorMessage;
                }
            }
            CommandCompleted?.Invoke(result.CapabilityId, result);
            return true;
        }

        public async Task<CommandResult> DispatchAsync(ISignalBackend backend, string capabilityId, string value, CancellationToken ct = default)
        {
            var cmd = CreateCommand(capabilityId, value);
            CommandResult result;
            try
            {
                result = await backend.ExecuteAsync(cmd, ct);
                if (result.RequestId != cmd.RequestId || string.IsNullOrEmpty(result.CapabilityId))
                {
                    result = new CommandResult
                    {
                        RequestId = cmd.RequestId,
                        CapabilityId = capabilityId,
                        Success = result.Success,
                        RestartRequired = result.RestartRequired,
                        ConfirmedValue = result.ConfirmedValue,
                        ErrorCode = result.ErrorCode,
                        ErrorMessage = result.ErrorMessage,
                    };
                }
            }
            catch (Exception ex)
            {
                result = new CommandResult
                {
                    RequestId = cmd.RequestId,
                    CapabilityId = capabilityId,
                    Success = false,
                    ConfirmedValue = GetConfirmedValue(capabilityId, ""),
                    ErrorCode = "EXCEPTION",
                    ErrorMessage = ex.Message,
                };
            }
            TryComplete(result);
            return result;
        }
    }

    /// <summary>
    /// Fan curve validation and monotonic editing rules shared between UI and unit tests.
    /// Curves have 8 temperature points (bytes 0..7, 20..110 °C) and 8 PWM points (bytes 8..15, 0..100 %).
    /// </summary>
    public static class FanCurveValidator
    {
        public const int PointCount = 8;
        public const int TempMin = 20;
        public const int TempMax = 110;
        public const int PwmMin = 0;
        public const int PwmMax = 100;

        public static byte[] Normalize(byte[] raw)
        {
            var curve = new byte[PointCount * 2];
            if (raw is { Length: >= PointCount * 2 })
                Array.Copy(raw, curve, PointCount * 2);

            for (int i = 0; i < PointCount; i++)
            {
                int t = Math.Clamp((int)curve[i], TempMin, TempMax);
                if (i > 0 && t <= curve[i - 1]) t = Math.Min(TempMax, curve[i - 1] + 1);
                curve[i] = (byte)t;

                int minThermalPwm = (int)Math.Ceiling(Math.Max(0, (t - 70) * 1.2));
                int p = Math.Clamp(curve[i + PointCount], Math.Max(PwmMin, minThermalPwm), PwmMax);
                if (i > 0 && p < curve[i - 1 + PointCount]) p = curve[i - 1 + PointCount];
                curve[i + PointCount] = (byte)p;
            }
            return curve;
        }

        public static void SetPoint(byte[] curve, int index, int tempC, int pwmPct, bool clampByBand = false)
        {
            if (curve is null || curve.Length < PointCount * 2) throw new ArgumentException("Invalid curve length", nameof(curve));
            if (index < 0 || index >= PointCount) throw new ArgumentOutOfRangeException(nameof(index));

            int t = Math.Clamp(tempC, TempMin, TempMax);
            if (clampByBand)
            {
                int minX = 30 + index * 10;
                int maxX = Math.Min(TempMax, minX + 9);
                t = Math.Clamp(t, minX, maxX);
            }

            int minThermalPwm = (int)Math.Ceiling(Math.Max(0, (t - 70) * 1.2));
            int p = Math.Clamp(pwmPct, Math.Max(PwmMin, minThermalPwm), PwmMax);

            curve[index] = (byte)t;
            curve[index + PointCount] = (byte)p;

            for (int i = index + 1; i < PointCount; i++)
            {
                if (curve[i] < curve[i - 1]) curve[i] = curve[i - 1];
                if (curve[i + PointCount] < curve[i - 1 + PointCount]) curve[i + PointCount] = curve[i - 1 + PointCount];
            }
            for (int i = index - 1; i >= 0; i--)
            {
                if (curve[i] > curve[i + 1]) curve[i] = curve[i + 1];
                if (curve[i + PointCount] > curve[i + 1 + PointCount]) curve[i + PointCount] = curve[i + 1 + PointCount];
            }
        }

        public static void ShiftAllPwm(byte[] curve, int deltaPwm)
        {
            if (curve is null || curve.Length < PointCount * 2) throw new ArgumentException("Invalid curve length", nameof(curve));
            for (int i = 0; i < PointCount; i++)
            {
                int minThermalPwm = (int)Math.Ceiling(Math.Max(0, (curve[i] - 70) * 1.2));
                curve[i + PointCount] = (byte)Math.Clamp(curve[i + PointCount] + deltaPwm, minThermalPwm, PwmMax);
            }
            for (int i = 1; i < PointCount; i++)
            {
                if (curve[i + PointCount] < curve[i - 1 + PointCount])
                    curve[i + PointCount] = curve[i - 1 + PointCount];
            }
        }

        public static bool IsValidMonotonic(byte[] curve)
        {
            if (curve is null || curve.Length < PointCount * 2) return false;
            for (int i = 0; i < PointCount; i++)
            {
                if (curve[i] < TempMin || curve[i] > TempMax) return false;
                if (curve[i + PointCount] > PwmMax) return false;
                if (i > 0)
                {
                    if (curve[i] < curve[i - 1]) return false;
                    if (curve[i + PointCount] < curve[i - 1 + PointCount]) return false;
                }
            }
            return true;
        }
    }
}
