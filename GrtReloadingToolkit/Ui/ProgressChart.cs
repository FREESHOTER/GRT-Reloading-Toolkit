using System.Globalization;
using GrtReloadingToolkit.Cartridges;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// What the latest training session taught the model, as pairs of bars: each figure before the session (grey-blue) and with it (amber). The unexplained SD and
/// the SD expected of the match lot should go down, the leave-one-out link and the number of proven measures should go up; every figure has a scale of its own,
/// so a pair is read against itself.
/// </summary>
internal sealed class ProgressChart : ChartBase
{
    private ProgressReport? _r;

    public override bool HasData => _r != null;
    protected override string Waiting => Lang.T("Press \"What the training taught\": the model before and after the latest session is drawn here.");

    public void SetData(ProgressReport? r)
    {
        _r = r;
        Invalidate();
    }

    protected override void Draw(Graphics g, int w, int h, ChartPalette p)
    {
        var r = _r!;
        float s = Math.Max(0.75f, Math.Min(w, h) / 220f);
        using var small = new Font("Segoe UI", 8f * s);
        using var bold = new Font("Segoe UI Semibold", 8.5f * s);
        using var title = new Font("Segoe UI Semibold", 10f * s);
        var ci = CultureInfo.InvariantCulture;
        var before = Color.FromArgb(125, 160, 200);
        var after = p.Accent;

        Text(g, string.Format(ci, Lang.T("What the session of {0} taught"), r.LatestDate.Length > 0 ? r.LatestDate : "?"), title, p.Text, 10 * s, 5 * s);

        // each row: a name, the value before and the value with the session, whether lower is better
        var rows = new List<(string Name, double? Before, double? After, string Format, bool LowerIsBetter)>
        {
            (Lang.T("SD left unexplained (m/s)"), r.Before?.SdResidual, r.After.SdResidual, "0.00", true),
            (Lang.T("proven measures"), r.Before?.Proven.Count, r.After.Proven.Count, "0", false),
            (Lang.T("leave-one-out link"), r.Before?.LeaveOneOutR, r.After.LeaveOneOutR, "0.00", false),
        };
        if (r.LotAfter != null) rows.Add((Lang.T("SD expected of the match lot (m/s)"), r.LotBefore?.SdAtRandom, r.LotAfter.SdAtRandom, "0.00", true));

        float left = 215 * s, right = 56 * s, top = 28 * s, bottom = 30 * s;
        var area = new RectangleF(left, top, w - left - right, h - top - bottom);
        if (area.Width < 60 || area.Height < 40) return;
        float rowH = area.Height / rows.Count, barH = Math.Min(rowH * 0.34f, 14 * s);
        for (int i = 0; i < rows.Count; i++)
        {
            var (name, b0, a0, fmt, lower) = rows[i];
            float cy = area.Top + rowH * (i + 0.5f);
            Text(g, Fit(g, name, bold, left - 16 * s), bold, p.Text, left - 8 * s, cy, StringAlignment.Far, StringAlignment.Center);
            double max = Math.Max(Math.Max(b0 ?? 0, a0 ?? 0) * 1.15, 1e-6);
            void Bar(double? v, float y, Color c, string tag)
            {
                if (v is not { } val) { Text(g, tag + " —", small, p.TextDim, area.Left + 4 * s, y, StringAlignment.Near, StringAlignment.Center); return; }
                float bw = Math.Max(2, (float)(Math.Max(val, 0) / max) * area.Width);
                using (var fill = new SolidBrush(Color.FromArgb(220, c))) g.FillRectangle(fill, area.Left, y - barH / 2, bw, barH);
                Text(g, val.ToString(fmt, ci), small, p.Text, area.Left + bw + 5 * s, y, StringAlignment.Near, StringAlignment.Center);
            }
            Bar(b0, cy - barH * 0.62f, before, Lang.T("before"));
            Bar(a0, cy + barH * 0.62f, after, Lang.T("with"));
            if (b0 is { } bv && a0 is { } av && Math.Abs(av - bv) > 1e-9)
            {
                bool better = lower ? av < bv : av > bv;
                Text(g, better ? "▲" : "▼", bold, better ? Color.FromArgb(52, 211, 153) : Color.FromArgb(248, 113, 113), w - 10 * s, cy, StringAlignment.Far, StringAlignment.Center);
            }
        }
        Legend(g, new[] { (Lang.T("before the session"), before), (Lang.T("with the session"), after) }, small, p.TextDim, 10 * s, h - 20 * s, w - 20 * s);
    }
}
