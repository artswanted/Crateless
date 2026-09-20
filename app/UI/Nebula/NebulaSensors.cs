namespace GHelper.UI.Nebula
{
    /// <summary>
    /// Sensor snapshot for the shell. Read on a worker thread once per second while the window
    /// is visible; pages only read the fields. Uses the existing HardwareControl readers and
    /// never wakes a sleeping dGPU (NvidiaGpuControl returns null for it).
    /// </summary>
    public static class NebulaSensors
    {
        public const int HistoryLength = 90;

        public static readonly Queue<float> CpuHistory = new();
        public static readonly Queue<float> GpuHistory = new();

        public static int? IgpuUse, IgpuTemp, IgpuPower;
        public static float? DgpuPower;
        public static DateTime LastRead = DateTime.MinValue;

        public static int? CpuMhz;
        public static int? GpuMhz, GpuMemMhz;          // dGPU current clocks (NVAPI), only while awake
        public static int? GpuMv;                      // dGPU core voltage in mV when the driver reports it
        public static int? RamMhz;                     // configured memory speed (WMI, read once)
        private static NvAPIWrapper.GPU.PhysicalGPU? nvGpu;
        private static bool nvFailed, ramRead;
        public static (long usedGb, long totalGb)? Disk;

        private static System.Diagnostics.PerformanceCounter? cpuPerf;
        private static bool cpuPerfFailed;
        private static readonly Lazy<int> cpuBaseMhz = new(() =>
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                return key?.GetValue("~MHz") is int v ? v : 0;
            }
            catch { return 0; }
        });
        private static int diskTick;

        // AppConfig.IsAMDiGPU() means "iGPU-only laptop"; here we want "has an AMD iGPU at all".
        public static readonly bool HasIgpu = AppConfig.IsAMDiGPU() || PawnIO.CpuInfo.Name.Contains("Radeon", StringComparison.OrdinalIgnoreCase);

        /// <summary>Blocking read; call from a worker thread.</summary>
        public static void Read()
        {
            HardwareControl.ReadSensors();
            HardwareControl.cpuUsage = HardwareControl.GetCPUUsage();
            HardwareControl.InitCPUPowerAsync();
            HardwareControl.cpuPower = HardwareControl.GetCPUPower();

            try
            {
                var gc = HardwareControl.GpuControl;
                bool awake = HardwareControl.gpuTemp is > 0;
                HardwareControl.gpuUsage = awake ? gc?.GetGpuUse() : null;
                DgpuPower = awake ? gc?.GetGpuPower() : null;
            }
            catch { HardwareControl.gpuUsage = null; DgpuPower = null; }

            if (HasIgpu)
            {
                try
                {
                    var i = HardwareControl.AmdApu().GetiGpuSensors();
                    IgpuUse = i.use; IgpuTemp = i.temp; IgpuPower = i.gfxPower;
                }
                catch { IgpuUse = IgpuTemp = IgpuPower = null; }
            }

            var ram = HardwareControl.GetRAMInfo();
            HardwareControl.ramUsage = ram?.percent;
            HardwareControl.ramUsedMb = ram?.usedMb;

            // CPU frequency: % Processor Performance × base clock (same source Task Manager uses)
            if (!cpuPerfFailed)
            {
                try
                {
                    cpuPerf ??= new System.Diagnostics.PerformanceCounter("Processor Information", "% Processor Performance", "_Total", true);
                    float perf = cpuPerf.NextValue();
                    int baseMhz = cpuBaseMhz.Value;
                    CpuMhz = perf > 0 && baseMhz > 0 ? (int)Math.Round(baseMhz * perf / 100f) : null;
                }
                catch { cpuPerfFailed = true; CpuMhz = null; }
            }

            // dGPU clocks through NVAPI; skipped while the GPU sleeps so we never wake it
            if (!nvFailed && HardwareControl.gpuTemp is > 0 && HardwareControl.GpuControl?.IsNvidia == true)
            {
                try
                {
                    nvGpu ??= NvAPIWrapper.GPU.PhysicalGPU.GetPhysicalGPUs().FirstOrDefault(g => g.SystemType == NvAPIWrapper.Native.GPU.SystemType.Laptop);
                    var clocks = nvGpu?.CurrentClockFrequencies;
                    GpuMhz = clocks is null ? null : (int)(clocks.GraphicsClock.Frequency / 1000);
                    GpuMemMhz = clocks is null ? null : (int)(clocks.MemoryClock.Frequency / 1000);
                    try
                    {
                        var ps = nvGpu?.PerformanceStatesInfo;
                        var v = ps?.CurrentPerformanceState?.Voltages?.FirstOrDefault();
                        GpuMv = v is null || v.CurrentVoltageInMicroVolt == 0 ? null : (int)(v.CurrentVoltageInMicroVolt / 1000);
                    }
                    catch { GpuMv = null; }
                }
                catch { nvFailed = true; GpuMhz = GpuMemMhz = GpuMv = null; }
            }
            else { GpuMhz = GpuMemMhz = GpuMv = null; }

            if (!ramRead)
            {
                ramRead = true;
                try
                {
                    using var searcher = new System.Management.ManagementObjectSearcher("SELECT ConfiguredClockSpeed, Speed FROM Win32_PhysicalMemory");
                    foreach (System.Management.ManagementObject mo in searcher.Get())
                    {
                        int v = Convert.ToInt32(mo["ConfiguredClockSpeed"] ?? mo["Speed"] ?? 0);
                        if (v > 0) { RamMhz = v; break; }
                    }
                }
                catch { RamMhz = null; }
            }

            // system drive, every 30 s
            if (diskTick++ % 30 == 0)
            {
                try
                {
                    var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\");
                    long total = d.TotalSize / (1024L * 1024 * 1024);
                    long used = (d.TotalSize - d.TotalFreeSpace) / (1024L * 1024 * 1024);
                    Disk = (used, total);
                }
                catch { Disk = null; }
            }
        }

        /// <summary>Call on the UI thread after Read().</summary>
        public static void Commit()
        {
            LastRead = DateTime.Now;
            Push(CpuHistory, HardwareControl.cpuTemp);
            Push(GpuHistory, HardwareControl.gpuTemp);
        }

        private static void Push(Queue<float> q, float? v)
        {
            q.Enqueue(v is > 0 ? v.Value : float.NaN);
            while (q.Count > HistoryLength) q.Dequeue();
        }
    }
}
