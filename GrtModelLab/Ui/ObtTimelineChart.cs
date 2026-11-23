using System.Globalization;

namespace GrtModelLab.Ui;

/// <summary>
/// Charge vs GRT's own native OBT (longitudinal, Christopher Long) node time, read directly from GRT
/// per swept charge — not recomputed independently. GRT calibrates its own internal velocity model
/// against the actual shot; re-deriving the same longitudinal-wave formula ourselves, uncalibrated,
/// would just be a worse copy of a number GRT already gives us (see e.g. the community "Freq_canna.xls"
/// spreadsheet, which does exactly that from scratch). This plots the barrel's actual computed shot
/// time alongside GRT's own nearest node time for each swept charge, so you can see directly where —
/// if anywhere — they meet, using only GRT's own calibrated numbers.
/// </summary>
internal sealed class ObtTimelineChart : Panel
{
    public sealed record ObtPoint(double ChargeGr, double BarrelTimeMs, double? GrtObtMs, string GrtObtNode);

    private ObtPoint[] _points = Array.Empty<ObtPoint>();

    public ObtTimelineChart()
    {
        SetStyle(ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        BackColor = Color.FromArgb(18, 24, 36);
    }

    public void Show(ObtPoint[] points)
    {
        _points = points.OrderBy(p => p.ChargeGr).ToArray();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Render(e.Graphics, Width, Height);
    }

    /// <summary>Renders this chart to a standalone PNG (e.g. for attaching to a GRT note as a
    /// gallery picture) - independent of the control's own current on-screen size.</summary>
    public byte[]? RenderPng(int width = 1400, int height = 480)
    {
        if (_points.Length < 1) return null;
        using var bmp = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) Render(g, width, height);
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        return ms.ToArray();
    }

    private void Render(Graphics g, int width, int height)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        var ci = CultureInfo.InvariantCulture;

        if (_points.Length < 1)
        {
            using var f0 = new Font("Segoe UI", 9f);
            using var b0 = new SolidBrush(Color.FromArgb(150, 165, 180));
            g.DrawString("Scansiona la scala di cariche di GRT per confrontare qui il tempo di canna con il nodo OBT nativo di GRT.", f0, b0, 10, 10);
            return;
        }

        float s = Math.Max(1f, width / 900f);
        float ml = 80 * s, mr = 30 * s, mt = 24 * s, mb = 40 * s;
        float pw = width - ml - mr, ph = height - mt - mb;
        if (pw < 40 || ph < 40) return;

        double chargeMin = _points.Min(p => p.ChargeGr), chargeMax = _points.Max(p => p.ChargeGr);
        if (chargeMax - chargeMin < 1e-6) { chargeMin -= 0.1; chargeMax += 0.1; }

        var withObt = _points.Where(p => p.GrtObtMs is not null).ToArray();
        double timeMin = _points.Min(p => p.BarrelTimeMs), timeMax = _points.Max(p => p.BarrelTimeMs);
        if (withObt.Length > 0)
        {
            timeMin = Math.Min(timeMin, withObt.Min(p => p.GrtObtMs!.Value));
            timeMax = Math.Max(timeMax, withObt.Max(p => p.GrtObtMs!.Value));
        }
        double pad = Math.Max(1e-6, (timeMax - timeMin) * 0.2);
        timeMin -= pad; timeMax += pad;

        float X(double chargeGr) => (float)(ml + (chargeGr - chargeMin) / (chargeMax - chargeMin) * pw);
        float Y(double t) => (float)(mt + ph - (t - timeMin) / (timeMax - timeMin) * ph);

        using var axisPen = new Pen(Color.FromArgb(90, 148, 163, 184), Math.Max(1f, s));
        using var fnt = new Font("Segoe UI", 7.5f * s);
        using var textBrush = new SolidBrush(Color.FromArgb(210, 220, 230));
        using var faintBrush = new SolidBrush(Color.FromArgb(150, 165, 180));

        g.DrawRectangle(axisPen, ml, mt, pw, ph);

        int yTicks = 4;
        for (int i = 0; i <= yTicks; i++)
        {
            double t = timeMin + (timeMax - timeMin) * i / yTicks;
            float y = Y(t);
            g.DrawLine(axisPen, ml - 4 * s, y, ml, y);
            string lbl = t.ToString("0.000", ci);
            var sz = g.MeasureString(lbl, fnt);
            g.DrawString(lbl, fnt, textBrush, ml - sz.Width - 6 * s, y - sz.Height / 2);
        }
        g.DrawString("tempo (ms)", fnt, faintBrush, 2 * s, 2 * s);

