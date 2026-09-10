using System.Globalization;

namespace GrtReloadingToolkit.Ui;

/// <summary>MV vs cumulative round count for one firearm — throat-erosion / drift view.</summary>
internal sealed class BarrelChart : Panel
{
    private string? _name;
    private List<(string date, string load, double chargeGr, int cumRounds, double mv, double? sd)>? _pts;

    public BarrelChart()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(18, 24, 36);
    }

    public void Show((string? name, List<(string, string, double, int, double, double?)>? pts) data)
    {
        _name = data.name;
        _pts = data.pts;
        Invalidate();
    }

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
            g.DrawString(_name is null ? "select a firearm" : $"{_name}: no journal entries with MV yet", fnt, tb, 10, 10);
            return;
        }

        float ml = 52, mr = 16, mt = 22, mb = 30;
        float pw = Width - ml - mr, ph = Height - mt - mb;
        if (pw < 40 || ph < 30) return;

        double xMin = _pts.Min(p => p.cumRounds), xMax = _pts.Max(p => p.cumRounds);
        double yMin = _pts.Min(p => p.mv), yMax = _pts.Max(p => p.mv);
        if (xMax - xMin < 1) xMax = xMin + 1;
        double yPad = Math.Max(2, (yMax - yMin) * 0.15);
        yMin -= yPad; yMax += yPad;
        float X(double v) => ml + (float)((v - xMin) / (xMax - xMin)) * pw;
        float Y(double v) => mt + ph - (float)((v - yMin) / (yMax - yMin)) * ph;

        using var axis = new Pen(Color.FromArgb(120, 148, 163, 184));
        g.DrawRectangle(axis, ml, mt, pw, ph);
        g.DrawString($"{yMax:0} m/s", fnt, tb, 4, mt - 3);
        g.DrawString($"{yMin:0}", fnt, tb, 4, mt + ph - 12);
        g.DrawString($"{xMin:0} rd", fnt, tb, ml, mt + ph + 6);
        g.DrawString($"{xMax:0} rd", fnt, tb, ml + pw - 40, mt + ph + 6);
        g.DrawString(_name ?? "", new Font("Segoe UI", 8f, FontStyle.Bold), tb, ml, 3);

        // trend line (linear regression of MV vs cumRounds)
        if (_pts.Count >= 3)
        {
            var (a, b) = LinFit(_pts.Select(p => (double)p.cumRounds).ToArray(), _pts.Select(p => p.mv).ToArray());
            using var trend = new Pen(Color.FromArgb(120, 251, 191, 36), 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            g.DrawLine(trend, X(xMin), Y(a + b * xMin), X(xMax), Y(a + b * xMax));
            double per100 = b * 100;
            g.DrawString(string.Format(CultureInfo.InvariantCulture, "trend {0:+0.0;-0.0} m/s / 100 rd", per100),
                fnt, new SolidBrush(Color.FromArgb(251, 191, 36)), ml + 6, mt + 4);
        }

        using var line = new Pen(Color.FromArgb(56, 189, 248), 2f);
        var pts = _pts.Select(p => new PointF(X(p.cumRounds), Y(p.mv))).ToArray();
        if (pts.Length > 1) g.DrawLines(line, pts);
        using var dot = new SolidBrush(Color.FromArgb(56, 189, 248));
        foreach (var pt in pts) g.FillEllipse(dot, pt.X - 3, pt.Y - 3, 6, 6);
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
