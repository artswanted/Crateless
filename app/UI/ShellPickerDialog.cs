using GHelper.UI.SignalGlass;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace GHelper.UI
{
    /// <summary>
    /// First-launch interface selector shown when no "theme" flag has been configured yet.
    /// Allows choosing between Signal Glass, Nebula, and Classic G-Helper.
    /// </summary>
    public sealed class ShellPickerDialog : RForm
    {
        private readonly SignalTheme sigTheme;
        private readonly GlassPainter painter = new();
        private readonly List<UiElement> uiElements = new();

        private UiShellMode selected = UiShellMode.SignalGlass;
        private string hoverId = "";
        private string focusedId = "picker:card:signal";
        private bool showFocusRing = true;
        private float scale = 1f;

        private sealed record Option(UiShellMode Mode, string Id, string Title, string Badge, string Description, Color SwatchBg, Color SwatchAccent);

        private readonly Option[] options;

        public UiShellMode SelectedMode => selected;

        public ShellPickerDialog()
        {
            InitTheme(true);
            sigTheme = SignalTheme.ForMode(darkTheme);

            Text = SignalText.T("Choose interface", "Выберите интерфейс");
            try { Icon = SignalAssets.CloneAppIcon() ?? System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { Icon = Properties.Resources.standard; }

            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            DoubleBuffered = true;

            scale = Math.Max(1f, DeviceDpi / 96f);
            ClientSize = new Size((int)Math.Round(780f * scale), (int)Math.Round(468f * scale));

            options = new[]
            {
                new Option(
                    UiShellMode.SignalGlass,
                    "picker:card:signal",
                    "Signal Glass",
                    SignalText.T("Recommended", "Рекомендуется"),
                    SignalText.T("60s CPU/GPU plots, quick inspector, Glass Lite & RTL.", "Графики CPU/GPU за 60с, панель управления, Glass Lite."),
                    SignalTheme.Dark.Surface,
                    SignalTheme.Dark.Accent),
                new Option(
                    UiShellMode.Nebula,
                    "picker:card:nebula",
                    "Nebula",
                    "",
                    SignalText.T("Left navigation rail with device illustration.", "Левое меню с рендером ноутбука и карточками."),
                    NebulaTheme.Dark.Card,
                    NebulaTheme.Dark.Accent),
                new Option(
                    UiShellMode.Classic,
                    "picker:card:classic",
                    SignalText.T("Classic (G-Helper)", "Классический (G-Helper)"),
                    "",
                    SignalText.T("Compact original G-Helper window.", "Компактное классическое окно G-Helper."),
                    Color.FromArgb(40, 40, 40),
                    Color.FromArgb(58, 174, 239)),
            };
        }

        protected override AccessibleObject CreateAccessibilityInstance() =>
            new SignalAccessibleObject(
                this,
                () => uiElements,
                () => focusedId,
                id => { focusedId = id; showFocusRing = true; Invalidate(); });

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;

            uiElements.Clear();
            bool opaque = SignalTheme.EffectiveOpaque;
            painter.DrawBackdrop(g, ClientSize, scale, sigTheme, darkTheme, opaque);

            float pad = 28f;
            float w = 780f - pad * 2f;

            var logo = SignalAssets.Logo((int)Math.Round(24f * scale));
            if (logo is not null) g.DrawImage(logo, pad * scale, 26f * scale, 24f * scale, 24f * scale);

            painter.DrawText(g, SignalText.T("Choose interface", "Выберите интерфейс"),
                Px(pad + 34f, 22f, w - 34f, 34f),
                painter.Font(22f, scale, FontStyle.Bold, true), sigTheme.Text, GlassPainter.FmtLeft);

            painter.DrawText(g, SignalText.T("You can switch this anytime in Settings or the tray menu.", "Вы сможете переключить интерфейс в любое время в настройках или меню трея."),
                Px(pad, 62f, w, 24f),
                painter.Font(13f, scale), sigTheme.Muted, GlassPainter.FmtLeft);

            float cardGap = 16f;
            float cardW = (w - cardGap * 2f) / 3f;
            float cardH = 264f;
            float cy = 104f;

            for (int i = 0; i < options.Length; i++)
            {
                var opt = options[i];
                bool isSel = selected == opt.Mode;
                float cx = pad + i * (cardW + cardGap);
                var cardDip = new RectangleF(cx, cy, cardW, cardH);
                var cardPx = Px(cardDip);

                GlassPainter.DrawPanel(g, cardPx, scale, sigTheme, opaque, 16f);

                if (isSel)
                {
                    using var selPen = new Pen(sigTheme.Accent, Math.Max(2f, 2f * scale));
                    using var p = GlassPainter.Rounded(cardPx, 16f * scale);
                    g.DrawPath(selPen, p);
                }

                // Mini preview bar inside card
                var prevDip = new RectangleF(cx + 14f, cy + 14f, cardW - 28f, 88f);
                var prevPx = Px(prevDip);
                using (var pp = GlassPainter.Rounded(prevPx, 10f * scale))
                using (var pb = new SolidBrush(opt.SwatchBg))
                    g.FillPath(pb, pp);

                using (var ab = new SolidBrush(opt.SwatchAccent))
                using (var ap = GlassPainter.Rounded(Px(prevDip.X + 12f, prevDip.Y + 14f, 64f, 16f), 6f * scale))
                    g.FillPath(ab, ap);

                using (var lineB = new SolidBrush(Color.FromArgb(110, 255, 255, 255)))
                {
                    g.FillRectangle(lineB, (prevDip.X + 12f) * scale, (prevDip.Y + 44f) * scale, (prevDip.Width - 24f) * scale, 8f * scale);
                    g.FillRectangle(lineB, (prevDip.X + 12f) * scale, (prevDip.Y + 60f) * scale, (prevDip.Width * 0.6f) * scale, 8f * scale);
                }

                painter.DrawText(g, opt.Title, Px(cx + 14f, cy + 116f, cardW - 28f, 26f),
                    painter.Font(14.5f, scale, FontStyle.Bold), isSel ? sigTheme.Accent : sigTheme.Text, GlassPainter.FmtLeft);

                float descY = cy + 146f;
                if (!string.IsNullOrEmpty(opt.Badge))
                {
                    painter.DrawText(g, opt.Badge, Px(cx + 14f, descY, cardW - 28f, 20f),
                        painter.Font(11.5f, scale, FontStyle.Bold), sigTheme.Accent, GlassPainter.FmtLeft);
                    descY += 24f;
                }

                painter.DrawText(g, opt.Description, Px(cx + 14f, descY, cardW - 28f, cardDip.Bottom - descY - 12f),
                    painter.Font(12f, scale), sigTheme.Muted, GlassPainter.FmtWrap);

                if (showFocusRing && focusedId == opt.Id)
                    GlassPainter.DrawFocusRing(g, cardPx, scale, sigTheme, false, 16f);

                uiElements.Add(new UiElement
                {
                    Id = opt.Id,
                    BoundsPx = cardPx,
                    Name = opt.Title,
                    Value = opt.Description,
                    Role = AccessibleRole.RadioButton,
                    States = isSel ? AccessibleStates.Checked : AccessibleStates.None,
                    Selected = isSel,
                    RadiusDip = 16f,
                    OnActivate = _ => { selected = opt.Mode; Invalidate(); },
                });
            }

            // Confirm button
            var okDip = new RectangleF(780f - pad - 160f, 396f, 160f, 40f);
            var okPx = Px(okDip);
            GlassPainter.DrawControlBox(g, okPx, scale, sigTheme, opaque, true, hoverId == "picker:confirm", false, true);
            painter.DrawText(g, SignalText.T("Confirm", "Подтвердить"), okPx,
                painter.Font(14f, scale, FontStyle.Bold), sigTheme.OnAccent, GlassPainter.FmtMiddleCenter);
            if (showFocusRing && focusedId == "picker:confirm")
                GlassPainter.DrawFocusRing(g, okPx, scale, sigTheme, true);

            uiElements.Add(new UiElement
            {
                Id = "picker:confirm",
                BoundsPx = okPx,
                Name = SignalText.T("Confirm", "Подтвердить"),
                Selected = true,
                OnActivate = _ => ConfirmSelection(),
            });
        }

        private void ConfirmSelection()
        {
            UiShell.ApplySelection(selected);
            DialogResult = DialogResult.OK;
            Close();
        }

        private RectangleF Px(float x, float y, float w, float h) => new(x * scale, y * scale, w * scale, h * scale);
        private RectangleF Px(RectangleF r) => new(r.X * scale, r.Y * scale, r.Width * scale, r.Height * scale);

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            string hit = "";
            for (int i = uiElements.Count - 1; i >= 0; i--)
                if (uiElements[i].BoundsPx.Contains(e.Location)) { hit = uiElements[i].Id; break; }
            if (hit != hoverId) { hoverId = hit; Cursor = hit.Length > 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            showFocusRing = false;
            for (int i = uiElements.Count - 1; i >= 0; i--)
            {
                var el = uiElements[i];
                if (el.BoundsPx.Contains(e.Location))
                {
                    focusedId = el.Id;
                    el.OnActivate?.Invoke(e.Location);
                    return;
                }
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            for (int i = uiElements.Count - 1; i >= 0; i--)
            {
                if (uiElements[i].Id.StartsWith("picker:card:") && uiElements[i].BoundsPx.Contains(e.Location))
                {
                    uiElements[i].OnActivate?.Invoke(e.Location);
                    ConfirmSelection();
                    return;
                }
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                UiShell.ApplySelection(selected);
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            if (keyData == Keys.Left || keyData == Keys.Right)
            {
                int idx = Array.FindIndex(options, o => o.Mode == selected);
                idx = keyData == Keys.Right ? (idx + 1) % options.Length : (idx - 1 + options.Length) % options.Length;
                selected = options[idx].Mode;
                focusedId = options[idx].Id;
                showFocusRing = true;
                Invalidate();
                return true;
            }
            if (keyData == Keys.Tab || keyData == (Keys.Shift | Keys.Tab))
            {
                showFocusRing = true;
                int cur = uiElements.FindIndex(e => e.Id == focusedId);
                int step = (keyData & Keys.Shift) == Keys.Shift ? -1 : 1;
                focusedId = uiElements[cur < 0 ? 0 : (cur + step + uiElements.Count) % uiElements.Count].Id;
                Invalidate();
                return true;
            }
            if (keyData == Keys.Enter)
            {
                ConfirmSelection();
                return true;
            }
            if (keyData == Keys.Space && uiElements.FirstOrDefault(e => e.Id == focusedId) is { } el)
            {
                el.OnActivate?.Invoke(Point.Empty);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) painter.Dispose();
            base.Dispose(disposing);
        }
    }
}
