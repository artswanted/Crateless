using System.Runtime.InteropServices;

namespace GHelper.Battery
{
    /// <summary>
    /// The idle timeouts of the power plan Windows is currently using: when the screen goes off,
    /// when the machine sleeps and when it hibernates, separately for the charger and the battery.
    /// Read and written straight on the active scheme, so the Control Panel page is not needed.
    /// </summary>
    public static class PowerPlan
    {
        /// <summary>The three timeouts we expose, in the order they happen.</summary>
        public enum Idle { ScreenOff, Sleep, Hibernate }

        private static readonly Guid SubVideo = new("7516b95f-f776-4464-8c53-06167f40cc99");
        private static readonly Guid VideoIdle = new("3c0bc021-c8a8-4e07-a973-6b14cbcb2b7e");
        private static readonly Guid SubSleep = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
        private static readonly Guid StandbyIdle = new("29f6c1db-86da-48c5-9fdb-f2b67b1f44da");
        private static readonly Guid HibernateIdle = new("9d7815a6-7ee4-497e-8888-515a05f02364");

        private static (Guid sub, Guid setting) Keys(Idle what) => what switch
        {
            Idle.ScreenOff => (SubVideo, VideoIdle),
            Idle.Sleep => (SubSleep, StandbyIdle),
            _ => (SubSleep, HibernateIdle),
        };

        [DllImport("powrprof.dll")]
        private static extern uint PowerGetActiveScheme(IntPtr UserPowerKey, out IntPtr ActivePolicyGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerSetActiveScheme(IntPtr RootPowerKey, [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadACValueIndex(IntPtr RootPowerKey,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SubGroupGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SettingGuid, out uint Value);

        [DllImport("powrprof.dll")]
        private static extern uint PowerReadDCValueIndex(IntPtr RootPowerKey,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SubGroupGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SettingGuid, out uint Value);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteACValueIndex(IntPtr RootPowerKey,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SubGroupGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SettingGuid, uint Value);

        [DllImport("powrprof.dll")]
        private static extern uint PowerWriteDCValueIndex(IntPtr RootPowerKey,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SubGroupGuid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SettingGuid, uint Value);

        [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
        private static extern uint PowerReadFriendlyName(IntPtr RootPowerKey,
            [MarshalAs(UnmanagedType.LPStruct)] Guid SchemeGuid,
            IntPtr SubGroupGuid, IntPtr SettingGuid, IntPtr Buffer, ref uint BufferSize);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        /// <summary>The scheme Windows is on right now. Every read and write goes to this one.</summary>
        public static Guid Active()
        {
            IntPtr p = IntPtr.Zero;
            try
            {
                if (PowerGetActiveScheme(IntPtr.Zero, out p) != 0 || p == IntPtr.Zero) return Guid.Empty;
                return Marshal.PtrToStructure<Guid>(p);
            }
            catch (Exception ex) { Logger.WriteLine("Power plan: " + ex.Message); return Guid.Empty; }
            finally { if (p != IntPtr.Zero) LocalFree(p); }
        }

        /// <summary>Name of the active plan as the Control Panel shows it, or "" when it cannot be read.</summary>
        public static string PlanName()
        {
            Guid scheme = Active();
            if (scheme == Guid.Empty) return "";
            uint size = 0;
            if (PowerReadFriendlyName(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ref size) != 0 || size == 0) return "";
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                if (PowerReadFriendlyName(IntPtr.Zero, scheme, IntPtr.Zero, IntPtr.Zero, buf, ref size) != 0) return "";
                return Marshal.PtrToStringUni(buf) ?? "";
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        /// <summary>Seconds before the timeout fires; 0 means never, -1 means the plan does not report it.</summary>
        public static int Get(Idle what, bool onAc)
        {
            Guid scheme = Active();
            if (scheme == Guid.Empty) return -1;
            var (sub, setting) = Keys(what);
            uint value;
            uint hr = onAc
                ? PowerReadACValueIndex(IntPtr.Zero, scheme, sub, setting, out value)
                : PowerReadDCValueIndex(IntPtr.Zero, scheme, sub, setting, out value);
            if (hr != 0) return -1;
            // an unset timeout comes back as 0xFFFFFFFF, and anything past a day means the same thing
            return value > 86400 ? 0 : (int)value;
        }

        /// <summary>
        /// Writes one timeout and re-activates the scheme, which is what makes Windows pick the new
        /// value up. Returns false when the plan refused the write.
        /// </summary>
        public static bool Set(Idle what, bool onAc, int seconds)
        {
            Guid scheme = Active();
            if (scheme == Guid.Empty) return false;
            var (sub, setting) = Keys(what);
            uint hr = onAc
                ? PowerWriteACValueIndex(IntPtr.Zero, scheme, sub, setting, (uint)Math.Max(0, seconds))
                : PowerWriteDCValueIndex(IntPtr.Zero, scheme, sub, setting, (uint)Math.Max(0, seconds));
            if (hr != 0)
            {
                Logger.WriteLine($"Power plan: {what} {(onAc ? "AC" : "DC")} = {seconds}s failed ({hr})");
                return false;
            }
            PowerSetActiveScheme(IntPtr.Zero, scheme);
            Logger.WriteLine($"Power plan: {what} {(onAc ? "AC" : "DC")} = {seconds}s");
            return true;
        }
    }
}
