using GHelper.Peripherals;
using GHelper.Peripherals.Headset;
using GHelper.Peripherals.Keyboard;
using GHelper.Peripherals.Mouse;

namespace GHelper.UI.Nebula.Pages
{
    /// <summary>
    /// Devices page: connected ASUS peripherals with the everyday settings inline
    /// (DPI, polling, lighting, sidetone, power). The classic per-device window stays
    /// reachable for button bindings, per-key RGB and the equalizer.
    /// </summary>
    public sealed class DevicesPage : NebulaPage
    {
        public override string Id => "mouse";
        public override string Title => NebulaText.T("Devices", "Устройства");
        public override string Subtitle => NebulaText.T("Settings of connected peripherals.", "Настройки подключённой периферии.");

        private int selected;
        private List<IPeripheral> devices = new();
        private long batteryRequested;
        private float contentBottom = 720;
        private string status = "";

        public override float ContentHeight => contentBottom + 20;

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

        // ---- painting ------------------------------------------------------------------------------
        public override void Paint(NebulaCanvas c)
        {
            var th = c.Theme;

            // ---- list ------------------------------------------------------------------------------
            float listH = Math.Max(559, 205 + Math.Max(1, devices.Count) * 80 + 60);
            c.Surface(236, 139, 272, listH);
            c.Txt(NebulaText.T("Connected devices", "Подключённые устройства"), 256, 169, c.F(15, FontStyle.Bold), th.Text);

            if (devices.Count == 0)
            {
                c.Txt(NebulaText.T("No ASUS peripherals found.", "Периферия ASUS не обнаружена."), 256, 231, c.F(12), th.Muted);
                c.Txt(NebulaText.T("Mice, keyboards and headsets", "Мыши, клавиатуры и гарнитуры"), 256, 256, c.F(10), th.Faint);
                c.Txt(NebulaText.T("appear here automatically.", "появятся здесь автоматически."), 256, 274, c.F(10), th.Faint);
            }

            float y = 205;
            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                bool sel = i == selected;
                var r = c.R(252, y, 240, 70);
                c.Card(r, 8, sel ? th.AccentBg : c.IsHover("dev:" + i) ? th.Raised : th.Card, sel ? th.Accent : th.Line);
                var thumb = NebulaAssets.Scaled(NebulaAssets.DeviceRender(d.DeviceType(), SafeName(d)), (int)c.S(44));
                if (thumb is not null) c.G.DrawImage(thumb, c.S(260), c.S(y + 13), c.S(44), c.S(44));
                c.Txt(Trim(SafeName(d), 22), 311, y + 26, c.F(13, FontStyle.Bold), sel ? th.Accent : th.Text);
                c.Txt(StatusLine(d), 311, y + 49, c.F(9), th.Muted);
                c.Hit(r, "dev:" + i);
                y += 80;
            }

            // ---- details ---------------------------------------------------------------------------
            float detailBottom = devices.Count == 0 ? PaintLaptop(c) : PaintDevice(c, devices[selected]);
            contentBottom = Math.Max(139 + listH, detailBottom);
        }

        private float PaintLaptop(NebulaCanvas c)
        {
            var th = c.Theme;
            c.Surface(524, 139, 530, 559);
            c.Txt(NebulaText.T("Laptop", "Ноутбук"), 544, 169, c.F(15, FontStyle.Bold), th.Text);
            c.Txt(AppConfig.GetModelShort(), 544, 190, c.F(11), th.Muted);
            var lap = NebulaAssets.HeroScaled((int)c.S(360));
            if (lap is not null)
            {
                float r = Math.Min(c.S(360) / lap.Width, c.S(240) / lap.Height);
                c.G.DrawImage(lap, c.S(609), c.S(320), lap.Width * r, lap.Height * r);
            }
            c.Txt(NebulaText.T("Keyboard lighting and keys live in their own sections.", "Подсветка и клавиши ноутбука — в своих разделах."), 544, 240, c.F(12), th.Muted);
            c.Button(544, 262, 220, 36, NebulaText.T("Lighting", "Подсветка"), "dev:light", primary: false);
            c.Button(776, 262, 220, 36, NebulaText.T("Keys", "Клавиши"), "dev:keys", primary: false);
            PaintAuraSync(c, 598);
            return 698;
        }

