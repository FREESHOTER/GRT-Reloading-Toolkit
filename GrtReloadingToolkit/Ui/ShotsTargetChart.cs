using System.Drawing.Drawing2D;

namespace GrtReloadingToolkit.Ui;

/// <summary>One shot on the chart: where it went from the aim, in the unit of the angle shown (MOA or mrad), its number, and the colour class it belongs to.</summary>
internal sealed record TargetPoint(double X, double Up, string Label, int Class, bool Flagged = false);

/// <summary>
/// The shots on a target with the aim at the centre, on an equal-aspect plane: rings in MOA or milliradians, the shots coloured by series (or by what is behind
/// each one), the average point with an arrow from the aim to it (that arrow is the zero to correct), and the ellipse that holds most of the shots (it shows
/// at a glance whether the spread is round or one-sided). A picture of what the text of the reading says.
/// </summary>
internal sealed class ShotsTargetChart : ChartBase
{
    private IReadOnlyList<TargetPoint> _points = Array.Empty<TargetPoint>();
    private IReadOnlyList<(string Name, Color Color)> _classes = Array.Empty<(string, Color)>();
    private string _unit = "MOA";
    private string _title = "";
    private bool _aimKnown = true;

    public override bool HasData => _points.Count >= 2;
    protected override string Waiting => Lang.T("The picture of the shots appears here once they are read.");

    public void SetData(IReadOnlyList<TargetPoint> points, IReadOnlyList<(string Name, Color Color)> classes, string angleUnit, string title, bool aimKnown = true)
    {
        _points = points;
        _classes = classes;
        _unit = angleUnit;
        _title = title;
        _aimKnown = aimKnown;
        Invalidate();
    }

