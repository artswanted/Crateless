using GHelper;
using GHelper.UI;
using GHelper.UI.SignalGlass;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Crateless.Tests
{
    internal static class Program
    {
        private static int passed;
        private static int failed;
        private static string? screenshotDir;

        [DllImport("user32.dll")]
        private static extern uint GetGuiResources(IntPtr hProcess, uint uiFlags);

        [STAThread]
        public static int Main(string[] args)
        {
            // Redirect both CRATELESS_CONFIG_DIR and APPDATA to a clean temp directory BEFORE JIT touches AppConfig
            string tempRoot = Path.Combine(Path.GetTempPath(), "CratelessTests_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(tempRoot);
            Environment.SetEnvironmentVariable("CRATELESS_CONFIG_DIR", tempRoot);
            Environment.SetEnvironmentVariable("APPDATA", tempRoot);

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--screenshots" && i + 1 < args.Length)
                    screenshotDir = args[++i];
            }

            try
            {
                return RunAllNoInline();
            }
            finally
            {
                try { Directory.Delete(tempRoot, recursive: true); } catch { }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int RunAllNoInline()
        {
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Run("UiShell_ThemeFlag_And_FirstLaunchPrompt", TestUiShellAndFirstLaunch);
            Run("SignalTheme_Tokens_And_WCAG_Contrast", TestThemeTokensAndContrast);
            Run("SignalAssets_EmbeddedResources_AllLoaded", TestSignalAssets);
            Run("RingHistoryBuffer_WindowAndGapSemantics", TestRingHistoryBuffer);
            Run("CommandCoordinator_Concurrency_StaleDrop_Rollback", TestCommandCoordinator);
            Run("FanCurveValidator_Monotonicity_ThermalFloor", TestFanCurveValidator);
            Run("SignalLayout_Responsive_RTL_DPI", TestSignalLayout);
            Run("ShellPickerDialog_Render_And_Accessibility", TestShellPickerDialog);
            Run("LidLightingHelper_SlashAndMatrixMetadata", TestSlashAndClassicShellControl);
            Run("MockSignalBackend_States_Forms_And_Stress500", TestFormsStatesAccessibilityAndStress);

            Console.WriteLine();
            Console.WriteLine($"Summary: {passed} passed, {failed} failed.");
            return failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action body)
        {
            try
            {
                body();
                passed++;
                Console.WriteLine($"[PASS] {name}");
            }
            catch (Exception ex)
            {
                failed++;
                Console.Error.WriteLine($"[FAIL] {name}: {ex}");
            }
        }

        private static void Assert(bool cond, string message)
        {
            if (!cond) throw new Exception(message);
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"{message}: expected '{expected}', got '{actual}'");
        }

        private static void TestUiShellAndFirstLaunch()
        {
            // Clean config => no theme, not prompted
            Assert(UiShell.NeedsFirstLaunchPrompt, "First launch should prompt when no theme flag or ui_shell_prompted exists");
            AssertEqual(UiShellMode.Classic, UiShell.Current, "Missing theme defaults safely to Classic parser until chosen");

            AssertEqual(UiShellMode.SignalGlass, UiShell.Parse("signal"), "Parse 'signal'");
            AssertEqual(UiShellMode.SignalGlass, UiShell.Parse("SIGNAL-GLASS"), "Parse 'SIGNAL-GLASS'");
            AssertEqual(UiShellMode.SignalGlass, UiShell.Parse("SignalGlass"), "Parse 'SignalGlass'");
            AssertEqual(UiShellMode.Nebula, UiShell.Parse("nebula"), "Parse 'nebula'");
            AssertEqual(UiShellMode.Classic, UiShell.Parse("classic"), "Parse 'classic'");
            AssertEqual(UiShellMode.Classic, UiShell.Parse("flat"), "Parse 'flat'");
            AssertEqual(UiShellMode.Classic, UiShell.Parse(""), "Parse empty");

            UiShell.ApplySelection(UiShellMode.SignalGlass);
            Assert(!UiShell.NeedsFirstLaunchPrompt, "Prompt flag should be cleared after selection");
            AssertEqual("signal", AppConfig.GetString("theme"), "Config theme key");
            Assert(UiShell.IsSignalGlass, "IsSignalGlass");

            UiShell.ApplySelection(UiShellMode.Nebula);
            AssertEqual("nebula", AppConfig.GetString("theme"), "Config nebula key");
            Assert(UiShell.IsNebula, "IsNebula");

            UiShell.ApplySelection(UiShellMode.Classic);
            AssertEqual("classic", AppConfig.GetString("theme"), "Config classic key");
            Assert(UiShell.IsClassic, "IsClassic");

            UiShell.ApplySelection(UiShellMode.SignalGlass);
        }

        private static void TestThemeTokensAndContrast()
        {
            foreach (var (name, th) in new[] { ("Dark", SignalTheme.Dark), ("Light", SignalTheme.Light) })
            {
                Assert(SignalTheme.ContrastRatio(th.Text, th.Bg) >= 4.5, $"{name} Text/Bg contrast");
                Assert(SignalTheme.ContrastRatio(th.Text, th.Surface) >= 4.5, $"{name} Text/Surface contrast");
                Assert(SignalTheme.ContrastRatio(th.Text, th.Raised) >= 4.5, $"{name} Text/Raised contrast");
                Assert(SignalTheme.ContrastRatio(th.Muted, th.Bg) >= 4.5, $"{name} Muted/Bg contrast");
                Assert(SignalTheme.ContrastRatio(th.Muted, th.Surface) >= 4.5, $"{name} Muted/Surface contrast");
                Assert(SignalTheme.ContrastRatio(th.OnAccent, th.Accent) >= 4.5, $"{name} OnAccent/Accent contrast");
                Assert(SignalTheme.ContrastRatio(th.Accent, th.Surface) >= 3.0, $"{name} Accent/Surface contrast");
                Assert(SignalTheme.ContrastRatio(th.Gpu, th.Surface) >= 3.0, $"{name} Gpu/Surface contrast");
                Assert(SignalTheme.ContrastRatio(th.Danger, th.Surface) >= 3.0, $"{name} Danger/Surface contrast");
                Assert(SignalTheme.ContrastRatio(th.Warning, th.Surface) >= 3.0, $"{name} Warning/Surface contrast");
                Assert(SignalTheme.ContrastRatio(th.Line, th.Surface) >= 3.0, $"{name} Line/Surface contrast");
            }

            var prevGlass = SignalTheme.GlassIntensity;
            SignalTheme.GlassIntensity = GlassIntensityMode.Opaque;
            Assert(SignalTheme.EffectiveOpaque, "Opaque mode forces EffectiveOpaque");
            SignalTheme.GlassIntensity = prevGlass;
        }

        private static void TestSignalAssets()
        {
            Assert(SignalAssets.Background(true, false) is not null, "Dark bg missing");
            Assert(SignalAssets.Background(false, false) is not null, "Light bg missing");
            Assert(SignalAssets.Background(false, true) is not null, "Light@2x bg missing");

            foreach (int sz in new[] { 16, 24, 32, 48, 256 })
                Assert(SignalAssets.Logo(sz) is not null, $"Logo {sz} missing");

            using (var appIcon = SignalAssets.CloneAppIcon())
                Assert(appIcon is not null && appIcon.Width > 0, "crateless.ico missing");

            foreach (int gpu in new[] { AsusACPI.GPUModeEco, AsusACPI.GPUModeStandard, AsusACPI.GPUModeUltimate })
            foreach (bool auto in new[] { false, true })
            foreach (bool dark in new[] { false, true })
            {
                using var ico = SignalAssets.CloneTrayIcon(gpu, auto, dark);
                Assert(ico is not null && ico.Width > 0, $"Tray icon missing gpu={gpu} auto={auto} dark={dark}");
            }

            foreach (string icon in new[] { "fan", "gpu", "display", "battery", "power", "light", "keyboard", "mouse", "wallpaper", "updates", "settings" })
            foreach (bool dark in new[] { true, false })
            {
                Assert(SignalAssets.IconScaled(icon, dark, 18) is not null, $"Icon {icon} (dark={dark}) missing");
            }

            foreach (string model in new[] { "ROG Zephyrus G14", "ROG Strix SCAR", "ASUS TUF A15", "ROG Flow X13", "ROG Ally X", "ASUS VivoBook Pro" })
            {
                Assert(SignalAssets.FamilySilhouette(model, true) is not null, $"Silhouette for {model} missing");
            }
        }

        private static void TestRingHistoryBuffer()
        {
            var buf = new RingHistoryBuffer(60);
            for (int i = 0; i < 65; i++)
                buf.Append(i == 61 ? null : (float)i);

            AssertEqual(60, buf.Count, "Capacity capped at 60");
            AssertEqual(59, buf.ValidCount, "Null gap excluded from ValidCount");
            var snap = buf.Snapshot();
            AssertEqual(60, snap.Length, "Snapshot length");
            AssertEqual(5f, snap[0], "Oldest dropped cleanly");
            Assert(snap[56] is null, "Null gap preserved without fabricating zero");
            AssertEqual(64f, snap[59], "Latest sample preserved");
        }

        private static void TestCommandCoordinator()
        {
            var coord = new CommandCoordinator();
            coord.SeedObserved("performance_mode", "0");
            coord.SeedObserved("charge_limit", "80");

            // Stale out-of-order response must be dropped
            var cmd1 = coord.CreateCommand("performance_mode", "1");
            Assert(coord.IsPending("performance_mode"), "Command 1 pending");
            var cmd2 = coord.CreateCommand("performance_mode", "2");

            bool appliedStale = coord.TryComplete(new CommandResult
            {
                RequestId = cmd1.RequestId,
                CapabilityId = "performance_mode",
                Success = true,
                ConfirmedValue = "1",
            });
            Assert(!appliedStale, "Stale cmd1 response must be ignored");
            Assert(coord.IsPending("performance_mode"), "cmd2 still pending");

            bool appliedNew = coord.TryComplete(new CommandResult
            {
                RequestId = cmd2.RequestId,
                CapabilityId = "performance_mode",
                Success = true,
                ConfirmedValue = "2",
            });
            Assert(appliedNew, "Latest cmd2 must apply");
            Assert(!coord.IsPending("performance_mode"), "Pending cleared");
            AssertEqual("2", coord.GetConfirmedValue("performance_mode"), "Confirmed updated to 2");

            // Error rollback on draft
            coord.SetDraft("charge_limit", "60");
            Assert(coord.IsDirty("charge_limit"), "Draft dirty before apply");
            var cmdErr = coord.CreateCommand("charge_limit", "60");
            coord.TryComplete(new CommandResult
            {
                RequestId = cmdErr.RequestId,
                CapabilityId = "charge_limit",
                Success = false,
                ConfirmedValue = "80",
                ErrorCode = "EC_ERR",
                ErrorMessage = "Write failed",
            });
            AssertEqual("80", coord.GetVisibleValue("charge_limit"), "Failed command rolls draft back to confirmed");
            Assert(!string.IsNullOrEmpty(coord.GetError("charge_limit")), "Error recorded");
        }

        private static void TestFanCurveValidator()
        {
            byte[] dirty = { 10, 95, 90, 50, 60, 75, 85, 130, 5, 60, 40, 20, 30, 50, 90, 120 };
            var norm = FanCurveValidator.Normalize(dirty);
            Assert(FanCurveValidator.IsValidMonotonic(norm), "Normalize produces valid monotonic curve");
            Assert(norm[0] >= FanCurveValidator.TempMin && norm[7] <= FanCurveValidator.TempMax, "Temp clamped");
            Assert(norm[15] <= FanCurveValidator.PwmMax, "PWM clamped");

            // Non-overheating safety rule: above 70 C minimum thermal PWM > 0
            for (int i = 0; i < FanCurveValidator.PointCount; i++)
            {
                if (norm[i] > 70)
                    Assert(norm[i + FanCurveValidator.PointCount] >= (int)Math.Ceiling((norm[i] - 70) * 1.2), "Thermal safety PWM floor");
            }

            FanCurveValidator.SetPoint(norm, 3, 68, 75);
            Assert(FanCurveValidator.IsValidMonotonic(norm), "SetPoint maintains monotonicity");
            FanCurveValidator.ShiftAllPwm(norm, -10);
            Assert(FanCurveValidator.IsValidMonotonic(norm), "ShiftAllPwm maintains monotonicity");
        }

        private static void TestSignalLayout()
        {
            foreach (var (w, h, expectCompact, expectMid) in new[]
            {
                (1440, 1060, false, false),
                (1100, 800,  false, true),
                (900,  640,  true,  false),
            })
            {
                var l = SignalShellLayout.Compute(w, h, 96f, rtl: false, drawerOpen: false);
                AssertEqual(expectCompact, l.CompactWidth, $"{w}x{h} CompactWidth");
                AssertEqual(expectMid, l.MidWidth, $"{w}x{h} MidWidth");
                if (!expectCompact) Assert(l.Inspector.X > l.Workspace.Right, $"{w}x{h} LTR Inspector on right");
                Assert(l.BottomCooling.Bottom <= l.Workspace.Bottom + 0.5f, $"{w}x{h} Bottom cards fit without scroll");
                Assert(l.CpuPlot.Width >= 200f && l.GpuPlot.Width >= 200f, $"{w}x{h} Plots have usable width");

                if (expectCompact)
                {
                    var open = SignalShellLayout.Compute(w, h, 96f, rtl: false, drawerOpen: true);
                    Assert(open.Inspector.Right <= w, "Drawer opens inside viewport");
                    Assert(!l.CompactSearch && l.SearchBox.Width >= 200f && l.SearchBox.Right <= l.TopNav.Right, "Collapsed drawer frees full search bar at 900w");
                }
                else if (expectMid)
                {
                    Assert(l.CompactSearch, "Compact search button at 1100w with open inspector");
                }
            }

            // RTL checks
            var rtl = SignalShellLayout.Compute(1440, 1060, 96f, rtl: true, drawerOpen: false);
            Assert(rtl.Inspector.Right < rtl.Workspace.X, "RTL Inspector mirrored to left");
            Assert(rtl.CpuPlot.Width >= 200f, "RTL CPU plot preserves width");

            // DPI checks: 100%, 125%, 150%, 200%
            foreach (float dpi in new[] { 96f, 120f, 144f, 192f })
            {
                float sc = dpi / 96f;
                var scaled = SignalShellLayout.Compute((int)(1440 * sc), (int)(1060 * sc), dpi, false, false);
                Assert(Math.Abs(scaled.Scale - sc) < 0.01f, $"DPI {dpi} scale");
                Assert(Math.Abs(scaled.ToPx(scaled.Workspace).Width - scaled.Workspace.Width * sc) < 0.5f, $"DPI {dpi} px projection");
            }
        }

        private static Bitmap RenderControl(Control ctrl)
        {
            if (!ctrl.IsHandleCreated) _ = ctrl.Handle;
            // DrawToBitmap captures from outer (0, 0) of the window including frame/titlebar;
            // using ctrl.Size avoids clipping the bottom rows off ClientSize.
            var sz = ctrl.Size;
            var bmp = new Bitmap(Math.Max(1, sz.Width), Math.Max(1, sz.Height));
            ctrl.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
            return bmp;
        }

        private static void SaveShot(Control ctrl, string fileName)
        {
            if (string.IsNullOrEmpty(screenshotDir)) return;
            Directory.CreateDirectory(screenshotDir);
            using var bmp = RenderControl(ctrl);
            bmp.Save(Path.Combine(screenshotDir, fileName), System.Drawing.Imaging.ImageFormat.Png);
        }

        private static void TestShellPickerDialog()
        {
            using var picker = new ShellPickerDialog();
            Assert(picker.ClientSize.Width >= 700 && picker.ClientSize.Height >= 440, "Picker client size");
            using var bmp = RenderControl(picker);
            SaveShot(picker, "01-picker.png");

            var acc = picker.AccessibilityObject;
            int childCount = acc.GetChildCount();
            Assert(childCount >= 4, $"Expected >= 4 UIA children on picker, got {childCount}");
            bool hasSignal = false, hasConfirm = false;
            for (int i = 0; i < childCount; i++)
            {
                var c = acc.GetChild(i);
                string n = c?.Name ?? "";
                if (n.Contains("Signal Glass", StringComparison.OrdinalIgnoreCase)) hasSignal = true;
                if (n.Length > 0 && c!.Bounds.Width > 0 && c.Bounds.Height > 0 && i == childCount - 1) hasConfirm = true;
            }
            Assert(hasSignal && hasConfirm, "Picker UIA contains Signal Glass option and Confirm button");
        }

        private static void TestSlashAndClassicShellControl()
        {
            AssertEqual(4, LidLightingHelper.BrightnessLabels.Length, "Lid brightness levels");
            Assert(LidLightingHelper.ModeLabels().Count >= 6, "Lid mode labels non-empty");
        }

        private static void TestFormsStatesAccessibilityAndStress()
        {
            var mock = new MockSignalBackend();
            var origCulture = CultureInfo.CurrentUICulture;

            try
            {
                using var form = new SignalGlassForm(mock);
                _ = form.Handle;

                void ApplyMockState(SensorSnapshot snap, CapabilitySet? caps = null)
                {
                    mock.NextSnapshot = snap;
                    if (caps is not null) mock.Capabilities = caps;
                    var t = typeof(SignalGlassForm);
                    t.GetField("snapshot", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, snap);
                    if (caps is not null)
                        t.GetField("caps", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(form, caps);
                    var cpuBuf = (RingHistoryBuffer)t.GetField("cpuHistory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
                    var gpuBuf = (RingHistoryBuffer)t.GetField("gpuHistory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(form)!;
                    cpuBuf.Clear();
                    gpuBuf.Clear();
                    for (int i = 0; i < 60; i++)
                    {
                        if (i is >= 25 and <= 27) { cpuBuf.Append(null); gpuBuf.Append(null); continue; }
                        cpuBuf.Append(snap.CpuTempC.HasValue ? 54f + (float)Math.Sin(i * 0.25) * 11f : null);
                        gpuBuf.Append(snap.GpuTempC.HasValue ? 49f + (float)Math.Cos(i * 0.2) * 9f : null);
                    }
                    form.Invalidate();
                }

                // 1) Test all backend states without throwing
                ApplyMockState(MockSignalBackend.CreateLoadingSnapshot(), MockSignalBackend.CreateFullCapabilities());
                using (RenderControl(form)) { }

                ApplyMockState(MockSignalBackend.CreateStaleSnapshot());
                using (RenderControl(form)) { }

                ApplyMockState(MockSignalBackend.CreateErrorSnapshot());
                using (RenderControl(form)) { }

                ApplyMockState(MockSignalBackend.CreateDisconnectedSnapshot());
                using (RenderControl(form)) { }

                ApplyMockState(MockSignalBackend.CreateReadySnapshot(), MockSignalBackend.CreateUnsupportedCapabilities());
                using (RenderControl(form)) { }

                // Ready state across all 12 screens & Dark / Light / RU / AR RTL / 900x640
                ApplyMockState(MockSignalBackend.CreateReadySnapshot(), MockSignalBackend.CreateFullCapabilities());

                // Seed mock ROG gallery thumbnails before navigating so Wallpapers preview grid is exercised offline too
                lock (GHelper.UI.Nebula.RogWallpapers.Items)
                {
                    if (GHelper.UI.Nebula.RogWallpapers.Items.Count == 0)
                    {
                        for (int wi = 0; wi < 3; wi++)
                        {
                            var bmpThumb = GHelper.UI.Nebula.AuraWallpaper.Render(
                                wi switch { 0 => Color.FromArgb(129, 229, 197), 1 => Color.FromArgb(255, 179, 145), _ => Color.FromArgb(120, 170, 255) },
                                (GHelper.UI.Nebula.AuraWallpaper.Style)(wi % 3), 320, 180);
                            var item = new GHelper.UI.Nebula.RogWallpapers.Item
                            {
                                Id = 9000 + wi,
                                Name = wi switch { 0 => "ROG Zephyrus Horizon", 1 => "ROG Scar Pulse", _ => "ROG Nebula Flow" },
                                Category = wi == 0 ? "Zephyrus" : "ROG",
                                Downloads = 1200 + wi * 340,
                                ThumbImage = bmpThumb,
                            };
                            item.Desktop.Add(new GHelper.UI.Nebula.RogWallpapers.Variant { Width = 3840, Height = 2160, Url = "https://example.invalid/mock.jpg" });
                            GHelper.UI.Nebula.RogWallpapers.Items.Add(item);
                        }
                    }
                }

                AppConfig.Set("ui_mode", "dark");
                SignalLang.Apply("en");
                form.OnThemeChanged();
                form.OnLanguageChanged();
                float sc = Math.Max(1f, form.DeviceDpi / 96f);
                form.ClientSize = new Size((int)(1440 * sc), (int)(1060 * sc));
                foreach (var target in new[] { "overview" }.Concat(SignalRegistry.InspectorTargets.Select(x => x.PageId)))
                {
                    form.NavigateTo(target, false);
                    using var bmp = RenderControl(form);
                    if (target == "overview") SaveShot(form, "02-overview-dark-1440.png");
                    if (target == "cooling") SaveShot(form, "06-cooling-dark.png");
                    if (target == "lighting") SaveShot(form, "10-lighting-slash-dark.png");
                    if (target == "wallpapers") SaveShot(form, "09-wallpapers-dark.png");
                    if (target == "settings") SaveShot(form, "07-settings-dark.png");
                }

                // UIA check on Overview
                form.NavigateTo("overview", false);
                using (RenderControl(form)) { }
                var acc = form.AccessibilityObject;
                int uiCount = acc.GetChildCount();
                Assert(uiCount >= 10, $"Expected >= 10 UIA elements on Overview, got {uiCount}");
                for (int i = 0; i < uiCount; i++)
                {
                    var child = acc.GetChild(i);
                    Assert(child is not null && !string.IsNullOrEmpty(child.Name), $"UIA child {i} missing Name");
                    Assert(child!.Bounds.Width > 0 && child.Bounds.Height > 0, $"UIA child {child.Name} invalid Bounds");
                }

                // Light theme 1440x1060
                AppConfig.Set("ui_mode", "light");
                form.OnThemeChanged();
                SaveShot(form, "03-overview-light-1440.png");

                // Russian 900x640
                AppConfig.Set("ui_mode", "dark");
                SignalLang.Apply("ru");
                form.OnThemeChanged();
                form.OnLanguageChanged();
                form.ClientSize = new Size((int)(900 * sc), (int)(640 * sc));
                SaveShot(form, "04-overview-ru-900.png");

                // Arabic RTL 1440x1060
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar-SA");
                form.OnLanguageChanged();
                form.ClientSize = new Size((int)(1440 * sc), (int)(1060 * sc));
                SaveShot(form, "05-overview-rtl-ar.png");

                // Restore EN for flyout + stress
                SignalLang.Apply("en");
                form.OnLanguageChanged();

                using var flyout = new SignalFlyoutForm(mock);
                _ = flyout.Handle;
                typeof(SignalFlyoutForm).GetField("snapshot", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(flyout, MockSignalBackend.CreateReadySnapshot());
                typeof(SignalFlyoutForm).GetField("caps", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(flyout, MockSignalBackend.CreateFullCapabilities());
                flyout.ApplyTheme();
                using (RenderControl(flyout)) { }
                SaveShot(flyout, "08-flyout-dark.png");
                Assert(flyout.AccessibilityObject.GetChildCount() >= 5, "Flyout UIA child count");

                // 500-cycle stress: theme + opaque glass + resize + offscreen paint, check GDI handle growth stays bounded
                IntPtr proc = System.Diagnostics.Process.GetCurrentProcess().Handle;
                uint gdiStart = GetGuiResources(proc, 0);
                for (int i = 0; i < 500; i++)
                {
                    AppConfig.Set("ui_mode", (i & 1) == 0 ? "dark" : "light");
                    SignalTheme.GlassIntensity = (i & 2) == 0 ? GlassIntensityMode.Standard : GlassIntensityMode.Opaque;
                    form.OnThemeChanged();
                    form.Size = (i % 3) switch
                    {
                        0 => new Size(1440, 1060),
                        1 => new Size(1100, 800),
                        _ => new Size(900, 640),
                    };
                    using var b = RenderControl(form);
                }
                uint gdiEnd = GetGuiResources(proc, 0);
                long delta = (long)gdiEnd - (long)gdiStart;
                Assert(delta < 200, $"Potential GDI handle leak over 500 cycles: start={gdiStart}, end={gdiEnd}, delta={delta}");
            }
            finally
            {
                CultureInfo.CurrentUICulture = origCulture;
                SignalTheme.GlassIntensity = GlassIntensityMode.Standard;
                AppConfig.Set("ui_mode", "");
            }
        }
    }
}
