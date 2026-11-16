namespace GHelper.UI.SignalGlass
{
    public sealed record SignalNavTarget(string PageId, string GroupId, string Icon, string TitleEn, string TitleRu);

    public sealed record SignalSearchItem(string PageId, string ControlId, string TitleEn, string TitleRu, string SubtitleEn, string SubtitleRu, string Keywords);

    public sealed record SignalToastState
    {
        public string Title { get; init; } = "";
        public string Message { get; init; } = "";
        public bool IsError { get; init; }
        public string ActionLabel { get; init; } = "";
        public Action? OnAction { get; init; }
        public long ExpiresAtTick { get; set; }
        public long RemainingMs { get; set; }
    }

    public sealed class SignalConfirmState
    {
        public string Title { get; init; } = "";
        public string Message { get; init; } = "";
        public string ConfirmLabel { get; init; } = "";
        public string CancelLabel { get; init; } = "";
        public bool IsWarning { get; init; } = true;
        public Action OnConfirm { get; init; } = () => { };
        public string ReturnFocusId { get; set; } = "";
    }

    public sealed class SignalPaintContext
    {
        public required Graphics G { get; init; }
        public required SignalTheme Theme { get; init; }
        public required GlassPainter Painter { get; init; }
        public required SignalShellLayout Layout { get; init; }
        public required bool Dark { get; init; }
        public required bool Opaque { get; init; }
        public required bool Rtl { get; init; }
        public required SensorSnapshot Snapshot { get; init; }
        public required CapabilitySet Caps { get; init; }
        public required ISignalBackend Backend { get; init; }
        public required CommandCoordinator Commands { get; init; }
        public required RingHistoryBuffer CpuHistory { get; init; }
        public required RingHistoryBuffer GpuHistory { get; init; }
        public required string HoverId { get; init; }
        public required string PressedId { get; init; }
        public required string FocusedId { get; init; }
        public required bool ShowFocusRing { get; init; }
        public required Point? MouseClient { get; init; }
        public required float ScrollDip { get; init; }
        public required List<UiElement> Elements { get; init; }
        public required Action<string> Navigate { get; init; }
        public required Action<string, string, bool> Toast { get; init; }
        public required Action<SignalToastState> ShowToast { get; init; }
        public required Action<SignalConfirmState> Confirm { get; init; }
        public required Action<Point, IEnumerable<(string text, bool isChecked, string? badge, Action action)>> ShowMenu { get; init; }
        public required Action RequestRefresh { get; init; }
        public required Action<string> RequestFocus { get; init; }
        public required Action<(string id, Action<float, float, bool> onDrag, RectangleF trackPx)> StartSliderDrag { get; init; }

        public float Scale => Layout.Scale;
        public float Dip(float px) => Layout.PxToDip(px);
        public RectangleF Px(float xDip, float yDip, float wDip, float hDip) =>
            new(xDip * Scale, yDip * Scale, wDip * Scale, hDip * Scale);
        public RectangleF Px(RectangleF dipRect) => Layout.ToPx(dipRect);

        internal RectangleF? ActiveClipPx { get; set; }

        public bool IsHover(string id) => HoverId == id;
        public bool IsPressed(string id) => PressedId == id;
        public bool IsFocused(string id) => ShowFocusRing && FocusedId == id;

        public void Register(UiElement el)
        {
            if (ActiveClipPx is { } clip)
            {
                var clipped = RectangleF.Intersect(el.BoundsPx, clip);
                if (clipped.Width < 2f || clipped.Height < 2f) return;
                el.BoundsPx = clipped;
            }
            Elements.Add(el);
        }

        public void DrawPanel(RectangleF dipRect, float radiusDip = SignalTheme.RadiusPanel) =>
            GlassPainter.DrawPanel(G, Px(dipRect), Scale, Theme, Opaque, radiusDip);

        public void DrawButton(string id, RectangleF dipRect, string text, Action<Point>? onClick,
            bool selected = false, bool enabled = true, string tooltip = "", AccessibleRole role = AccessibleRole.PushButton,
            float radiusDip = SignalTheme.RadiusControl, float fontSize = 13f)
        {
            var rPx = Px(dipRect);
            bool hv = enabled && IsHover(id);
            bool pr = enabled && IsPressed(id);
            GlassPainter.DrawControlBox(G, rPx, Scale, Theme, Opaque, selected, hv, pr, enabled, radiusDip);

            var f = Painter.Font(fontSize, Scale, FontStyle.Bold);
            Color fg = !enabled ? Theme.Muted : selected ? Theme.OnAccent : Theme.Text;
            var pad = 10f * Scale;
            var textRect = new RectangleF(rPx.X + pad, rPx.Y + (rPx.Height - f.Height) / 2f, Math.Max(4f, rPx.Width - pad * 2f), f.Height + 2f);
            Painter.DrawText(G, text, textRect, f, fg, GlassPainter.FmtCenter);

            if (IsFocused(id)) GlassPainter.DrawFocusRing(G, rPx, Scale, Theme, selected, radiusDip);

            if (onClick is not null || !enabled)
            {
                var st = AccessibleStates.None;
                if (selected) st |= AccessibleStates.Checked;
                if (pr) st |= AccessibleStates.Pressed;
                Register(new UiElement
                {
                    Id = id,
                    BoundsPx = rPx,
                    Name = text,
                    Value = selected ? "Selected" : "",
                    Tooltip = tooltip,
                    Role = role,
                    States = st,
                    Enabled = enabled,
                    Focusable = enabled,
                    Selected = selected,
                    RadiusDip = radiusDip,
                    OnActivate = onClick,
                });
            }
        }

        public void DrawToggle(string id, RectangleF dipRect, string accName, bool isOn, Action onToggle,
            bool enabled = true, string tooltip = "")
        {
            var rPx = Px(dipRect);
            GlassPainter.DrawToggle(G, rPx, Scale, Theme, isOn, enabled && IsHover(id), enabled);
            if (IsFocused(id)) GlassPainter.DrawFocusRing(G, rPx, Scale, Theme, false, dipRect.Height / 2f);

            // Expand hit target to at least 36x32 DIP for comfortable pointer use
            var hitPx = rPx;
            float dx = Math.Max(0f, (38f * Scale - hitPx.Width) / 2f);
            float dy = Math.Max(0f, (32f * Scale - hitPx.Height) / 2f);
            hitPx.Inflate(dx, dy);

            Register(new UiElement
            {
                Id = id,
                BoundsPx = hitPx,
                Name = accName,
                Value = isOn ? SignalText.T("On", "Вкл") : SignalText.T("Off", "Выкл"),
                Tooltip = tooltip,
                Role = AccessibleRole.CheckButton,
                States = isOn ? AccessibleStates.Checked : AccessibleStates.None,
                Enabled = enabled,
                Focusable = enabled,
                RadiusDip = dipRect.Height / 2f,
                OnActivate = _ => onToggle(),
            });
        }

        public RectangleF DrawSlider(string id, RectangleF dipRect, string accName, float t, string accValue,
            Action<float, bool> onDrag, Action<int> onStep, bool enabled = true)
        {
            var hitDip = new RectangleF(dipRect.X - 6f, dipRect.Y - 10f, dipRect.Width + 12f, Math.Max(32f, dipRect.Height + 20f));
            var trackDip = new RectangleF(dipRect.X, dipRect.Y + (dipRect.Height - 6f) / 2f, dipRect.Width, 6f);
            var hitPx = Px(hitDip);
            var trackPx = GlassPainter.DrawSlider(G, Px(trackDip), Scale, Theme, t, enabled && IsHover(id), enabled);
            if (IsFocused(id)) GlassPainter.DrawFocusRing(G, hitPx, Scale, Theme, false, 8f);

            Register(new UiElement
            {
                Id = id,
                BoundsPx = hitPx,
                Name = accName,
                Value = accValue,
                Role = AccessibleRole.Slider,
                Enabled = enabled,
                Focusable = enabled,
                IsSlider = true,
                SliderTrackPx = trackPx,
                RadiusDip = 8f,
                OnStep = onStep,
                OnActivate = pt =>
                {
                    if (!enabled) return;
                    StartSliderDrag((id, (tx, _, done) => onDrag(tx, done), trackPx));
                    float nt = trackPx.Width > 0 ? Math.Clamp((pt.X - trackPx.X) / trackPx.Width, 0f, 1f) : t;
                    onDrag(nt, true);
                },
            });
            return trackPx;
        }

        /// <summary>
        /// Renders one setting row with title + 2-line max subtitle on one side and either
        /// an interactive button/dropdown OR clean read-only text on the opposite side (RTL aware).
        /// </summary>
        public float DrawSettingRow(float dipY, float x, float w, string title, string subtitle, string valueText,
            string id, Action<Point>? onActivate, bool enabled = true, bool readOnly = false, bool showChevron = false,
            bool drawDivider = true, string tooltip = "")
        {
            const float rowH = 74f;
            float ctrlW = Math.Min(238f, Math.Max(136f, w * 0.28f));
            float ctrlH = 38f;
            float textW = w - ctrlW - 24f;

            var titleRectDip = Rtl
                ? new RectangleF(x + ctrlW + 24f, dipY + 6f, textW, 24f)
                : new RectangleF(x, dipY + 6f, textW, 24f);
            var subRectDip = Rtl
                ? new RectangleF(x + ctrlW + 24f, dipY + 32f, textW, 34f)
                : new RectangleF(x, dipY + 32f, textW, 34f);
            var ctrlDip = Rtl
                ? new RectangleF(x, dipY + 12f, ctrlW, ctrlH)
                : new RectangleF(x + w - ctrlW, dipY + 12f, ctrlW, ctrlH);

            var fmtText = Rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft;
            var fmtWrap = new StringFormat(GlassPainter.FmtWrap) { Alignment = Rtl ? StringAlignment.Far : StringAlignment.Near };
            using (fmtWrap)
            {
                Painter.DrawText(G, title, Px(titleRectDip), Painter.Font(15f, Scale, FontStyle.Bold), enabled ? Theme.Text : Theme.Muted, fmtText);
                if (!string.IsNullOrEmpty(subtitle))
                    Painter.DrawText(G, subtitle, Px(subRectDip), Painter.Font(12f, Scale), Theme.Muted, fmtWrap);
            }

            if (readOnly)
            {
                var roFmt = Rtl ? GlassPainter.FmtLeft : GlassPainter.FmtRight;
                var roFont = Painter.Font(14f, Scale, FontStyle.Bold);
                var roRect = Px(new RectangleF(ctrlDip.X, ctrlDip.Y + 8f, ctrlDip.Width, 24f));
                Painter.DrawText(G, valueText, roRect, roFont, enabled ? Theme.Text : Theme.Muted, roFmt);

                Register(new UiElement
                {
                    Id = id,
                    BoundsPx = Px(new RectangleF(x, dipY, w, rowH)),
                    Name = title,
                    Value = valueText,
                    Tooltip = subtitle,
                    Role = AccessibleRole.StaticText,
                    States = AccessibleStates.ReadOnly,
                    Focusable = false,
                    Enabled = true,
                });
            }
            else
            {
                string label = showChevron && enabled ? $"{valueText}  ⌄" : valueText;
                DrawButton(id, ctrlDip, label, onActivate, selected: false, enabled: enabled, tooltip: tooltip.Length > 0 ? tooltip : title,
                    role: showChevron ? AccessibleRole.ComboBox : AccessibleRole.PushButton);
            }

            if (drawDivider)
            {
                using var pen = new Pen(Color.FromArgb(80, Theme.Line), Math.Max(1f, Scale));
                float ly = (dipY + rowH) * Scale;
                G.DrawLine(pen, x * Scale, ly, (x + w) * Scale, ly);
            }

            return dipY + rowH;
        }

        public float DrawToggleRow(float dipY, float x, float w, string title, string subtitle, string id,
            bool isOn, Action onToggle, bool enabled = true, bool drawDivider = true, string tooltip = "")
        {
            const float rowH = 74f;
            const float togW = 42f;
            const float togH = 24f;
            float textW = w - togW - 32f;

            var titleRectDip = Rtl
                ? new RectangleF(x + togW + 32f, dipY + 6f, textW, 24f)
                : new RectangleF(x, dipY + 6f, textW, 24f);
            var subRectDip = Rtl
                ? new RectangleF(x + togW + 32f, dipY + 32f, textW, 34f)
                : new RectangleF(x, dipY + 32f, textW, 34f);
            var togDip = Rtl
                ? new RectangleF(x, dipY + 18f, togW, togH)
                : new RectangleF(x + w - togW, dipY + 18f, togW, togH);

            var fmtText = Rtl ? GlassPainter.FmtRight : GlassPainter.FmtLeft;
            var fmtWrap = new StringFormat(GlassPainter.FmtWrap) { Alignment = Rtl ? StringAlignment.Far : StringAlignment.Near };
            using (fmtWrap)
            {
                Painter.DrawText(G, title, Px(titleRectDip), Painter.Font(15f, Scale, FontStyle.Bold), enabled ? Theme.Text : Theme.Muted, fmtText);
                if (!string.IsNullOrEmpty(subtitle))
                    Painter.DrawText(G, subtitle, Px(subRectDip), Painter.Font(12f, Scale), Theme.Muted, fmtWrap);
            }

            DrawToggle(id, togDip, title, isOn, onToggle, enabled, tooltip);

            if (drawDivider)
            {
                using var pen = new Pen(Color.FromArgb(80, Theme.Line), Math.Max(1f, Scale));
                float ly = (dipY + rowH) * Scale;
                G.DrawLine(pen, x * Scale, ly, (x + w) * Scale, ly);
            }

            return dipY + rowH;
        }
    }

    public abstract class SignalPage
    {
        public abstract string Id { get; }
        public abstract string GroupId { get; } // "overview", "tuning", "devices", "system"
        public abstract string Title { get; }
        public virtual string Subtitle => SignalText.T("Settings apply only to supported hardware", "Настройки применяются только к поддерживаемому оборудованию");
        public virtual bool HasFooter => true;
        public virtual bool CanApply => false;
        public virtual bool CanReset => false;
        public virtual float ContentHeightDip(SignalPaintContext ctx) => ctx.Layout.WorkspaceBody.Height;

        public virtual void OnEnter(SignalPaintContext? ctx) { }
        public virtual void Apply() { }
        public virtual void Reset() { }
        public virtual bool HandleEscape() => false;
        public abstract void Paint(SignalPaintContext ctx);
    }

    public static class SignalRegistry
    {
        public static readonly SignalNavTarget[] InspectorTargets =
        {
            new("cooling",    "tuning",  "fan",       "Cooling",            "Охлаждение"),
            new("graphics",   "tuning",  "gpu",       "Graphics",           "Графика"),
            new("display",    "tuning",  "display",   "Display",            "Экран"),
            new("battery",    "tuning",  "battery",   "Battery",            "Батарея"),
            new("power",      "tuning",  "power",     "Power",              "Питание"),
            new("lighting",   "tuning",  "light",     "Lighting",           "Подсветка"),
            new("keys",       "tuning",  "keyboard",  "Keys",               "Клавиши"),
            new("devices",    "devices", "mouse",     "Devices",            "Устройства"),
            new("wallpapers", "devices", "wallpaper", "Wallpapers",         "Обои"),
            new("updates",    "system",  "updates",   "Updates",            "Обновления"),
            new("settings",   "system",  "settings",  "Settings",           "Настройки"),
        };

        public static readonly SignalSearchItem[] SearchIndex =
        {
            new("overview",   "",                 "Overview",               "Обзор",                  "CPU & GPU 60s monitoring, fans, memory, battery", "Мониторинг CPU/GPU за 60с, вентиляторы, память, батарея", "cpu gpu temp rpm ram overview"),
            new("cooling",    "cooling:apply",    "Cooling & modes",        "Охлаждение и режимы",    "Fan curves, SPL/sPPT/fPPT, boost, undervolt",     "Кривые вентиляторов, лимиты мощности, буст, андервольт", "fan curve cooling spl sppt fppt undervolt hysteresis boost"),
            new("graphics",   "graphics:apply",   "Graphics",               "Графика",                "GPU mode, clocks, Dynamic Boost, TGP, dGPU apps", "Режим GPU, частоты, Dynamic Boost, TGP, процессы dGPU", "gpu eco standard ultimate optimized mux vram processes dgpu"),
            new("display",    "display:rate",     "Display",                "Экран",                  "Refresh rate, overdrive, Mini-LED, GameVisual, OLED","Частота, overdrive, Mini-LED, GameVisual, OLED", "hz refresh overdrive miniled elmb hdr gamevisual gamut oled pixel shift"),
            new("battery",    "battery:apply",    "Battery",                "Батарея",                "Charge limit, one-time 100%, health, cycles",     "Лимит заряда, разовый 100%, здоровье, циклы", "battery charge limit health capacity cycles wear"),
            new("power",      "power:rules",      "Power",                  "Питание",                "Screen off, sleep, hibernate AC/DC, power rules", "Таймауты экрана/сна AC/DC, правила смены питания", "power sleep hibernate timeout rules ac dc unplugged"),
            new("lighting",   "lighting:effect",  "Lighting",               "Подсветка",              "Aura effect, color, brightness, sleep, Matrix",   "Эффект Aura, цвет, яркость, сон, Matrix/Slash", "aura lighting rgb color brightness matrix slash"),
            new("keys",       "keys:fnlock",      "Keys & shortcuts",       "Клавиши и сочетания",    "M1-M4 bindings, Fn+F5, Fn lock",                  "Привязки M1-M4, Fn+F5, Fn Lock", "keys hotkeys m1 m2 m3 m4 fn lock shortcut"),
            new("devices",    "devices:refresh",  "Connected devices",      "Подключённые устройства","Mice, keyboards, headsets, DPI, ANC, polling",   "Мыши, клавиатуры, гарнитуры, DPI, ANC, опрос", "mouse keyboard headset dpi polling anc sidetone aura sync"),
            new("wallpapers", "wallpapers:apply", "Wallpapers",             "Обои",                   "Signal Glass art, Aura tint, custom image",       "Встроенные обои, Aura-цвет, своя картинка", "wallpaper background aura gallery image"),
            new("updates",    "updates:check",    "Updates",                "Обновления",             "Crateless releases, ASUS BIOS and drivers",       "Релизы Crateless, BIOS и драйверы ASUS", "updates bios driver release version"),
            new("settings",   "settings:shell",   "Settings",               "Настройки",              "Interface, language, theme, glass, startup",      "Интерфейс, язык, тема, стекло, автозапуск", "settings theme language interface signal nebula classic glass motion startup"),
            new("settings",   "settings:shell",   "Interface switcher",     "Переключение интерфейса","Switch between Signal Glass, Nebula and Classic", "Переключение между Signal Glass, Nebula и Classic", "interface shell theme signal nebula classic"),
        };
    }
}
