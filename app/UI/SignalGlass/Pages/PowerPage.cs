using GHelper.Battery;
using GHelper.Mode;

namespace GHelper.UI.SignalGlass.Pages
{
    /// <summary>
    /// 06 Power: separate AC and Battery (DC) timeout selectors for screen/sleep/hibernate,
    /// plus per-mode AC/DC switching rules. External power changes never overwrite user draft.
    /// </summary>
    public sealed class PowerPage : SignalPage
    {
        public override string Id => "power";
        public override string GroupId => "tuning";
        public override string Title => SignalText.T("Power", "Питание");
        public override bool CanApply => dirty;
        public override bool CanReset => true;

        private readonly Dictionary<(PowerPlan.Idle, bool), int> draftTimeouts = new();
        private bool draftRulesEnabled;
        private int draftOnBatMode = PowerRules.Keep;
        private int draftOnAcMode = PowerRules.Restore;
        private bool draftCompactIdle;
        private bool dirty;
        private float lastContentH = 680f;

        public override void OnEnter(SignalPaintContext? ctx)
        {
            if (!dirty) LoadFromBackend();
        }

        private void LoadFromBackend()
        {
            draftTimeouts.Clear();
            try
            {
                foreach (PowerPlan.Idle w in Enum.GetValues<PowerPlan.Idle>())
                {
                    foreach (bool ac in new[] { true, false })
                        draftTimeouts[(w, ac)] = PowerPlan.Get(w, ac);
                }
            }
            catch { }

            draftRulesEnabled = PowerRules.Enabled;
            int cur = Modes.GetCurrent();
            draftOnBatMode = PowerRules.OnBattery(cur);
            draftOnAcMode = PowerRules.OnAc(cur);
            draftCompactIdle = AppConfig.Is("compact_idle");
            dirty = false;
        }

        public override void Reset()
        {
            LoadFromBackend();
        }

        public override void Apply()
        {
            foreach (var ((what, ac), sec) in draftTimeouts)
            {
                if (sec >= 0) PowerPlan.Set(what, ac, sec);
            }
            PowerRules.SetEnabled(draftRulesEnabled);
            int cur = Modes.GetCurrent();
            PowerRules.SetOnBattery(cur, draftOnBatMode);
            PowerRules.SetOnAc(cur, draftOnAcMode);
            AppConfig.Set("compact_idle", draftCompactIdle ? 1 : 0);
            dirty = false;
        }

        public override float ContentHeightDip(SignalPaintContext ctx) => lastContentH;

        public override void Paint(SignalPaintContext ctx)
        {
            var body = ctx.Layout.WorkspaceBody;
            float x = body.X, w = body.Width, y = body.Y - ctx.ScrollDip;
            float pad = 24f, rx = x + pad, rw = w - pad * 2f;

            bool hibSupported = GetTimeout(PowerPlan.Idle.Hibernate, true) >= 0 || GetTimeout(PowerPlan.Idle.Hibernate, false) >= 0;
            int rows = 6 + (hibSupported ? 1 : 0);
            float panelH = rows * 74f + 32f;
            ctx.DrawPanel(new RectangleF(x, y, w, panelH));
            float ry = y + 16f;

            ry = PaintDualTimeoutRow(ctx, ry, rx, rw, PowerPlan.Idle.ScreenOff,
                SignalText.T("Turn screen off", "Отключать экран"),
                SignalText.T("Separate AC and battery values", "Раздельные значения от сети и от батареи"));

            ry = PaintDualTimeoutRow(ctx, ry, rx, rw, PowerPlan.Idle.Sleep,
                SignalText.T("Sleep after", "Переход в сон"),
                SignalText.T("Never is an explicit option", "Вариант «Никогда» выбирается явно"));

            if (hibSupported)
            {
                ry = PaintDualTimeoutRow(ctx, ry, rx, rw, PowerPlan.Idle.Hibernate,
                    SignalText.T("Hibernate after", "Гибернация"),
                    SignalText.T("Only if hibernation is available", "Только при поддержке гибернации"));
            }

            ry = ctx.DrawToggleRow(ry, rx, rw,
                SignalText.T("Power source switching rules", "Автопереключение по питанию"),
                SignalText.T("Per-mode actions when unplugging or reconnecting AC", "Действия при отключении/подключении зарядки"),
                "power:rules",
                draftRulesEnabled,
                () => { draftRulesEnabled = !draftRulesEnabled; dirty = true; });

            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("On battery mode", "Режим от батареи"),
                SignalText.T("Save previous AC mode for restore", "Запоминает предыдущий режим от сети"),
                DescribeRule(draftOnBatMode, ac: false),
                "power:batmode",
                pt => ShowRuleMenu(ctx, pt, ac: false),
                enabled: draftRulesEnabled,
                showChevron: draftRulesEnabled);

            ry = ctx.DrawSettingRow(ry, rx, rw,
                SignalText.T("On AC reconnect", "При подключении к сети"),
                SignalText.T("Do not override an explicit user choice silently", "Не сбрасывает ручной выбор без правила"),
                DescribeRule(draftOnAcMode, ac: true),
                "power:acmode",
                pt => ShowRuleMenu(ctx, pt, ac: true),
                enabled: draftRulesEnabled,
                showChevron: draftRulesEnabled);

