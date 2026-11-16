using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace GHelper.UI.SignalGlass
{
    /// <summary>
    /// WinForms / GDI+ "Glass Lite" renderer for Signal Glass.
    /// Caches the scaled backdrop bitmap across resize/theme/DPI changes so 1s sensor ticks
    /// only repaint dynamic regions. Supports full opaque fallback for high contrast or
    /// reduced transparency without hiding any controls.
    /// </summary>
    public sealed class GlassPainter : IDisposable
    {
        private Bitmap? backdropCache;
        private Size backdropSize;
        private bool backdropDark;
        private bool backdropOpaque;

        private readonly Dictionary<string, Font> fontCache = new();
        private static readonly string TextFamily = PickFamily("Segoe UI Variable Text", "Segoe UI");
        private static readonly string DisplayFamily = PickFamily("Segoe UI Variable Display", "Segoe UI");

        public static readonly StringFormat FmtLeft = new(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        public static readonly StringFormat FmtRight = new(StringFormat.GenericTypographic) { Alignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        public static readonly StringFormat FmtCenter = new(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        public static readonly StringFormat FmtMiddleCenter = new(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter };
        public static readonly StringFormat FmtWrap = new(StringFormat.GenericTypographic) { Trimming = StringTrimming.EllipsisWord };

        private static string PickFamily(string preferred, string fallback)
        {
            try { using var f = new FontFamily(preferred); return preferred; }
            catch { return fallback; }
        }

        public Font Font(float dipPx, float scale, FontStyle style = FontStyle.Regular, bool display = false)
        {
            float px = Math.Max(8f, dipPx * scale);
            string key = $"{px:F1}|{(int)style}|{display}";
            if (!fontCache.TryGetValue(key, out var f))
            {
                f = new Font(display ? DisplayFamily : TextFamily, px, style, GraphicsUnit.Pixel);
                fontCache[key] = f;
            }
            return f;
        }

        public void InvalidateBackdrop()
        {
            backdropCache?.Dispose();
            backdropCache = null;
        }

        public void DrawBackdrop(Graphics g, Size clientSize, float scale, SignalTheme theme, bool dark, bool opaque)
        {
            if (clientSize.Width <= 0 || clientSize.Height <= 0) return;
            if (opaque)
            {
                g.Clear(theme.Bg);
                return;
            }

            if (backdropCache is null || backdropSize != clientSize || backdropDark != dark || backdropOpaque != opaque)
            {
                RebuildBackdrop(clientSize, scale, theme, dark, opaque);
            }

            if (backdropCache is not null)
                g.DrawImageUnscaled(backdropCache, 0, 0);
            else
                g.Clear(theme.Bg);
        }

        private void RebuildBackdrop(Size size, float scale, SignalTheme theme, bool dark, bool opaque)
        {
            backdropCache?.Dispose();
            backdropCache = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            backdropSize = size;
            backdropDark = dark;
            backdropOpaque = opaque;

            using var bg = Graphics.FromImage(backdropCache);
            bg.SmoothingMode = SmoothingMode.AntiAlias;
            bg.InterpolationMode = InterpolationMode.HighQualityBilinear;

            var rect = new RectangleF(0, 0, size.Width, size.Height);
            using (var grad = new LinearGradientBrush(rect, theme.Bg, theme.Surface, 45f))
            {
                bg.FillRectangle(grad, rect);
            }

            var img = SignalAssets.Background(dark, scale > 1.25f);
            if (img is not null)
            {
                float ratio = Math.Max((float)size.Width / img.Width, (float)size.Height / img.Height);
                float w = img.Width * ratio;
                float h = img.Height * ratio;
                float x = (size.Width - w) / 2f;
                float y = (size.Height - h) / 2f;

                using var ia = new ImageAttributes();
                float alpha = dark ? 0.34f : 0.72f;
                var cm = new ColorMatrix { Matrix33 = alpha };
                ia.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

                bg.DrawImage(
                    img,
                    new[] { new PointF(x, y), new PointF(x + w, y), new PointF(x, y + h) },
                    new RectangleF(0, 0, img.Width, img.Height),
                    GraphicsUnit.Pixel,
                    ia);
            }

            // Subtle accent atmospheric wash top-right
            using var wash = new GraphicsPath();
            wash.AddEllipse(size.Width * 0.45f, -size.Height * 0.2f, size.Width * 0.85f, size.Height * 0.9f);
            using var pgb = new PathGradientBrush(wash)
            {
                CenterColor = Color.FromArgb(dark ? 16 : 10, theme.Accent),
                SurroundColors = new[] { Color.FromArgb(0, theme.Accent) },
            };
            bg.FillPath(pgb, wash);
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            if (r.Width <= 0 || r.Height <= 0)
            {
                p.AddRectangle(new RectangleF(r.X, r.Y, Math.Max(0.1f, r.Width), Math.Max(0.1f, r.Height)));
                return p;
            }
            float d = Math.Max(0f, Math.Min(radius * 2f, Math.Min(r.Width, r.Height)));
            if (d <= 0.5f) { p.AddRectangle(r); return p; }
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void DrawPanel(Graphics g, RectangleF boundsPx, float scale, SignalTheme theme, bool opaque, float radiusDip = SignalTheme.RadiusPanel)
        {
            if (boundsPx.Width <= 1 || boundsPx.Height <= 1) return;
            var state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float r = radiusDip * scale;

                if (!opaque)
                {
                    // Soft two-step shadow (6 DIP vertical offset)
                    using var s1 = Rounded(new RectangleF(boundsPx.X, boundsPx.Y + 6f * scale, boundsPx.Width, boundsPx.Height), r);
                    using var sb1 = new SolidBrush(Color.FromArgb(28, 0, 0, 0));
                    g.FillPath(sb1, s1);

                    using var s2 = Rounded(new RectangleF(boundsPx.X, boundsPx.Y + 2f * scale, boundsPx.Width, boundsPx.Height), r);
                    using var sb2 = new SolidBrush(Color.FromArgb(18, 0, 0, 0));
                    g.FillPath(sb2, s2);
                }

                using var shape = Rounded(boundsPx, r);
                if (opaque)
                {
                    using var fill = new SolidBrush(theme.Surface);
                    g.FillPath(fill, shape);
                }
                else
                {
                    int alpha = (int)Math.Round(SignalTheme.PanelOpacity * 255);
                    using var grad = new LinearGradientBrush(
                        boundsPx,
                        Color.FromArgb(alpha, theme.Raised),
                        Color.FromArgb(alpha, theme.Surface),
                        90f);
                    grad.SetBlendTriangularShape(0.25f, 0.7f);
                    g.FillPath(grad, shape);

                    // Top highlight sheen clipped to panel shape (max 52 DIP)
                    var savedClip = g.Clip;
                    g.SetClip(shape, CombineMode.Intersect);
                    float sheenH = Math.Min(SignalTheme.HighlightHeight * scale, boundsPx.Height * 0.5f);
                    var band = new RectangleF(boundsPx.X, boundsPx.Y, boundsPx.Width, Math.Max(2f, sheenH));
                    using var sheen = new LinearGradientBrush(band, Color.FromArgb(24, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f);
                    g.FillRectangle(sheen, band);
                    g.Clip = savedClip;
                }

                // Crisp rim border + top optical edge highlight
                using (var rim = new Pen(Color.FromArgb(opaque ? 180 : 115, theme.Line), Math.Max(1f, scale)))
                    g.DrawPath(rim, shape);

                if (!opaque)
                {
                    float inset = Math.Min(r, boundsPx.Width * 0.35f);
                    using var topEdge = new Pen(Color.FromArgb(95, 255, 255, 255), Math.Max(1f, scale));
                    g.DrawLine(topEdge, boundsPx.Left + inset, boundsPx.Top + scale * 0.8f, boundsPx.Right - inset, boundsPx.Top + scale * 0.8f);
                }
            }
            finally
            {
                g.Restore(state);
            }
        }

        public static void DrawControlBox(Graphics g, RectangleF boundsPx, float scale, SignalTheme theme, bool opaque,
            bool selected, bool hover, bool pressed, bool enabled, float radiusDip = SignalTheme.RadiusControl)
        {
            if (boundsPx.Width <= 1 || boundsPx.Height <= 1) return;
            var state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float r = radiusDip * scale;
                using var path = Rounded(boundsPx, r);

                Color fill;
                if (!enabled)
                {
                    fill = Color.FromArgb(opaque ? 255 : 150, theme.Surface);
                }
                else if (selected)
                {
                    fill = pressed ? ControlPaint.Dark(theme.Accent, 0.08f) : hover ? ControlPaint.Light(theme.Accent, 0.06f) : theme.Accent;
                }
                else
                {
                    Color baseCol = pressed ? theme.Plot : hover ? theme.Raised : theme.Surface;
                    fill = Color.FromArgb(opaque ? 255 : 235, baseCol);
                }

                using (var b = new SolidBrush(fill))
                    g.FillPath(b, path);

                Color stroke = selected ? theme.Accent : hover && enabled ? theme.Accent : Color.FromArgb(opaque ? 190 : 120, theme.Line);
                using (var pen = new Pen(stroke, Math.Max(1f, scale)))
                    g.DrawPath(pen, path);
            }
            finally
            {
                g.Restore(state);
            }
        }

        public static void DrawFocusRing(Graphics g, RectangleF boundsPx, float scale, SignalTheme theme, bool onAccent = false, float radiusDip = SignalTheme.RadiusControl)
        {
            var state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = boundsPx;
                r.Inflate(2f * scale, 2f * scale);
                using var path = Rounded(r, (radiusDip + 2f) * scale);
                using var pen = new Pen(theme.FocusRingColor(onAccent), Math.Max(2f, 2f * scale));
                g.DrawPath(pen, path);
            }
            finally
            {
                g.Restore(state);
            }
        }

        public static void DrawToggle(Graphics g, RectangleF boundsPx, float scale, SignalTheme theme, bool on, bool hover, bool enabled)
        {
            var state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float r = boundsPx.Height / 2f;
                using var track = Rounded(boundsPx, r);
                Color trackColor = !enabled ? theme.Surface : on ? theme.Accent : (hover ? theme.Line : theme.Raised);
                using (var tb = new SolidBrush(trackColor)) g.FillPath(tb, track);
                using (var pen = new Pen(on ? theme.Accent : theme.Line, Math.Max(1f, scale))) g.DrawPath(pen, track);

                float knobD = boundsPx.Height - 6f * scale;
                float kx = on ? (boundsPx.Right - 3f * scale - knobD) : (boundsPx.Left + 3f * scale);
                var knobRect = new RectangleF(kx, boundsPx.Top + 3f * scale, knobD, knobD);
                using var kb = new SolidBrush(!enabled ? theme.Muted : on ? theme.OnAccent : theme.Text);
                g.FillEllipse(kb, knobRect);
            }
            finally
            {
                g.Restore(state);
            }
        }

        public static RectangleF DrawSlider(Graphics g, RectangleF trackPx, float scale, SignalTheme theme, float t, bool hover, bool enabled)
        {
            t = Math.Clamp(t, 0f, 1f);
            var state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float trackH = Math.Max(4f, 6f * scale);
                var tr = new RectangleF(trackPx.X, trackPx.Y + (trackPx.Height - trackH) / 2f, trackPx.Width, trackH);
                using (var bgPath = Rounded(tr, trackH / 2f))
                using (var bgBrush = new SolidBrush(Color.FromArgb(140, theme.Line)))
                    g.FillPath(bgBrush, bgPath);

                float fillW = tr.Width * t;
                if (fillW > 1f)
                {
                    var fr = new RectangleF(tr.X, tr.Y, fillW, trackH);
                    using var fPath = Rounded(fr, trackH / 2f);
                    using var fBrush = new SolidBrush(enabled ? theme.Accent : theme.Muted);
                    g.FillPath(fBrush, fPath);
                }

                float thumbD = (hover ? 20f : 18f) * scale;
                float cx = tr.X + tr.Width * t;
                float cy = tr.Y + trackH / 2f;
                var knob = new RectangleF(cx - thumbD / 2f, cy - thumbD / 2f, thumbD, thumbD);
                using (var kb = new SolidBrush(theme.Text)) g.FillEllipse(kb, knob);
                using (var kpen = new Pen(enabled ? theme.Accent : theme.Muted, Math.Max(2f, 3f * scale))) g.DrawEllipse(kpen, knob);
                return tr;
            }
            finally
            {
                g.Restore(state);
            }
        }

        /// <summary>
        /// Draws a 60-second telemetry chart (always LTR time progression -60s..0s).
        /// Missing samples (null) create a true visual gap rather than dropping to 0.
        /// </summary>
        public void DrawHistoryChart(Graphics g, RectangleF boundsPx, float scale, SignalTheme theme,
            float?[] samples, int capacity, Color seriesColor, string unitLabel, Point? hoverPoint, out string hoverTooltip)
        {
            hoverTooltip = "";
            if (boundsPx.Width < 40 || boundsPx.Height < 40) return;

            var state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                using (var bgPath = Rounded(boundsPx, SignalTheme.RadiusPlot * scale))
                using (var bgBrush = new SolidBrush(theme.Plot))
                    g.FillPath(bgBrush, bgPath);

                float padL = 44f * scale, padR = 16f * scale, padT = 16f * scale, padB = 30f * scale;
                var plot = new RectangleF(boundsPx.X + padL, boundsPx.Y + padT,
                    Math.Max(20f, boundsPx.Width - padL - padR),
                    Math.Max(20f, boundsPx.Height - padT - padB));

                var axisFont = Font(11f, scale);
                using var axisBrush = new SolidBrush(theme.Muted);
                using var gridPen = new Pen(Color.FromArgb(75, theme.Line), Math.Max(1f, scale));

                for (int i = 0; i <= 5; i++)
                {
                    float yy = plot.Top + plot.Height * i / 5f;
                    g.DrawLine(gridPen, plot.Left, yy, plot.Right, yy);
                    string lbl = (100 - i * 20).ToString();
                    g.DrawString(lbl, axisFont, axisBrush, plot.Left - 8f * scale, yy - axisFont.Height / 2f, FmtRight);
                }

                for (int i = 0; i <= 6; i++)
                {
                    float xx = plot.Left + plot.Width * i / 6f;
                    string lbl = (-60 + i * 10) + "s";
                    g.DrawString(lbl, axisFont, axisBrush, xx, plot.Bottom + 6f * scale, FmtCenter);
                }

                if (samples.Length > 0 && capacity > 1)
                {
                    // Right-align existing samples to 0s (latest at plot.Right)
                    int offset = Math.Max(0, capacity - samples.Length);
                    var segment = new List<PointF>();
                    var allValid = new List<(int slot, float val, PointF pt)>();

                    void FlushSegment()
                    {
                        if (segment.Count == 1)
                        {
                            using var dot = new SolidBrush(seriesColor);
                            float r = 3f * scale;
                            g.FillEllipse(dot, segment[0].X - r, segment[0].Y - r, r * 2f, r * 2f);
                        }
                        else if (segment.Count >= 2)
                        {
                            using var area = new GraphicsPath();
                            area.AddLine(segment[0].X, plot.Bottom, segment[0].X, segment[0].Y);
                            area.AddLines(segment.ToArray());
                            area.AddLine(segment[^1].X, segment[^1].Y, segment[^1].X, plot.Bottom);
                            area.CloseFigure();
                            using var fillBrush = new SolidBrush(Color.FromArgb(26, seriesColor));
                            g.FillPath(fillBrush, area);

                            using var linePen = new Pen(seriesColor, Math.Max(1.8f, 2.5f * scale))
                            {
                                LineJoin = LineJoin.Round,
                                StartCap = LineCap.Round,
                                EndCap = LineCap.Round,
                            };
                            g.DrawLines(linePen, segment.ToArray());
                        }
                        segment.Clear();
                    }

                    for (int i = 0; i < samples.Length; i++)
                    {
                        if (!samples[i].HasValue)
                        {
                            FlushSegment();
                            continue;
                        }
                        float v = samples[i]!.Value;
                        int slot = offset + i;
                        float px = plot.Left + plot.Width * slot / (capacity - 1f);
                        float norm = Math.Clamp(v / 100f, 0f, 1f);
                        float py = plot.Bottom - plot.Height * norm;
                        var pt = new PointF(px, py);
                        segment.Add(pt);
                        allValid.Add((slot, v, pt));
                    }
                    FlushSegment();

                    if (hoverPoint.HasValue && plot.Contains(hoverPoint.Value) && allValid.Count > 0)
                    {
                        var nearest = allValid.MinBy(x => Math.Abs(x.pt.X - hoverPoint.Value.X));
                        if (Math.Abs(nearest.pt.X - hoverPoint.Value.X) <= 18f * scale)
                        {
                            using var guidePen = new Pen(Color.FromArgb(140, seriesColor), Math.Max(1f, scale)) { DashStyle = DashStyle.Dash };
                            g.DrawLine(guidePen, nearest.pt.X, plot.Top, nearest.pt.X, plot.Bottom);

                            float hr = 4.5f * scale;
                            using var hb = new SolidBrush(theme.Surface);
                            using var hp = new Pen(seriesColor, 2f * scale);
                            g.FillEllipse(hb, nearest.pt.X - hr, nearest.pt.Y - hr, hr * 2f, hr * 2f);
                            g.DrawEllipse(hp, nearest.pt.X - hr, nearest.pt.Y - hr, hr * 2f, hr * 2f);

                            int secAgo = (capacity - 1) - nearest.slot;
                            string label = $"{(secAgo == 0 ? "0s" : $"-{secAgo}s")} · {Math.Round(nearest.val)}{unitLabel}";
                            hoverTooltip = label;

                            var tf = Font(11f, scale, FontStyle.Bold);
                            var sz = g.MeasureString(label, tf, 400, FmtLeft);
                            float bw = sz.Width + 14f * scale;
                            float bh = 22f * scale;
                            float bx = Math.Clamp(nearest.pt.X - bw / 2f, plot.Left, plot.Right - bw);
                            float by = Math.Max(plot.Top + 2f * scale, nearest.pt.Y - bh - 8f * scale);
                            var badgeRect = new RectangleF(bx, by, bw, bh);
                            using var badgePath = Rounded(badgeRect, 6f * scale);
                            using var bb = new SolidBrush(theme.Raised);
                            using var bp = new Pen(seriesColor, Math.Max(1f, scale));
                            g.FillPath(bb, badgePath);
                            g.DrawPath(bp, badgePath);
                            using var tb = new SolidBrush(theme.Text);
                            g.DrawString(label, tf, tb, bx + 7f * scale, by + (bh - tf.Height) / 2f, FmtLeft);
                        }
                    }
                }
            }
            finally
            {
                g.Restore(state);
            }
        }

        public void DrawText(Graphics g, string text, RectangleF rectPx, Font font, Color color, StringFormat fmt)
        {
            if (string.IsNullOrEmpty(text) || rectPx.Width <= 0 || rectPx.Height <= 0) return;
            using var b = new SolidBrush(color);
            g.DrawString(text, font, b, rectPx, fmt);
        }

        public void Dispose()
        {
            backdropCache?.Dispose();
            backdropCache = null;
            foreach (var f in fontCache.Values) f.Dispose();
            fontCache.Clear();
        }
    }
}
