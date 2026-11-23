using System.Globalization;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The water each case holds, case by case, with the average and the band of one and two standard deviations: a case far out of the band is a case to put
/// aside or to sort apart, and the width of the band is the spread of the volume that goes into the velocity. A picture of the table beside it.
/// </summary>
internal sealed class CaseVolumeChart : ChartBase
{
    private IReadOnlyList<double> _values = Array.Empty<double>();
    private string _unit = "";

    public override bool HasData => _values.Count >= 2;
    protected override string Waiting => Lang.T("Enter the weights of 3 or more cases: their volumes are drawn here.");

    public void SetData(IReadOnlyList<double> values, string unit)
    {
        _values = values;
        _unit = unit;
        Invalidate();
    }

    protected override void Draw(Graphics g, int w, int h, ChartPalette p)
    {
        float s = Math.Max(0.75f, Math.Min(w, h) / 360f);
        using var small = new Font("Segoe UI", 8f * s);
        using var title = new Font("Segoe UI Semibold", 10f * s);
        float left = 50 * s, right = 14 * s, top = 30 * s, bottom = 46 * s;
        Text(g, Lang.T("Water held by each case"), title, p.Text, 10 * s, 6 * s);
        var plot = new RectangleF(left, top, w - left - right, h - top - bottom);
        if (plot.Width < 60 || plot.Height < 40) return;

        int n = _values.Count;
        double mean = _values.Average(), sd = n > 1 ? Math.Sqrt(_values.Sum(v => (v - mean) * (v - mean)) / (n - 1)) : 0;
        double lo = Math.Min(_values.Min(), mean - 2.4 * sd), hi = Math.Max(_values.Max(), mean + 2.4 * sd);
        if (hi - lo <= 0) { lo -= 1; hi += 1; }
        double pad = (hi - lo) * 0.08;
        lo -= pad; hi += pad;
        double step = NiceStep(hi - lo, 5);
        double first = Math.Ceiling(lo / step) * step;
        float Y(double v) => plot.Bottom - (float)((v - lo) / (hi - lo)) * plot.Height;
        float X(int i) => plot.Left + plot.Width * (n == 1 ? 0.5f : (i + 0.5f) / n);

        using (var grid = new Pen(p.Grid))
            for (double v = first; v <= hi; v += step)
            {
                g.DrawLine(grid, plot.Left, Y(v), plot.Right, Y(v));
                Text(g, Num(v, step), small, p.TextDim, plot.Left - 4, Y(v), StringAlignment.Far, StringAlignment.Center);
            }
        Text(g, _unit, small, p.TextDim, 4, top - 14 * s);
        Text(g, Lang.T("case"), small, p.TextDim, plot.Right, plot.Bottom + 18 * s, StringAlignment.Far);

        if (sd > 0)
        {
            using var b2 = new SolidBrush(Color.FromArgb(22, p.Primary));
            using var b1 = new SolidBrush(Color.FromArgb(38, p.Primary));
            g.FillRectangle(b2, plot.Left, Y(mean + 2 * sd), plot.Width, Y(mean - 2 * sd) - Y(mean + 2 * sd));
            g.FillRectangle(b1, plot.Left, Y(mean + sd), plot.Width, Y(mean - sd) - Y(mean + sd));
            Text(g, "+2 SD", small, p.TextDim, plot.Right - 2, Y(mean + 2 * sd) + 1, StringAlignment.Far);
            Text(g, "−2 SD", small, p.TextDim, plot.Right - 2, Y(mean - 2 * sd) - 13 * s, StringAlignment.Far);
        }
        using (var mp = new Pen(p.Accent, 1.8f * s) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) g.DrawLine(mp, plot.Left, Y(mean), plot.Right, Y(mean));

        // the line that joins the cases in the order they were entered
        using (var path = new Pen(Color.FromArgb(70, p.Primary), 1.2f))
            for (int i = 1; i < n; i++) g.DrawLine(path, X(i - 1), Y(_values[i - 1]), X(i), Y(_values[i]));
        for (int i = 0; i < n; i++)
        {
            bool far = sd > 0 && Math.Abs(_values[i] - mean) > 2 * sd;
            var c = far ? Color.FromArgb(248, 113, 113) : p.Primary;
            using var glow = new SolidBrush(Color.FromArgb(55, c));
            using var fill = new SolidBrush(c);
            g.FillEllipse(glow, X(i) - 8 * s, Y(_values[i]) - 8 * s, 16 * s, 16 * s);
            g.FillEllipse(fill, X(i) - 4.5f * s, Y(_values[i]) - 4.5f * s, 9 * s, 9 * s);
            if (n <= 30) Text(g, (i + 1).ToString(CultureInfo.InvariantCulture), small, p.TextDim, X(i), plot.Bottom + 3, StringAlignment.Center);
        }
        string note = string.Format(CultureInfo.InvariantCulture, Lang.T("average {0} {2} · SD {1} {2}"),
            mean.ToString("0.00", CultureInfo.InvariantCulture), sd.ToString("0.00", CultureInfo.InvariantCulture), _unit);
        Text(g, note, small, p.Accent, 10 * s, h - 5 * s, StringAlignment.Near, StringAlignment.Far);
    }
}
