using System.Globalization;

namespace GrtReloadingToolkit.Ui;

/// <summary>One bar of the ranking: the charge (and what it is loaded with), its score out of 10 or none when too little was shot, and the figures behind it.</summary>
internal sealed record RankBar(string Label, double? Score, string Detail);

/// <summary>
/// The charges tested at one distance, best first, as bars of their score out of 10 (green from 8, amber from 6, red below): the table says which
/// charge is first, the picture says by how much, and how close the second one is. A charge with too few shots has an empty bar, not a zero.
/// </summary>
internal sealed class RankChart : ChartBase
{
    private IReadOnlyList<RankBar> _bars = Array.Empty<RankBar>();

    public override bool HasData => _bars.Count > 0;
    protected override string Waiting => Lang.T("The ranking of the charges is drawn here once there are rounds logged at this distance.");

    public void SetData(IReadOnlyList<RankBar> bars)
    {
        _bars = bars;
        Invalidate();
    }

    private static Color Rating(double score) => score >= 8 ? Color.FromArgb(52, 211, 153) : score >= 6 ? Color.FromArgb(251, 191, 36) : Color.FromArgb(248, 113, 113);

    protected override void Draw(Graphics g, int w, int h, ChartPalette p)
    {
        float s = Math.Max(0.75f, Math.Min(w, h) / 330f);
        using var small = new Font("Segoe UI", 8f * s);
        using var bold = new Font("Segoe UI Semibold", 8.5f * s);
        using var title = new Font("Segoe UI Semibold", 10f * s);
        float left = 110 * s, right = 150 * s, top = 28 * s, bottom = 22 * s;
        Text(g, Lang.T("Score of each charge (out of 10)"), title, p.Text, 10 * s, 5 * s);
        var plot = new RectangleF(left, top, w - left - right, h - top - bottom);
        if (plot.Width < 60 || plot.Height < 30) return;

        float X(double v) => plot.Left + (float)(v / 10.0) * plot.Width;
        using (var grid = new Pen(p.Grid))
            for (int v = 0; v <= 10; v += 2)
            {
                g.DrawLine(grid, X(v), plot.Top, X(v), plot.Bottom);
                Text(g, v.ToString(CultureInfo.InvariantCulture), small, p.TextDim, X(v), plot.Bottom + 3 * s, StringAlignment.Center);
            }
        float rowH = plot.Height / _bars.Count, barH = Math.Min(rowH * 0.62f, 22 * s);
        for (int i = 0; i < _bars.Count; i++)
        {
            var b = _bars[i];
            float cy = plot.Top + rowH * (i + 0.5f);
            Text(g, b.Label, i == 0 ? bold : small, i == 0 ? p.Accent : p.Text, left - 6 * s, cy, StringAlignment.Far, StringAlignment.Center);
            var track = new RectangleF(plot.Left, cy - barH / 2, plot.Width, barH);
            using (var tb = new SolidBrush(Color.FromArgb(24, p.TextDim))) g.FillRectangle(tb, track);
            if (b.Score is { } sc)
            {
                var c = Rating(sc);
                var bar = new RectangleF(plot.Left, cy - barH / 2, Math.Max(2, X(Math.Clamp(sc, 0, 10)) - plot.Left), barH);
                using (var fb = new SolidBrush(Color.FromArgb(i == 0 ? 235 : 170, c))) g.FillRectangle(fb, bar);
                Text(g, sc.ToString("0.0", CultureInfo.InvariantCulture), bold, p.Back, bar.Right - 4 * s, cy, StringAlignment.Far, StringAlignment.Center);
            }
            Text(g, b.Detail, small, p.TextDim, plot.Right + 8 * s, cy, StringAlignment.Near, StringAlignment.Center);
        }
    }
}