        private float PaintDevice(NebulaCanvas c, IPeripheral d)
        {
            var th = c.Theme;
            // header block first, body height is known only after painting → paint body into a list of rows
            float y = 139;
            float top = y;
            // we draw the surface last (after measuring), so collect with a deferred height: draw a tall
            // surface now and rely on the page background matching the card fill outside the content.
            float estimated = 300 + 90 * 8;
            c.Surface(524, top, 530, estimated);

            c.Txt(SafeName(d), 544, top + 30, c.F(15, FontStyle.Bold), th.Text);
            c.Txt(TypeName(d) + " · " + (d.IsDeviceReady ? NebulaText.T("connected", "подключено") : NebulaText.T("not ready", "не готово")), 544, top + 51, c.F(11), th.Muted);

            var big = NebulaAssets.Scaled(NebulaAssets.DeviceRender(d.DeviceType(), SafeName(d)), (int)c.S(120));
            if (big is not null) c.G.DrawImage(big, c.S(914), c.S(top + 20), c.S(120), c.S(120));

            y = top + 90;
            if (d.HasBattery() && d.IsDeviceReady)
            {
                c.Txt(NebulaText.T("Battery", "Батарея"), 544, y, c.F(12), th.Muted);
                c.Txt(d.Battery + "%" + (d.Charging ? " ⚡" : ""), 894, y, c.F(12, FontStyle.Bold), th.Text, StringAlignment.Far);
                c.Bar(544, y + 8, 350, 3, d.Battery / 100f, th.Accent);
                y += 34;
            }
            y += 20;

            if (!d.IsDeviceReady)
            {
                c.Txt(NebulaText.T("Device is not responding. Reconnect it to see its settings.", "Устройство не отвечает. Переподключи его, чтобы увидеть настройки."), 544, y + 12, c.F(12), th.Muted);
                y += 40;
            }
            else
            {
                y = d switch
                {
                    AsusMouse m => PaintMouse(c, m, y),
                    AsusHeadset h => PaintHeadset(c, h, y),
                    AsusKeyboard k => PaintKeyboard(c, k, y),
                    _ => y,
                };
            }

            if (status.Length > 0) { c.Txt(status, 544, y + 12, c.F(11), th.Warning); y += 26; }

            string more = d.DeviceType() switch
            {
                PeripheralType.Mouse => NebulaText.T("Button bindings, lighting zones and DPI colours are in the device window.", "Привязки кнопок, зоны подсветки и цвета DPI — в окне устройства."),
                PeripheralType.Headset => NebulaText.T("Equalizer bands and microphone type are in the device window.", "Полосы эквалайзера и тип микрофона — в окне устройства."),
                _ => NebulaText.T("Key bindings, per-key lighting and profiles are in the device window.", "Привязки клавиш, попиксельная подсветка и профили — в окне устройства."),
            };
            c.Txt(more, 544, y + 26, c.F(11), th.Faint);
            c.Button(544, y + 40, 490, 36, NebulaText.T("Open device window", "Открыть окно устройства"), "dev:open", primary: false, enabled: d.IsDeviceReady);
            y += 96;

            y = PaintAuraSync(c, y, d.DeviceType());
            return y;
        }

