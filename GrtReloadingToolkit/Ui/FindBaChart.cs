using System.Drawing.Imaging;
using System.Globalization;
using GrtPluginKit.Grt;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Generic scatter + OLS trend-line panel. Originally only Ba/velocity/group vs. temperature for
/// Find best Ba; generalized (rather than copy-pasted a third/fourth time) once SD Root-Cause needed
/// the exact same shape for temperature-vs-velocity and temperature-vs-group-dispersion. Points and
/// axis unit names are already in the caller's chosen DISPLAY units (via <see cref="GrtUnits"/>) --
/// this panel has no notion of what X/Y physically are.
/// </summary>
internal sealed class FindBaChart : Panel
{
    internal enum YAxis { Ba, Velocity, Group }

    private List<(double X, double Y)>? _pts;
    private string _xUnitName = "";
    private string _yUnitName = "";
    private string _emptyMessage = "";

    public FindBaChart()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(18, 24, 36);
    }

    /// <summary>Ba/velocity/group vs. temperature for a set of journal entries -- Find best Ba's own
    /// use, kept as a convenience overload so that form doesn't need to know about display units.</summary>
    public void SetData(IEnumerable<JournalEntry> entries, YAxis axis)
    {
        var u = GrtUnits.Current;
        double? YOf(JournalEntry e) => axis switch { YAxis.Ba => e.Ba, YAxis.Velocity => e.VelocityAvgMs, _ => e.GroupMoa };
        var pts = entries
            .Select(e => (TempC: e.TemperatureC, Y: YOf(e)))
            .Where(p => p.TempC.HasValue && p.Y.HasValue)
            .Select(p => (X: u.TemperatureValue(p.TempC!.Value), Y: axis == YAxis.Velocity ? u.VelocityValue(p.Y!.Value) : p.Y!.Value));
        string yName = axis switch { YAxis.Ba => "Ba", YAxis.Velocity => u.VelocityUnitName, _ => "MOA" };
        SetData(pts, u.TemperatureUnitName, yName, Lang.T("No entries with both temperature and this value logged yet."));
    }

    /// <summary>Any (X, Y) series already converted to display units, with the unit names appended
    /// after each axis's numbers (e.g. "°C", "m/s", "MOA") -- the same two-label-per-axis layout
    /// this panel always used, just no longer tied to <see cref="JournalEntry"/>.</summary>
    public void SetData(IEnumerable<(double X, double Y)> points, string xUnitName, string yUnitName, string emptyMessage)
    {
        _pts = points.OrderBy(p => p.X).ToList();
        _xUnitName = xUnitName;
        _yUnitName = yUnitName;
        _emptyMessage = emptyMessage;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Render(e.Graphics, Width, Height);
    }

    /// <summary>Renders the current series to a stand-alone PNG (for the GRT report gallery), same
    /// pattern as <see cref="Ocw.LadderChart.RenderPng()"/>.</summary>
    public byte[]? RenderPng(int width = 900, int height = 560)
    {
        if (_pts is null || _pts.Count == 0) return null;
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) Render(g, width, height);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private void Render(Graphics g, int width, int height)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        using var tb = new SolidBrush(Color.FromArgb(210, 220, 230));
        using var fnt = new Font("Segoe UI", 8f);

        if (_pts is null || _pts.Count == 0)
        {
            g.DrawString(_emptyMessage, fnt, tb, 10, 10);
            return;
        }

        float ml = 52, mr = 16, mt = 22, mb = 30;
        float pw = width - ml - mr, ph = height - mt - mb;
        if (pw < 40 || ph < 30) return;

        double xMin = _pts.Min(p => p.X), xMax = _pts.Max(p => p.X);
        double yMin = _pts.Min(p => p.Y), yMax = _pts.Max(p => p.Y);
        if (xMax - xMin < 0.5) { xMax += 0.5; xMin -= 0.5; }
        double yPad = Math.Max(yMax - yMin, 1e-6) * 0.15;
        yMin -= yPad; yMax += yPad;
        float X(double v) => ml + (float)((v - xMin) / (xMax - xMin)) * pw;
        float Y(double v) => mt + ph - (float)((v - yMin) / (yMax - yMin)) * ph;

        using var axisPen = new Pen(Color.FromArgb(120, 148, 163, 184));
        g.DrawRectangle(axisPen, ml, mt, pw, ph);
        // Invariant, like every other number this plugin renders: interpolation alone picks up the
        // OS locale, so on an Italian machine the axis read "0,4523" beside a grid column showing
        // "0.4523" for the same Ba.
        var ci = CultureInfo.InvariantCulture;
        g.DrawString(string.Format(ci, "{0:0.####} {1}", yMax, _yUnitName), fnt, tb, 4, mt - 3);
        g.DrawString(yMin.ToString("0.####", ci), fnt, tb, 4, mt + ph - 12);
        g.DrawString(string.Format(ci, "{0:0.#} {1}", xMin, _xUnitName), fnt, tb, ml, mt + ph + 6);
        g.DrawString(string.Format(ci, "{0:0.#} {1}", xMax, _xUnitName), fnt, tb, ml + pw - 40, mt + ph + 6);

        // trend line (linear regression of Y vs X)
        if (_pts.Count >= 3)
        {
            var (a, b) = LinFit(_pts.Select(p => p.X).ToArray(), _pts.Select(p => p.Y).ToArray());
            using var trend = new Pen(Color.FromArgb(120, 251, 191, 36), 1.5f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            g.DrawLine(trend, X(xMin), Y(a + b * xMin), X(xMax), Y(a + b * xMax));
        }

        using var dot = new SolidBrush(Color.FromArgb(56, 189, 248));
        foreach (var p in _pts) g.FillEllipse(dot, X(p.X) - 3, Y(p.Y) - 3, 6, 6);
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
