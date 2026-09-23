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
        /// <summary>One entry per physical disk, newest reading; the label lists its drive letters.</summary>
        public static List<(string label, long usedGb, long totalGb)> Disks = new();
        private static List<(string label, string[] roots)>? diskLayout;
        private const long Gb = 1024L * 1024 * 1024;

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

            // drives, every 30 s; the layout itself is re-read every ten minutes so a disk added
            // while the app is running still shows up
            if (diskTick % 30 == 0)
            {
                if (diskTick % 600 == 0) diskLayout = null;
                ReadDisks();
            }
            diskTick++;
        }

        /// <summary>
        /// Usage per physical disk. A single SSD split into partitions is still one disk, so its
        /// volumes are summed; two disks stay two lines.
        /// </summary>
        private static void ReadDisks()
        {
            try
            {
                diskLayout ??= ReadDiskLayout();
                var list = new List<(string, long, long)>();
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
                    if (total > 0) list.Add((label, used / Gb, total / Gb));
                }
                Disks = list;
            }
            catch (Exception ex) { Logger.WriteLine("Drives: " + ex.Message); }
        }

        /// <summary>
        /// Which volumes sit on which physical disk, through WMI. When that cannot be answered we
        /// fall back to a line per fixed volume, which is what the page used to show.
        /// </summary>
        private static List<(string label, string[] roots)> ReadDiskLayout()
        {
            var disks = new List<(string label, string[] roots)>();
            try
            {
                using var drives = new System.Management.ManagementObjectSearcher("SELECT DeviceID FROM Win32_DiskDrive");
                foreach (System.Management.ManagementObject drive in drives.Get())
                {
                    // the device id looks like \.\PHYSICALDRIVE0 and goes into a WQL string, where a backslash escapes
                    string id = (drive["DeviceID"]?.ToString() ?? "").Replace("\\", "\\\\");
                    if (id.Length == 0) continue;

                    var roots = new List<string>();
                    using var parts = new System.Management.ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID='{id}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                    foreach (System.Management.ManagementObject part in parts.Get())
                    {
                        using var volumes = new System.Management.ManagementObjectSearcher(
                            $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{part["DeviceID"]}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
                        foreach (System.Management.ManagementObject volume in volumes.Get())
                            if (volume["DeviceID"]?.ToString() is { Length: > 0 } letter) roots.Add(letter + "\\");
                    }
                    if (roots.Count > 0) disks.Add((string.Join(" ", roots.Select(r => r[..2])), roots.ToArray()));
                }
            }
            catch (Exception ex) { Logger.WriteLine("Disk layout: " + ex.Message); }

            if (disks.Count == 0)
                foreach (var d in DriveInfo.GetDrives())
                    try { if (d.DriveType == DriveType.Fixed && d.IsReady) disks.Add((d.Name[..2], new[] { d.Name })); }
                    catch { }

            // the disk Windows lives on goes first
            string system = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            return disks.OrderByDescending(d => d.roots.Contains(system, StringComparer.OrdinalIgnoreCase))
                        .ThenBy(d => d.label, StringComparer.OrdinalIgnoreCase)
                        .ToList();
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