        int xTicks = Math.Min(_points.Length - 1, 8);
        xTicks = Math.Max(xTicks, 1);
        for (int i = 0; i <= xTicks; i++)
        {
            double gr = chargeMin + (chargeMax - chargeMin) * i / xTicks;
            float x = X(gr);
            g.DrawLine(axisPen, x, mt + ph, x, mt + ph + 4 * s);
            string lbl = gr.ToString("0.00", ci);
            var sz = g.MeasureString(lbl, fnt);
            g.DrawString(lbl, fnt, textBrush, x - sz.Width / 2, mt + ph + 6 * s);
        }
        g.DrawString("carica (grani)", fnt, faintBrush, ml, mt + ph + 18 * s);

        // Barrel time (BT) - this barrel's own actual computed shot time for each charge.
        using var btPen = new Pen(Color.FromArgb(86, 196, 240), 2.2f * s) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        var btPts = _points.Select(p => new PointF(X(p.ChargeGr), Y(p.BarrelTimeMs))).ToArray();
        if (btPts.Length > 1) g.DrawLines(btPen, btPts);
        foreach (var p in btPts) g.FillEllipse(Brushes.White, p.X - 2.5f * s, p.Y - 2.5f * s, 5 * s, 5 * s);

        // GRT's own OBT node time, read per charge - drawn as its own line only between consecutive
        // charges that both have a value (a gap means GRT returned nothing for that charge), with each
        // point labeled by GRT's own node name (e.g. "#5", "#5 1/2") since the node GRT picks can
        // change across the ladder.
        using var obtPen = new Pen(Color.FromArgb(230, 140, 235, 120), 2.2f * s) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        using var obtBrush = new SolidBrush(Color.FromArgb(230, 140, 235, 120));
        PointF? prev = null;
        foreach (var p in _points)
        {
            if (p.GrtObtMs is not { } obtMs) { prev = null; continue; }
            var pt = new PointF(X(p.ChargeGr), Y(obtMs));
            if (prev is { } pv) g.DrawLine(obtPen, pv, pt);
            g.FillRectangle(obtBrush, pt.X - 3 * s, pt.Y - 3 * s, 6 * s, 6 * s);
            if (!string.IsNullOrEmpty(p.GrtObtNode))
            {
                var sz = g.MeasureString(p.GrtObtNode, fnt);
                g.DrawString(p.GrtObtNode, fnt, obtBrush, pt.X - sz.Width / 2, pt.Y - sz.Height - 4 * s);
            }
            prev = pt;
        }

        float lx = ml + 6 * s, ly = mt + 6 * s;
        g.DrawLine(btPen, lx, ly + 6 * s, lx + 20 * s, ly + 6 * s);
        g.DrawString("tempo di canna (questo sparo)", fnt, textBrush, lx + 24 * s, ly);
        g.DrawLine(obtPen, lx, ly + 22 * s, lx + 20 * s, ly + 22 * s);
        g.DrawString("nodo OBT nativo di GRT", fnt, textBrush, lx + 24 * s, ly + 16 * s);

        if (withObt.Length == 0) return;

        // Highlight whichever charge brings barrel time closest to GRT's own reported node time -
        // i.e. where GRT's native (longitudinal) theory itself says this charge sits nearest a node.
        // Reuses NodeTimelineChart's own closest/edge-of-range logic on the BT-minus-OBT delta, so the
        // same "is this a real crossing or just the edge of what was tested" honesty applies here too.
        var deltaPoints = withObt.Select(p => new NodeTimelineChart.SweepPoint(p.ChargeGr, p.BarrelTimeMs - p.GrtObtMs!.Value)).ToArray();
        var (bestIdx, edgeOfRange) = NodeTimelineChart.FindQuietest(deltaPoints);
        var bp = withObt[bestIdx];
        double bestAbs = Math.Abs(bp.BarrelTimeMs - bp.GrtObtMs!.Value);
        var bestPt = new PointF(X(bp.ChargeGr), Y(bp.BarrelTimeMs));
        var hiColor = edgeOfRange ? Color.FromArgb(235, 235, 160, 60) : Color.FromArgb(235, 255, 210, 90);
        using var bestPen = new Pen(hiColor, 2.2f * s);
        g.DrawEllipse(bestPen, bestPt.X - 8 * s, bestPt.Y - 8 * s, 16 * s, 16 * s);
        string bestLbl = edgeOfRange
            ? string.Format(ci, "{0:0.00} gr è la più vicina testata, ma non incontra mai davvero il nodo di GRT - allarga la scala", bp.ChargeGr)
            : string.Format(ci, "più vicina al nodo di GRT: {0:0.00} gr (scarto {1:0.000} ms)", bp.ChargeGr, bestAbs);
        var blSz = g.MeasureString(bestLbl, fnt);
        float blx = Math.Clamp(bestPt.X - blSz.Width / 2, ml, ml + pw - blSz.Width);
        float bly = bestPt.Y < mt + ph / 2 ? bestPt.Y + 12 * s : bestPt.Y - 12 * s - blSz.Height;
        g.DrawString(bestLbl, fnt, new SolidBrush(hiColor), blx, bly);
    }
}
