namespace GHelper.Mode
{
    /// <summary>
    /// What the performance mode should do when the charger comes off or goes back on.
    /// 
    /// The rule is written per mode, because the sensible answer depends on where you are: Turbo
    /// on battery may be pointless on a machine that does not allow it, while Balanced is usually
    /// fine to keep. Each mode therefore carries its own target for battery and for mains, and the
    /// mains side can also mean "back to whatever I was in before the charger came off".
    /// </summary>
    internal static class PowerRules
    {
        /// <summary>Stay in the mode that is already active.</summary>
        public const int Keep = -1;

        /// <summary>Return to the mode that was active when the charger came off.</summary>
        public const int Restore = -2;

        public static bool Enabled => AppConfig.Is("power_rules");

        public static void SetEnabled(bool on)
        {
            if (on) EnsureDefaults();
            AppConfig.Set("power_rules", on ? 1 : 0);
        }

        public static int OnBattery(int mode) => AppConfig.Get("power_rule_bat_" + mode, Keep);
        public static int OnAc(int mode) => AppConfig.Get("power_rule_ac_" + mode, Keep);

        public static void SetOnBattery(int mode, int target) => AppConfig.Set("power_rule_bat_" + mode, target);
        public static void SetOnAc(int mode, int target) => AppConfig.Set("power_rule_ac_" + mode, target);

        /// <summary>
        /// The first time the rules are switched on they get a starting point rather than an empty
        /// table: Turbo steps down to Balanced on battery, everything else stays, and plugging in
        /// returns to the mode you were in.
        /// </summary>
        private static void EnsureDefaults()
        {
            if (AppConfig.Is("power_rules_set")) return;
            AppConfig.Set("power_rules_set", 1);
            foreach (int mode in Modes.GetList())
                SetOnAc(mode, Restore);
            SetOnBattery(AsusACPI.PerformanceTurbo, AsusACPI.PerformanceBalanced);
        }

        /// <summary>
        /// The mode to be in after the power source changed, or <see cref="Keep"/> when the rule
        /// says to stay. Going to battery also records the mode to come back to.
        /// </summary>
        public static int Resolve(bool onAc)
        {
            int current = Modes.GetCurrent();
            int target;

            if (!onAc)
            {
                AppConfig.Set("power_rule_return", current);
                target = OnBattery(current);
            }
            else
            {
                target = OnAc(current);
                if (target == Restore) target = AppConfig.Get("power_rule_return", Keep);
            }

            if (target < 0 || target == current || !Modes.Exists(target)) return Keep;
            return target;
        }

        /// <summary>The rule as a line of text for the page, already resolved for display.</summary>
        public static string Describe(int target, bool onAc, Func<int, string> name, string keep, string restore)
        {
            if (target == Keep) return keep;
            if (target == Restore && onAc) return restore;
            return Modes.Exists(target) ? name(target) : keep;
        }
    }
}
