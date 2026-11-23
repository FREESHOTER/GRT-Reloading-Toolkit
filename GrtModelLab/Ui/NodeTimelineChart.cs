using System.Globalization;

namespace GrtModelLab.Ui;

/// <summary>
/// The actual "can I decide a charge from this" picture: for every charge GRT's ladder swept, how
/// fast the muzzle is still moving SIDEWAYS (transversely) at the exact moment the bullet leaves -
/// computed from GRT's own real P(t)/v(t) curve via <see cref="GrtModelLab.BarrelVibration.ForcedResponse"/>,
/// not assumed. The charge whose curve comes closest to the zero line is the one where THIS
/// mechanism's own prediction says the muzzle is quietest at exit - a real, computed number with no
/// invented scale factor, though still only one candidate excitation mechanism (see ForcedResponse's
/// own class doc for what it does and doesn't account for).
/// </summary>
internal sealed class NodeTimelineChart : Panel
{
    public sealed record SweepPoint(double ChargeGr, double ExitVelocityMmPerMs);

    private SweepPoint[] _points = Array.Empty<SweepPoint>();

    public NodeTimelineChart()
    {
        SetStyle(ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        BackColor = Color.FromArgb(18, 24, 36);
    }

    public void Show(SweepPoint[] points)
    {
        _points = points.OrderBy(p => p.ChargeGr).ToArray();
        Invalidate();
    }

    /// <summary>
    /// Finds the charge closest to zero transverse speed, and flags whether that's a real node (the
    /// curve actually crosses zero somewhere in the swept range) or just the least-bad end of a range
    /// that never gets close - e.g. a curve that decreases monotonically across the whole ladder, where
    /// the "closest" point is only closest because it's the last one tested, not because it's near a
    /// real node. In that case the true node (if any) likely lies outside the swept charges, and the
    /// ladder should be widened before trusting this number.
    /// </summary>
    public static (int BestIdx, bool EdgeOfRange) FindQuietest(SweepPoint[] pointsAscendingByCharge)
    {
        var pts = pointsAscendingByCharge;
        int best = 0;
        for (int i = 1; i < pts.Length; i++)
            if (Math.Abs(pts[i].ExitVelocityMmPerMs) < Math.Abs(pts[best].ExitVelocityMmPerMs)) best = i;
        bool crossesZero = false;
        for (int i = 1; i < pts.Length; i++)
            if (Math.Sign(pts[i - 1].ExitVelocityMmPerMs) != Math.Sign(pts[i].ExitVelocityMmPerMs)) { crossesZero = true; break; }
        bool edgeOfRange = !crossesZero && (best == 0 || best == pts.Length - 1);
        return (best, edgeOfRange);
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
            g.DrawString("Scansiona la scala di cariche di GRT per vedere qui il moto della volata all'uscita.", f0, b0, 10, 10);
            return;
        }

        float s = Math.Max(1f, width / 900f);
        float ml = 80 * s, mr = 30 * s, mt = 24 * s, mb = 40 * s;
        float pw = width - ml - mr, ph = height - mt - mb;
        if (pw < 40 || ph < 40) return;

        double chargeMin = _points.Min(p => p.ChargeGr), chargeMax = _points.Max(p => p.ChargeGr);
        if (chargeMax - chargeMin < 1e-6) { chargeMin -= 0.1; chargeMax += 0.1; }
        double velMax = _points.Select(p => Math.Abs(p.ExitVelocityMmPerMs)).Max();
        if (velMax < 1e-9) velMax = 1e-9;
        velMax *= 1.25; // headroom so the curve never hugs the top/bottom edge
        double velMin = -velMax;

        float X(double chargeGr) => (float)(ml + (chargeGr - chargeMin) / (chargeMax - chargeMin) * pw);
        float Y(double v) => (float)(mt + ph - (v - velMin) / (velMax - velMin) * ph);

        using var axisPen = new Pen(Color.FromArgb(90, 148, 163, 184), Math.Max(1f, s));
        using var fnt = new Font("Segoe UI", 7.5f * s);
        using var textBrush = new SolidBrush(Color.FromArgb(210, 220, 230));
        using var faintBrush = new SolidBrush(Color.FromArgb(150, 165, 180));

        g.DrawRectangle(axisPen, ml, mt, pw, ph);

        // Y axis (exit transverse velocity) ticks
        int yTicks = 4;
        for (int i = -yTicks; i <= yTicks; i++)
        {
            double v = velMax * i / yTicks;
            float y = Y(v);
            g.DrawLine(axisPen, ml - 4 * s, y, ml, y);
            string lbl = v.ToString("0.0000", ci);
            var sz = g.MeasureString(lbl, fnt);
            g.DrawString(lbl, fnt, textBrush, ml - sz.Width - 6 * s, y - sz.Height / 2);
        }
        g.DrawString("velocità trasversale alla volata all'uscita (mm/ms)", fnt, faintBrush, 2 * s, 2 * s);

        // X axis (charge) ticks
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

        // Zero line: "muzzle perfectly still at the moment of exit" - the target this chart is
        // actually about, drawn distinctly since everything else is secondary to how close the
        // curve gets to it.
        using var zeroPen = new Pen(Color.FromArgb(200, 235, 200, 90), 1.6f * s) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        float zeroY = Y(0);
        g.DrawLine(zeroPen, ml, zeroY, ml + pw, zeroY);

        // The swept charge -> exit-velocity curve.
        using var curvePen = new Pen(Color.FromArgb(86, 196, 240), 2.2f * s) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        var pts = _points.Select(p => new PointF(X(p.ChargeGr), Y(p.ExitVelocityMmPerMs))).ToArray();
        if (pts.Length > 1) g.DrawLines(curvePen, pts);
        foreach (var p in pts) g.FillEllipse(Brushes.White, p.X - 3 * s, p.Y - 3 * s, 6 * s, 6 * s);

        // Highlight whichever charge comes closest to zero - the point this whole chart exists to find.
        // But only call it a "node" if the curve actually crosses zero somewhere in range; otherwise
        // it's just the least-bad edge of a ladder that never gets close, and should read as a warning
        // to widen the range rather than as a found answer.
        var (bestIdx, edgeOfRange) = FindQuietest(_points);
        var best = pts[bestIdx];
        var highlightColor = edgeOfRange ? Color.FromArgb(235, 235, 160, 60) : Color.FromArgb(235, 140, 235, 120);
        using var bestPen = new Pen(highlightColor, 2.2f * s);
        g.DrawEllipse(bestPen, best.X - 7 * s, best.Y - 7 * s, 14 * s, 14 * s);
        string bestLbl = edgeOfRange
            ? string.Format(ci, "{0:0.00} gr è il bordo meno peggio di questo intervallo - la curva non si avvicina mai allo zero; allarga la scala", _points[bestIdx].ChargeGr)
            : string.Format(ci, "più vicino allo zero: {0:0.00} gr", _points[bestIdx].ChargeGr);
        var bestSz = g.MeasureString(bestLbl, fnt);
        float lblX = Math.Clamp(best.X - bestSz.Width / 2, ml, ml + pw - bestSz.Width);
        float lblY = best.Y < mt + ph / 2 ? best.Y + 10 * s : best.Y - 10 * s - bestSz.Height;
        g.DrawString(bestLbl, fnt, new SolidBrush(highlightColor), lblX, lblY);
    }
}
