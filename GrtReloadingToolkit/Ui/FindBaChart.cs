using GrtPluginKit.Grt;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

/// <summary>Ba / velocity / group for a caliber+powder+bullet combo, plotted against temperature --
/// the one environmental variable GRT's own tcc/tch model actually ties to the charge (pressure and
/// humidity affect the trajectory downrange, not the powder's own burn behaviour).</summary>
internal sealed class FindBaChart : Panel
{
    internal enum YAxis { Ba, Velocity, Group }

    private List<(double tempC, double y)>? _pts;
    private YAxis _axis;

    public FindBaChart()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(18, 24, 36);
    }

    public void SetData(IEnumerable<JournalEntry> entries, YAxis axis)
    {
        _axis = axis;
        _pts = entries
            .Select(e => (e.TemperatureC, y: YOf(e, axis)))
            .Where(p => p.TemperatureC.HasValue && p.y.HasValue)
            .Select(p => (p.TemperatureC!.Value, p.y!.Value))
            .OrderBy(p => p.Item1)
            .ToList();
        Invalidate();
    }

    private static double? YOf(JournalEntry e, YAxis axis) => axis switch
    {
        YAxis.Ba => e.Ba,
        YAxis.Velocity => e.VelocityAvgMs,
        _ => e.GroupMoa,
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        using var tb = new SolidBrush(Color.FromArgb(210, 220, 230));
        using var fnt = new Font("Segoe UI", 8f);

        if (_pts is null || _pts.Count == 0)
        {
            g.DrawString(Lang.T("No entries with both temperature and this value logged yet."), fnt, tb, 10, 10);
            return;
        }

        var u = GrtUnits.Current;
        string yName = _axis switch { YAxis.Ba => "Ba", YAxis.Velocity => u.VelocityUnitName, _ => "MOA" };
        double XOf((double tempC, double y) p) => u.TemperatureValue(p.tempC);
        double YOf((double tempC, double y) p) => _axis == YAxis.Velocity ? u.VelocityValue(p.y) : p.y;

        float ml = 52, mr = 16, mt = 22, mb = 30;
        float pw = Width - ml - mr, ph = Height - mt - mb;
        if (pw < 40 || ph < 30) return;

        double xMin = _pts.Min(XOf), xMax = _pts.Max(XOf);
        double yMin = _pts.Min(YOf), yMax = _pts.Max(YOf);
        if (xMax - xMin < 0.5) { xMax += 0.5; xMin -= 0.5; }
        double yPad = Math.Max(yMax - yMin, 1e-6) * 0.15;
        yMin -= yPad; yMax += yPad;
        float X(double v) => ml + (float)((v - xMin) / (xMax - xMin)) * pw;
        float Y(double v) => mt + ph - (float)((v - yMin) / (yMax - yMin)) * ph;

        using var axisPen = new Pen(Color.FromArgb(120, 148, 163, 184));
        g.DrawRectangle(axisPen, ml, mt, pw, ph);
        g.DrawString($"{yMax:0.####} {yName}", fnt, tb, 4, mt - 3);
        g.DrawString($"{yMin:0.####}", fnt, tb, 4, mt + ph - 12);
        g.DrawString($"{xMin:0.#} {u.TemperatureUnitName}", fnt, tb, ml, mt + ph + 6);
        g.DrawString($"{xMax:0.#} {u.TemperatureUnitName}", fnt, tb, ml + pw - 40, mt + ph + 6);

        // trend line (linear regression of the plotted value vs temperature)
        if (_pts.Count >= 3)
        {
            var (a, b) = LinFit(_pts.Select(XOf).ToArray(), _pts.Select(YOf).ToArray());
            using var trend = new Pen(Color.FromArgb(120, 251, 191, 36), 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            g.DrawLine(trend, X(xMin), Y(a + b * xMin), X(xMax), Y(a + b * xMax));
        }

        using var dot = new SolidBrush(Color.FromArgb(56, 189, 248));
        foreach (var p in _pts) g.FillEllipse(dot, X(XOf(p)) - 3, Y(YOf(p)) - 3, 6, 6);
    }

    private static (double a, double b) LinFit(double[] x, double[] y)
    {
        int n = x.Length;
        double sx = x.Sum(), sy = y.Sum(), sxy = 0, sxx = 0;
        for (int i = 0; i < n; i++) { sxy += x[i] * y[i]; sxx += x[i] * x[i]; }
        double d = n * sxx - sx * sx;
        double b = Math.Abs(d) < 1e-9 ? 0 : (n * sxy - sx * sy) / d;
        return ((sy - b * sx) / n, b);
    }
}