        private float PaintAuraSync(NebulaCanvas c, float sy, PeripheralType? only = null)
        {
            var th = c.Theme;
            c.Txt("Aura Sync", 544, sy, c.F(13, FontStyle.Bold), th.Text);
            float y = sy + 30;
            if (only is null)
            {
                c.Txt(NebulaText.T("Mice follow the keyboard colour", "Мыши повторяют цвет клавиатуры"), 544, y, c.F(12), th.Muted);
                c.Toggle(996, y - 17, PeripheralsProvider.IsAuraSync, "dev:sync:mouse"); y += 30;
                c.Txt(NebulaText.T("External keyboards", "Внешние клавиатуры"), 544, y, c.F(12), th.Muted);
                c.Toggle(996, y - 17, PeripheralsProvider.IsKeyboardAuraSync, "dev:sync:kb"); y += 30;
                c.Txt(NebulaText.T("Headsets", "Гарнитуры"), 544, y, c.F(12), th.Muted);
                c.Toggle(996, y - 17, PeripheralsProvider.IsHeadsetAuraSync, "dev:sync:hs"); y += 30;
                return y;
            }
            string label = NebulaText.T("Follow the laptop keyboard colour", "Повторять цвет клавиатуры ноутбука");
            c.Txt(label, 544, y, c.F(12), th.Muted);
            switch (only)
            {
                case PeripheralType.Mouse: c.Toggle(996, y - 17, PeripheralsProvider.IsAuraSync, "dev:sync:mouse"); break;
                case PeripheralType.Keyboard: c.Toggle(996, y - 17, PeripheralsProvider.IsKeyboardAuraSync, "dev:sync:kb"); break;
                default: c.Toggle(996, y - 17, PeripheralsProvider.IsHeadsetAuraSync, "dev:sync:hs"); break;
            }
            return y + 30;
        }

        // ---- mouse -----------------------------------------------------------------------------------
        private static readonly string[] PollingNames = { "125 Hz", "250 Hz", "500 Hz", "1000 Hz", "2000 Hz", "4000 Hz", "8000 Hz", "16000 Hz" };
        private static readonly (PowerOffSetting v, string en, string ru)[] PowerOff =
        {
            (PowerOffSetting.Minutes1, "1 min", "1 мин"), (PowerOffSetting.Minutes2, "2 min", "2 мин"), (PowerOffSetting.Minutes3, "3 min", "3 мин"),
            (PowerOffSetting.Minutes5, "5 min", "5 мин"), (PowerOffSetting.Minutes10, "10 min", "10 мин"), (PowerOffSetting.Never, "Never", "Никогда"),
        };

