using System.Drawing.Imaging;
using System.Globalization;
using GrtReloadingToolkit.Log;
using GrtReloadingToolkit.Ocw;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// A true 2D target-plane plot: impacts, the group centroid, and the point of aim, on an
/// equal-aspect axis (a circular group must look circular, not squashed) — confirmed no such chart
/// existed anywhere in this plugin before Group Analysis, since every other chart here plots one
/// series against a non-spatial X axis (charge step, round count, shot index). Same dark-navy/
/// Segoe-UI/<c>OnPaint</c>+<c>RenderPng</c> conventions as <see cref="FindBaChart"/>/<see cref="SdRootCauseChart"/>.
/// </summary>
internal sealed class GroupTargetChart : Panel
{
    private TargetGroup? _group;
    private IReadOnlyList<MahalanobisOutlier> _outliers = Array.Empty<MahalanobisOutlier>();

    public GroupTargetChart()
    {
        DoubleBuffered = true;
        BackColor = ChartPalette.Dark.Back;
    }

    public void SetData(TargetGroup group, IReadOnlyList<MahalanobisOutlier> outliers)
    {
        _group = group;
        _outliers = outliers;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Render(e.Graphics, Width, Height, ChartPalette.Dark);
    }

    /// <summary>Renders the current group to a stand-alone PNG (for the GRT report gallery), same
    /// pattern as <see cref="Ocw.LadderChart.RenderPng()"/>.</summary>
    /// <param name="print">white ground for paper (<see cref="ChartPalette.Print"/>).</param>
    public byte[]? RenderPng(int width = 640, int height = 640, bool print = false)
    {
        if (_group is null || _group.Impacts.Count == 0) return null;
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) Render(g, width, height, ChartPalette.For(print));
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private void Render(Graphics g, int width, int height, ChartPalette c)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.Clear(c.Back);
        using var tb = new SolidBrush(c.Text);
        using var fnt = new Font("Segoe UI", 8f);

        if (_group is null || _group.Impacts.Count == 0)
        {
            g.DrawString(Lang.T("Load a group first."), fnt, tb, 10, 10);
            return;
        }

        float margin = 30;
        float pw = width - 2 * margin, ph = height - 2 * margin;
        if (pw < 40 || ph < 40) return;

        double cx = _group.CenterXMoa, cy = _group.CenterYMoa;
        double half = 0.5; // never zoom in past +/-0.5 MOA, so a one-hole group still shows scale
        foreach (var im in _group.Impacts)
        {
            half = Math.Max(half, Math.Abs(im.XMoa - cx));
            half = Math.Max(half, Math.Abs(im.YMoa - cy));
        }
        half = Math.Max(half, Math.Abs(_group.AimXMoa - cx));
        half = Math.Max(half, Math.Abs(_group.AimYMoa - cy));
        half *= 1.2; // padding so the outermost hole isn't drawn on the frame

        // Equal aspect: one scale for both axes, driven by whichever dimension is tighter.
        float scale = (float)(Math.Min(pw, ph) / (2 * half));
        float originX = margin + pw / 2f, originY = margin + ph / 2f;
        float X(double xMoa) => originX + (float)(xMoa - cx) * scale;
        float Y(double yMoa) => originY - (float)(yMoa - cy) * scale; // screen Y grows down, target "up" is +Y

        using var axisPen = new Pen(c.AxisFaint);
        g.DrawRectangle(axisPen, margin, margin, pw, ph);
        g.DrawLine(axisPen, X(cx - half), originY, X(cx + half), originY);
        g.DrawLine(axisPen, originX, Y(cy - half), originX, Y(cy + half));

        var ci = CultureInfo.InvariantCulture;
        // rings round the centre of the group in the angle unit of the program, and the mean radius as a dashed ring: the picture reads in the same numbers as the verdict
        var units = Measuring.ProgramUnits.Current;
        double k = units.AngleValue(1.0);                                    // unit per MOA
        double ringStep = ChartBase.NiceStep(half * k, 3);
        using (var ringPen = new Pen(c.Grid))
            for (double r = ringStep; r < half * k * 1.4; r += ringStep)
            {
                float pr = (float)(r / k) * scale;
                g.DrawEllipse(ringPen, originX - pr, originY - pr, 2 * pr, 2 * pr);
                g.DrawString(r.ToString("0.##", ci), fnt, new SolidBrush(c.TextDim), originX + pr * 0.7071f + 2, originY - pr * 0.7071f - 12);
            }
        if (_group.MeanRadiusMoa > 0)
        {
            float mr = (float)_group.MeanRadiusMoa * scale;
            using var mrPen = new Pen(Color.FromArgb(170, c.Accent), 1.6f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            g.DrawEllipse(mrPen, originX - mr, originY - mr, 2 * mr, 2 * mr);
        }
        g.DrawString(string.Format(ci, "±{0:0.0#} {1}   ", half * k, units.AngleUnitName) + Lang.T("(dashed ring = mean radius)"), fnt, tb, margin, height - margin + 6);

        // Point of aim: a plain cross, drawn first so impacts and the centroid sit on top of it.
        using var poaPen = new Pen(c.Aim, 1.5f);
        float poaX = X(_group.AimXMoa), poaY = Y(_group.AimYMoa);
        g.DrawLine(poaPen, poaX - 7, poaY, poaX + 7, poaY);
        g.DrawLine(poaPen, poaX, poaY - 7, poaX, poaY + 7);

        var flaggedByIndex = _outliers.Where(o => o.Flagged).Select(o => o.Index).ToHashSet();
        using var normalBrush = new SolidBrush(c.Primary);
        using var flaggedBrush = new SolidBrush(c.Accent);
        using var flaggedPen = new Pen(c.Accent, 1.5f);
        for (int i = 0; i < _group.Impacts.Count; i++)
        {
            var im = _group.Impacts[i];
            float x = X(im.XMoa), y = Y(im.YMoa);
            if (flaggedByIndex.Contains(i))
            {
                g.FillEllipse(flaggedBrush, x - 4, y - 4, 8, 8);
                g.DrawEllipse(flaggedPen, x - 8, y - 8, 16, 16);
            }
            else g.FillEllipse(normalBrush, x - 4, y - 4, 8, 8);
        }

        // Centroid: a small diamond in the same amber used for a flagged outlier's ring elsewhere in
        // this plugin, but solid rather than a ring, so it never reads as a second flagged shot.
        using var centroidBrush = new SolidBrush(c.Accent);
        float ccx = X(cx), ccy = Y(cy);
        PointF[] diamond = { new(ccx, ccy - 6), new(ccx + 6, ccy), new(ccx, ccy + 6), new(ccx - 6, ccy) };
        g.FillPolygon(centroidBrush, diamond);
    }
}
