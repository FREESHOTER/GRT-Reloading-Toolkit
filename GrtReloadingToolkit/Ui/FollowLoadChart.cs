using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using GrtPluginKit.Grt;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

internal enum FollowChartMode { MeanOverTime, SpreadOverTime, VelocityVsTemperature, GroupOverTime }

/// <summary>
/// What one load did, outing after outing: the mean velocity with its spread and every shot, the SD and ES, the velocity against
/// the temperature of the day, the group. Same look as the other charts (dark navy, Segoe UI 8, the plugin's blue and amber); the
/// control limits on the mean are the ones the analysis uses for "is this outing normal" (individuals chart, moving range).
/// </summary>
internal sealed class FollowLoadChart : Panel
{
    private FollowUpResult? _result;
    private FollowChartMode _mode;
    private readonly GrtUnits _u = GrtUnits.Current;

    private static readonly Color Blue = Color.FromArgb(56, 189, 248), Amber = Color.FromArgb(251, 191, 36);
    private static readonly Color Ink = Color.FromArgb(210, 220, 230), Axis = Color.FromArgb(120, 148, 163, 184);

    public FollowLoadChart()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(18, 24, 36);
    }

    public void SetData(FollowUpResult? result, FollowChartMode mode) { _result = result; _mode = mode; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e) { base.OnPaint(e); Render(e.Graphics, Width, Height, _mode); }

    /// <summary>The chart in <paramref name="mode"/> as a stand-alone PNG for the GRT gallery; null when there is nothing to draw.</summary>
    public byte[]? RenderPng(FollowChartMode mode, int width = 900, int height = 420)
    {
        if (_result is null || _result.Sessions.Count == 0) return null;
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) Render(g, width, height, mode);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private readonly record struct Pt(double X, double Y);

    private void Render(Graphics g, int width, int height, FollowChartMode mode)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        using var tb = new SolidBrush(Ink);
        using var fnt = new Font("Segoe UI", 8f);
        var ci = CultureInfo.InvariantCulture;

        var used = _result?.Sessions.Where(s => s.Stats.N >= LoadFollowUp.MinShots).ToList() ?? new List<SessionStat>();
        if (used.Count == 0)
        {
            g.DrawString("Registra uscite con i loro colpi per vedere il carico nel tempo.", fnt, tb, 10, 10);
            return;
        }

        double t0 = used[0].Session.Date.ToOADate();
        bool byTemp = mode == FollowChartMode.VelocityVsTemperature;
        double X(SessionStat s) => byTemp ? _u.TemperatureValue(s.Session.TempC ?? double.NaN) : s.Session.Date.ToOADate() - t0;

        // series: (points, colour, label)
        var series = new List<(List<Pt> Pts, Color Col, string Label, bool Line)>();
        var shotDots = new List<Pt>();
        string yTitle;
        switch (mode)
        {
            case FollowChartMode.SpreadOverTime:
                series.Add((used.Select(s => new Pt(X(s), _u.VelocityValue(s.Stats.Sd))).ToList(), Blue, "SD", true));
                series.Add((used.Select(s => new Pt(X(s), _u.VelocityValue(s.Stats.Es))).ToList(), Amber, "ES", true));
                yTitle = _u.VelocityUnitName;
                break;
            case FollowChartMode.GroupOverTime:
                series.Add((used.Where(s => s.Session.GroupMoa is not null).Select(s => new Pt(X(s), s.Session.GroupMoa!.Value)).ToList(), Blue, "MOA", true));
                yTitle = "MOA";
                break;
            default:
                var means = used.Where(s => !double.IsNaN(X(s))).Select(s => new Pt(X(s), _u.VelocityValue(s.Stats.Mean))).ToList();
                series.Add((means, Blue, "media", !byTemp));
                foreach (var s in used.Where(s => !double.IsNaN(X(s))))
                    foreach (double v in s.Session.VelocitiesMps.Where(v => v > 0)) shotDots.Add(new Pt(X(s), _u.VelocityValue(v)));
                yTitle = _u.VelocityUnitName;
                break;
        }

        var all = series.SelectMany(s => s.Pts).Concat(shotDots).ToList();
        if (all.Count == 0)
        {
            g.DrawString(byTemp ? "Nessuna uscita ha una temperatura scritta." : "Ancora niente da disegnare per questa vista.", fnt, tb, 10, 10);
            return;
        }

        float ml = 58, mr = 18, mt = 22, mb = 34;
        float pw = width - ml - mr, ph = height - mt - mb;
        if (pw < 60 || ph < 40) return;

        var (x0, x1) = Range(all.Min(p => p.X), all.Max(p => p.X), 0.04);
        var (y0, y1) = Range(all.Min(p => p.Y), all.Max(p => p.Y), 0.12);
        float Px(double x) => ml + (float)((x - x0) / (x1 - x0)) * pw;
        float Py(double y) => mt + ph - (float)((y - y0) / (y1 - y0)) * ph;

        using var axisPen = new Pen(Axis);
        using var gridPen = new Pen(Color.FromArgb(40, 148, 163, 184));
        g.DrawRectangle(axisPen, ml, mt, pw, ph);
        for (int i = 0; i <= 4; i++)
        {
            double y = y0 + (y1 - y0) * i / 4;
            g.DrawLine(gridPen, ml, Py(y), ml + pw, Py(y));
            g.DrawString(y.ToString(Math.Abs(y1 - y0) < 1.2 ? "0.00" : Math.Abs(y1 - y0) < 6 ? "0.0" : "0", ci), fnt, tb, 4, Py(y) - 7);
        }
        g.DrawString(yTitle, fnt, tb, ml, 3);

        // x axis: dates at both ends and the middle, or temperatures every quarter
        if (byTemp)
            for (int i = 0; i <= 4; i++)
            {
                double x = x0 + (x1 - x0) * i / 4;
                g.DrawString(x.ToString("0", ci), fnt, tb, Px(x) - 8, mt + ph + 4);
            }
        else
            foreach (double frac in new[] { 0.0, 0.5, 1.0 })
            {
                double x = x0 + (x1 - x0) * frac;
                string label = DateTime.FromOADate(t0 + x).ToString("yyyy-MM-dd", ci);
                g.DrawString(label, fnt, tb, Math.Min(Px(x) - 28, width - 66), mt + ph + 4);
            }
        if (byTemp) g.DrawString(_u.TemperatureUnitName, fnt, tb, ml + pw - 22, mt + ph + 18);

        // control limits for the mean over time (individuals chart on the session means)
        if (mode == FollowChartMode.MeanOverTime && used.Count >= LoadFollowUp.MinBaseline)
        {
            var (centre, sigma) = LoadFollowUp.MeanControl(used);
            using var dash = new Pen(Color.FromArgb(150, Amber)) { DashStyle = DashStyle.Dash };
            using var mid = new Pen(Color.FromArgb(120, Ink));
            foreach (double v in new[] { centre + 3 * sigma, centre - 3 * sigma })
            {
                float y = Py(_u.VelocityValue(v));
                if (y > mt && y < mt + ph) g.DrawLine(dash, ml, y, ml + pw, y);
            }
            g.DrawLine(mid, ml, Py(_u.VelocityValue(centre)), ml + pw, Py(_u.VelocityValue(centre)));
        }

        // the temperature line, when the analysis found one
        if (byTemp && _result?.Temperature is { } te && used.Count(s => s.Session.TempC is not null) >= 2)
        {
            var withT = used.Where(s => s.Session.TempC is not null).ToList();
            double meanT = withT.Average(s => s.Session.TempC!.Value), meanV = withT.Average(s => s.Stats.Mean);
            double slope = te.MpsPerC.Estimate;
            double ta = withT.Min(s => s.Session.TempC!.Value), tz = withT.Max(s => s.Session.TempC!.Value);
            using var pen = new Pen(Amber, 1.5f);
            g.DrawLine(pen, Px(_u.TemperatureValue(ta)), Py(_u.VelocityValue(meanV + slope * (ta - meanT))),
                Px(_u.TemperatureValue(tz)), Py(_u.VelocityValue(meanV + slope * (tz - meanT))));
        }

        using var dotBrush = new SolidBrush(Color.FromArgb(90, Blue));
        foreach (var p in shotDots) g.FillEllipse(dotBrush, Px(p.X) - 2, Py(p.Y) - 2, 4, 4);

        // the SD bars behind the means
        if (mode == FollowChartMode.MeanOverTime)
        {
            using var bar = new Pen(Color.FromArgb(160, Blue));
            foreach (var s in used)
            {
                float x = Px(X(s));
                g.DrawLine(bar, x, Py(_u.VelocityValue(s.Stats.Mean + s.Stats.Sd)), x, Py(_u.VelocityValue(s.Stats.Mean - s.Stats.Sd)));
            }
        }

        foreach (var (pts, col, label, line) in series)
        {
            if (pts.Count == 0) continue;
            using var pen = new Pen(col, 1.5f);
            var ordered = byTemp ? pts : pts.OrderBy(p => p.X).ToList();
            if (line && ordered.Count > 1) g.DrawLines(pen, ordered.Select(p => new PointF(Px(p.X), Py(p.Y))).ToArray());
            using var br = new SolidBrush(col);
            foreach (var p in ordered) g.FillEllipse(br, Px(p.X) - 3.5f, Py(p.Y) - 3.5f, 7, 7);
            if (series.Count > 1)
            {
                int idx = series.FindIndex(s => s.Label == label);
                g.FillRectangle(br, ml + pw - 70 + idx * 36, 6, 8, 8);
                g.DrawString(label, fnt, tb, ml + pw - 59 + idx * 36, 3);
            }
        }
    }

    /// <summary>A range that holds the data with some air around it; a single value gets a range of its own.</summary>
    private static (double Lo, double Hi) Range(double lo, double hi, double air)
    {
        if (hi - lo < 1e-9) { double pad = Math.Max(1, Math.Abs(lo) * 0.01); return (lo - pad, hi + pad); }
        double span = hi - lo;
        return (lo - span * air, hi + span * air);
    }
}
