using System.Drawing.Imaging;
using System.Globalization;
using GrtModelLab.BarrelProfile;
using GrtModelLab.BarrelVibration;

namespace GrtModelLab.Ui;

/// <summary>
/// A to-scale-along-the-axis, exaggerated-in-diameter side view of the barrel — the "picture" the
/// numbers in the grid describe. Diameter is exaggerated vertically (true scale would be a sliver:
/// a 700&#160;mm barrel is only ~30&#160;mm across) with the exaggeration factor stated on the chart.
/// Optionally overlays a V2 mode shape on the same axis — reusing this chart's own barrel silhouette
/// as the backdrop rather than building a second chart control.
/// </summary>
internal sealed class BarrelProfileChart : Panel
{
    private BarrelProfileRecord? _r;
    private bool _inches;
    private BeamFem.Mode? _mode;
    private int _modeNumber;

    public BarrelProfileChart()
    {
        // Without this, growing the panel (e.g. maximizing the window) only invalidates the newly
        // exposed strip — Render() still draws a full, correctly-scaled picture, but GDI+ clips it
        // to that strip, so the old smaller render is never cleared and shows through underneath.
        SetStyle(ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        BackColor = Color.FromArgb(18, 24, 36);
    }

    /// <param name="mode">A V2 mode shape to overlay on the barrel silhouette, or null to draw the
    /// barrel alone. <paramref name="modeNumber"/> is 1-based, only used for the on-chart label.</param>
    public void Show(BarrelProfileRecord r, bool inches, BeamFem.Mode? mode = null, int modeNumber = 1)
    {
        _r = r; _inches = inches; _mode = mode; _modeNumber = modeNumber;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Render(e.Graphics, Width, Height);
    }

    public byte[]? RenderPng(int width = 1400, int height = 480)
    {
        var pts = _r?.Points;
        if (pts is null || pts.Count < 2) return null;
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) Render(g, width, height);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private void Render(Graphics g, int width, int height)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        var pts = _r?.Points.OrderBy(p => p.PositionMm).ToList();
        if (_r is null || pts is null || pts.Count < 2)
        {
            using var f0 = new Font("Segoe UI", 9f);
            using var b0 = new SolidBrush(Color.FromArgb(150, 165, 180));
            g.DrawString("Aggiungi almeno 2 punti al profilo (posizione + diametro esterno) per vedere la forma della canna.", f0, b0, 10, 10);
            return;
        }

        float s = Math.Max(1f, width / 900f);
        float ml = 60 * s, mr = 30 * s, mt = 34 * s, mb = 38 * s;
        float pw = width - ml - mr, ph = height - mt - mb;
        if (pw < 40 || ph < 40) return;
        float midY = mt + ph / 2f;

        double xMax = Math.Max(_r.LengthMm, pts.Max(p => p.PositionMm));
        double dMax = Math.Max(pts.Max(p => p.OuterDiameterMm), _r.BoreDiameterMm) * 1.1;
        float X(double posMm) => ml + (float)(posMm / Math.Max(1e-6, xMax)) * pw;
        // diameter exaggerated to use ~90% of the plot height, symmetric about the centerline
        float halfPh = ph * 0.45f;
        float Half(double diaMm) => (float)(diaMm / Math.Max(1e-6, dMax)) * halfPh;

        var ci = CultureInfo.InvariantCulture;
        using var axisPen = new Pen(Color.FromArgb(90, 148, 163, 184), Math.Max(1f, s));
        using var centerPen = new Pen(Color.FromArgb(50, 148, 163, 184), Math.Max(1f, s)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
        using var outerPen = new Pen(Color.FromArgb(86, 196, 240), 2.2f * s) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
        using var boreBrush = new SolidBrush(Color.FromArgb(60, 251, 191, 36));
        using var fnt = new Font("Segoe UI", 7.5f * s);
        using var fntB = new Font("Segoe UI", 7.5f * s, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.FromArgb(210, 220, 230));
        using var faintBrush = new SolidBrush(Color.FromArgb(150, 165, 180));

        g.DrawLine(centerPen, ml, midY, ml + pw, midY);

        // bore band (constant diameter for this version)
        float boreHalf = Half(_r.BoreDiameterMm);
        g.FillRectangle(boreBrush, ml, midY - boreHalf, pw, boreHalf * 2);

        // outer profile: top edge, then mirrored bottom edge
        var top = pts.Select(p => new PointF(X(p.PositionMm), midY - Half(p.OuterDiameterMm))).ToArray();
        var bot = pts.Select(p => new PointF(X(p.PositionMm), midY + Half(p.OuterDiameterMm))).ToArray();
        g.DrawLines(outerPen, top);
        g.DrawLines(outerPen, bot);
        foreach (var p in top) g.FillEllipse(Brushes.White, p.X - 2.5f * s, p.Y - 2.5f * s, 5 * s, 5 * s);
        foreach (var p in bot) g.FillEllipse(Brushes.White, p.X - 2.5f * s, p.Y - 2.5f * s, 5 * s, 5 * s);

        // breech / muzzle end caps
        g.DrawLine(axisPen, X(0), midY - Half(pts[0].OuterDiameterMm), X(0), midY + Half(pts[0].OuterDiameterMm));
        g.DrawLine(axisPen, X(xMax), midY - Half(pts[^1].OuterDiameterMm), X(xMax), midY + Half(pts[^1].OuterDiameterMm));

        // position axis — as many ticks as fit without the labels running into each other
        string unit = _inches ? "in" : "mm";
        double sampleMax = _inches ? xMax / 25.4 : xMax;
        float labelW = g.MeasureString(sampleMax.ToString(_inches ? "0.0" : "0", ci), fnt).Width + 14 * s;
        int ticks = Math.Max(2, Math.Min(8, (int)(pw / labelW)));
        for (int i = 0; i <= ticks; i++)
        {
            double posMm = xMax * i / ticks;
            float x = X(posMm);
            g.DrawLine(axisPen, x, mt + ph, x, mt + ph + 4 * s);
            string lbl = _inches ? (posMm / 25.4).ToString("0.0", ci) : posMm.ToString("0", ci);
            var sz = g.MeasureString(lbl, fnt);
            g.DrawString(lbl, fnt, textBrush, Math.Clamp(x - sz.Width / 2, 0, width - sz.Width), mt + ph + 6 * s);
        }
        g.DrawString($"posizione ({unit})", fnt, faintBrush, ml, mt + ph + 18 * s);
        double exaggeration = (halfPh * xMax) / (dMax * pw);
        string exagText = $"~{exaggeration:0}× esagerato";
        var exagSz = g.MeasureString(exagText, fnt);
        g.DrawString(exagText, fnt, faintBrush, width - exagSz.Width - 6 * s, 4 * s);
        g.DrawString("Foro", fntB, new SolidBrush(Color.FromArgb(251, 191, 36)), ml + 4 * s, midY - 8 * s);

        if (_mode is { } mode) DrawModeShape(g, mode, _modeNumber, mt, ph, midY, X, s, fnt, ci);
    }

    /// <summary>
    /// Overlays a mode shape as a single centerline curve (not top+bottom like the barrel outline —
    /// a mode shape is one displacement value per position, there's nothing to mirror). Scaled to
    /// its own peak-normalized [-1,1] range times a fraction of the plot's half-HEIGHT (not the
    /// barrel's own halfPh, which is a diameter scale the mode shape has no reason to share): this
    /// is a SHAPE, not a real deflection in mm (mode shapes are only defined up to an arbitrary
    /// scale, and the actual physical amplitude depends on the excitation energy, which this model
    /// doesn't simulate).
    /// </summary>
    private static void DrawModeShape(Graphics g, BeamFem.Mode mode, int modeNumber, float mt, float ph, float midY,
        Func<double, float> x, float s, Font fnt, CultureInfo ci)
    {
        // Deliberately SMALLER than the barrel outline's own halfPh (0.45): matching it exactly
        // means the shape's peak can reach all the way to the plot's outer edge whenever that peak
        // lands where a tapered barrel is thin (the muzzle, for most modes) - on a real taper this
        // reads as the curve "escaping" the picture, even though it never actually exceeds the plot
        // bounds. A smaller fraction keeps visible headroom from the top/bottom edges and the
        // "~Nx exaggerated" label at every mesh/profile, not just the uniform test barrel where the
        // peak never stood out because the outline was the same thickness everywhere.
        float modeHalf = ph * 0.35f;
        var shapePts = new PointF[mode.PositionsMm.Length];
        for (int i = 0; i < mode.PositionsMm.Length; i++)
            shapePts[i] = new PointF(x(mode.PositionsMm[i]), midY - (float)mode.Displacement[i] * modeHalf);

        using var modePen = new Pen(Color.FromArgb(230, 140, 235, 120), 2.2f * s)
        {
            DashStyle = System.Drawing.Drawing2D.DashStyle.Dash,
            LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
        };
        g.DrawLines(modePen, shapePts);

        // Interior zero-crossings, linearly interpolated between the two straddling nodes — the
        // classic "how many nodes does this mode have" picture, not the OBT sense of "node" (that
        // one is a point in TIME the barrel motion reverses, not a point in SPACE on this shape).
        using var nodeBrush = new SolidBrush(Color.FromArgb(235, 140, 235, 120));
        for (int i = 0; i + 1 < mode.Displacement.Length; i++)
        {
            double a = mode.Displacement[i], b = mode.Displacement[i + 1];
            if (Math.Sign(a) == 0 || Math.Sign(b) == 0 || Math.Sign(a) == Math.Sign(b)) continue;
            double t = a / (a - b);
            double posMm = mode.PositionsMm[i] + t * (mode.PositionsMm[i + 1] - mode.PositionsMm[i]);
            float px = x(posMm);
            g.FillEllipse(nodeBrush, px - 3.5f * s, midY - 3.5f * s, 7 * s, 7 * s);
        }

        // Fixed top-left corner (mirrors the barrel's own "~Nx exaggerated" label, top-right) —
        // anchoring to the curve's own endpoint instead would collide with that label whenever the
        // mode happens to peak near the muzzle, which is the common case for a cantilever's low modes.
        string label = string.Format(ci, "modo {0}: forma a {1:0.0} Hz (ampiezza arbitraria)", modeNumber, mode.FrequencyHz);
        g.DrawString(label, fnt, new SolidBrush(Color.FromArgb(230, 140, 235, 120)), shapePts[0].X, mt + 2 * s);
    }
}