        private float PaintMouse(NebulaCanvas c, AsusMouse m, float y)
        {
            var th = c.Theme;

            // DPI profiles
            var dpis = m.DpiSettings ?? Array.Empty<AsusMouseDPI>();
            int count = Math.Min(dpis.Length, Math.Max(1, m.CurrentDPIProfileCount));
            if (count > 0)
            {
                c.Txt("DPI", 544, y, c.F(12, FontStyle.Bold), th.Text);
                c.Txt(NebulaText.T("Active profile and its sensitivity", "Активный профиль и его чувствительность"), 544, y + 18, c.F(10), th.Faint);
                float bx = 544; float bw = Math.Min(116, (490 - 8 * (count - 1)) / count);
                for (int i = 0; i < count; i++)
                {
                    var dpi = dpis[i];
                    c.Segment(bx, y + 30, bw, 34, dpi.DPI.ToString(), i == m.DpiProfile, m.CanChangeDPIProfile() ? "dev:dpi:" + i : "");
                    bx += bw + 8;
                }
                y += 84;
                if (m.DpiProfile >= 0 && m.DpiProfile < dpis.Length)
                {
                    var cur = dpis[m.DpiProfile];
                    int min = Math.Max(50, m.MinDPI()), max = Math.Max(min + 50, m.MaxDPI());
                    c.Txt(NebulaText.T("Sensitivity", "Чувствительность"), 544, y, c.F(12), th.Muted);
                    c.Txt(cur.DPI + " DPI", 1034, y, c.F(14, FontStyle.Bold), th.Text, StringAlignment.Far);
                    c.Slider(544, y + 18, 490, (cur.DPI - min) / (float)(max - min), "slider:dev:dpi");
                    y += 60;
                }
            }

            // polling
            var rates = m.SupportedPollingrates();
            if (rates.Length > 0)
            {
                c.Txt(NebulaText.T("Polling rate", "Частота опроса"), 544, y, c.F(12, FontStyle.Bold), th.Text);
                float bx = 544; float bw = Math.Min(116, (490 - 8 * (rates.Length - 1)) / rates.Length);
                foreach (var pr in rates)
                {
                    string name = (int)pr < PollingNames.Length ? PollingNames[(int)pr] : pr.ToString();
                    c.Segment(bx, y + 12, bw, 34, name, pr == m.PollingRate, m.CanSetPollingRate() ? "dev:poll:" + (int)pr : "");
                    bx += bw + 8;
                }
                y += 66;
            }

            if (m.HasAngleSnapping())
            {
                c.Txt(NebulaText.T("Angle snapping", "Выравнивание угла"), 544, y, c.F(13), th.Text);
                c.Txt(NebulaText.T("Straightens slow horizontal and vertical strokes.", "Выпрямляет медленные движения по горизонтали и вертикали."), 544, y + 18, c.F(10), th.Faint);
                c.Toggle(996, y - 16, m.AngleSnapping, "dev:snap");
                y += 50;
            }

            if (m.HasRGB())
            {
                var ls = m.LightingSetting is { Length: > 0 } arr ? arr[0] : null;
                c.Txt(NebulaText.T("Lighting", "Подсветка"), 544, y, c.F(12, FontStyle.Bold), th.Text);
                c.ValueRow(544, y + 34, 490, NebulaText.T("Effect", "Эффект"), ls is null ? "—" : ls.LightingMode.ToString(), "dev:mlight:mode", ls is not null);
                if (ls is not null)
                {
                    c.Txt(NebulaText.T("Brightness", "Яркость"), 544, y + 74, c.F(12), th.Muted);
                    c.Txt(ls.Brightness + " %", 1034, y + 74, c.F(14, FontStyle.Bold), th.Text, StringAlignment.Far);
                    c.Slider(544, y + 92, 490, ls.Brightness / 100f, "slider:dev:mbright");
                    c.Card(c.R(544, y + 112, 26, 26), 13, ls.RGBColor, th.Line);
                    c.Txt(NebulaText.T("Colour", "Цвет"), 580, y + 130, c.F(12), th.Muted);
                    c.Hit(c.R(544, y + 108, 120, 34), "dev:mlight:color");
                    y += 150;
                }
                else y += 60;
            }

            if (m.HasAutoPowerOff() || m.HasLowBatteryWarning())
            {
                c.Txt(NebulaText.T("Power", "Питание"), 544, y, c.F(12, FontStyle.Bold), th.Text);
                if (m.HasAutoPowerOff())
                {
                    var cur = PowerOff.FirstOrDefault(p => p.v == m.PowerOffSetting);
                    c.ValueRow(544, y + 34, 490, NebulaText.T("Sleep after", "Засыпать через"), cur.en is null ? "—" : NebulaText.T(cur.en, cur.ru), "dev:mpower");
                    y += 40;
                }
                if (m.HasLowBatteryWarning())
                {
                    c.Txt(NebulaText.T("Low battery warning", "Предупреждение о низком заряде"), 544, y + 34, c.F(12), th.Muted);
                    c.Txt(m.LowBatteryWarning + " %", 1034, y + 34, c.F(14, FontStyle.Bold), th.Text, StringAlignment.Far);
                    int step = Math.Max(1, m.LowBatteryWarningStep()), max = Math.Max(step, m.LowBatteryWarningMax());
                    c.Slider(544, y + 52, 490, m.LowBatteryWarning / (float)max, "slider:dev:mlowbat");
                    y += 60;
                }
                y += 20;
            }
            return y;
        }

