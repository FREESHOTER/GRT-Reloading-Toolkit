using GrtOcwAnalyzer.Analysis;

namespace GrtOcwAnalyzer.Ui;

/// <summary>MV and vertical-POI vs charge, with the recommended node shaded.</summary>
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
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        if (_r is null || _r.Rows.Count < 2) return;

        var rows = _r.Rows;
        float ml = 54, mr = 54, mt = 26, mb = 34;
        float pw = Width - ml - mr, ph = Height - mt - mb;
        if (pw < 40 || ph < 40) return;

        double cMin = rows.Min(x => x.ChargeGr), cMax = rows.Max(x => x.ChargeGr);
        float X(double c) => ml + (float)((c - cMin) / Math.Max(1e-9, cMax - cMin)) * pw;

        var mv = rows.Where(x => x.HasVel).ToList();
        var po = rows.Where(x => x.HasTarget).ToList();
        (double lo, double hi) MvR = mv.Count > 0 ? (mv.Min(x => x.MeanMps), mv.Max(x => x.MeanMps)) : (0, 1);
        (double lo, double hi) PoR = po.Count > 0 ? (po.Min(x => x.PoiYMoa), po.Max(x => x.PoiYMoa)) : (0, 1);
        MvR = Pad(MvR); PoR = Pad(PoR);
        float Ymv(double v) => mt + ph - (float)((v - MvR.lo) / (MvR.hi - MvR.lo)) * ph;
        float Ypo(double v) => mt + ph - (float)((v - PoR.lo) / (PoR.hi - PoR.lo)) * ph;

        // node band
        if (_r.BestNode is { } n)
        {
            using var band = new SolidBrush(Color.FromArgb(40, 120, 200, 120));
            g.FillRectangle(band, X(n.LowGr), mt, X(n.HighGr) - X(n.LowGr), ph);
        }

        using var axis = new Pen(Color.FromArgb(120, 148, 163, 184));
        g.DrawRectangle(axis, ml, mt, pw, ph);

        using var fnt = new Font("Segoe UI", 7.5f);
        using var tb = new SolidBrush(Color.FromArgb(210, 220, 230));
        foreach (var x in rows)
        {
            g.DrawString(x.ChargeGr.ToString("0.0"), fnt, tb, X(x.ChargeGr) - 10, mt + ph + 4);
            g.DrawLine(axis, X(x.ChargeGr), mt + ph, X(x.ChargeGr), mt + ph + 3);
        }

        // MV (cyan)
        if (mv.Count > 1)
        {
            using var p = new Pen(Color.FromArgb(56, 189, 248), 2f);
            var pts = mv.Select(x => new PointF(X(x.ChargeGr), Ymv(x.MeanMps))).ToArray();
            g.DrawLines(p, pts);
            using var dot = new SolidBrush(Color.FromArgb(56, 189, 248));
            foreach (var pt in pts) g.FillEllipse(dot, pt.X - 3, pt.Y - 3, 6, 6);
            g.DrawString($"MV {MvR.hi:0}", fnt, tb, 4, mt - 2);
            g.DrawString($"{MvR.lo:0} m/s", fnt, tb, 4, mt + ph - 10);
        }

        // POI-Y (amber) — bigger markers where group is tighter
        if (po.Count > 1)
        {
            using var p = new Pen(Color.FromArgb(251, 191, 36), 2f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            var pts = po.Select(x => new PointF(X(x.ChargeGr), Ypo(x.PoiYMoa))).ToArray();
            g.DrawLines(p, pts);
            double grpMax = po.Max(x => x.MeanRadiusMoa);
            using var dot = new SolidBrush(Color.FromArgb(251, 191, 36));
            for (int i = 0; i < po.Count; i++)
            {
                float rad = 3 + 5 * (float)(1 - po[i].MeanRadiusMoa / Math.Max(1e-6, grpMax)); // tighter group => bigger dot
                g.FillEllipse(dot, pts[i].X - rad, pts[i].Y - rad, rad * 2, rad * 2);
            }
            g.DrawString($"POI-Y {PoR.hi:0.0}", fnt, tb, Width - mr + 2, mt - 2);
            g.DrawString($"{PoR.lo:0.0} MOA", fnt, tb, Width - mr + 2, mt + ph - 10);
        }

        // legend
        using var lg = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        g.DrawString("— MV    - - POI-Y (marker = group, bigger = tighter)", lg, tb, ml, 4);
    }

    private static (double lo, double hi) Pad((double lo, double hi) r)
    {
        if (r.hi - r.lo < 1e-9) return (r.lo - 1, r.hi + 1);
        double p = (r.hi - r.lo) * 0.12;
        return (r.lo - p, r.hi + p);
    }
}
