using GrtPluginKit.Grt;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

/// <summary>Fired-case head diameter vs charge for one firearm+powder group -- the flagged
/// (accelerating) steps are drawn in a different colour, same "highlight what's flagged, don't just
/// list it" convention as <see cref="GroupTargetChart"/>'s outlier dots.</summary>
internal sealed class PressureSignChart : Panel
{
    private IReadOnlyList<PressureSign.ChargePoint>? _series;
    private IReadOnlyList<PressureSign.AccelerationFlag>? _flags;

    public PressureSignChart()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(18, 24, 36);
    }

    public void SetData(IReadOnlyList<PressureSign.ChargePoint>? series, IReadOnlyList<PressureSign.AccelerationFlag>? flags)
    {
        _series = series;
        _flags = flags;
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

        if (_series is null || _series.Count < 2)
        {
            g.DrawString(_series is null ? "select a firearm and powder" : "not enough readings yet", fnt, tb, 10, 10);
            return;
        }

        float ml = 60, mr = 16, mt = 14, mb = 26;
        float pw = Width - ml - mr, ph = Height - mt - mb;
        if (pw < 40 || ph < 30) return;

        var u = GrtUnits.Current;
        double xMin = _series.Min(p => p.ChargeGr), xMax = _series.Max(p => p.ChargeGr);
        double yMin = _series.Min(p => p.AvgHeadDiameterMm), yMax = _series.Max(p => p.AvgHeadDiameterMm);
        if (xMax - xMin < 1e-9) xMax = xMin + 1;
        double yPad = Math.Max(1e-4, (yMax - yMin) * 0.2);
        yMin -= yPad; yMax += yPad;
        float X(double v) => ml + (float)((v - xMin) / (xMax - xMin)) * pw;
        float Y(double v) => mt + ph - (float)((v - yMin) / (yMax - yMin)) * ph;

        using var axis = new Pen(Color.FromArgb(120, 148, 163, 184));
        g.DrawRectangle(axis, ml, mt, pw, ph);
        g.DrawString(u.Length(yMax), fnt, tb, 2, mt - 3);
        g.DrawString(u.Length(yMin), fnt, tb, 2, mt + ph - 12);
        g.DrawString(u.Charge(xMin), fnt, tb, ml, mt + ph + 6);
        g.DrawString(u.Charge(xMax), fnt, tb, ml + pw - 50, mt + ph + 6);

        var flagged = new HashSet<(double, double)>((_flags ?? Array.Empty<PressureSign.AccelerationFlag>())
            .Select(f => (f.FromChargeGr, f.ToChargeGr)));

        using var line = new Pen(Color.FromArgb(56, 189, 248), 2f);
        using var flagLine = new Pen(Color.FromArgb(248, 113, 113), 3f);
        for (int i = 1; i < _series.Count; i++)
        {
            var a = _series[i - 1];
            var b = _series[i];
            var pen = flagged.Contains((a.ChargeGr, b.ChargeGr)) ? flagLine : line;
            g.DrawLine(pen, X(a.ChargeGr), Y(a.AvgHeadDiameterMm), X(b.ChargeGr), Y(b.AvgHeadDiameterMm));
        }
        using var dot = new SolidBrush(Color.FromArgb(56, 189, 248));
        foreach (var p in _series) g.FillEllipse(dot, X(p.ChargeGr) - 3, Y(p.AvgHeadDiameterMm) - 3, 6, 6);
    }
}
