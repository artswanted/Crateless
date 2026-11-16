using GHelper.Peripherals;
using GHelper.Peripherals.Headset;
using GHelper.Peripherals.Keyboard;
using GHelper.Peripherals.Mouse;

namespace GHelper.UI.SignalGlass.Pages
{
    /// <summary>
    /// 09 Connected devices: mice, keyboards and headsets from PeripheralsProvider.
    /// Shows an empty state when nothing is connected (not an error) and keeps per-type capabilities isolated.
    /// </summary>
    public sealed class DevicesPage : SignalPage
    {
        public override string Id => "devices";
        public override string GroupId => "devices";
        public override string Title => SignalText.T("Connected devices", "Подключённые устройства");
        public override bool HasFooter => false;

        private List<IPeripheral> devices = new();
        private int selectedIndex;
        private long lastBatteryPoll;
        private float lastContentH = 620f;
        private Action? requestRepaint;

        public override void OnEnter(SignalPaintContext? ctx)
        {
            requestRepaint = ctx?.RequestRefresh;
            RefreshDevices(true);
        }

        private void RefreshDevices(bool refreshBattery)
        {
            try { devices = PeripheralsProvider.AllPeripherals(); } catch { devices = new(); }
            if (selectedIndex >= devices.Count) selectedIndex = 0;

            long now = Environment.TickCount64;
            if (refreshBattery || now - lastBatteryPoll > 30_000)
            {
                lastBatteryPoll = now;
                Task.Run(() =>
                {
                    try { PeripheralsProvider.RefreshBatteryForAllDevices(); } catch { }
                    requestRepaint?.Invoke();
                });
            }
        }

        public override float ContentHeightDip(SignalPaintContext ctx) => lastContentH;

        public override void Paint(SignalPaintContext ctx)
        {
            requestRepaint = ctx.RequestRefresh;
            RefreshDevices(false);

            var body = ctx.Layout.WorkspaceBody;
            float x = body.X, w = body.Width, y = body.Y - ctx.ScrollDip;
            float pad = 24f, rx = x + pad, rw = w - pad * 2f;

            if (devices.Count == 0)
            {
                // Empty state per 14-state-empty
                ctx.DrawPanel(new RectangleF(x, y, w, body.Height));
                float cw = Math.Min(640f, w - 48f), ch = 220f;
                var card = new RectangleF(x + (w - cw) / 2f, y + (body.Height - ch) / 2f, cw, ch);
                ctx.DrawPanel(card);
                ctx.Painter.DrawText(ctx.G, SignalText.T("No compatible devices", "Нет совместимых устройств"),
                    ctx.Px(card.X + 32f, card.Y + 42f, cw - 64f, 32f),
                    ctx.Painter.Font(22f, ctx.Scale, FontStyle.Bold), ctx.Theme.Text, GlassPainter.FmtLeft);
                ctx.Painter.DrawText(ctx.G, SignalText.T("Connect a supported mouse, keyboard or headset.", "Подключите поддерживаемую мышь, клавиатуру или гарнитуру."),
                    ctx.Px(card.X + 32f, card.Y + 86f, cw - 64f, 44f),
                    ctx.Painter.Font(14f, ctx.Scale), ctx.Theme.Muted, GlassPainter.FmtWrap);
                ctx.DrawButton("devices:refresh", new RectangleF(card.Right - 156f, card.Bottom - 62f, 124f, 38f),
                    SignalText.T("Refresh", "Обновить"), _ => RefreshDevices(true), selected: true);
                lastContentH = body.Height;
                return;
            }

            var d = devices[selectedIndex];
            string devName = SafeName(d);
            bool ready = d.IsDeviceReady;

            int rowCount = 2 + CountDeviceRows(d);
            float panelH = rowCount * 74f + 32f;
            ctx.DrawPanel(new RectangleF(x, y, w, panelH));
            float ry = y + 16f;

            string status = !ready ? SignalText.T("Not ready", "Не готово")
                : d.HasBattery() ? $"{d.Battery}%{(d.Charging ? " ⚡" : "")}"
                : SignalText.T("Connected", "Подключено");

            ry = ctx.DrawSettingRow(ry, rx, rw,
                devName,
                status,
                devices.Count > 1 ? $"{selectedIndex + 1}/{devices.Count} · {SignalText.T("Switch", "Выбрать")}" : SignalText.T("Open controls", "Открыть окно"),
                "devices:select",
                pt =>
                {
                    if (devices.Count == 1)
                    {
                        if (ready) Program.settingsForm.OpenPeripheral(d);
                    }
                    else
                    {
                        var items = devices.Select((dev, i) => (SafeName(dev), i == selectedIndex, (string?)null, (Action)(() => selectedIndex = i))).ToList();
                        items.Add((SignalText.T("Open device controls…", "Открыть окно устройства…"), false, null, () => { if (ready) Program.settingsForm.OpenPeripheral(d); }));
                        ctx.ShowMenu(pt, items);
                    }
                },
                enabled: ready || devices.Count > 1,
                showChevron: devices.Count > 1);

            if (d is AsusMouse m)
            {
                ry = PaintMouseRows(ctx, ry, rx, rw, m, ready);
            }
            else if (d is AsusHeadset h)
            {
                ry = PaintHeadsetRows(ctx, ry, rx, rw, h, ready);
            }
            else if (d is AsusKeyboard k)
            {
                ry = PaintKeyboardRows(ctx, ry, rx, rw, k, ready);
            }

            bool sync = d.DeviceType() switch
            {
                PeripheralType.Mouse => PeripheralsProvider.IsAuraSync,
                PeripheralType.Keyboard => PeripheralsProvider.IsKeyboardAuraSync,
                _ => PeripheralsProvider.IsHeadsetAuraSync,
            };
            ctx.DrawToggleRow(ry, rx, rw,
                "Aura Sync",
                SignalText.T("Follow the laptop keyboard color", "Повторять цвет подсветки ноутбука"),
                "devices:sync",
                sync,
                () =>
                {
                    switch (d.DeviceType())
                    {
                        case PeripheralType.Mouse: PeripheralsProvider.SetAuraSync(!sync); break;
                        case PeripheralType.Keyboard: PeripheralsProvider.SetKeyboardAuraSync(!sync); break;
                        default: PeripheralsProvider.SetHeadsetAuraSync(!sync); break;
                    }
                },
                drawDivider: false);

            lastContentH = panelH + 16f;
        }

