using System.Diagnostics;

namespace GHelper.UI.Nebula.Pages
{
    /// <summary>Updates page: Crateless releases, BIOS and driver updates from the ASUS site for this model.</summary>
    public sealed class UpdatesPage : NebulaPage
    {
        public override string Id => "updates";
        public override string Title => NebulaText.T("Updates", "Обновления");
        public override string Subtitle => NebulaText.T("Crateless, BIOS and drivers for this exact model.", "Crateless, BIOS и драйверы именно для этой модели.");

        private readonly UpdatesController controller = new();
        private CancellationTokenSource cts = new();
        private List<UpdatesController.DriverUpdate> bios = new();
        private List<UpdatesController.DriverUpdate> drivers = new();
        private bool loadingBios, loadingDrivers, biosDone, driversDone;
        private string error = "";
        private string model = "", biosVersion = "", serial = "";
        private long lastLoad;
        private bool showHidden;
        private float bottom = 700;

        public override float ContentHeight => bottom + 20;

        public override void Refresh(bool opened)
        {
            if (!opened) return;
            if (Environment.TickCount64 - lastLoad > 5 * 60_000 || (!biosDone && !loadingBios)) Load();
        }

        private void Load()
        {
            lastLoad = Environment.TickCount64;
            (biosVersion, model) = AppConfig.GetBiosAndModel();
            cts.Cancel(); cts.Dispose(); cts = new CancellationTokenSource();
            var token = cts.Token;
            error = "";
            bios = new(); drivers = new();
            biosDone = driversDone = false;
            loadingBios = loadingDrivers = true;

            string rog = AppConfig.IsROG() ? "&systemCode=rog" : "";
            string biosUrl = $"https://rog.asus.com/support/webapi/product/GetPDBIOS?website=global&model={model}&cpu={model}{rog}";
            string driversUrl = $"https://rog.asus.com/support/webapi/product/GetPDDrivers?website=global&model={model}&cpu={model}&osid=52{rog}";

            Fetch(biosUrl, 1, token, list => { bios = list; loadingBios = false; biosDone = true; });
            Fetch(driversUrl, 0, token, list => { drivers = list; loadingDrivers = false; driversDone = true; });
            Task.Run(() => { try { serial = controller.GetSerialNumber(); } catch { } Repaint(); });
        }

        private void Fetch(string url, int type, CancellationToken token, Action<List<UpdatesController.DriverUpdate>> done)
        {
            Task.Run(async () =>
            {
                try
                {
                    var list = await controller.FetchUpdates(url, token);
                    done(list);
                    Repaint();
                    try { controller.ResolveStatus(list, type, biosVersion, token); }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex) { Logger.WriteLine(ex.Message); }
                    done(list);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    error = ex.Message;
                    if (type == 1) { loadingBios = false; biosDone = true; } else { loadingDrivers = false; driversDone = true; }
                    Logger.WriteLine("Updates: " + ex.Message);
                }
                Repaint();
            }, token);
        }

        private static void Repaint()
        {
            try { Program.nebulaForm?.BeginInvoke(Program.nebulaForm.Invalidate); } catch { }
        }

        private int NewCount => bios.Concat(drivers).Count(u => u.status == UpdatesController.STATUS_NEW);

        public override void Paint(NebulaCanvas c)
        {
            var th = c.Theme;
            float y = 139;

            // ---- app -------------------------------------------------------------------------------
            var uc = Program.settingsForm.UpdateControl;
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            c.Surface(236, y, 1158, 118);
            c.Txt("Crateless", 256, y + 30, c.F(15, FontStyle.Bold), th.Text);
            string appLine = uc is { update: true }
                ? NebulaText.T("A newer release is available", "Доступен новый релиз") + (uc.latestTag.Length > 0 ? ": " + uc.latestTag : "")
                : NebulaText.T("Installed", "Установлена") + $" {ver?.Major}.{ver?.Minor}.{ver?.Build}" + (uc is { checkedOnce: true } ? " · " + NebulaText.T("up to date", "актуальная") : "");
            c.Txt(appLine, 256, y + 53, c.F(12), uc is { update: true } ? th.Warning : th.Muted);
            c.Txt(NebulaText.T("Checked automatically every 12 hours from this repo's releases.", "Проверяется автоматически раз в 12 часов по релизам репозитория."), 256, y + 76, c.F(10), th.Faint);
            c.Txt(NebulaText.T("Notify", "Уведомлять"), 1240, y + 44, c.F(11), th.Muted, StringAlignment.Far);
            c.Toggle(1252, y + 27, !AppConfig.Is("skip_updates"), "updates:notify");
            c.Button(1054, y + 66, 160, 32, NebulaText.T("Check now", "Проверить"), "updates:check", primary: false);
            c.Button(1226, y + 66, 148, 32, uc is { update: true } ? NebulaText.T("Download", "Скачать") : NebulaText.T("Releases  ↗", "Релизы  ↗"), "updates:releases", primary: uc is { update: true });
            y += 118 + 16;

            // ---- summary ---------------------------------------------------------------------------
            int n = NewCount;
            string head = model + (biosVersion.Length > 0 ? " · BIOS " + biosVersion : "") + (serial.Length > 0 ? " · S/N " + serial : "");
            c.Txt(head, 256, y + 12, c.F(11), th.Faint);
            string state = loadingBios || loadingDrivers ? NebulaText.T("Loading from asus.com…", "Загружаем с asus.com…")
                         : error.Length > 0 ? NebulaText.T("Could not reach the ASUS site: ", "Не удалось связаться с сайтом ASUS: ") + error
                         : n > 0 ? NebulaText.T($"New updates: {n}", $"Новых обновлений: {n}") : NebulaText.T("Everything is up to date", "Всё актуально");
            c.Txt(state, 1394, y + 12, c.F(12, FontStyle.Bold), error.Length > 0 ? th.Danger : n > 0 ? th.Warning : th.Accent, StringAlignment.Far);
            y += 34;

            y = Table(c, y, "BIOS", bios, loadingBios, biosDone);
            y = Table(c, y + 16, NebulaText.T("Drivers and software", "Драйверы и ПО"), drivers, loadingDrivers, driversDone);

            var lf = c.F(11, FontStyle.Bold);
            string more = showHidden ? NebulaText.T("Hide unrelated entries", "Скрыть лишние записи") : NebulaText.T("Show all entries", "Показать все записи");
            c.Txt(more, 1394, y + 30, lf, c.IsHover("updates:hidden") ? th.Text : th.Accent, StringAlignment.Far);
            c.Hit(c.R(1394 - c.TextWidth(more, lf) - 8, y + 12, c.TextWidth(more, lf) + 16, 26), "updates:hidden");
            c.Txt("⬤ " + NebulaText.T("newer than installed", "новее установленного") + "    • " + NebulaText.T("installed", "установлено") + "    " + NebulaText.T("grey: cannot compare", "серым: нельзя сравнить"), 256, y + 30, c.F(10), th.Faint);
            bottom = y + 44;
        }