        // ---- headset ---------------------------------------------------------------------------------
        private float PaintHeadset(NebulaCanvas c, AsusHeadset h, float y)
        {
            var th = c.Theme;
            if (h.HasRGB())
            {
                c.Txt(NebulaText.T("Lighting", "Подсветка"), 544, y, c.F(12, FontStyle.Bold), th.Text);
                c.ValueRow(544, y + 34, 490, NebulaText.T("Effect", "Эффект"), h.LightingMode.ToString(), "dev:hlight:mode");
                y += 44;
                if (h.HasBrightness())
                {
                    c.Txt(NebulaText.T("Brightness", "Яркость"), 544, y + 24, c.F(12), th.Muted);
                    c.Txt(h.Brightness + " %", 1034, y + 24, c.F(14, FontStyle.Bold), th.Text, StringAlignment.Far);
                    c.Slider(544, y + 42, 490, h.Brightness / 100f, "slider:dev:hbright");
                    y += 60;
                }
                c.Card(c.R(544, y + 12, 26, 26), 13, h.LightingColor, th.Line);
                c.Txt(NebulaText.T("Colour", "Цвет"), 580, y + 30, c.F(12), th.Muted);
                c.Hit(c.R(544, y + 8, 120, 34), "dev:hlight:color");
                y += 60;
            }

            if (h.HasSidetone())
            {
                c.Txt(NebulaText.T("Sidetone", "Сайдтон"), 544, y, c.F(13), th.Text);
                c.Txt(NebulaText.T("Hear your own voice in the headset.", "Слышать свой голос в наушниках."), 544, y + 18, c.F(10), th.Faint);
                c.Toggle(996, y - 16, h.SidetoneEnabled, "dev:sidetone");
                if (h.SidetoneEnabled)
                {
                    int max = Math.Max(1, h.MaxSidetone());
                    c.Slider(544, y + 40, 490, h.Sidetone / (float)max, "slider:dev:sidetone");
                    y += 30;
                }
                y += 50;
            }

            if (h.HasNoiseReduction())
            {
                c.Txt(NebulaText.T("Microphone noise reduction", "Шумоподавление микрофона"), 544, y, c.F(13), th.Text);
                c.Toggle(996, y - 16, h.NoiseReductionEnabled, "dev:nr");
                if (h.NoiseReductionEnabled && h.HasNoiseReductionLevel())
                {
                    string[] lv = { Properties.Strings.Low, Properties.Strings.Medium, Properties.Strings.High };
                    float bx = 544;
                    for (int i = 0; i < 3; i++) { c.Segment(bx, y + 14, 158, 32, lv[i], h.NoiseReduction == i, "dev:nr:" + i); bx += 166; }
                    y += 46;
                }
                y += 44;
            }

            if (h.HasAnc())
            {
                c.Txt("ANC", 544, y, c.F(13), th.Text);
                string[] modes = { Properties.Strings.Off, Properties.Strings.On, Properties.Strings.HeadsetAmbient };
                float bx = 544;
                for (int i = 0; i < 3; i++) { c.Segment(bx, y + 14, 158, 32, modes[i], h.AncMode == i, "dev:anc:" + i); bx += 166; }
                y += 76;
            }

            if (h.HasEqualizer())
            {
                c.Txt(NebulaText.T("Equalizer", "Эквалайзер"), 544, y, c.F(13), th.Text);
                c.Txt(NebulaText.T("Bands are tuned in the device window.", "Полосы настраиваются в окне устройства."), 544, y + 18, c.F(10), th.Faint);
                c.Toggle(996, y - 16, h.EqualizerEnabled, "dev:eq");
                y += 50;
            }

            if (h.HasDirac())
            {
                c.Txt("Dirac", 544, y, c.F(13), th.Text);
                c.Toggle(996, y - 16, h.Dirac, "dev:dirac");
                y += 44;
            }

            if (h.HasVoicePrompt())
            {
                string[] vp = { "English", "Chinese", Properties.Strings.HeadsetPromptSound };
                c.ValueRow(544, y, 490, NebulaText.T("Voice prompts", "Голосовые подсказки"), h.VoicePrompt >= 0 && h.VoicePrompt < vp.Length ? vp[h.VoicePrompt] : "—", "dev:voice");
                y += 44;
            }

            if (h.HasPowerSettings())
            {
                int[] timers = { 2, 3, 5, 10, 15, 0 };
                string cur = h.SleepTimer == 0 ? NebulaText.T("Never", "Никогда") : h.SleepTimer + " " + NebulaText.T("min", "мин");
                c.ValueRow(544, y, 490, NebulaText.T("Sleep after", "Засыпать через"), timers.Contains(h.SleepTimer) ? cur : "—", "dev:hpower");
                y += 44;
            }
            return y;
        }

