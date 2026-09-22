using System.Drawing.Drawing2D;

namespace GHelper.UI.Nebula
{
    /// <summary>
    /// Drawing helpers shared by the Nebula shell and its pages. Coordinates are design px
    /// on the 1440×1060 grid; <see cref="K"/> converts them to client px. Text y is the baseline.
    /// Nothing here reads hardware; pages paint from already-read state.
    /// </summary>
    public sealed class NebulaCanvas : IDisposable
    {
        public Graphics G = null!;
        public float K = 1f;
        public NebulaTheme Theme = NebulaTheme.Dark;
        public bool Dark = true;
        public string Hover = "";

        /// <summary>Interactive regions collected during a paint pass: (rect, id, tooltip).</summary>
        /// <summary>
        /// Clickable regions, kept in two buckets so a repaint of one layer does not throw away the
        /// other layer's regions: the shell can skip painting the page when only the rail changed.
        /// </summary>
        public readonly List<(RectangleF rect, string id, string tip)> Hits = new();
        public readonly List<(RectangleF rect, string id, string tip)> PageHits = new();
        private bool pageLayer;

        private readonly Dictionary<string, Font> fonts = new();
        private static readonly string TextFamily = PickFamily("Segoe UI Variable Text", "Segoe UI");
        private static readonly string DisplayFamily = PickFamily("Segoe UI Variable Display", "Segoe UI");

        private static readonly StringFormat FmtLeft = new(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip };
        private static readonly StringFormat FmtRight = new(StringFormat.GenericTypographic) { Alignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip };
        private static readonly StringFormat FmtCenter = new(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip };

        private static string PickFamily(string preferred, string fallback)
        {
            try { using var f = new FontFamily(preferred); return preferred; }
            catch { return fallback; }
        }

        public void Begin(Graphics g, float k, NebulaTheme theme, bool dark, string hover)
        {
            G = g; K = k; Theme = theme; Dark = dark; Hover = hover;
            pageLayer = false;
            Hits.Clear();
        }

        // ---- geometry ----------------------------------------------------------------------------
        public float S(float v) => v * K;
        public RectangleF R(float x, float y, float w, float h) => new(S(x), S(y), S(w), S(h));
        public bool IsHover(string id) => Hover == id;

        /// <summary>
        /// True when the rect can actually land on screen with the current clip. A repaint of one
        /// row asks GDI+ to clip everything else away, but laying the text out costs the same
        /// whether or not a pixel is written, so the primitives skip the work themselves.
        /// </summary>
        public bool Vis(RectangleF r) => G.IsVisible(r);

        public bool Vis(float x, float y, float w, float h) => G.IsVisible(R(x, y, w, h));
        public void Hit(RectangleF r, string id, string tooltip = "") => (pageLayer ? PageHits : Hits).Add((r, id, tooltip));

        /// <summary>Starts the page layer; its regions are kept apart from the shell's and cleared only when the page is repainted.</summary>
        public void BeginPageLayer()
        {
            pageLayer = true;
            PageHits.Clear();
        }

        public void EndPageLayer() => pageLayer = false;

        // ---- fonts / text ------------------------------------------------------------------------
        public Font F(float designPx, FontStyle style = FontStyle.Regular, bool display = false)
        {
            float px = Math.Max(8f, designPx * K);
            string key = $"{px:F1}|{(int)style}|{display}";
            if (!fonts.TryGetValue(key, out var f))
            {
                f = new Font(display ? DisplayFamily : TextFamily, px, style, GraphicsUnit.Pixel);
                fonts[key] = f;
            }
            return f;
        }

        public void Txt(string s, float x, float baseline, Font f, Color c, StringAlignment align = StringAlignment.Near)
        {
            if (string.IsNullOrEmpty(s)) return;
            float ascent = f.Size * f.FontFamily.GetCellAscent(f.Style) / f.FontFamily.GetEmHeight(f.Style);
            float top = S(baseline) - ascent;
            var fmt = align == StringAlignment.Far ? FmtRight : align == StringAlignment.Center ? FmtCenter : FmtLeft;
            const float w = 2000f;
            var rect = align switch
            {
                StringAlignment.Far => new RectangleF(S(x) - w, top, w, f.Height * 1.5f),
                StringAlignment.Center => new RectangleF(S(x) - w / 2, top, w, f.Height * 1.5f),
                _ => new RectangleF(S(x), top, w, f.Height * 1.5f),
            };
            if (!G.IsVisible(rect)) return;
            using var b = new SolidBrush(c);
            G.DrawString(s, f, b, rect, fmt);
        }