            ctx.DrawToggleRow(ry, rx, rw,
                SignalText.T("Show idle timeouts in fly-out", "Таймауты простоя в мини-окне"),
                SignalText.T("Adds screen/sleep timeouts to the tray fly-out", "Добавляет таймауты в компактное окно из трея"),
                "power:compactidle",
                draftCompactIdle,
                () => { draftCompactIdle = !draftCompactIdle; dirty = true; },
                drawDivider: false);

            lastContentH = panelH + 16f;
        }

        private float PaintDualTimeoutRow(SignalPaintContext ctx, float dipY, float x, float w, PowerPlan.Idle what, string title, string sub)
        {
            const float rowH = 74f;
            float btnW = Math.Min(132f, w * 0.16f);
            float controlsW = btnW * 2f + 10f;
            float textW = w - controlsW - 24f;

            var titleRect = ctx.Rtl ? new RectangleF(x + controlsW + 24f, dipY + 6f, textW, 24f) : new RectangleF(x, dipY + 6f, textW, 24f);
            var subRect = ctx.Rtl ? new RectangleF(x + controlsW + 24f, dipY + 32f, textW, 32f) : new RectangleF(x, dipY + 32f, textW, 32f);
            var fmt = ctx.Rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft;

            ctx.Painter.DrawText(ctx.G, title, ctx.Px(titleRect), ctx.Painter.Font(15f, ctx.Scale, FontStyle.Bold), ctx.Theme.Text, fmt);
            ctx.Painter.DrawText(ctx.G, sub, ctx.Px(subRect), ctx.Painter.Font(12f, ctx.Scale), ctx.Theme.Muted, fmt);

            float bx = ctx.Rtl ? x : (x + w - controlsW);
            int acSec = GetTimeout(what, true);
            int dcSec = GetTimeout(what, false);

            ctx.DrawButton($"power:{what}:ac", new RectangleF(bx, dipY + 12f, btnW, 38f),
                "AC " + PowerPlan.Label(acSec), pt => ShowTimeoutMenu(ctx, pt, what, true),
                enabled: acSec >= 0, role: AccessibleRole.ComboBox, fontSize: 12f);
            ctx.DrawButton($"power:{what}:dc", new RectangleF(bx + btnW + 10f, dipY + 12f, btnW, 38f),
                "DC " + PowerPlan.Label(dcSec), pt => ShowTimeoutMenu(ctx, pt, what, false),
                enabled: dcSec >= 0, role: AccessibleRole.ComboBox, fontSize: 12f);

            using var pen = new Pen(Color.FromArgb(80, ctx.Theme.Line), Math.Max(1f, ctx.Scale));
            ctx.G.DrawLine(pen, x * ctx.Scale, (dipY + rowH) * ctx.Scale, (x + w) * ctx.Scale, (dipY + rowH) * ctx.Scale);
            return dipY + rowH;
        }

        private int GetTimeout(PowerPlan.Idle what, bool ac) =>
            draftTimeouts.TryGetValue((what, ac), out int v) ? v : -1;

        private void ShowTimeoutMenu(SignalPaintContext ctx, Point pt, PowerPlan.Idle what, bool ac)
        {
            int now = GetTimeout(what, ac);
            ctx.ShowMenu(pt, PowerPlan.Spans.Select(s => (PowerPlan.Label(s), s == now, (string?)null, (Action)(() =>
            {
                draftTimeouts[(what, ac)] = s;
                dirty = true;
            }))));
        }

        private string DescribeRule(int target, bool ac)
        {
            if (ac && target == PowerRules.Restore) return SignalText.T("Restore previous", "Вернуть прежний");
            if (target < 0 || !Modes.Exists(target)) return SignalText.T("Keep current", "Не менять");
            return Modes.GetName(target);
        }

        private void ShowRuleMenu(SignalPaintContext ctx, Point pt, bool ac)
        {
            int curMode = Modes.GetCurrent();
            int now = ac ? draftOnAcMode : draftOnBatMode;
            var items = new List<(string, bool, string?, Action)>
            {
                (SignalText.T("Keep current", "Не менять"), now == PowerRules.Keep, null, () => { if (ac) draftOnAcMode = PowerRules.Keep; else draftOnBatMode = PowerRules.Keep; dirty = true; }),
            };
            if (ac)
            {
                items.Add((SignalText.T("Restore previous", "Вернуть прежний"), now == PowerRules.Restore, null,
                    () => { draftOnAcMode = PowerRules.Restore; dirty = true; }));
            }
            foreach (int m in Modes.GetList())
            {
                if (m == curMode) continue;
                int tm = m;
                items.Add((Modes.GetName(tm), now == tm, null, () => { if (ac) draftOnAcMode = tm; else draftOnBatMode = tm; dirty = true; }));
            }
            ctx.ShowMenu(pt, items);
        }
    }
}