        // ---- keyboard ---------------------------------------------------------------------------------
        private float PaintKeyboard(NebulaCanvas c, AsusKeyboard k, float y)
        {
            var th = c.Theme;
            if (k.HasProfiles())
            {
                c.Txt(NebulaText.T("Profile", "Профиль"), 544, y, c.F(12, FontStyle.Bold), th.Text);
                float bx = 544;
                for (int i = 0; i < 4; i++) { c.Segment(bx, y + 12, 116, 34, (i + 1).ToString(), false, "dev:kprofile:" + i); bx += 124; }
                y += 66;
            }
            if (k.HasAutoPowerOff())
            {
                var cur = PowerOff.FirstOrDefault(p => p.v == k.PowerOffSetting);
                c.ValueRow(544, y, 490, NebulaText.T("Sleep after", "Засыпать через"), cur.en is null ? "—" : NebulaText.T(cur.en, cur.ru), "dev:kpower");
                y += 44;
            }
            if (k.HasLowBatteryWarning())
            {
                c.Txt(NebulaText.T("Low battery warning", "Предупреждение о низком заряде"), 544, y, c.F(12), th.Muted);
                c.Txt(k.LowBatteryWarning + " %", 1034, y, c.F(14, FontStyle.Bold), th.Text, StringAlignment.Far);
                int max = Math.Max(1, k.LowBatteryWarningMax());
                c.Slider(544, y + 18, 490, k.LowBatteryWarning / (float)max, "slider:dev:klowbat");
                y += 60;
            }
            c.Txt(k.HasRGB() ? NebulaText.T("Lighting effects, per-key colours and key bindings are in the device window.", "Эффекты подсветки, цвета клавиш и привязки — в окне устройства.")
                              : NebulaText.T("Key bindings are in the device window.", "Привязки клавиш — в окне устройства."), 544, y + 12, c.F(11), th.Muted);
            return y + 30;
        }

        // ---- helpers ---------------------------------------------------------------------------------
        private static string SafeName(IPeripheral d) { try { return d.GetDisplayName(); } catch { return "ASUS device"; } }
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

        private void Run(Action a)
        {
            status = "";
            Task.Run(() =>
            {
                try { a(); }
                catch (Exception ex) { status = ex.Message; Logger.WriteLine("Device: " + ex.Message); }
                try { Program.nebulaForm?.BeginInvoke(Program.nebulaForm.Invalidate); } catch { }
            });
        }

        private static void PickColor(NebulaForm form, Color initial, Action<Color> apply)
        {
            var dlg = new RColorPicker(initial);
            dlg.ColorChanged += c => { apply(c); form.Invalidate(); };
            dlg.ShowDialog(form);
        }

