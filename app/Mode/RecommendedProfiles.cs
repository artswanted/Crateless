namespace GHelper.Mode
{
    /// <summary>Tuned power limits and fan curves for the three stock modes, offered only on the models they were tuned on.
    /// The built-in preset can be replaced by a snapshot of the current settings ("save current as recommended").</summary>
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

        private static readonly int[] StockModes = { AsusACPI.PerformanceBalanced, AsusACPI.PerformanceTurbo, AsusACPI.PerformanceSilent };

        // per-mode keys a snapshot covers; a key missing from the config is kept as "missing" and removed again on apply
        private static readonly string[] IntKeys = { "limit_total", "limit_slow", "limit_fast", "limit_cpu", "limit_crossload", "limit_gpucpu", "limit_cputemp", "auto_apply_power", "auto_apply", "hysteresis_up", "hysteresis_down" };
        private static readonly string[] CurveKeys = { "fan_profile_cpu", "fan_profile_gpu", "fan_profile_mid" };

        private const string SavedKey = "recommended_saved";

        private static Preset[]? Presets => AppConfig.ContainsModel("GA605W") ? GA605W : null;

        public static bool IsAvailable => Presets is not null;

        public static bool HasSaved => !string.IsNullOrEmpty(AppConfig.GetString(SavedKey));

        /// <summary>Writes all three stock modes at once (the saved snapshot if there is one, else the built-in preset) and re-applies the current mode.</summary>
        public static void Apply()
        {
            if (Presets is not { } presets) return;
            var snapshot = LoadSaved() ?? BuiltIn(presets);

            foreach (var (key, value) in snapshot)
            {
                if (value is null) AppConfig.Remove(key);
                else if (IsCurve(key)) AppConfig.Set(key, value);
                else if (int.TryParse(value, out int v)) AppConfig.Set(key, v);
            }

            Program.modeControl.SetPerformanceMode();
        }

        /// <summary>Remembers the current limits, curves and hysteresis of all three stock modes as the recommended set.</summary>
        public static void SaveCurrent()
        {
            var lines = AllKeys().Select(key =>
            {
                string? value = !AppConfig.Exists(key) ? null
                              : IsCurve(key) ? AppConfig.GetString(key)
                              : AppConfig.Get(key, int.MinValue) is int v && v != int.MinValue ? v.ToString() : null;
                return key + "=" + value;
            });
            AppConfig.Set(SavedKey, string.Join("\n", lines));
        }

        /// <summary>Drops the saved snapshot, so "recommended" means the built-in preset again.</summary>
        public static void ForgetSaved() => AppConfig.Remove(SavedKey);

        private static Dictionary<string, string?>? LoadSaved()
        {
            string? raw = AppConfig.GetString(SavedKey);
            if (string.IsNullOrEmpty(raw)) return null;

            var known = AllKeys().ToHashSet();
            var map = new Dictionary<string, string?>();
            foreach (var line in raw.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line[..eq];
                if (!known.Contains(key)) continue;
                string value = line[(eq + 1)..];
                map[key] = value.Length > 0 ? value : null;
            }
            return map.Count > 0 ? map : null;
        }

        private static Dictionary<string, string?> BuiltIn(Preset[] presets)
        {
            var map = AllKeys().ToDictionary(k => k, k => (string?)null);
            foreach (var p in presets)
            {
                string m = "_" + p.Mode;
                map["limit_total" + m] = p.Total.ToString();
                map["limit_slow" + m] = p.Slow.ToString();
                map["limit_fast" + m] = p.Fast.ToString();
                map["limit_cpu" + m] = p.Cpu.ToString();
                map["auto_apply_power" + m] = "1";
                // auto_apply stays off: the curves still reach the fans, these models need them whenever limits are applied (IsFanRequired)
                map["fan_profile_cpu" + m] = p.CpuFan;
                map["fan_profile_gpu" + m] = p.GpuFan;
                map["fan_profile_mid" + m] = p.MidFan;
            }
            return map;
        }

        private static IEnumerable<string> AllKeys() =>
            StockModes.SelectMany(m => IntKeys.Concat(CurveKeys).Select(k => k + "_" + m));

        private static bool IsCurve(string key) => key.StartsWith("fan_profile_");
    }
}