        private static int CountDeviceRows(IPeripheral d) => d switch
        {
            AsusMouse m => 1 + (m.SupportedPollingrates().Length > 0 ? 1 : 0) + (m.HasRGB() ? 1 : 0) + (m.HasAngleSnapping() ? 1 : 0),
            AsusHeadset h => (h.HasAnc() ? 1 : 0) + (h.HasSidetone() ? 1 : 0) + (h.HasNoiseReduction() ? 1 : 0) + (h.HasRGB() ? 1 : 0),
            AsusKeyboard k => (k.HasProfiles() ? 1 : 0) + (k.HasRGB() ? 1 : 0),
            _ => 0,
        };

        private static float PaintMouseRows(SignalPaintContext ctx, float ry, float rx, float rw, AsusMouse m, bool ready)
        {
            var dpis = m.DpiSettings ?? Array.Empty<AsusMouseDPI>();
            string curDpi = m.DpiProfile >= 0 && m.DpiProfile < dpis.Length ? dpis[m.DpiProfile].DPI.ToString() : "—";
            ry = ctx.DrawSettingRow(ry, rx, rw,
                "DPI",
                SignalText.T("Active profile sensitivity", "Чувствительность активного профиля"),
                curDpi,
                "devices:mouse:dpi",
                pt =>
                {
                    int cnt = Math.Min(dpis.Length, Math.Max(1, m.CurrentDPIProfileCount));
                    ctx.ShowMenu(pt, Enumerable.Range(0, cnt).Select(i => ($"Profile {i + 1}: {dpis[i].DPI} DPI", i == m.DpiProfile, (string?)null, (Action)(() => Task.Run(() => m.SetDPIProfile(i))))));
                },
                enabled: ready && m.CanChangeDPIProfile() && dpis.Length > 0,
                showChevron: true);

            var rates = m.SupportedPollingrates();
            if (rates.Length > 0)
            {
                ry = ctx.DrawSettingRow(ry, rx, rw,
                    SignalText.T("Polling rate", "Частота опроса"),
                    SignalText.T("Select from capabilities", "По поддерживаемым значениям"),
                    $"{m.PollingRate}",
                    "devices:mouse:poll",
                    pt => ctx.ShowMenu(pt, rates.Select(r => (r.ToString(), r == m.PollingRate, (string?)null, (Action)(() => Task.Run(() => m.SetPollingRate(r)))))),
                    enabled: ready && m.CanSetPollingRate(),
                    showChevron: true);
            }

            if (m.HasAngleSnapping())
            {
                ry = ctx.DrawToggleRow(ry, rx, rw,
                    SignalText.T("Angle snapping", "Выравнивание угла"),
                    SignalText.T("Straightens slow horizontal/vertical strokes", "Выпрямляет медленные движения"),
                    "devices:mouse:snap",
                    m.AngleSnapping,
                    () => Task.Run(() => m.SetAngleSnapping(!m.AngleSnapping)),
                    enabled: ready);
            }

            if (m.HasRGB())
            {
                var ls = m.LightingSetting is { Length: > 0 } arr ? arr[0] : null;
                ry = ctx.DrawSettingRow(ry, rx, rw,
                    SignalText.T("Mouse lighting", "Подсветка мыши"),
                    SignalText.T("Available only on compatible devices", "Доступно для совместимых устройств"),
                    ls is null ? "—" : ls.LightingMode.ToString(),
                    "devices:mouse:rgb",
                    pt =>
                    {
                        if (ls is null) return;
                        ctx.ShowMenu(pt, Enum.GetValues<LightingMode>().Select(mode => (mode.ToString(), mode == ls.LightingMode, (string?)null, (Action)(() =>
                        {
                            ls.LightingMode = mode;
                            Task.Run(() => m.SetLightingSetting(ls, m.SupportedLightingZones().Length > 1 ? LightingZone.All : m.SupportedLightingZones()[0]));
                        }))));
                    },
                    enabled: ready && ls is not null,
                    showChevron: true);
            }
            return ry;
        }

