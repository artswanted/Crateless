namespace GHelper.AutoUpdate
{
    /// <summary>Background BIOS and driver check against the ASUS support site, on the same interval as the app release check.</summary>
    public static class DriverUpdateCheck
    {
        private const int TypeDrivers = 0, TypeBios = 1;

        /// <summary>Newer BIOS / driver packages found by the last check (or by the updates page), kept in the config between runs.</summary>
        public static int NewBios { get; private set; }
        public static int NewDrivers { get; private set; }
        public static int NewCount => NewBios + NewDrivers;
        public static bool Checking { get; private set; }
        public static DateTime LastCheck { get; private set; } = DateTime.MinValue;

        public static (string bios, string drivers) Urls(string model)
        {
            string rog = AppConfig.IsROG() ? "&systemCode=rog" : "";
            return ($"https://rog.asus.com/support/webapi/product/GetPDBIOS?website=global&model={model}&cpu={model}{rog}",
                    $"https://rog.asus.com/support/webapi/product/GetPDDrivers?website=global&model={model}&cpu={model}&osid=52{rog}");
        }

        public static void StartBackgroundChecks()
        {
            NewBios = AppConfig.Get("drivers_new_bios", 0);
            NewDrivers = AppConfig.Get("drivers_new", 0);
            int at = AppConfig.Get("drivers_checked_at", 0);
            if (at > 0) LastCheck = DateTimeOffset.FromUnixTimeSeconds(at).LocalDateTime;

            Task.Run(async () =>
            {
                // later than the app check: the local driver inventory is not free, so it stays out of the way of start-up
                await Task.Delay(TimeSpan.FromMinutes(1));
                DateTime lastAttempt = DateTime.MinValue;
                while (true)
                {
                    if (!Checking && DateTime.Now - LastCheck >= TimeSpan.FromHours(AutoUpdateControl.IntervalHours) && DateTime.Now - lastAttempt >= TimeSpan.FromMinutes(15))
                    {
                        lastAttempt = DateTime.Now;
                        await CheckAsync();
                    }
                    await Task.Delay(TimeSpan.FromMinutes(1));
                }
            });
        }

        private static async Task CheckAsync()
        {
            Checking = true;
            try
            {
                var (biosVersion, model) = AppConfig.GetBiosAndModel();
                var (biosUrl, driversUrl) = Urls(model);
                var controller = new UpdatesController();

                var bios = await controller.FetchUpdates(biosUrl);
                controller.ResolveStatus(bios, TypeBios, biosVersion);
                var drivers = await controller.FetchUpdates(driversUrl);
                controller.ResolveStatus(drivers, TypeDrivers, biosVersion);

                bool unseen = Report(TypeBios, bios) | Report(TypeDrivers, drivers);
                if (unseen && !AppConfig.Is("skip_updates")) Balloon();
                Logger.WriteLine($"Driver check: BIOS {NewBios} new, drivers {NewDrivers} new");
            }
            catch (Exception ex)
            {
                Logger.WriteLine("Driver check failed: " + ex.Message);
            }
            Checking = false;
            Repaint();
        }

        /// <summary>Takes a resolved list (from the background check or the updates page) and refreshes the counters.
        /// Returns true when it holds a package + version not announced before.</summary>
        public static bool Report(int type, List<UpdatesController.DriverUpdate> list)
        {
            var fresh = list.Where(u => u.status == UpdatesController.STATUS_NEW).ToList();
            if (type == TypeBios) { NewBios = fresh.Count; AppConfig.Set("drivers_new_bios", NewBios); }
            else { NewDrivers = fresh.Count; AppConfig.Set("drivers_new", NewDrivers); }

            LastCheck = DateTime.Now;
            AppConfig.Set("drivers_checked_at", (int)DateTimeOffset.Now.ToUnixTimeSeconds());
            Repaint();

            // remembered per package + version, so a balloon only appears for something not announced before
            var keys = fresh.Select(u => u.title + " " + u.version).ToList();
            var seen = (AppConfig.GetString("drivers_notified") ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
            if (!keys.Any(k => !seen.Contains(k))) return false;
            AppConfig.Set("drivers_notified", string.Join("\n", seen.Union(keys)));
            return true;
        }

        private static void Balloon()
        {
            try
            {
                var tray = Program.trayIcon;
                if (tray is null || NewCount == 0) return;
                tray.BalloonTipTitle = "Crateless";
                tray.BalloonTipText = UI.Nebula.NebulaText.Tf("New BIOS / driver updates: {0}", "Новые обновления BIOS и драйверов: {0}", NewCount);
                tray.ShowBalloonTip(10000);
            }
            catch (Exception ex) { Logger.WriteLine(ex.Message); }
        }

        private static void Repaint()
        {
            try { Program.nebulaForm?.BeginInvoke(Program.nebulaForm.Invalidate); } catch { }
            try { Program.nebulaCompact?.BeginInvoke(Program.nebulaCompact.Invalidate); } catch { }
        }
    }
}
