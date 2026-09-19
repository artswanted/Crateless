using GHelper.Peripherals;

namespace GHelper.UI.Nebula.Pages
{
    /// <summary>Devices page (design kit screen "mouse"): connected ASUS peripherals and Aura sync.</summary>
    public sealed class DevicesPage : NebulaPage
    {
        public override string Id => "mouse";
        public override string Title => NebulaText.T("Devices", "Устройства");
        public override string Subtitle => NebulaText.T("Settings of connected peripherals.", "Настройки подключённой периферии.");

        private int selected;
        private List<IPeripheral> devices = new();
        private long batteryRequested;

        public override void Refresh(bool opened)
        {
            try { devices = PeripheralsProvider.AllPeripherals(); } catch { devices = new(); }
            if (selected >= devices.Count) selected = 0;

            long now = Environment.TickCount64;
            if (opened || now - batteryRequested > 30_000)
            {
                batteryRequested = now;
                Task.Run(PeripheralsProvider.RefreshBatteryForAllDevices);
            }
        }

        public override void Paint(NebulaCanvas c)
        {
            var th = c.Theme;

            // ---- list ------------------------------------------------------------------------------
            c.Surface(236, 139, 272, 559);
            c.Txt(NebulaText.T("Connected devices", "Подключённые устройства"), 256, 169, c.F(15, FontStyle.Bold), th.Text);

            if (devices.Count == 0)
            {
                c.Txt(NebulaText.T("No ASUS peripherals found.", "Периферия ASUS не обнаружена."), 256, 231, c.F(12), th.Muted);
                c.Txt(NebulaText.T("Mice, keyboards and headsets", "Мыши, клавиатуры и гарнитуры"), 256, 256, c.F(10), th.Faint);
                c.Txt(NebulaText.T("appear here automatically.", "появятся здесь автоматически."), 256, 274, c.F(10), th.Faint);
            }

            float y = 205;
            for (int i = 0; i < devices.Count && i < 6; i++)
            {
                var d = devices[i];
                bool sel = i == selected;
                var r = c.R(252, y, 240, 70);
                c.Card(r, 8, sel ? th.AccentBg : c.IsHover("dev:" + i) ? th.Raised : th.Card, sel ? th.Accent : th.Line);
                c.IconAt(IconFor(d), 268, y + 23, 24, sel);
                c.Txt(Trim(SafeName(d), 22), 311, y + 26, c.F(13, FontStyle.Bold), sel ? th.Accent : th.Text);
                c.Txt(StatusLine(d), 311, y + 49, c.F(9), th.Muted);
                c.Hit(r, "dev:" + i);
                y += 80;
            }

            c.Txt(NebulaText.T("Detection is automatic;", "Обнаружение автоматическое;"), 256, 642, c.F(10), th.Faint);
            c.Txt(NebulaText.T("plug a device in to see it here.", "подключи устройство, и оно появится."), 256, 663, c.F(10), th.Faint);

            // ---- details ---------------------------------------------------------------------------
            c.Surface(524, 139, 530, 559);
            if (devices.Count == 0)
            {
                c.Txt(NebulaText.T("Laptop", "Ноутбук"), 544, 169, c.F(15, FontStyle.Bold), th.Text);
                c.Txt(AppConfig.GetModelShort(), 544, 190, c.F(11), th.Muted);
                c.Txt(NebulaText.T("Keyboard lighting and keys live in their own sections.", "Подсветка и клавиши ноутбука — в своих разделах."), 544, 240, c.F(12), th.Muted);
                c.Button(544, 262, 220, 36, NebulaText.T("Lighting", "Подсветка"), "dev:light", primary: false);
                c.Button(776, 262, 220, 36, NebulaText.T("Keys", "Клавиши"), "dev:keys", primary: false);
            }
            else
            {
                var d = devices[selected];
                c.Txt(SafeName(d), 544, 169, c.F(15, FontStyle.Bold), th.Text);
                c.Txt(TypeName(d) + " · " + (d.IsDeviceReady ? NebulaText.T("connected", "подключено") : NebulaText.T("not ready", "не готово")), 544, 190, c.F(11), th.Muted);

                c.IconAt(IconFor(d), 760, 230, 48, true);
                if (d.HasBattery() && d.IsDeviceReady)
                {
                    string bat = NebulaText.T("CHARGE", "ЗАРЯД") + " " + d.Battery + "%" + (d.Charging ? " ⚡" : "");
                    float w = c.TextWidth(bat, c.F(11, FontStyle.Bold)) + 24;
                    c.Pill(789 - w / 2, 375, bat, th.AccentBg, th.Accent);
                    c.Bar(544, 420, 490, 4, d.Battery / 100f, th.Accent);
                }

                c.Txt(NebulaText.T("Profiles, DPI, buttons, lighting and power", "Профили, DPI, кнопки, подсветка и питание"), 544, 485, c.F(13), th.Text);
                c.Txt(NebulaText.T("are configured in the device window (classic form for now).", "настраиваются в окне устройства (пока классическая форма)."), 544, 506, c.F(11), th.Faint);
                c.Button(544, 530, 490, 36, NebulaText.T("Open device settings", "Открыть настройки устройства"), "dev:open", primary: true, enabled: d.IsDeviceReady);
            }

            // ---- aura sync ----------------------------------------------------------------------------
            float sy = 598;
            c.Txt(NebulaText.T("Aura Sync", "Aura Sync"), 544, sy, c.F(13, FontStyle.Bold), th.Text);
            c.Txt(NebulaText.T("Mice follow the keyboard colour", "Мыши повторяют цвет клавиатуры"), 544, sy + 30, c.F(12), th.Muted);
            c.Toggle(996, sy + 13, PeripheralsProvider.IsAuraSync, "dev:sync:mouse");
            c.Txt(NebulaText.T("External keyboards", "Внешние клавиатуры"), 544, sy + 60, c.F(12), th.Muted);
            c.Toggle(996, sy + 43, PeripheralsProvider.IsKeyboardAuraSync, "dev:sync:kb");
            c.Txt(NebulaText.T("Headsets", "Гарнитуры"), 544, sy + 90, c.F(12), th.Muted);
            c.Toggle(996, sy + 73, PeripheralsProvider.IsHeadsetAuraSync, "dev:sync:hs");
        }

