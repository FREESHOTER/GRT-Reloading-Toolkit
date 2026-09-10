using System.Drawing.Imaging;
using System.Globalization;

namespace GrtReloadingToolkit.Ocw;

/// <summary>
/// Primary signal + vertical-POI vs the ladder step, recommended node shaded.
/// Charge mode: primary = muzzle velocity. Seating mode: primary = group mean radius.
/// </summary>
internal sealed class LadderChart : Panel
{
    private LadderResult? _r;

    public LadderChart()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(18, 24, 36);
    }

    public void Show(LadderResult r) { _r = r; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Render(e.Graphics, Width, Height);
    }

    /// <summary>Renders the current result to a stand-alone PNG (for the GRT report gallery).</summary>
    public byte[]? RenderPng(int width = 1600, int height = 760)
    {
        if (_r is null || _r.Rows.Count < 2) return null;
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
            Render(g, width, height);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private void Render(Graphics g, int width, int height)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        if (_r is null || _r.Rows.Count < 2) return;

        bool seat = _r.Mode == LadderMode.Seating;
        var rows = _r.Rows;
        float s = Math.Max(1f, width / 900f);              // scale everything up for big export PNGs
        float ml = 60 * s, mr = 58 * s, mt = 30 * s, mb = 38 * s;
        float pw = width - ml - mr, ph = height - mt - mb;
        if (pw < 40 || ph < 40) return;
        float fpt = 7.5f * s, dotR = 3.5f * s, penW = 2f * s;

        double cMin = rows.Min(x => x.X), cMax = rows.Max(x => x.X);
        float X(double c) => ml + (float)((c - cMin) / Math.Max(1e-9, cMax - cMin)) * pw;

        // primary series
        Func<LadderStep, bool> hasP = seat ? x => x.HasTarget : x => x.HasVel;
        Func<LadderStep, double> valP = seat ? x => x.MeanRadiusMoa : x => x.MeanMps;
        string pName = seat ? "group MOA" : "MV";
        string pUnitLo = seat ? "MOA" : "m/s";

        var pr = rows.Where(hasP).ToList();
        var po = rows.Where(x => x.HasTarget).ToList();
        (double lo, double hi) PR = pr.Count > 0 ? (pr.Min(valP), pr.Max(valP)) : (0, 1);
        (double lo, double hi) PoR = po.Count > 0 ? (po.Min(x => x.PoiYMoa), po.Max(x => x.PoiYMoa)) : (0, 1);
        PR = Pad(PR); PoR = Pad(PoR);
        float Yp(double v) => mt + ph - (float)((v - PR.lo) / (PR.hi - PR.lo)) * ph;
        float Ypo(double v) => mt + ph - (float)((v - PoR.lo) / (PoR.hi - PoR.lo)) * ph;

        if (_r.BestNode is { } n)
        {
            using var band = new SolidBrush(Color.FromArgb(40, 120, 200, 120));
            g.FillRectangle(band, X(n.Low), mt, X(n.High) - X(n.Low), ph);
        }

        using var axis = new Pen(Color.FromArgb(120, 148, 163, 184), Math.Max(1f, s));
        g.DrawRectangle(axis, ml, mt, pw, ph);

        var ci = CultureInfo.InvariantCulture;
        using var fnt = new Font("Segoe UI", fpt);
        using var tb = new SolidBrush(Color.FromArgb(210, 220, 230));
        foreach (var x in rows)
        {
            g.DrawString(x.X.ToString(seat ? "0.0##" : "0.0", ci), fnt, tb, X(x.X) - 12 * s, mt + ph + 5 * s);
            g.DrawLine(axis, X(x.X), mt + ph, X(x.X), mt + ph + 4 * s);
        }

        if (pr.Count > 1)
        {
            using var p = new Pen(Color.FromArgb(56, 189, 248), penW);
            var pts = pr.Select(x => new PointF(X(x.X), Yp(valP(x)))).ToArray();
            g.DrawLines(p, pts);
            using var dot = new SolidBrush(Color.FromArgb(56, 189, 248));
            foreach (var pt in pts) g.FillEllipse(dot, pt.X - dotR, pt.Y - dotR, dotR * 2, dotR * 2);
            g.DrawString(FormattableString.Invariant($"{pName} {PR.hi:0.0#}"), fnt, tb, 4 * s, mt - 2 * s);
            g.DrawString(FormattableString.Invariant($"{PR.lo:0.0#} {pUnitLo}"), fnt, tb, 4 * s, mt + ph - 12 * s);
        }

        if (po.Count > 1)
        {
            using var p = new Pen(Color.FromArgb(251, 191, 36), penW) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            var pts = po.Select(x => new PointF(X(x.X), Ypo(x.PoiYMoa))).ToArray();
            g.DrawLines(p, pts);
            double grpMax = po.Max(x => x.MeanRadiusMoa);
            using var dot = new SolidBrush(Color.FromArgb(251, 191, 36));
            for (int i = 0; i < po.Count; i++)
            {
                float rad = (3 + 5 * (float)(1 - po[i].MeanRadiusMoa / Math.Max(1e-6, grpMax))) * s;
                g.FillEllipse(dot, pts[i].X - rad, pts[i].Y - rad, rad * 2, rad * 2);
            }
            g.DrawString(FormattableString.Invariant($"POI-Y {PoR.hi:0.0}"), fnt, tb, width - mr + 2 * s, mt - 2 * s);
            g.DrawString(FormattableString.Invariant($"{PoR.lo:0.0} MOA"), fnt, tb, width - mr + 2 * s, mt + ph - 12 * s);
        }

        using var lg = new Font("Segoe UI", fpt, FontStyle.Bold);
        g.DrawString($"- {pName}    - - POI-Y (marker = group, bigger = tighter)", lg, tb, ml, 4 * s);
    }

    private static (double lo, double hi) Pad((double lo, double hi) r)
    {
        if (r.hi - r.lo < 1e-9) return (r.lo - 1, r.hi + 1);
        double p = (r.hi - r.lo) * 0.12;
        return (r.lo - p, r.hi + p);
    }
}