        private float Table(NebulaCanvas c, float y, string title, List<UpdatesController.DriverUpdate> list, bool loading, bool done)
        {
            var th = c.Theme;
            var rows = list.Where(u => showHidden || u.status != UpdatesController.STATUS_HIDDEN).ToList();
            float h = 70 + Math.Max(1, rows.Count) * 34 + 12;
            c.Surface(236, y, 1158, h);
            c.Txt(title, 256, y + 30, c.F(15, FontStyle.Bold), th.Text);
            c.Txt(rows.Count.ToString(), 1374, y + 30, c.F(12), th.Faint, StringAlignment.Far);

            float ry = y + 62;
            if (loading && rows.Count == 0) { c.Txt(NebulaText.T("Loading…", "Загружаем…"), 256, ry + 20, c.F(12), th.Muted); return y + h; }
            if (done && rows.Count == 0) { c.Txt(NebulaText.T("Nothing listed for this model.", "Для этой модели записей нет."), 256, ry + 20, c.F(12), th.Muted); return y + h; }

            c.Txt(NebulaText.T("Category", "Категория"), 256, ry, c.F(10, FontStyle.Bold), th.Faint);
            c.Txt(NebulaText.T("Title", "Название"), 470, ry, c.F(10, FontStyle.Bold), th.Faint);
            c.Txt(NebulaText.T("Date", "Дата"), 1150, ry, c.F(10, FontStyle.Bold), th.Faint, StringAlignment.Far);
            c.Txt(NebulaText.T("Version", "Версия"), 1374, ry, c.F(10, FontStyle.Bold), th.Faint, StringAlignment.Far);
            ry += 10;

            for (int i = 0; i < rows.Count; i++)
            {
                var u = rows[i];
                ry += 24;
                string id = "updates:open:" + u.downloadUrl.GetHashCode();
                bool hv = c.IsHover(id);
                if (hv) c.Card(c.R(246, ry - 18, 1138, 30), 6, th.Raised);
                Color vcol = u.status switch
                {
                    UpdatesController.STATUS_NEW => th.Warning,
                    UpdatesController.STATUS_NOT_FOUND => th.Faint,
                    _ => th.Accent,
                };
                string mark = u.status == UpdatesController.STATUS_NEW ? "⬤ " : u.status == UpdatesController.STATUS_UPTODATE ? "• " : "";
                c.Txt(Trim(u.categoryName, 26), 256, ry, c.F(11), th.Muted);
                c.Txt(Trim(u.title, 78), 470, ry, c.F(11, u.status == UpdatesController.STATUS_NEW ? FontStyle.Bold : FontStyle.Regular), th.Text);
                c.Txt(u.date, 1150, ry, c.F(11), th.Muted, StringAlignment.Far);
                c.Txt(mark + u.version.Replace("latest version at the ", ""), 1374, ry, c.F(11, FontStyle.Bold), vcol, StringAlignment.Far);
                c.Hit(c.R(246, ry - 18, 1138, 30), id, u.tip ?? "");
                ry += 10;
            }
            return y + h;
        }

        private static string Trim(string s, int max) => string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..(max - 1)] + "…";

        public override bool Click(string id, Point at, NebulaForm form)
        {
            if (id.StartsWith("updates:open:"))
            {
                var u = bios.Concat(drivers).FirstOrDefault(x => "updates:open:" + x.downloadUrl.GetHashCode() == id);
                if (u.downloadUrl is { Length: > 0 })
                    try { Process.Start(new ProcessStartInfo(u.downloadUrl) { UseShellExecute = true }); } catch (Exception ex) { Logger.WriteLine(ex.Message); }
                return true;
            }
            switch (id)
            {
                case "updates:notify": AppConfig.Set("skip_updates", AppConfig.Is("skip_updates") ? 0 : 1); return true;
                case "updates:check":
                    Program.settingsForm.UpdateControl?.CheckNow();
                    Load();
                    return true;
                case "updates:releases": Program.settingsForm.CheckAppUpdate(); return true;
                case "updates:hidden": showHidden = !showHidden; return true;
            }
            return false;
        }
    }
}
