using System.Globalization;
using GrtReloadingToolkit.Cartridges;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The SD budget as a picture. Above, one bar for each item that makes up the SD (the change of velocity between series, the movement inside a series, each
/// measure of the cartridges that is proven, the chronograph, the rest), as its share of the variance, solid when the item is proven and pale when it may be
/// chance. Below, the series themselves: the mean of each with one standard deviation either side and the overall mean, so that "the change from one series to the
/// next" can be seen as the dots that do not line up.
/// </summary>
internal sealed class BudgetChart : ChartBase
{
    private VarianceBudgetResult? _b;

    public override bool HasData => _b != null;
    protected override string Waiting => Lang.T("Pick the strings of a session and compute the budget: where the SD comes from is drawn here.");

    public void SetData(VarianceBudgetResult? b)
    {
        _b = b;
        Invalidate();
    }

    private static Color KindColor(BudgetPart p, int index) => p.Kind switch
    {
        "between" => Series[0],
        "trend" => Series[1],
        "chrono" => Color.FromArgb(148, 163, 184),
        "rest" => Series[3],
        _ => Series[(2 + index) % Series.Length],
    };


    protected override void Draw(Graphics g, int w, int h, ChartPalette p)
    {
        var b = _b!;
        float s = Math.Max(0.75f, Math.Min(w, h) / 400f);
        using var small = new Font("Segoe UI", 8f * s);
        using var title = new Font("Segoe UI Semibold", 10f * s);
        var ci = CultureInfo.InvariantCulture;

        float top = 8 * s, bottomNote = 22 * s;
        float partsH = (h - top - bottomNote) * 0.56f;
        Text(g, Lang.T("Where the SD comes from"), title, p.Text, 10 * s, top);

        // ---- the items of the budget ----
        float left = 250 * s, right = 128 * s, y0 = top + 24 * s;
        var area = new RectangleF(left, y0, w - left - right, partsH - 24 * s);
        if (area.Width < 60 || area.Height < 30) return;
        double maxShare = Math.Max(0.05, b.Parts.Max(x => x.Share));
        float rowH = area.Height / b.Parts.Count, barH = Math.Min(rowH * 0.64f, 20 * s);
        int measureIndex = 0;
        for (int i = 0; i < b.Parts.Count; i++)
        {
            var part = b.Parts[i];
            float cy = area.Top + rowH * (i + 0.5f);
            var color = KindColor(part, part.Kind == "measure" ? measureIndex++ : 0);
            Text(g, Fit(g, CartridgeText.PartLabel(part), small, left - 14 * s), small, p.Text, left - 6 * s, cy, StringAlignment.Far, StringAlignment.Center);
            using (var track = new SolidBrush(Color.FromArgb(22, p.TextDim))) g.FillRectangle(track, area.Left, cy - barH / 2, area.Width, barH);
            float bw = Math.Max(2, (float)(part.Share / maxShare) * area.Width);
            using (var fill = new SolidBrush(Color.FromArgb(part.Proven || part.P is null ? 235 : 110, color))) g.FillRectangle(fill, area.Left, cy - barH / 2, bw, barH);
            string proof = part.P is null ? "" : part.Proven ? "  ✓" : "  ?";
            Text(g, (part.Share * 100).ToString("0", ci) + " %  ·  " + part.SdMps.ToString("0.0#", ci) + " m/s" + proof, small, part.Proven ? p.Accent : p.TextDim,
                area.Right + 8 * s, cy, StringAlignment.Near, StringAlignment.Center);
        }

        // ---- the series ----
        float sy = top + partsH + 6 * s;
        Text(g, Lang.T("The series: mean and one SD either side"), title, p.Text, 10 * s, sy);
        var sp = new RectangleF(52 * s, sy + 26 * s, w - 52 * s - 16 * s, h - bottomNote - (sy + 26 * s) - 16 * s);
        if (sp.Width > 60 && sp.Height > 24 && b.Series.Count > 0)
        {
            double lo = b.Series.Min(x => x.Mean - Math.Max(x.Sd, 0.2)), hi = b.Series.Max(x => x.Mean + Math.Max(x.Sd, 0.2));
            double pad = (hi - lo) * 0.15;
            lo -= pad; hi += pad;
            double step = NiceStep(hi - lo, 3);
            float Y(double v) => sp.Bottom - (float)((v - lo) / (hi - lo)) * sp.Height;
            float X(int i) => sp.Left + sp.Width * (i + 0.5f) / b.Series.Count;
            using (var grid = new Pen(p.Grid))
                for (double v = Math.Ceiling(lo / step) * step; v <= hi; v += step)
                {
                    g.DrawLine(grid, sp.Left, Y(v), sp.Right, Y(v));
                    Text(g, Num(v, step), small, p.TextDim, sp.Left - 4 * s, Y(v), StringAlignment.Far, StringAlignment.Center);
                }
            using (var mp = new Pen(Color.FromArgb(170, p.Accent), 1.5f * s) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) g.DrawLine(mp, sp.Left, Y(b.Mean), sp.Right, Y(b.Mean));
            for (int i = 0; i < b.Series.Count; i++)
            {
                var r = b.Series[i];
                var c = Series[i % Series.Length];
                using (var ep = new Pen(Color.FromArgb(200, c), 2.2f * s)) g.DrawLine(ep, X(i), Y(r.Mean - r.Sd), X(i), Y(r.Mean + r.Sd));
                using (var cap = new Pen(c, 2f * s)) { g.DrawLine(cap, X(i) - 4 * s, Y(r.Mean - r.Sd), X(i) + 4 * s, Y(r.Mean - r.Sd)); g.DrawLine(cap, X(i) - 4 * s, Y(r.Mean + r.Sd), X(i) + 4 * s, Y(r.Mean + r.Sd)); }
                using (var dot = new SolidBrush(c)) g.FillEllipse(dot, X(i) - 4.5f * s, Y(r.Mean) - 4.5f * s, 9 * s, 9 * s);
                Text(g, (i + 1).ToString(ci), small, p.TextDim, X(i), sp.Bottom + 3 * s, StringAlignment.Center);
            }
            Text(g, "m/s", small, p.TextDim, 4, sp.Top - 12 * s);
        }

        Text(g, string.Format(ci, Lang.T("SD of all the shots {0} → inside the series {1} → what is left {2} m/s"), b.SdTotal.ToString("0.00", ci), b.SdWithin.ToString("0.00", ci), b.SdRest.ToString("0.00", ci)),
            small, p.Accent, 10 * s, h - 5 * s, StringAlignment.Near, StringAlignment.Far);
    }
}
