using GHelper.AutoUpdate;
using System.Diagnostics;

namespace GHelper.UI.SignalGlass.Pages
{
    /// <summary>
    /// 11 Updates: real build version, GitHub release check, BIOS/driver lookup on ASUS for this
    /// exact model (no automatic BIOS flashing or automatic executable execution).
    /// </summary>
    public sealed class UpdatesPage : SignalPage
    {
        public override string Id => "updates";
        public override string GroupId => "system";
        public override string Title => SignalText.T("Updates", "Обновления");
        public override bool HasFooter => false;

        private readonly UpdatesController controller = new();
        private CancellationTokenSource cts = new();
        private List<UpdatesController.DriverUpdate> bios = new();
        private List<UpdatesController.DriverUpdate> drivers = new();
        private bool loadingBios, loadingDrivers;
        private string error = "";
        private string biosVersion = "", model = "";
        private float lastContentH = 620f;
        private Action? requestRepaint;

        public override void OnEnter(SignalPaintContext? ctx)
        {
            requestRepaint = ctx?.RequestRefresh;
            (biosVersion, model) = DriverUpdateCheck.BiosAndModel();
        }

        private void CheckDriversAndBios()
        {
            (biosVersion, model) = DriverUpdateCheck.BiosAndModel();
            var (biosUrl, driversUrl) = DriverUpdateCheck.Urls(model);

            cts.Cancel(); cts.Dispose(); cts = new CancellationTokenSource();
            var token = cts.Token;
            error = "";
            loadingBios = loadingDrivers = true;

            Fetch(biosUrl, 1, token, list => { bios = list; loadingBios = false; });
            Fetch(driversUrl, 0, token, list => { drivers = list; loadingDrivers = false; });
        }

        private void Fetch(string url, int type, CancellationToken token, Action<List<UpdatesController.DriverUpdate>> done)
        {
            Task.Run(async () =>
            {
                try
                {
                    var list = await controller.FetchUpdates(url, token);
                    controller.ResolveStatus(list, type, biosVersion, token);
                    DriverUpdateCheck.Report(type, list);
                    done(list);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    error = ex.Message;
                    if (type == 1) loadingBios = false; else loadingDrivers = false;
                }
                requestRepaint?.Invoke();
            }, token);
        }

        public override float ContentHeightDip(SignalPaintContext ctx) => lastContentH;

        public override void Paint(SignalPaintContext ctx)
        {
            requestRepaint = ctx.RequestRefresh;
            var body = ctx.Layout.WorkspaceBody;
            float x = body.X, w = body.Width, y = body.Y - ctx.ScrollDip;
            float pad = 24f, rx = x + pad, rw = w - pad * 2f;

            var uc = Program.settingsForm?.UpdateControl;
            bool appNew = AutoUpdateControl.UpdateAvailable;
            bool checking = AutoUpdateControl.Checking || loadingBios || loadingDrivers;

            var freshList = bios.Concat(drivers).Where(u => u.status == UpdatesController.STATUS_NEW).Take(8).ToList();
            int rows = 6 + freshList.Count;
            float panelH = rows * 74f + 32f;
            ctx.DrawPanel(new RectangleF(x, y, w, panelH));
            float ry = y + 16f;

            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Crateless version", "Версия Crateless"),
                appNew ? SignalText.Tf("Available: {0}", "Доступна: {0}", AutoUpdateControl.LatestTag) : SignalText.T("Read from build", "Версия сборки"),
                AutoUpdateControl.AppVersion,
                "updates:version",
                null,
                readOnly: true);

            string checkLabel = AutoUpdateControl.Checking ? SignalText.T("Checking…", "Проверяем…")
                : appNew ? $"{SignalText.T("Update available", "Доступно обновление")} ({AutoUpdateControl.LatestTag})"
                : AutoUpdateControl.Checked ? SignalText.T("Up to date", "Актуальная")
                : SignalText.T("Check now", "Проверить сейчас");

            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Check for updates", "Проверка обновлений"),
                appNew ? SignalText.T("Click to open releases page", "Открыть страницу релизов") : SignalText.T("Checks GitHub releases", "Проверяет релизы на GitHub"),
                checkLabel,
                "updates:check",
                _ =>
                {
                    if (appNew) uc?.LoadReleases();
                    else { uc?.CheckNow(); CheckDriversAndBios(); }
                },
                enabled: !checking);