        private static float PaintHeadsetRows(SignalPaintContext ctx, float ry, float rx, float rw, AsusHeadset h, bool ready)
        {
            if (h.HasAnc())
            {
                string[] modes = { Properties.Strings.Off, Properties.Strings.On, Properties.Strings.HeadsetAmbient };
                string cur = h.AncMode >= 0 && h.AncMode < modes.Length ? modes[h.AncMode] : "—";
                ry = ctx.DrawSettingRow(ry, rx, rw,
                    "ANC",
                    SignalText.T("Active noise cancellation", "Активное шумоподавление"),
                    cur,
                    "devices:hs:anc",
                    pt => ctx.ShowMenu(pt, modes.Select((m, i) => (m, i == h.AncMode, (string?)null, (Action)(() => Task.Run(() => h.SetAnc(i)))))),
                    enabled: ready,
                    showChevron: true);
            }

            if (h.HasSidetone())
            {
                ry = ctx.DrawToggleRow(ry, rx, rw,
                    SignalText.T("Sidetone", "Сайдтон"),
                    SignalText.T("Hear your voice in the headset", "Слышать свой голос в гарнитуре"),
                    "devices:hs:sidetone",
                    h.SidetoneEnabled,
                    () => Task.Run(() => h.SetSidetone(!h.SidetoneEnabled, h.Sidetone)),
                    enabled: ready);
            }

            if (h.HasNoiseReduction())
            {
                ry = ctx.DrawToggleRow(ry, rx, rw,
                    SignalText.T("Mic noise reduction", "Шумоподавление микрофона"),
                    SignalText.T("Filters background microphone noise", "Фильтрация фонового шума микрофона"),
                    "devices:hs:nr",
                    h.NoiseReductionEnabled,
                    () => Task.Run(() => h.SetNoiseReduction(!h.NoiseReductionEnabled, Math.Max(0, h.NoiseReduction))),
                    enabled: ready);
            }

            if (h.HasRGB())
            {
                ry = ctx.DrawSettingRow(ry, rx, rw,
                    SignalText.T("Headset lighting", "Подсветка гарнитуры"),
                    "",
                    h.LightingMode.ToString(),
                    "devices:hs:rgb",
                    pt => ctx.ShowMenu(pt, Enum.GetValues<HeadsetLightingMode>()
                        .Where(x => x != HeadsetLightingMode.MqaIndicator)
                        .Select(m => (m.ToString(), m == h.LightingMode, (string?)null, (Action)(() => Task.Run(() => h.SetLighting(m, h.Brightness, h.LightingColor)))))),
                    enabled: ready,
                    showChevron: true);
            }
            return ry;
        }

        private static float PaintKeyboardRows(SignalPaintContext ctx, float ry, float rx, float rw, AsusKeyboard k, bool ready)
        {
            if (k.HasProfiles())
            {
                ry = ctx.DrawSettingRow(ry, rx, rw,
                    SignalText.T("Profile", "Профиль"),
                    "",
                    SignalText.T("Choose", "Выбрать"),
                    "devices:kb:profile",
                    pt => ctx.ShowMenu(pt, Enumerable.Range(0, 4).Select(i => ($"Profile {i + 1}", false, (string?)null, (Action)(() => Task.Run(() => k.SetProfile(i)))))),
                    enabled: ready,
                    showChevron: true);
            }

            if (k.HasRGB())
            {
                ry = ctx.DrawSettingRow(ry, rx, rw,
                    SignalText.T("Keyboard lighting", "Подсветка клавиатуры"),
                    SignalText.T("Per-key RGB is in device controls", "Попиксельная RGB в окне устройства"),
                    SignalText.T("Open controls", "Открыть окно"),
                    "devices:kb:open",
                    _ => Program.settingsForm.OpenPeripheral(k),
                    enabled: ready);
            }
            return ry;
        }

        private static string SafeName(IPeripheral d)
        {
            try { return d.GetDisplayName(); } catch { return "ASUS Device"; }
        }
    }
}