        /// <summary>Text width in design px.</summary>
        public float TextWidth(string s, Font f) => G.MeasureString(s, f, 4000, FmtLeft).Width / K;

        // ---- shapes ------------------------------------------------------------------------------
        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Max(1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public void Card(RectangleF r, float radius, Color fill, Color? stroke = null, float strokeWidth = 1f)
        {
            if (!Vis(r)) return;
            using var path = Rounded(r, S(radius));
            using var b = new SolidBrush(fill);
            G.FillPath(b, path);
            if (stroke is not null)
            {
                using var pen = new Pen(stroke.Value, Math.Max(1f, strokeWidth * K));
                G.DrawPath(pen, path);
            }
        }

        /// <summary>Standard surface card (rx 12, card fill, line stroke).</summary>
        public void Surface(float x, float y, float w, float h) => Card(R(x, y, w, h), 12, Theme.Card, Theme.Line);

        public void IconAt(string name, float x, float y, float designSize, bool accent = false)
        {
            if (!Vis(x, y, designSize, designSize)) return;
            int px = Math.Max(12, (int)Math.Round(designSize * K));
            var img = NebulaAssets.IconScaled(name, Dark, px);
            if (img is null) return;
            if (accent)
            {
                using var tinted = ControlHelper.TintImage(img, Theme.Accent);
                G.DrawImage(tinted, S(x), S(y), px, px);
            }
            else
            {
                G.DrawImage(img, S(x), S(y), px, px);
            }
        }

        public float Pill(float x, float y, string text, Color fill, Color fore)
        {
            var f = F(11, FontStyle.Bold);
            float width = TextWidth(text, f) + 24;
            Card(R(x, y, width, 25), 6, fill);
            Txt(text, x + 12, y + 17, f, fore);
            return width;
        }

        // ---- controls ----------------------------------------------------------------------------
        /// <summary>Segmented option button (36 px high in the kit).</summary>
        public void Segment(float x, float y, float w, float h, string text, bool selected, string id, bool enabled = true)
        {
            var r = R(x, y, w, h);
            bool hv = enabled && IsHover(id);
            Color fill = selected ? Theme.Accent : hv ? Theme.Line : Theme.Raised;
            Color fore = selected ? Theme.OnAccent : enabled ? Theme.Text : Theme.Faint;
            Card(r, 8, fill);
            Txt(text, x + w / 2, y + h / 2 + 4.5f, F(12, FontStyle.Bold), fore, StringAlignment.Center);
            if (enabled) Hit(r, id);
        }

        /// <summary>Accent action button.</summary>
        public void Button(float x, float y, float w, float h, string text, string id, bool primary = true, bool enabled = true)
        {
            var r = R(x, y, w, h);
            bool hv = enabled && IsHover(id);
            Color fill = !enabled ? Theme.Raised : primary ? (hv ? Theme.Text : Theme.Accent) : (hv ? Theme.Line : Theme.Raised);
            Color fore = !enabled ? Theme.Faint : primary ? Theme.OnAccent : Theme.Text;
            Card(r, 8, fill);
            Txt(text, x + w / 2, y + h / 2 + 4.5f, F(12, FontStyle.Bold), fore, StringAlignment.Center);
            if (enabled) Hit(r, id);
        }

        /// <summary>Switch, 38×22 in the kit.</summary>
        public void Toggle(float x, float y, bool on, string id, bool enabled = true)
        {
            var r = R(x, y, 38, 22);
            Card(r, 11, on ? Theme.Accent : (enabled ? Theme.Line : Theme.Raised));
            float kx = on ? x + 19 : x + 3;
            Card(R(kx, y + 3, 16, 16), 8, on ? Theme.OnAccent : Theme.Muted);
            if (enabled) Hit(R(x - 6, y - 6, 50, 34), id);
        }

        /// <summary>Horizontal slider: 4 px track, 14 px knob. Returns the track rect (client px) for drag math.</summary>
        public RectangleF Slider(float x, float y, float w, float t, string id, bool enabled = true)
        {
            t = Math.Clamp(t, 0f, 1f);
            var track = R(x, y, w, 4);
            if (enabled) Hit(R(x - 8, y - 14, w + 16, 32), id);
            if (!Vis(x - 8, y - 14, w + 16, 32)) return track;
            using (var b = new SolidBrush(Theme.Line)) G.FillRectangle(b, track);
            using (var b = new SolidBrush(enabled ? Theme.Accent : Theme.Faint)) G.FillRectangle(b, R(x, y, w * t, 4));
            float kx = x + w * t - 7;
            Card(R(kx, y - 5, 14, 14), 7, enabled ? Theme.Accent : Theme.Faint);
            return track;
        }

        public void Bar(float x, float y, float w, float h, float t, Color color)
        {
            if (!Vis(x, y, w, h)) return;
            using (var b = new SolidBrush(Theme.Line)) G.FillRectangle(b, R(x, y, w, h));
            using (var b = new SolidBrush(color)) G.FillRectangle(b, R(x, y, w * Math.Clamp(t, 0, 1), h));
        }

        /// <summary>Row with a label on the left and a clickable value on the right (dropdown style).</summary>
        public void ValueRow(float x, float baseline, float w, string label, string value, string id, bool enabled = true)
        {
            Txt(label, x, baseline, F(13), Theme.Text);
            bool hv = enabled && IsHover(id);
            Txt(value + (enabled ? "  ⌄" : ""), x + w, baseline, F(12, FontStyle.Bold), hv ? Theme.Text : enabled ? Theme.Accent : Theme.Faint, StringAlignment.Far);
            if (enabled) Hit(R(x, baseline - 20, w, 30), id);
        }

        public void Sparkline(RectangleF r, IReadOnlyList<float> data, int capacity, Color color, float min = 30, float max = 100)
        {
            if (!Vis(r)) return;
            using (var pen = new Pen(Theme.Line, 1f)) G.DrawLine(pen, r.X, r.Bottom, r.Right, r.Bottom);
            if (data.Count < 2) return;

            var pts = new List<PointF>();
            for (int i = 0; i < data.Count; i++)
            {
                if (float.IsNaN(data[i])) continue;
                float xx = r.X + r.Width * i / (capacity - 1);
                float v = Math.Clamp((data[i] - min) / (max - min), 0, 1);
                pts.Add(new PointF(xx, r.Bottom - v * r.Height));
            }
            if (pts.Count < 2) return;

            using (var fillPath = new GraphicsPath())
            {
                fillPath.AddLine(pts[0].X, r.Bottom, pts[0].X, pts[0].Y);
                fillPath.AddLines(pts.ToArray());
                fillPath.AddLine(pts[^1].X, pts[^1].Y, pts[^1].X, r.Bottom);
                fillPath.CloseFigure();
                using var lg = new LinearGradientBrush(r, Color.FromArgb(70, color), Color.FromArgb(0, color), LinearGradientMode.Vertical);
                G.FillPath(lg, fillPath);
            }
            using (var pen = new Pen(color, Math.Max(1.5f, 2f * K)) { LineJoin = LineJoin.Round })
                G.DrawLines(pen, pts.ToArray());
        }

        /// <summary>Ring gauge: value 0..1, centre (cx, cy) and radius in design px.</summary>
        public void Ring(float cx, float cy, float radius, float t, Color color, float thickness = 10)
        {
            var r = R(cx - radius, cy - radius, radius * 2, radius * 2);
            if (!Vis(r)) return;
            using (var pen = new Pen(Theme.Line, S(thickness)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                G.DrawArc(pen, r, 135, 270);
            if (t > 0)
                using (var pen = new Pen(color, S(thickness)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    G.DrawArc(pen, r, 135, 270 * Math.Clamp(t, 0, 1));
        }

        /// <summary>Blends a raster's black background into the page background at the edges.</summary>
        public void FadeEdges(RectangleF r, float band)
        {
            if (!Vis(r)) return;
            Color bg = Theme.Bg, clear = Color.FromArgb(0, Theme.Bg);
            band = Math.Min(band, Math.Min(r.Width, r.Height) / 3);
            var top = new RectangleF(r.X, r.Y, r.Width, band);
            var bottom = new RectangleF(r.X, r.Bottom - band, r.Width, band);
            var left = new RectangleF(r.X, r.Y, band, r.Height);
            var right = new RectangleF(r.Right - band, r.Y, band, r.Height);
            using (var b = new LinearGradientBrush(top, bg, clear, LinearGradientMode.Vertical)) G.FillRectangle(b, top);
            using (var b = new LinearGradientBrush(bottom, clear, bg, LinearGradientMode.Vertical)) G.FillRectangle(b, bottom);
            using (var b = new LinearGradientBrush(left, bg, clear, LinearGradientMode.Horizontal)) G.FillRectangle(b, left);
            using (var b = new LinearGradientBrush(right, clear, bg, LinearGradientMode.Horizontal)) G.FillRectangle(b, right);
        }

        public void Dispose()
        {
            foreach (var f in fonts.Values) f.Dispose();
            fonts.Clear();
        }
    }
}
