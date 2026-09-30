namespace GHelper.Mode
{
    /// <summary>Tuned power limits and fan curves for the three stock modes, offered only on the models they were tuned on.</summary>
    internal static class RecommendedProfiles
    {
        private sealed record Preset(int Mode, int Total, int Slow, int Fast, int Cpu, string? CpuFan, string? GpuFan, string? MidFan);

        // Zephyrus G16 GA605W*. No curves for Silent: BIOS keeps its own there.
        private static readonly Preset[] GA605W =
        {
            new(AsusACPI.PerformanceBalanced, 45, 55, 65, 45,
                "1E-32-3C-40-48-50-5A-64-00-08-14-1C-41-52-5F-64",
                "1E-32-3C-40-48-50-5A-64-00-0C-19-20-3A-48-58-64",
                "1E-32-3C-40-48-50-5A-64-00-08-14-1C-41-52-5F-64"),
            new(AsusACPI.PerformanceTurbo, 60, 70, 80, 60,
                "1E-32-3C-40-48-50-5A-64-00-0F-1E-2A-48-5A-64-64",
                "1E-32-3C-40-48-50-5A-64-00-12-23-2D-4B-5C-64-64",
                "1E-32-3C-40-48-50-5A-64-00-0F-1E-2A-48-5A-64-64"),
            new(AsusACPI.PerformanceSilent, 15, 25, 40, 15, null, null, null),
        };

        private static Preset[]? Presets => AppConfig.ContainsModel("GA605W") ? GA605W : null;

        public static bool IsAvailable => Presets is not null;

        /// <summary>Writes all three stock modes at once and re-applies the current one.</summary>
        public static void Apply()
        {
            if (Presets is not { } presets) return;

            foreach (var p in presets)
            {
                string m = "_" + p.Mode;
                AppConfig.Set("limit_total" + m, p.Total);
                AppConfig.Set("limit_slow" + m, p.Slow);
                AppConfig.Set("limit_fast" + m, p.Fast);
                AppConfig.Set("limit_cpu" + m, p.Cpu);
                AppConfig.Remove("limit_crossload" + m);
                AppConfig.Remove("limit_gpucpu" + m);
                AppConfig.Remove("limit_cputemp" + m);
                AppConfig.Set("auto_apply_power" + m, 1);

                // the curves still reach the fans: these models need them whenever limits are applied (IsFanRequired)
                AppConfig.Remove("auto_apply" + m);
                SetCurve("cpu", m, p.CpuFan);
                SetCurve("gpu", m, p.GpuFan);
                SetCurve("mid", m, p.MidFan);
            }

            Program.modeControl.SetPerformanceMode();
        }

        private static void SetCurve(string fan, string mode, string? curve)
        {
            string key = "fan_profile_" + fan + mode;
            if (curve is null) AppConfig.Remove(key);
            else AppConfig.Set(key, curve);
        }
    }
}