        // ---- actions ---------------------------------------------------------------------------------
        public override void Drag(string id, float t, bool done)
        {
            if (selected >= devices.Count) return;
            if (devices[selected] is AsusMouse m)
            {
                switch (id)
                {
                    case "slider:dev:dpi":
                    {
                        if (m.DpiSettings is null || m.DpiProfile < 0 || m.DpiProfile >= m.DpiSettings.Length) return;
                        int min = Math.Max(50, m.MinDPI()), max = Math.Max(min + 50, m.MaxDPI()), step = Math.Max(1, m.DPIIncrements());
                        uint v = (uint)Math.Clamp((int)Math.Round((min + t * (max - min)) / step) * step, min, max);
                        var cur = m.DpiSettings[m.DpiProfile];
                        cur.DPI = v;
                        if (done) { int p = m.DpiProfile; Run(() => m.SetDPIForProfile(cur, p)); }
                        return;
                    }
                    case "slider:dev:mbright":
                    {
                        if (m.LightingSetting is not { Length: > 0 }) return;
                        var ls = m.LightingSetting[0];
                        ls.Brightness = (int)Math.Round(t * 100);
                        if (done) Run(() => m.SetLightingSetting(ls, m.SupportedLightingZones().Length > 1 ? LightingZone.All : m.SupportedLightingZones()[0]));
                        return;
                    }
                    case "slider:dev:mlowbat":
                    {
                        int step = Math.Max(1, m.LowBatteryWarningStep()), max = Math.Max(step, m.LowBatteryWarningMax());
                        int v = Math.Clamp((int)Math.Round(t * max / step) * step, 0, max);
                        if (done) Run(() => m.SetEnergySettings(v, m.PowerOffSetting));
                        return;
                    }
                }
            }
            if (devices[selected] is AsusHeadset h)
            {
                switch (id)
                {
                    case "slider:dev:hbright":
                        if (done) Run(() => h.SetLighting(h.LightingMode, (int)Math.Round(t * 100), h.LightingColor));
                        return;
                    case "slider:dev:sidetone":
                        if (done) Run(() => h.SetSidetone(true, (int)Math.Round(t * Math.Max(1, h.MaxSidetone()))));
                        return;
                }
            }
            if (devices[selected] is AsusKeyboard k && id == "slider:dev:klowbat")
            {
                int step = Math.Max(1, k.LowBatteryWarningStep()), max = Math.Max(step, k.LowBatteryWarningMax());
                int v = Math.Clamp((int)Math.Round(t * max / step) * step, 0, max);
                if (done) Run(() => k.SetEnergySettings(v, k.PowerOffSetting));
            }
        }

