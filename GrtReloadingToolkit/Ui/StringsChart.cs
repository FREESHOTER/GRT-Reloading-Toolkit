using System.Globalization;

namespace GrtReloadingToolkit.Ui;

/// <summary>One string for the chart: its name, the velocities of its shots in the unit shown, and the shots the statistics flagged as outliers (by position).</summary>
internal sealed record StringPoint(string Label, IReadOnlyList<double> Values, IReadOnlySet<int>? Flagged = null);

/// <summary>
/// Every string side by side: the shots of each as dots, its average as a bar and one standard deviation either side as a band, and a line that joins the
/// averages in the order of the strings (so a ladder shows its flat spot, a long series its drift). The string with the smallest SD is marked. A picture of the
/// table of statistics beside it: the eye sees at once which string is tight, which one is high, and which dot is far from its own.
/// </summary>
internal sealed class StringsChart : ChartBase
{
    private IReadOnlyList<StringPoint> _strings = Array.Empty<StringPoint>();
    private string _unit = "m/s";

    public override bool HasData => _strings.Any(s => s.Values.Count >= 1);
    protected override string Waiting => Lang.T("Add the strings: their shots are drawn here, one column for each string.");

    public void SetData(IReadOnlyList<StringPoint> strings, string velocityUnit)
    {
        _strings = strings;
        _unit = velocityUnit;
        Invalidate();
    }

    private static (double Mean, double Sd) Stats(IReadOnlyList<double> v)
    {
        double mean = v.Average();
        double sd = v.Count > 1 ? Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / (v.Count - 1)) : 0;
        return (mean, sd);
    }

    private static string Short(string label, int max)
    {
        int i = label.IndexOf(" (", StringComparison.Ordinal);
        string s = i > 0 ? label[..i] : label;
        return s.Length <= max ? s : s[..(max - 1)] + "…";
    }

    protected override void Draw(Graphics g, int w, int h, ChartPalette p)
    {
        float s = Math.Max(0.75f, Math.Min(w, h) / 360f);
        using var small = new Font("Segoe UI", 8f * s);
        using var title = new Font("Segoe UI Semibold", 10f * s);
        float left = 50 * s, right = 14 * s, top = 30 * s, bottom = 58 * s;
        Text(g, Lang.T("The shots of each string") + " (" + _unit + ")", title, p.Text, 10 * s, 6 * s);
        var plot = new RectangleF(left, top, w - left - right, h - top - bottom);
        if (plot.Width < 80 || plot.Height < 50) return;

        var shown = _strings.Where(x => x.Values.Count > 0).ToList();
        int n = shown.Count;
        double lo = shown.Min(x => x.Values.Min()), hi = shown.Max(x => x.Values.Max());
        if (hi - lo < 1e-9) { lo -= 1; hi += 1; }
        double pad = (hi - lo) * 0.1;
        lo -= pad; hi += pad;
        double step = NiceStep(hi - lo, 5);
        double first = Math.Ceiling(lo / step) * step;
        float Y(double v) => plot.Bottom - (float)((v - lo) / (hi - lo)) * plot.Height;
        float cell = plot.Width / n;
        float X(int i) => plot.Left + cell * (i + 0.5f);

        using (var grid = new Pen(p.Grid))
            for (double v = first; v <= hi; v += step)
            {
                g.DrawLine(grid, plot.Left, Y(v), plot.Right, Y(v));
                Text(g, Num(v, step), small, p.TextDim, plot.Left - 4, Y(v), StringAlignment.Far, StringAlignment.Center);
            }

        var stats = shown.Select(x => Stats(x.Values)).ToList();
        int best = -1;
        double bestSd = double.MaxValue;
        for (int i = 0; i < n; i++)
            if (shown[i].Values.Count >= 3 && stats[i].Sd < bestSd) { bestSd = stats[i].Sd; best = i; }

        float half = Math.Min(cell * 0.34f, 34 * s);
        for (int i = 0; i < n; i++)
        {
            var color = i == best ? p.Accent : p.Primary;
            var (mean, sd) = stats[i];
            if (sd > 0)
            {
                using var band = new SolidBrush(Color.FromArgb(i == best ? 52 : 34, color));
                g.FillRectangle(band, X(i) - half, Y(mean + sd), 2 * half, Y(mean - sd) - Y(mean + sd));
            }
            using (var bar = new Pen(color, 2.4f * s)) g.DrawLine(bar, X(i) - half, Y(mean), X(i) + half, Y(mean));

            var vals = shown[i].Values;
            for (int k = 0; k < vals.Count; k++)
            {
                // the dots of one string are spread across its column in the order they were shot, so none hides another
                float dx = vals.Count == 1 ? 0 : ((k + 0.5f) / vals.Count - 0.5f) * 2 * half * 0.8f;
                bool flagged = shown[i].Flagged?.Contains(k) == true;
                var c = flagged ? Color.FromArgb(248, 113, 113) : Series[i % Series.Length];
                float r = (flagged ? 4.8f : 3.6f) * s;
                using var glow = new SolidBrush(Color.FromArgb(55, c));
                using var fill = new SolidBrush(c);
                g.FillEllipse(glow, X(i) + dx - r - 2, Y(vals[k]) - r - 2, 2 * r + 4, 2 * r + 4);
                g.FillEllipse(fill, X(i) + dx - r, Y(vals[k]) - r, 2 * r, 2 * r);
            }
            int maxChars = Math.Max(4, (int)(cell / (6.2f * s)));
            Text(g, Short(shown[i].Label, maxChars), small, i == best ? p.Accent : p.TextDim, X(i), plot.Bottom + 4 * s, StringAlignment.Center);
            Text(g, "n " + vals.Count.ToString(CultureInfo.InvariantCulture), small, p.TextDim, X(i), plot.Bottom + 4 * s + small.GetHeight(g), StringAlignment.Center);
        }

        // the averages joined, to read the trend from string to string
        if (n >= 2)
            using (var trend = new Pen(Color.FromArgb(150, p.Accent), 1.6f * s) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
                g.DrawLines(trend, Enumerable.Range(0, n).Select(i => new PointF(X(i), Y(stats[i].Mean))).ToArray());

        string note = best >= 0
            ? string.Format(CultureInfo.InvariantCulture, Lang.T("smallest SD: {0} ({1} {2})"), Short(shown[best].Label, 24), bestSd.ToString("0.0", CultureInfo.InvariantCulture), _unit)
            : Lang.T("band = one standard deviation either side of the average");
        Text(g, note, small, p.Accent, 10 * s, h - 5 * s, StringAlignment.Near, StringAlignment.Far);
    }
}
