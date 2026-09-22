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

        private static readonly Color Ok = Color.FromArgb(62, 207, 142), Unknown = Color.FromArgb(167, 139, 250), Bios = Color.FromArgb(96, 165, 250);
        private static readonly Color[] Palette = { Color.FromArgb(244, 114, 182), Color.FromArgb(56, 189, 248), Color.FromArgb(250, 204, 21), Color.FromArgb(52, 211, 153), Color.FromArgb(251, 146, 60), Color.FromArgb(129, 140, 248), Color.FromArgb(45, 212, 191), Color.FromArgb(232, 121, 249) };
        private static Color CategoryColor(string name) { int h = 0; foreach (char ch in name) h = h * 31 + ch; return Palette[Math.Abs(h) % Palette.Length]; }

        private int Count(int status) => bios.Concat(drivers).Count(u => u.status == status);
        private const int STATUS_UNKNOWN = 0;

        private static (string avail, string installed) Versions(UpdatesController.DriverUpdate u)
        {
            string avail = u.version.Replace("latest version at the ", ""), inst = "";
            if (u.tip is { Length: > 0 })
                foreach (var line in u.tip.Split('\n'))
                    if (line.StartsWith("Installed: ")) inst = line[11..].Trim();
            return (avail, inst);
        }

        public override void Paint(NebulaCanvas c)
        {
            var th = c.Theme;
            float y = 139;

            // ---- app -------------------------------------------------------------------------------
            var uc = Program.settingsForm.UpdateControl;
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            bool appNew = uc is { update: true };
            c.Surface(236, y, 1158, 118);
            c.Txt("Crateless", 256, y + 30, c.F(15, FontStyle.Bold), th.Text);
            float px = 256 + c.TextWidth("Crateless", c.F(15, FontStyle.Bold)) + 14;
            if (appNew) c.Pill(px, y + 11, NebulaText.T("Update available", "Есть обновление") + (uc!.latestTag.Length > 0 ? " " + uc.latestTag : ""), Color.FromArgb(50, th.Warning), th.Warning);
            else if (uc is { checkedOnce: true }) c.Pill(px, y + 11, "✓ " + NebulaText.T("Up to date", "Актуальная"), Color.FromArgb(50, Ok), Ok);
            c.Txt(NebulaText.T("Installed", "Установлена") + $" {ver?.Major}.{ver?.Minor}.{ver?.Build}", 256, y + 53, c.F(12), th.Muted);
            var last = AutoUpdate.AutoUpdateControl.LastCheck;
            string when = AutoUpdate.AutoUpdateControl.Checking ? NebulaText.T("Checking…", "Проверяем…")
                        : last == DateTime.MinValue ? NebulaText.T("never", "ещё не проверяли")
                        : last.Date == DateTime.Today ? last.ToString("HH:mm")
                        : last.ToString("dd.MM HH:mm");
            c.Txt(NebulaText.T("Checked automatically every 12 hours from this repo's releases.", "Проверяется автоматически раз в 12 часов по релизам репозитория.")
                  + "  ·  " + NebulaText.T("Last checked", "Последняя проверка") + ": " + when, 256, y + 76, c.F(10), th.Faint);
            c.Txt(NebulaText.T("Notify", "Уведомлять"), 1240, y + 44, c.F(11), th.Muted, StringAlignment.Far);
            c.Toggle(1252, y + 27, !AppConfig.Is("skip_updates"), "updates:notify");
            c.Button(1054, y + 66, 160, 32, AutoUpdate.AutoUpdateControl.Checking ? NebulaText.T("Checking…", "Проверяем…") : NebulaText.T("Check now", "Проверить"), "updates:check", primary: false, enabled: !AutoUpdate.AutoUpdateControl.Checking);
            c.Button(1226, y + 66, 148, 32, appNew ? NebulaText.T("Download", "Скачать") : NebulaText.T("Releases  ↗", "Релизы  ↗"), "updates:releases", primary: appNew);
            y += 118 + 16;

            // ---- summary strip ---------------------------------------------------------------------
            bool loading = loadingBios || loadingDrivers;
            int nNew = Count(UpdatesController.STATUS_NEW), nOk = Count(UpdatesController.STATUS_UPTODATE),
                nUnk = Count(UpdatesController.STATUS_NOT_FOUND) + Count(STATUS_UNKNOWN), nHid = Count(UpdatesController.STATUS_HIDDEN);
            c.Surface(236, y, 1158, 92);
            string head = model + (biosVersion.Length > 0 ? " · BIOS " + biosVersion : "") + (serial.Length > 0 ? " · S/N " + serial : "");
            c.Txt(head, 256, y + 28, c.F(11), th.Faint);
            float sx = 256;
            sx += c.Pill(sx, y + 44, NebulaText.T($"Updates available: {nNew}", $"Есть обновления: {nNew}"), Color.FromArgb(nNew > 0 ? 60 : 25, th.Warning), nNew > 0 ? th.Warning : th.Faint) + 10;
            sx += c.Pill(sx, y + 44, NebulaText.T($"Installed and current: {nOk}", $"Установлено, актуально: {nOk}"), Color.FromArgb(50, Ok), Ok) + 10;
            sx += c.Pill(sx, y + 44, NebulaText.T($"Cannot compare: {nUnk}", $"Не сравнить: {nUnk}"), Color.FromArgb(45, Unknown), Unknown) + 10;
            if (nHid > 0) c.Pill(sx, y + 44, NebulaText.T($"Hidden: {nHid}", $"Скрыто: {nHid}"), th.Raised, th.Faint);
            string state = loading ? NebulaText.T("Loading from asus.com…", "Загружаем с asus.com…")
                         : error.Length > 0 ? NebulaText.T("Could not reach the ASUS site: ", "Не удалось связаться с сайтом ASUS: ") + error
                         : nNew > 0 ? NebulaText.T("There is something to update", "Есть что обновить") : NebulaText.T("Everything is up to date", "Всё актуально");
            c.Txt(state, 1374, y + 62, c.F(12, FontStyle.Bold), error.Length > 0 ? th.Danger : loading ? th.Muted : nNew > 0 ? th.Warning : Ok, StringAlignment.Far);
            y += 92 + 16;

            // ---- sections --------------------------------------------------------------------------
            var all = bios.Select(u => (u, isBios: true)).Concat(drivers.Select(u => (u, isBios: false))).ToList();
            y = Section(c, y, NebulaText.T("Updates available", "Есть обновления"), NebulaText.T("Newer than what is installed. Click a row to download from asus.com.", "Новее установленного. Клик по строке скачивает с asus.com."),
                        all.Where(x => x.u.status == UpdatesController.STATUS_NEW).ToList(), th.Warning, loading,
                        NebulaText.T("Nothing newer than what is installed.", "Ничего новее установленного нет."));
            y = Section(c, y + 16, NebulaText.T("Installed and current", "Установлено, актуально"), NebulaText.T("The installed version matches or is newer than the one on asus.com.", "Установленная версия совпадает с версией на asus.com или новее."),
                        all.Where(x => x.u.status == UpdatesController.STATUS_UPTODATE).ToList(), Ok, loading,
                        NebulaText.T("Nothing matched yet.", "Пока ничего не сопоставлено."));
            y = Section(c, y + 16, NebulaText.T("Cannot compare", "Не сравнить"), NebulaText.T("No installed version was found for these, so they may or may not be needed.", "Установленная версия не найдена, поэтому непонятно, нужны они или нет."),
                        all.Where(x => x.u.status is UpdatesController.STATUS_NOT_FOUND or STATUS_UNKNOWN).ToList(), Unknown, loading,
                        NebulaText.T("Everything could be compared.", "Всё удалось сопоставить."));
            if (showHidden)
                y = Section(c, y + 16, NebulaText.T("Hidden", "Скрытые"), NebulaText.T("Entries for hardware this laptop does not have.", "Записи для железа, которого в этом ноутбуке нет."),
                            all.Where(x => x.u.status == UpdatesController.STATUS_HIDDEN).ToList(), th.Faint, loading, "");

            var lf = c.F(11, FontStyle.Bold);
            string more = showHidden ? NebulaText.T("Hide entries for absent hardware", "Скрыть записи для отсутствующего железа") : NebulaText.T("Show entries for absent hardware", "Показать записи для отсутствующего железа") + (nHid > 0 ? $" ({nHid})" : "");
            c.Txt(more, 1394, y + 30, lf, c.IsHover("updates:hidden") ? th.Text : th.Accent, StringAlignment.Far);
            c.Hit(c.R(1394 - c.TextWidth(more, lf) - 8, y + 12, c.TextWidth(more, lf) + 16, 26), "updates:hidden");
            bottom = y + 44;
        }

        private float Section(NebulaCanvas c, float y, string title, string hint, List<(UpdatesController.DriverUpdate u, bool isBios)> rows, Color tone, bool loading, string empty)
        {
            var th = c.Theme;
            const float rowH = 44;
            float h = 78 + (rows.Count == 0 ? 36 : rows.Count * rowH) + 14;
            c.Surface(236, y, 1158, h);
            // coloured stripe on the left edge tells the sections apart at a glance
            c.Card(c.R(236, y + 16, 5, h - 32), 2, tone);
            c.Txt(title, 262, y + 30, c.F(15, FontStyle.Bold), th.Text);
            c.Pill(262 + c.TextWidth(title, c.F(15, FontStyle.Bold)) + 12, y + 12, rows.Count.ToString(), Color.FromArgb(50, tone), tone);
            c.Txt(hint, 262, y + 52, c.F(10), th.Faint);

            float ry = y + 78;
            if (rows.Count == 0)
            {
                c.Txt(loading ? NebulaText.T("Loading…", "Загружаем…") : empty, 262, ry + 22, c.F(12), loading ? th.Muted : th.Faint);
                return y + h;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                var (u, isBios) = rows[i];
                var r = c.R(248, ry, 1134, rowH - 4);
                string id = "updates:open:" + u.downloadUrl.GetHashCode();
                bool hv = c.IsHover(id);
                if (hv) c.Card(r, 8, th.Raised);
                else if (i % 2 == 1) c.Card(r, 8, Color.FromArgb(70, th.Raised));
                if (u.status == UpdatesController.STATUS_NEW) c.Card(c.R(248, ry + 8, 3, rowH - 20), 2, tone);

                float baseline = ry + 26;
                // status pill
                string mark = u.status switch
                {
                    UpdatesController.STATUS_NEW => NebulaText.T("NEW", "НОВОЕ"),
                    UpdatesController.STATUS_UPTODATE => "✓ " + NebulaText.T("OK", "ОК"),
                    UpdatesController.STATUS_HIDDEN => NebulaText.T("absent", "нет железа"),
                    _ => "?",
                };
                c.Pill(262, ry + 8, mark, Color.FromArgb(u.status == UpdatesController.STATUS_NEW ? 70 : 40, tone), tone);
                string cat = isBios ? "BIOS" : Trim(u.categoryName, 22);
                var cc = isBios ? Bios : CategoryColor(u.categoryName ?? "");
                c.Pill(372, ry + 8, cat, Color.FromArgb(u.status == UpdatesController.STATUS_HIDDEN ? 18 : 36, cc), u.status == UpdatesController.STATUS_HIDDEN ? th.Faint : cc);
                c.Txt(Trim(u.title, 64), 560, baseline, c.F(12, u.status == UpdatesController.STATUS_NEW ? FontStyle.Bold : FontStyle.Regular), u.status == UpdatesController.STATUS_HIDDEN ? th.Faint : th.Text);
                c.Txt(u.date, 1120, baseline, c.F(11), th.Muted, StringAlignment.Far);

                var (avail, inst) = Versions(u);
                if (u.status == UpdatesController.STATUS_NEW && inst.Length > 0)
                {
                    c.Txt(avail, 1374, baseline - 6, c.F(12, FontStyle.Bold), tone, StringAlignment.Far);
                    c.Txt(NebulaText.T("installed ", "стоит ") + inst, 1374, baseline + 10, c.F(9), th.Faint, StringAlignment.Far);
                }
                else if (u.status == UpdatesController.STATUS_UPTODATE && inst.Length > 0 && inst != avail)
                {
                    c.Txt(inst, 1374, baseline - 6, c.F(12, FontStyle.Bold), tone, StringAlignment.Far);
                    c.Txt(NebulaText.T("site ", "на сайте ") + avail, 1374, baseline + 10, c.F(9), th.Faint, StringAlignment.Far);
                }
                else
                    c.Txt(avail, 1374, baseline, c.F(12, FontStyle.Bold), u.status == UpdatesController.STATUS_UPTODATE ? tone : th.Muted, StringAlignment.Far);

                c.Hit(r, id, u.tip ?? "");
                ry += rowH;
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