        public override bool Click(string id, Point at, NebulaForm form)
        {
            if (id.StartsWith("dev:") && int.TryParse(id[4..], out int idx)) { selected = idx; status = ""; return true; }

            switch (id)
            {
                case "dev:open": if (selected < devices.Count) Program.settingsForm.OpenPeripheral(devices[selected]); return true;
                case "dev:light": form.ShowPage("light"); return true;
                case "dev:keys": form.ShowPage("keyboard"); return true;
                case "dev:sync:mouse": PeripheralsProvider.SetAuraSync(!PeripheralsProvider.IsAuraSync); return true;
                case "dev:sync:kb": PeripheralsProvider.SetKeyboardAuraSync(!PeripheralsProvider.IsKeyboardAuraSync); return true;
                case "dev:sync:hs": PeripheralsProvider.SetHeadsetAuraSync(!PeripheralsProvider.IsHeadsetAuraSync); return true;
            }
            if (selected >= devices.Count) return false;

            if (devices[selected] is AsusMouse m)
            {
                if (id.StartsWith("dev:dpi:")) { int p = int.Parse(id[8..]); Run(() => m.SetDPIProfile(p)); return true; }
                if (id.StartsWith("dev:poll:")) { var pr = (PollingRate)int.Parse(id[9..]); Run(() => m.SetPollingRate(pr)); return true; }
                switch (id)
                {
                    case "dev:snap": Run(() => m.SetAngleSnapping(!m.AngleSnapping)); return true;
                    case "dev:mlight:mode":
                    {
                        if (m.LightingSetting is not { Length: > 0 }) return true;
                        var ls = m.LightingSetting[0];
                        Menu(form, at, Enum.GetValues<LightingMode>().Select(mode => (mode.ToString(), mode == ls.LightingMode, (Action)(() =>
                        {
                            ls.LightingMode = mode;
                            Run(() => m.SetLightingSetting(ls, m.SupportedLightingZones().Length > 1 ? LightingZone.All : m.SupportedLightingZones()[0]));
                        }))));
                        return true;
                    }
                    case "dev:mlight:color":
                    {
                        if (m.LightingSetting is not { Length: > 0 }) return true;
                        var ls = m.LightingSetting[0];
                        PickColor(form, ls.RGBColor, col =>
                        {
                            ls.RGBColor = col;
                            Run(() => m.SetLightingSetting(ls, m.SupportedLightingZones().Length > 1 ? LightingZone.All : m.SupportedLightingZones()[0]));
                        });
                        return true;
                    }
                    case "dev:mpower":
                        Menu(form, at, PowerOff.Select(p => (NebulaText.T(p.en, p.ru), p.v == m.PowerOffSetting, (Action)(() => Run(() => m.SetEnergySettings(m.LowBatteryWarning, p.v))))));
                        return true;
                }
            }

            if (devices[selected] is AsusHeadset h)
            {
                if (id.StartsWith("dev:nr:")) { int lv = int.Parse(id[7..]); Run(() => h.SetNoiseReduction(true, lv)); return true; }
                if (id.StartsWith("dev:anc:")) { int mode = int.Parse(id[8..]); Run(() => h.SetAnc(mode)); return true; }
                switch (id)
                {
                    case "dev:hlight:mode":
                        Menu(form, at, Enum.GetValues<HeadsetLightingMode>().Where(x => x != HeadsetLightingMode.MqaIndicator)
                            .Select(mode => (mode.ToString(), mode == h.LightingMode, (Action)(() => Run(() => h.SetLighting(mode, h.Brightness, h.LightingColor))))));
                        return true;
                    case "dev:hlight:color":
                        PickColor(form, h.LightingColor, col => Run(() => h.SetLighting(h.LightingMode, h.Brightness, col)));
                        return true;
                    case "dev:sidetone": Run(() => h.SetSidetone(!h.SidetoneEnabled, h.Sidetone)); return true;
                    case "dev:nr": Run(() => h.SetNoiseReduction(!h.NoiseReductionEnabled, Math.Max(0, h.NoiseReduction))); return true;
                    case "dev:eq": Run(() => h.SetEqualizer(!h.EqualizerEnabled)); return true;
                    case "dev:dirac": Run(() => h.SetDirac(!h.Dirac)); return true;
                    case "dev:voice":
                    {
                        string[] vp = { "English", "Chinese", Properties.Strings.HeadsetPromptSound };
                        Menu(form, at, vp.Select((n, i) => (n, i == h.VoicePrompt, (Action)(() => Run(() => h.SetVoicePrompt(i))))));
                        return true;
                    }
                    case "dev:hpower":
                    {
                        int[] timers = { 2, 3, 5, 10, 15, 0 };
                        Menu(form, at, timers.Select(t => (t == 0 ? NebulaText.T("Never", "Никогда") : t + " " + NebulaText.T("min", "мин"), t == h.SleepTimer,
                            (Action)(() => Run(() => h.SetEnergySettings(h.LowBatteryWarning, t))))));
                        return true;
                    }
                }
            }

            if (devices[selected] is AsusKeyboard k)
            {
                if (id.StartsWith("dev:kprofile:")) { int p = int.Parse(id[13..]); Run(() => k.SetProfile(p)); return true; }
                if (id == "dev:kpower")
                {
                    Menu(form, at, PowerOff.Select(p => (NebulaText.T(p.en, p.ru), p.v == k.PowerOffSetting, (Action)(() => Run(() => k.SetEnergySettings(k.LowBatteryWarning, p.v))))));
                    return true;
                }
            }
            return false;
        }
    }
}