        private static string SafeName(IPeripheral d)
        {
            try { return d.GetDisplayName(); } catch { return "ASUS device"; }
        }

        private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

        private static string StatusLine(IPeripheral d)
        {
            if (!d.IsDeviceReady) return NebulaText.T("not ready", "не готово");
            if (d.HasBattery()) return NebulaText.T("battery", "батарея") + " " + d.Battery + "%" + (d.Charging ? " ⚡" : "");
            return NebulaText.T("connected", "подключено");
        }

        private static string TypeName(IPeripheral d) => d.DeviceType() switch
        {
            PeripheralType.Mouse => NebulaText.T("Mouse", "Мышь"),
            PeripheralType.Keyboard => NebulaText.T("Keyboard", "Клавиатура"),
            PeripheralType.Headset => NebulaText.T("Headset", "Гарнитура"),
            _ => NebulaText.T("Device", "Устройство"),
        };

        private static string IconFor(IPeripheral d) => d.DeviceType() switch
        {
            PeripheralType.Keyboard => "keyboard",
            PeripheralType.Headset => "heart",
            _ => "mouse",
        };

        public override bool Click(string id, Point at, NebulaForm form)
        {
            if (id.StartsWith("dev:") && int.TryParse(id[4..], out int idx))
            {
                selected = idx;
                return true;
            }
            switch (id)
            {
                case "dev:open":
                    if (selected < devices.Count) Program.settingsForm.OpenPeripheral(devices[selected]);
                    return true;
                case "dev:light": form.ShowPage("light"); return true;
                case "dev:keys": form.ShowPage("keyboard"); return true;
                case "dev:sync:mouse": PeripheralsProvider.SetAuraSync(!PeripheralsProvider.IsAuraSync); return true;
                case "dev:sync:kb": PeripheralsProvider.SetKeyboardAuraSync(!PeripheralsProvider.IsKeyboardAuraSync); return true;
                case "dev:sync:hs": PeripheralsProvider.SetHeadsetAuraSync(!PeripheralsProvider.IsHeadsetAuraSync); return true;
            }
            return false;
        }
    }
}
