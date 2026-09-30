using GHelper.Helpers;
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GHelper.AutoUpdate
{
    public class AutoUpdateControl
    {

        SettingsForm settings;

        public string versionUrl = "https://github.com/artswanted/Crateless/releases";
        public bool update { get => UpdateAvailable; set => UpdateAvailable = value; }
        public bool checkedOnce { get => Checked; set => Checked = value; }
        public string latestTag { get => LatestTag; set => LatestTag = value; }

        /// <summary>Result of the last release check, shared with the Nebula shell and kept in the config between runs.</summary>
        public static bool UpdateAvailable;
        public static bool Checked;
        public static string LatestTag = "";
        public static string LatestUrl = "";
        public static DateTime LastCheck = DateTime.MinValue;
        public static bool Checking;

        public static string AppVersion
        {
            get { var v = Assembly.GetExecutingAssembly().GetName().Version; return $"{v?.Major}.{v?.Minor}.{v?.Build}"; }
        }

        /// <summary>Brings back what the last run found so the badge is correct before the first check finishes.</summary>
        private static void RestoreState()
        {
            LatestTag = AppConfig.GetString("update_tag") ?? "";
            LatestUrl = AppConfig.GetString("update_url") ?? "";
            int at = AppConfig.Get("update_checked_at", 0);
            if (at > 0) LastCheck = DateTimeOffset.FromUnixTimeSeconds(at).LocalDateTime;
            Checked = LatestTag.Length > 0;
            UpdateAvailable = Checked && IsNewer(LatestTag);
        }

        private static bool IsNewer(string tag)
        {
            try { return new Version(tag.TrimStart('v')).CompareTo(new Version(AppVersion)) > 0; }
            catch { return false; }
        }

        private static void SaveState()
        {
            AppConfig.Set("update_tag", LatestTag);
            AppConfig.Set("update_url", LatestUrl);
            AppConfig.Set("update_checked_at", (int)DateTimeOffset.Now.ToUnixTimeSeconds());
        }

        /// <summary>Repaints the Nebula windows after a check so the badge appears without any interaction.</summary>
        private static void Repaint()
        {
            try { Program.nebulaForm?.BeginInvoke(Program.nebulaForm.Invalidate); } catch { }
            try { Program.nebulaCompact?.BeginInvoke(Program.nebulaCompact.Invalidate); } catch { }
        }

        /// <summary>On-demand check without the 12 h throttle and without auto-download (Nebula updates page).</summary>
        public void CheckNow() => Task.Run(() => CheckForUpdatesAsync(false, silent: true));

        public static readonly int[] IntervalChoices = { 1, 3, 6, 12, 24 };

        /// <summary>Hours between background release checks, picked on the Nebula updates page.</summary>
        public static int IntervalHours
        {
            get { int h = AppConfig.Get("update_interval", 12); return IntervalChoices.Contains(h) ? h : 12; }
            set => AppConfig.Set("update_interval", value);
        }

        /// <summary>Checks this repo's releases whenever the chosen interval has passed since the last successful check, counting across restarts.</summary>
        public void StartBackgroundChecks()
        {
            RestoreState();
            Repaint();
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(20));
                DateTime lastAttempt = DateTime.MinValue;
                while (true)
                {
                    // a failed check (offline) is retried after 15 minutes instead of waiting out the whole interval
                    if (!Checking && DateTime.Now - LastCheck >= TimeSpan.FromHours(IntervalHours) && DateTime.Now - lastAttempt >= TimeSpan.FromMinutes(15))
                    {
                        lastAttempt = DateTime.Now;
                        CheckForUpdatesAsync(false, silent: true);
                    }
                    await Task.Delay(TimeSpan.FromMinutes(1));
                }
            });
        }

        static long lastUpdate;

        public AutoUpdateControl(SettingsForm settingsForm)
        {
            settings = settingsForm;
            var appVersion = new Version(Assembly.GetExecutingAssembly().GetName().Version.ToString());
            settings.SetVersionLabel(Properties.Strings.VersionLabel + $": {appVersion.Major}.{appVersion.Minor}.{appVersion.Build}");
        }

        public void CheckForUpdates()
        {
            // Run update once per chosen interval
            if (Math.Abs(DateTimeOffset.Now.ToUnixTimeSeconds() - lastUpdate) < IntervalHours * 3600) return;
            lastUpdate = DateTimeOffset.Now.ToUnixTimeSeconds();

            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(1));
                CheckForUpdatesAsync();
            });
        }

        public void Update()
        {
            if (update)
            {
                Task.Run(() =>
                {
                    CheckForUpdatesAsync(true);
                });
            } else
            {
                LoadReleases();
            }
        }

        public void LoadReleases()
        {
            try
            {
                Process.Start(new ProcessStartInfo(versionUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Failed to open releases page:" + ex.Message);
            }
        }

        async void CheckForUpdatesAsync(bool force = false, bool silent = false)
        {
            if (AppConfig.Is("skip_updates") && !force && !silent) return;
            Checking = true;
            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", "Crateless App");
                    var json = await httpClient.GetStringAsync("https://api.github.com/repos/artswanted/Crateless/releases/latest");
                    var config = JsonSerializer.Deserialize<JsonElement>(json);
                    var tag = config.GetProperty("tag_name").ToString().Replace("v", "");
                    var assets = config.GetProperty("assets");

                    // the standalone build carries the runtime with it, so it must not be replaced by the small one
                    bool standalone = IsStandaloneBuild();
                    string url = null, other = null;
                    for (int i = 0; i < assets.GetArrayLength(); i++)
                    {
                        string a = assets[i].GetProperty("browser_download_url").ToString();
                        if (!a.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                        if (a.Contains("standalone", StringComparison.OrdinalIgnoreCase) == standalone) url = a;
                        else other ??= a;
                    }
                    url ??= other ?? assets[0].GetProperty("browser_download_url").ToString();

                    var gitVersion = new Version(tag);
                    var appVersion = new Version(Assembly.GetExecutingAssembly().GetName().Version.ToString());

                    Checked = true;
                    LatestTag = tag;
                    LatestUrl = url;
                    LastCheck = DateTime.Now;
                    UpdateAvailable = gitVersion.CompareTo(appVersion) > 0;
                    SaveState();
                    Logger.WriteLine($"Update check: installed {AppVersion}, latest {tag}, newer = {UpdateAvailable}");

                    if (UpdateAvailable)
                    {
                        versionUrl = url;
                        settings.SetVersionLabel(Properties.Strings.DownloadUpdate + $": {AppVersion} \u2192 {tag}", true);

                        string[] args = Environment.GetCommandLineArgs();
                        if (force || args.Length > 1 && args[1] == "autoupdate")
                        {
                            AutoUpdate(url);
                            return;
                        }

                        // the Nebula shell carries a badge, so a background check only raises a tray balloon once per release
                        if (silent || UI.NebulaTheme.IsEnabled)
                        {
                            if (AppConfig.GetString("update_notified") != tag && !AppConfig.Is("skip_updates"))
                            {
                                AppConfig.Set("update_notified", tag);
                                Balloon(tag);
                            }
                        }
                        else if (AppConfig.GetString("skip_version") != tag)
                        {
                            DialogResult dialogResult = settings.ShowMessage(Properties.Strings.DownloadUpdate + ": Crateless " + tag + "?", "Update", MessageBoxButtons.YesNo);
                            if (dialogResult == DialogResult.Yes)
                                AutoUpdate(url);
                            else
                                AppConfig.Set("skip_version", tag);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Failed to check for updates: " + ex.Message);
            }
            Checking = false;
            Repaint();
        }

        /// <summary>A single-file self-contained build is over a hundred megabytes; the framework-dependent one is under twenty.</summary>
        private static bool IsStandaloneBuild()
        {
            try { return new FileInfo(Application.ExecutablePath).Length > 40 * 1024 * 1024; }
            catch { return false; }
        }

        private static void Balloon(string tag)
        {
            try
            {
                var tray = Program.trayIcon;
                if (tray is null) return;
                tray.BalloonTipTitle = "Crateless " + tag;
                tray.BalloonTipText = Properties.Strings.DownloadUpdate;
                tray.ShowBalloonTip(10000);
            }
            catch (Exception ex) { Logger.WriteLine(ex.Message); }
        }

        /// <summary>
        /// Replaces the exe once every copy of it has exited. A running exe cannot be overwritten, but it can be renamed,
        /// so the old one is moved to .old (deleted on the next start) and the new one takes its name; on failure the
        /// old exe is put back. Either way the app is started again, and errors go to update.log next to the config.
        /// </summary>
        private static string SwapScript(string exe, string zip, int pid)
        {
            static string Q(string s) => "'" + s.Replace("'", "''") + "'";
            string dir = Path.GetDirectoryName(exe) ?? ".";
            string name = Path.GetFileNameWithoutExtension(exe);
            string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Crateless", "update.log");

            return $@"
$exe = {Q(exe)}; $zip = {Q(zip)}; $tmp = {Q(Path.Combine(dir, ".crateless-update"))}; $log = {Q(log)}
function Note($m) {{ try {{ Add-Content -LiteralPath $log -Value ((Get-Date -Format s) + ' ' + $m) }} catch {{}} }}
Wait-Process -Id {pid} -Timeout 30 -ErrorAction SilentlyContinue
# an elevated copy may still hold the exe; listing works across elevation where waiting does not
$until = (Get-Date).AddSeconds(30)
while ((Get-Process -Name {Q(name)} -ErrorAction SilentlyContinue) -and (Get-Date) -lt $until) {{ Start-Sleep -Milliseconds 500 }}
try {{
    $ErrorActionPreference = 'Stop'
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
    Expand-Archive -LiteralPath $zip -DestinationPath $tmp -Force
    $new = Get-ChildItem -LiteralPath $tmp -Filter *.exe | Select-Object -First 1
    if (-not $new) {{ throw 'no exe in the archive' }}
    Remove-Item -LiteralPath ($exe + '.old') -Force -ErrorAction SilentlyContinue
    Move-Item -LiteralPath $exe -Destination ($exe + '.old') -Force
    try {{ Move-Item -LiteralPath $new.FullName -Destination $exe -Force }}
    catch {{ Move-Item -LiteralPath ($exe + '.old') -Destination $exe -Force; throw }}
    Note ('updated ' + $exe)
}} catch {{
    Note ('update failed: ' + $_.Exception.Message)
}} finally {{
    $ErrorActionPreference = 'Continue'
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $zip -Force -ErrorAction SilentlyContinue
}}
Start-Process -FilePath $exe -WorkingDirectory {Q(dir)}
";
        }

        public static string EscapeString(string input)
        {
            return Regex.Replace(Regex.Replace(input, @"\[|\]", "`$0"), @"\'", "''");
        }

        async void AutoUpdate(string requestUri)
        {

            Uri uri = new Uri(requestUri);
            string zipName = Path.GetFileName(uri.LocalPath);

            string exeLocation = Application.ExecutablePath;
            string exeDir = Path.GetDirectoryName(exeLocation);
            //exeDir = "C:\\Program Files\\GHelper";
            string exeName = Path.GetFileName(exeLocation);
            string zipLocation = exeDir + "\\" + zipName;

            using (HttpClient client = new HttpClient())
            {

                client.DefaultRequestHeaders.Add("User-Agent", "Crateless App");
                Logger.WriteLine(requestUri);
                Logger.WriteLine(exeDir);
                Logger.WriteLine(zipName);
                Logger.WriteLine(exeName);

                try
                {
                    var bytes = await client.GetByteArrayAsync(uri);
                    File.WriteAllBytes(zipLocation, bytes);
                    Logger.WriteLine($"Downloaded {bytes.Length}b: {zipLocation} (exists={File.Exists(zipLocation)}, size={new FileInfo(zipLocation).Length})");
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(ex.Message);
                    if (!ProcessHelper.IsUserAdministrator())
                    {
                        ProcessHelper.RunAsAdmin("autoupdate");
                        Application.Exit();
                    } else
                    {
                        LoadReleases();
                    }
                    return;
                }

                string command = SwapScript(exeLocation, zipLocation, Environment.ProcessId);
                Logger.WriteLine(command);

                try
                {
                    var cmd = new Process();
                    cmd.StartInfo.WorkingDirectory = exeDir;
                    cmd.StartInfo.UseShellExecute = false;
                    cmd.StartInfo.CreateNoWindow = true;
                    cmd.StartInfo.FileName = "powershell";
                    // encoded, so no quote in a path or in the script is eaten by command-line parsing
                    cmd.StartInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command));
                    cmd.Start();
                }
                catch (Exception ex)
                {
                    Logger.WriteLine(ex.Message);
                }

                Application.Exit();
            }

        }

    }
}
