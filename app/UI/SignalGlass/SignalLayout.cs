namespace GHelper.UI.SignalGlass
{
    public sealed class SignalShellLayout
    {
        public float WindowW { get; init; }
        public float WindowH { get; init; }
        public float Scale { get; init; }
        public bool Rtl { get; init; }
        public bool CompactWidth { get; init; }        // 900..999 DIP: inspector becomes drawer
        public bool MidWidth { get; init; }            // 1000..1199 DIP: 288 DIP inspector
        public bool CompactSearch { get; init; }       // search button instead of inline field

        public RectangleF Titlebar { get; init; }
        public RectangleF TopNav { get; init; }
        public RectangleF Workspace { get; init; }
        public RectangleF WorkspaceHeader { get; init; }
        public RectangleF WorkspaceBody { get; init; }
        public RectangleF WorkspaceFooter { get; init; }
        public RectangleF Inspector { get; init; }
        public RectangleF SearchBox { get; init; }

        // Overview sections (guaranteed visible without scroll down to 900x640)
        public RectangleF CpuPanel { get; init; }
        public RectangleF CpuMetrics { get; init; }
        public RectangleF CpuPlot { get; init; }
        public RectangleF GpuPanel { get; init; }
        public RectangleF GpuMetrics { get; init; }
        public RectangleF GpuPlot { get; init; }
        public RectangleF BottomCooling { get; init; }
        public RectangleF BottomMemory { get; init; }
        public RectangleF BottomBattery { get; init; }

        public float DipToPx(float dip) => dip * Scale;
        public float PxToDip(float px) => Scale > 0 ? px / Scale : px;

        public RectangleF ToPx(RectangleF dipRect) =>
            new(dipRect.X * Scale, dipRect.Y * Scale, dipRect.Width * Scale, dipRect.Height * Scale);

        public PointF PxToDipPoint(Point pt) =>
            new(PxToDip(pt.X), PxToDip(pt.Y));

        public static RectangleF MirrorX(RectangleF r, float totalW) =>
            new(totalW - r.Right, r.Y, r.Width, r.Height);

        public static SignalShellLayout Compute(int clientWidthPx, int clientHeightPx, float dpi, bool rtl, bool drawerOpen)
        {
            float scale = Math.Max(0.5f, dpi / 96f);
            float w = Math.Max(420f, clientWidthPx / scale);
            float h = Math.Max(360f, clientHeightPx / scale);

            float margin = h < 660f ? 16f : 24f;
            const float gap = 24f;
            float titleH = h < 660f ? 38f : SignalTheme.TitlebarH;

            bool compactWidth = w < 1000f;
            bool midWidth = w is >= 1000f and < 1200f;
            float inspW = compactWidth ? 320f : midWidth ? 288f : (w >= 1360f ? 344f : 320f);
            float wsW = compactWidth ? (w - margin * 2) : (w - margin * 2 - gap - inspW);

            float topY = titleH + (h < 660f ? 8f : 16f);
            float navH = h < 660f ? 50f : 56f;
            float wsY = topY + navH + (h < 660f ? 8f : 16f);
            float bottomMargin = h < 660f ? 14f : 24f;
            float wsH = Math.Max(260f, h - wsY - bottomMargin);

            var topNav = new RectangleF(margin, topY, wsW, navH);
            var ws = new RectangleF(margin, wsY, wsW, wsH);
            var insp = compactWidth
                ? new RectangleF(drawerOpen ? (w - inspW - margin) : (w + 40f), topY, inspW, Math.Max(260f, h - topY - bottomMargin))
                : new RectangleF(margin + wsW + gap, topY, inspW, Math.Max(260f, h - topY - bottomMargin));

            bool compactSearch = wsW < 760f;
            float searchW = compactSearch ? 38f : Math.Clamp(wsW - 496f, 200f, 472f);
            float searchH = h < 660f ? 34f : 38f;
            var searchBox = new RectangleF(topNav.Right - 16f - searchW, topY + (navH - searchH) / 2f, searchW, searchH);

            float headerH = h < 740f ? 56f : 74f;
            var header = new RectangleF(ws.X + 8f, ws.Y + 4f, ws.Width - 16f, headerH);
            float footerH = 44f;
            var footer = new RectangleF(ws.X, ws.Bottom - footerH, ws.Width, footerH);
            var body = new RectangleF(ws.X, header.Bottom + 6f, ws.Width, Math.Max(140f, footer.Top - 8f - (header.Bottom + 6f)));

            // Overview adaptive rows: must fit CPU + GPU + 3 bottom cards cleanly inside actual client area
            float ovTop = header.Bottom + 6f;
            float availOvH = Math.Max(240f, ws.Bottom - ovTop);
            float rowGap = availOvH < 520f ? 10f : 24f;
            float bottomH = Math.Clamp(availOvH * 0.31f, 112f, 246f);
            float chartPanelH = Math.Clamp((availOvH - bottomH - rowGap * 2f) / 2f, 108f, 264f);

            var cpuPanel = new RectangleF(ws.X, ovTop, wsW, chartPanelH);
            var gpuPanel = new RectangleF(ws.X, cpuPanel.Bottom + rowGap, wsW, chartPanelH);

            float metricW = wsW < 780f ? 156f : 180f;
            float plotPadV = chartPanelH < 170f ? 14f : 20f;
            var cpuMetrics = new RectangleF(cpuPanel.X + 20f, cpuPanel.Y + 12f, metricW, chartPanelH - 24f);
            var cpuPlot = new RectangleF(cpuMetrics.Right + 12f, cpuPanel.Y + plotPadV, Math.Max(200f, cpuPanel.Right - 20f - (cpuMetrics.Right + 12f)), chartPanelH - plotPadV * 2f);

            var gpuMetrics = new RectangleF(gpuPanel.X + 20f, gpuPanel.Y + 12f, metricW, chartPanelH - 24f);
            var gpuPlot = new RectangleF(gpuMetrics.Right + 12f, gpuPanel.Y + plotPadV, Math.Max(200f, gpuPanel.Right - 20f - (gpuMetrics.Right + 12f)), chartPanelH - plotPadV * 2f);

            float bottomY = gpuPanel.Bottom + rowGap;
            float cardGap = 24f;
            float cardW = (wsW - cardGap * 2f) / 3f;
            var bCool = new RectangleF(ws.X, bottomY, cardW, bottomH);
            var bMem = new RectangleF(ws.X + cardW + cardGap, bottomY, cardW, bottomH);
            var bBat = new RectangleF(ws.X + (cardW + cardGap) * 2f, bottomY, cardW, bottomH);

            if (rtl)
            {
                topNav = MirrorX(topNav, w);
                ws = MirrorX(ws, w);
                insp = MirrorX(insp, w);
                searchBox = MirrorX(searchBox, w);
                header = MirrorX(header, w);
                body = MirrorX(body, w);
                footer = MirrorX(footer, w);

                cpuPanel = MirrorX(cpuPanel, w);
                gpuPanel = MirrorX(gpuPanel, w);
                cpuMetrics = MirrorX(cpuMetrics, w);
                cpuPlot = MirrorX(cpuPlot, w);
                gpuMetrics = MirrorX(gpuMetrics, w);
                gpuPlot = MirrorX(gpuPlot, w);

                var mCool = MirrorX(bCool, w);
                var mMem = MirrorX(bMem, w);
                var mBat = MirrorX(bBat, w);
                bCool = mCool;
                bMem = mMem;
                bBat = mBat;
            }

            return new SignalShellLayout
            {
                WindowW = w,
                WindowH = h,
                Scale = scale,
                Rtl = rtl,
                CompactWidth = compactWidth,
                MidWidth = midWidth,
                CompactSearch = compactSearch,
                Titlebar = new RectangleF(0, 0, w, titleH),
                TopNav = topNav,
                Workspace = ws,
                WorkspaceHeader = header,
                WorkspaceBody = body,
                WorkspaceFooter = footer,
                Inspector = insp,
                SearchBox = searchBox,
                CpuPanel = cpuPanel,
                CpuMetrics = cpuMetrics,
                CpuPlot = cpuPlot,
                GpuPanel = gpuPanel,
                GpuMetrics = gpuMetrics,
                GpuPlot = gpuPlot,
                BottomCooling = bCool,
                BottomMemory = bMem,
                BottomBattery = bBat,
            };
        }
    }
}
