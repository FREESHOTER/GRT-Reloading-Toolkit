using System.Drawing.Imaging;
using System.Globalization;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// One output of a model-probe sweep against the probed input: the points joined in input order, the load's own
/// value ringed in orange. Unlike <see cref="FindBaChart"/> it pads the axes by a share of the data's own span and
/// prints four significant digits, because a probed input can be a Ba of 0.48 swept by a few percent, which that
/// chart's fixed ±0.5 padding and one-decimal labels would flatten into a single column.
/// </summary>
internal sealed class ProbeChart : Panel
{
    private List<(double X, double Y)> _pts = new();
    private double? _baselineX;
    private string _xLabel = "", _yLabel = "", _empty = "";

    public ProbeChart()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(18, 24, 36);
    }

    public void SetData(IEnumerable<(double X, double Y)> points, double? baselineX, string xLabel, string yLabel, string emptyMessage)
    {
        _pts = points.OrderBy(p => p.X).ToList();
        _baselineX = baselineX;
        _xLabel = xLabel; _yLabel = yLabel; _empty = emptyMessage;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Render(e.Graphics, Width, Height);
    }

    /// <summary>The chart as a PNG, for the GRT report gallery.</summary>
    public byte[]? RenderPng(int width = 900, int height = 560)
    {
        if (_pts.Count == 0) return null;
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) Render(g, width, height);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static string G(double v) => v.ToString("G4", CultureInfo.InvariantCulture);

    private void Render(Graphics g, int width, int height)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        using var tb = new SolidBrush(Color.FromArgb(210, 220, 230));
        using var fnt = new Font("Segoe UI", 8f);

        if (_pts.Count == 0) { g.DrawString(_empty, fnt, tb, 10, 10); return; }

        float ml = 60, mr = 16, mt = 24, mb = 34;
        float pw = width - ml - mr, ph = height - mt - mb;
        if (pw < 40 || ph < 30) return;

        double xMin = _pts.Min(p => p.X), xMax = _pts.Max(p => p.X);
        double yMin = _pts.Min(p => p.Y), yMax = _pts.Max(p => p.Y);
        double xSpan = xMax - xMin, ySpan = yMax - yMin;
        if (xSpan <= 0) xSpan = Math.Max(Math.Abs(xMax) * 0.01, 1e-9);
        if (ySpan <= 0) ySpan = Math.Max(Math.Abs(yMax) * 0.01, 1e-9);
        xMin -= xSpan * 0.05; xMax += xSpan * 0.05;
        yMin -= ySpan * 0.12; yMax += ySpan * 0.12;
        float X(double v) => ml + (float)((v - xMin) / (xMax - xMin)) * pw;
        float Y(double v) => mt + ph - (float)((v - yMin) / (yMax - yMin)) * ph;

        using var axisPen = new Pen(Color.FromArgb(120, 148, 163, 184));
        g.DrawRectangle(axisPen, ml, mt, pw, ph);
        g.DrawString(_yLabel, fnt, tb, ml, 4);
        g.DrawString(G(yMax), fnt, tb, 4, mt - 2);
        g.DrawString(G(yMin), fnt, tb, 4, mt + ph - 12);
        g.DrawString(G(xMin), fnt, tb, ml, mt + ph + 4);
        var xMaxText = G(xMax);
        g.DrawString(xMaxText, fnt, tb, ml + pw - g.MeasureString(xMaxText, fnt).Width, mt + ph + 4);
        var xl = g.MeasureString(_xLabel, fnt);
        g.DrawString(_xLabel, fnt, tb, ml + (pw - xl.Width) / 2, mt + ph + 16);

        using var line = new Pen(Color.FromArgb(56, 189, 248), 1.6f);
        for (int i = 1; i < _pts.Count; i++)
            g.DrawLine(line, X(_pts[i - 1].X), Y(_pts[i - 1].Y), X(_pts[i].X), Y(_pts[i].Y));

        using var dot = new SolidBrush(Color.FromArgb(56, 189, 248));
        using var ring = new Pen(Color.FromArgb(255, 128, 0), 2f);
        foreach (var p in _pts)
        {
            g.FillEllipse(dot, X(p.X) - 3, Y(p.Y) - 3, 6, 6);
            if (_baselineX is { } bx && p.X == bx) g.DrawEllipse(ring, X(p.X) - 6, Y(p.Y) - 6, 12, 12);
        }
    }
}