            string drvVal = loadingDrivers ? SignalText.T("Checking…", "Проверяем…")
                : DriverUpdateCheck.NewDrivers > 0 ? SignalText.Tf("{0} new", "Новых: {0}", DriverUpdateCheck.NewDrivers)
                : drivers.Count > 0 ? SignalText.T("Up to date", "Актуально")
                : SignalText.T("Check ASUS drivers", "Проверить драйверы");

            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Drivers", "Драйверы"),
                $"{model}" + (error.Length > 0 ? " · " + error : ""),
                drvVal,
                "updates:drivers",
                _ => CheckDriversAndBios(),
                enabled: !loadingDrivers);

            string biosVal = loadingBios ? SignalText.T("Checking…", "Проверяем…")
                : DriverUpdateCheck.NewBios > 0 ? SignalText.Tf("{0} new", "Новых: {0}", DriverUpdateCheck.NewBios)
                : !string.IsNullOrEmpty(biosVersion) ? $"BIOS {biosVersion}"
                : SignalText.T("Open instructions", "Открыть инструкцию");

            ry = ctx.DrawSettingRow(ry, rx, rw,
                "BIOS",
                SignalText.T("Never install or flash automatically", "BIOS никогда не устанавливается автоматически"),
                biosVal,
                "updates:bios",
                _ => OpenUrl($"https://rog.asus.com/support/"),
                enabled: !loadingBios);

            var last = AutoUpdateControl.LastCheck;
            string when = last == DateTime.MinValue ? SignalText.T("Not checked", "Не проверялось") : SignalText.FormatShortTime(last);
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Last check", "Последняя проверка"),
                SignalText.T("Persisted timestamp of the latest check", "Время последней выполненной проверки"),
                when,
                "updates:last",
                null,
                readOnly: true);

            bool notify = !AppConfig.Is("skip_updates");
            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("Download policy", "Политика загрузок"),
                SignalText.T("Ask first · verify model and publisher before running", "Спрашивать перед скачиванием и запуском"),
                notify ? SignalText.Tf("Notify ({0}h)", "Уведомлять ({0}ч)", AutoUpdateControl.IntervalHours) : SignalText.T("Manual", "Вручную"),
                "updates:policy",
                pt => ctx.ShowMenu(pt, new (string, bool, string?, Action)[]
                {
                    (SignalText.T("Manual", "Вручную"), !notify, null, () => AppConfig.Set("skip_updates", 1)),
                    ("1 h", notify && AutoUpdateControl.IntervalHours == 1, null, () => { AppConfig.Set("skip_updates", 0); AutoUpdateControl.IntervalHours = 1; }),
                    ("6 h", notify && AutoUpdateControl.IntervalHours == 6, null, () => { AppConfig.Set("skip_updates", 0); AutoUpdateControl.IntervalHours = 6; }),
                    ("12 h", notify && AutoUpdateControl.IntervalHours == 12, null, () => { AppConfig.Set("skip_updates", 0); AutoUpdateControl.IntervalHours = 12; }),
                    ("24 h", notify && AutoUpdateControl.IntervalHours == 24, null, () => { AppConfig.Set("skip_updates", 0); AutoUpdateControl.IntervalHours = 24; }),
                }),
                showChevron: true,
                drawDivider: freshList.Count > 0);

            for (int i = 0; i < freshList.Count; i++)
            {
                var u = freshList[i];
                ry = ctx.DrawSettingRow(ry, rx, rw,
                    u.title,
                    $"{u.categoryName} · {u.date}",
                    $"v{u.version}  ↗",
                    "updates:item:" + i,
                    _ =>
                    {
                        if (!string.IsNullOrEmpty(u.downloadUrl) && u.downloadUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                            OpenUrl(u.downloadUrl);
                    },
                    drawDivider: i < freshList.Count - 1);
            }

            lastContentH = panelH + 16f;
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }
}
