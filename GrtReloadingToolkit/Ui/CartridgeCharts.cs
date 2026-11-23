using System.Drawing.Drawing2D;
using System.Globalization;

namespace GrtReloadingToolkit.Ui;

internal sealed record ModelPoint(double X, double Y, int Series);

/// <summary>
/// The measure that moves the velocity the most, against the velocity, each series with the mean of its own taken out (so a warmer day or another barrel cannot
/// pass for the measure): the dots are the shots, the line the slope the data give. A picture of why the model believes in a measure.
/// </summary>
internal sealed class ModelChart : ChartBase
{
    private IReadOnlyList<ModelPoint> _points = Array.Empty<ModelPoint>();
    private string _measure = "", _unit = "";
    private double _modelSlope;

    public override bool HasData => _points.Count >= 4;
    protected override string Waiting => Lang.T("Press \"Learn from the archive\": the measure that moves the velocity is drawn here.");

    public void SetData(IReadOnlyList<ModelPoint> points, string measure, string unit, double modelSlope)
    {
        _points = points;
        _measure = measure;
        _unit = unit;
        _modelSlope = modelSlope;
        Invalidate();
    }

    protected override void Draw(Graphics g, int w, int h, ChartPalette p)
    {
        float s = Math.Max(0.75f, Math.Min(w, h) / 420f);
        using var small = new Font("Segoe UI", 8f * s);
        using var title = new Font("Segoe UI Semibold", 10f * s);
        float left = 46 * s, right = 14 * s, top = 30 * s, bottom = 56 * s;
        Text(g, Lang.T("What moves the velocity:") + " " + _measure, title, p.Text, 10 * s, 6 * s);
        var plot = new RectangleF(left, top, w - left - right, h - top - bottom);
        if (plot.Width < 60 || plot.Height < 50) return;

        double xr = _points.Max(q => Math.Abs(q.X)) * 1.15, yr = _points.Max(q => Math.Abs(q.Y)) * 1.15;
        if (xr <= 0) xr = 1;
        if (yr <= 0) yr = 1;
        double xs = NiceStep(xr, 3), ys = NiceStep(yr, 3);
        xr = Math.Ceiling(xr / xs) * xs;
        yr = Math.Ceiling(yr / ys) * ys;
        PointF P(double x, double y) => new(plot.Left + (float)((x + xr) / (2 * xr)) * plot.Width, plot.Bottom - (float)((y + yr) / (2 * yr)) * plot.Height);

        using (var grid = new Pen(p.Grid))
        using (var axis = new Pen(p.AxisFaint))
        {
            for (double v = -xr; v <= xr + 1e-9; v += xs) { g.DrawLine(Math.Abs(v) < 1e-9 ? axis : grid, P(v, -yr), P(v, yr)); Text(g, Num(v, xs), small, p.TextDim, P(v, -yr).X, plot.Bottom + 3, StringAlignment.Center); }
            for (double v = -yr; v <= yr + 1e-9; v += ys) { g.DrawLine(Math.Abs(v) < 1e-9 ? axis : grid, P(-xr, v), P(xr, v)); Text(g, Num(v, ys), small, p.TextDim, plot.Left - 4, P(-xr, v).Y, StringAlignment.Far, StringAlignment.Center); }
        }
        Text(g, _measure + (_unit.Length > 0 ? " (" + _unit + ")" : "") + " — " + Lang.T("less or more than the series average"), small, p.TextDim, plot.Left + plot.Width / 2, plot.Bottom + 18 * s, StringAlignment.Center);
        Text(g, "m/s", small, p.TextDim, 4, top - 14 * s);

        // the slope the data give, through the point where both are at their series' average
        double sxx = _points.Sum(q => q.X * q.X), sxy = _points.Sum(q => q.X * q.Y);
        double slope = sxx > 0 ? sxy / sxx : 0;
        double syy = _points.Sum(q => q.Y * q.Y), r = sxx > 0 && syy > 0 ? sxy / Math.Sqrt(sxx * syy) : 0;
        double x1 = -xr, x2 = xr;
        double y1 = slope * x1, y2 = slope * x2;
        if (Math.Abs(y1) > yr) { x1 = Math.Sign(x1) * yr / Math.Abs(slope); y1 = slope * x1; }
        if (Math.Abs(y2) > yr) { x2 = Math.Sign(x2) * yr / Math.Abs(slope); y2 = slope * x2; }
        using (var band = new Pen(Color.FromArgb(40, p.Accent), 14 * s)) g.DrawLine(band, P(x1, y1), P(x2, y2));
        using (var line = new Pen(p.Accent, 2.2f * s)) g.DrawLine(line, P(x1, y1), P(x2, y2));

        int nSeries = _points.Select(q => q.Series).Distinct().Count();
        foreach (var q in _points)
        {
            var pt = P(q.X, q.Y);
            var c = Series[Math.Abs(q.Series) % Series.Length];
            using var glow = new SolidBrush(Color.FromArgb(60, c));
            using var fill = new SolidBrush(c);
            g.FillEllipse(glow, pt.X - 7 * s, pt.Y - 7 * s, 14 * s, 14 * s);
            g.FillEllipse(fill, pt.X - 4 * s, pt.Y - 4 * s, 8 * s, 8 * s);
        }
        string note = string.Format(CultureInfo.InvariantCulture, Lang.T("{0} shots in {1} series · link {2} · the model: {3} m/s per {4}"), _points.Count, nSeries,
            r.ToString("+0.00;-0.00", CultureInfo.InvariantCulture), _modelSlope.ToString("+0.00;-0.00", CultureInfo.InvariantCulture), _unit.Length > 0 ? _unit : Lang.T("unit"));
        Text(g, note, small, p.Accent, 10 * s, h - 6 * s, StringAlignment.Near, StringAlignment.Far);
    }
}