    protected override void Draw(Graphics g, int w, int h, ChartPalette p)
    {
        float scale = Math.Max(0.8f, Math.Min(w, h) / 560f);
        using var small = new Font("Segoe UI", 8.5f * scale);
        using var mid = new Font("Segoe UI", 9.5f * scale);
        using var title = new Font("Segoe UI Semibold", 11f * scale);

        float top = 30 * scale, legendH = (_classes.Count > 0 ? 40 : 10) * scale, margin = 14 * scale;
        Text(g, _title, title, p.Text, margin, 6 * scale);
        float side = Math.Min(w - 2 * margin, h - top - legendH - margin);
        if (side < 60) return;
        float cx = w / 2f, cy = top + side / 2f;

        double extent = _points.Max(q => Math.Max(Math.Abs(q.X), Math.Abs(q.Up)));
        double step = NiceStep(extent * 1.3 * 2 / 2, 4);
        double radius = Math.Ceiling(extent * 1.15 / step) * step;
        if (radius < step) radius = step;
        float k = (side / 2f) / (float)radius;                       // pixels per unit of angle
        PointF P(double x, double up) => new(cx + (float)x * k, cy - (float)up * k);

        // rings and axes
        using (var gridPen = new Pen(p.Grid, 1f))
        using (var axisPen = new Pen(p.AxisFaint, 1f))
        {
            for (double r = step; r <= radius + 1e-9; r += step)
            {
                float pr = (float)r * k;
                g.DrawEllipse(r >= radius - 1e-9 ? axisPen : gridPen, cx - pr, cy - pr, 2 * pr, 2 * pr);
                Text(g, Num(r, step), small, p.TextDim, cx + pr * 0.7071f + 2, cy - pr * 0.7071f - 12 * scale);
            }
            g.DrawLine(axisPen, cx - side / 2, cy, cx + side / 2, cy);
            g.DrawLine(axisPen, cx, cy - side / 2, cx, cy + side / 2);
        }
        Text(g, _unit, small, p.TextDim, cx + side / 2 - 2, cy + 3, StringAlignment.Far);

        // the aim
        using (var aimPen = new Pen(p.Aim, 1.6f))
        {
            g.DrawEllipse(aimPen, cx - 5, cy - 5, 10, 10);
            g.DrawLine(aimPen, cx - 9, cy, cx + 9, cy);
            g.DrawLine(aimPen, cx, cy - 9, cx, cy + 9);
        }

        // the average and the ellipse of the shots
        double mx = _points.Average(q => q.X), mu = _points.Average(q => q.Up);
        if (_points.Count >= 4)
        {
            double sxx = _points.Sum(q => (q.X - mx) * (q.X - mx)) / (_points.Count - 1), suu = _points.Sum(q => (q.Up - mu) * (q.Up - mu)) / (_points.Count - 1);
            double sxu = _points.Sum(q => (q.X - mx) * (q.Up - mu)) / (_points.Count - 1);
            double tr = sxx + suu, det = sxx * suu - sxu * sxu, disc = Math.Sqrt(Math.Max(0, tr * tr / 4 - det));
            double l1 = tr / 2 + disc, l2 = Math.Max(tr / 2 - disc, 1e-12), ang = 0.5 * Math.Atan2(2 * sxu, sxx - suu);
            double a = 2 * Math.Sqrt(l1), b = 2 * Math.Sqrt(l2);          // two standard deviations along each axis
            var poly = new List<PointF>();
            for (int i = 0; i <= 72; i++)
            {
                double t = i * Math.PI / 36, ex = a * Math.Cos(t), eu = b * Math.Sin(t);
                poly.Add(P(mx + ex * Math.Cos(ang) - eu * Math.Sin(ang), mu + ex * Math.Sin(ang) + eu * Math.Cos(ang)));
            }
            using var fill = new SolidBrush(Color.FromArgb(28, p.Primary));
            using var edge = new Pen(Color.FromArgb(130, p.Primary), 1.4f) { DashStyle = DashStyle.Dash };
            g.FillPolygon(fill, poly.ToArray());
            g.DrawPolygon(edge, poly.ToArray());
        }
        var centre = P(mx, mu);
        double off = Math.Sqrt(mx * mx + mu * mu);
        if (_aimKnown && off > step * 0.12)
        {
            using var arrow = new Pen(p.Accent, 2.2f) { CustomEndCap = new AdjustableArrowCap(4, 5) };
            g.DrawLine(arrow, cx, cy, centre.X, centre.Y);
        }

        // the shots
        foreach (var q in _points.OrderBy(q => q.Flagged))
        {
            var pt = P(q.X, q.Up);
            Color c = _classes.Count > 0 ? _classes[Math.Clamp(q.Class, 0, _classes.Count - 1)].Color : Series[q.Class % Series.Length];
            float r = q.Flagged ? 7.5f * scale : 5.5f * scale;
            using (var glow = new SolidBrush(Color.FromArgb(50, c))) g.FillEllipse(glow, pt.X - r - 3, pt.Y - r - 3, 2 * r + 6, 2 * r + 6);
            using (var fillB = new SolidBrush(c)) g.FillEllipse(fillB, pt.X - r, pt.Y - r, 2 * r, 2 * r);
            using (var edgePen = new Pen(q.Flagged ? p.Text : Color.FromArgb(160, p.Back), q.Flagged ? 1.8f : 1f)) g.DrawEllipse(edgePen, pt.X - r, pt.Y - r, 2 * r, 2 * r);
            if (_points.Count <= 45 && q.Label.Length > 0) Text(g, q.Label, small, p.Text, pt.X + r + 1, pt.Y - r - 9 * scale);
        }
        using (var cb = new SolidBrush(p.Accent)) { g.FillPolygon(cb, new[] { new PointF(centre.X, centre.Y - 7), new PointF(centre.X + 7, centre.Y), new PointF(centre.X, centre.Y + 7), new PointF(centre.X - 7, centre.Y) }); }
        using (var dp = new Pen(p.Back, 1.2f)) g.DrawPolygon(dp, new[] { new PointF(centre.X, centre.Y - 7), new PointF(centre.X + 7, centre.Y), new PointF(centre.X, centre.Y + 7), new PointF(centre.X - 7, centre.Y) });

        // what the average says
        string offText = (mx >= 0 ? "→ " : "← ") + Math.Abs(mx).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "   " + (mu >= 0 ? "↑ " : "↓ ") + Math.Abs(mu).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " " + _unit;
        Text(g, (_aimKnown ? Lang.T("centre of the shots") : Lang.T("centre of the shots (the aim is not known)")) + ":  " + offText, mid, p.Accent, margin, h - legendH - 2 * scale + (legendH > 20 ? 0 : 0), StringAlignment.Near, StringAlignment.Far);

        if (_classes.Count > 0) Legend(g, _classes, small, p.TextDim, margin, h - legendH + 4 * scale, w - 2 * margin);
    }
}
