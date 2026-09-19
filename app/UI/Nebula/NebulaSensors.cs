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