internal sealed record PlanLane(string Name, IReadOnlyList<(double Estimate, string Label)> Items, double PredictedSd);

/// <summary>
/// The plan of a race or of a training, group by group: each cartridge a dot at the velocity the model expects of it (from the median cartridge of the lot), the
/// groups one under the other. A tight lane is a group that should shoot a small SD; a lane far from the others is a group that shoots at another velocity.
/// </summary>
internal sealed class PlanChart : ChartBase
{
    private IReadOnlyList<PlanLane> _lanes = Array.Empty<PlanLane>();
    private double _randomSd;

    public override bool HasData => _lanes.Count > 0 && _lanes.Any(l => l.Items.Count > 0);
    protected override string Waiting => Lang.T("Compute the plan: the groups of cartridges are drawn here.");

    public void SetData(IReadOnlyList<PlanLane> lanes, double randomSd)
    {
        _lanes = lanes;
        _randomSd = randomSd;
        Invalidate();
    }

    protected override void Draw(Graphics g, int w, int h, ChartPalette p)
    {
        float s = Math.Max(0.75f, Math.Min(w, h) / 380f);
        using var small = new Font("Segoe UI", 8f * s);
        using var title = new Font("Segoe UI Semibold", 10f * s);
        using var bold = new Font("Segoe UI Semibold", 8.5f * s);
        float left = 86 * s, right = 70 * s, top = 30 * s, bottom = 44 * s;
        Text(g, Lang.T("The cartridges of each group, by expected velocity"), title, p.Text, 10 * s, 6 * s);
        var plot = new RectangleF(left, top, w - left - right, h - top - bottom);
        if (plot.Width < 60 || plot.Height < 40) return;

        double all = _lanes.SelectMany(l => l.Items).Max(i => Math.Abs(i.Estimate)) * 1.1;
        if (all <= 0) all = 1;
        double step = NiceStep(all * 2, 5);
        double range = Math.Ceiling(all / step) * step;
        float X(double v) => plot.Left + (float)((v + range) / (2 * range)) * plot.Width;

        using (var grid = new Pen(p.Grid))
        using (var zero = new Pen(p.AxisFaint) { DashStyle = DashStyle.Dash })
        {
            for (double v = -range; v <= range + 1e-9; v += step)
            {
                g.DrawLine(Math.Abs(v) < 1e-9 ? zero : grid, X(v), plot.Top, X(v), plot.Bottom);
                Text(g, Num(v, step), small, p.TextDim, X(v), plot.Bottom + 3, StringAlignment.Center);
            }
        }
        Text(g, Lang.T("m/s from the median cartridge"), small, p.TextDim, plot.Left + plot.Width / 2, plot.Bottom + 17 * s, StringAlignment.Center);

        float laneH = plot.Height / _lanes.Count;
        for (int i = 0; i < _lanes.Count; i++)
        {
            var lane = _lanes[i];
            float cy = plot.Top + laneH * (i + 0.5f);
            var color = Series[i % Series.Length];
            if (lane.Items.Count > 0)
            {
                double lo = lane.Items.Min(t => t.Estimate), hi = lane.Items.Max(t => t.Estimate);
                using var band = new SolidBrush(Color.FromArgb(36, color));
                using var edge = new Pen(Color.FromArgb(120, color), 1.2f);
                var rect = new RectangleF(X(lo) - 6 * s, cy - laneH * 0.32f, X(hi) - X(lo) + 12 * s, laneH * 0.64f);
                using var path = HomeColors.Round(rect, 6 * s);
                g.FillPath(band, path);
                g.DrawPath(edge, path);
                // the dots of a lane that fall on one another are spread up and down a little so that each is seen
                var placed = new List<(float X, float Y)>();
                foreach (var it in lane.Items.OrderBy(t => t.Estimate))
                {
                    float x = X(it.Estimate), y = cy;
                    int stack = placed.Count(q => Math.Abs(q.X - x) < 7 * s);
                    y += (stack % 2 == 0 ? 1 : -1) * ((stack + 1) / 2) * 6 * s;
                    placed.Add((x, y));
                    using var fill = new SolidBrush(color);
                    g.FillEllipse(fill, x - 4 * s, y - 4 * s, 8 * s, 8 * s);
                }
            }
            Text(g, lane.Name, bold, p.Text, 8 * s, cy, StringAlignment.Near, StringAlignment.Center);
            Text(g, "SD " + lane.PredictedSd.ToString("0.00", CultureInfo.InvariantCulture), small, color, w - right + 8 * s, cy, StringAlignment.Near, StringAlignment.Center);
        }
        if (_randomSd > 0)
            Text(g, string.Format(CultureInfo.InvariantCulture, Lang.T("shot in random order: SD {0} m/s"), _randomSd.ToString("0.00", CultureInfo.InvariantCulture)), small, p.Accent, 10 * s, h - 5 * s, StringAlignment.Near, StringAlignment.Far);
    }
}
