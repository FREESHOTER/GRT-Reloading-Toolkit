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
        BackColor = Color.FromArgb(18, 24, 36);
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
        Render(e.Graphics, Width, Height);
    }

    /// <summary>Renders the current group to a stand-alone PNG (for the GRT report gallery), same
    /// pattern as <see cref="Ocw.LadderChart.RenderPng()"/>.</summary>
    public byte[]? RenderPng(int width = 640, int height = 640)
    {
        if (_group is null || _group.Impacts.Count == 0) return null;
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

        using var axisPen = new Pen(Color.FromArgb(90, 148, 163, 184));
        g.DrawRectangle(axisPen, margin, margin, pw, ph);
        g.DrawLine(axisPen, X(cx - half), originY, X(cx + half), originY);
        g.DrawLine(axisPen, originX, Y(cy - half), originX, Y(cy + half));

        var ci = CultureInfo.InvariantCulture;
        g.DrawString(string.Format(ci, "±{0:0.0} MOA", half), fnt, tb, margin, height - margin + 6);

        // Point of aim: a plain cross, drawn first so impacts and the centroid sit on top of it.
        using var poaPen = new Pen(Color.FromArgb(160, 210, 220, 230), 1.5f);
        float poaX = X(_group.AimXMoa), poaY = Y(_group.AimYMoa);
        g.DrawLine(poaPen, poaX - 7, poaY, poaX + 7, poaY);
        g.DrawLine(poaPen, poaX, poaY - 7, poaX, poaY + 7);

        var flaggedByIndex = _outliers.Where(o => o.Flagged).Select(o => o.Index).ToHashSet();
        using var normalBrush = new SolidBrush(Color.FromArgb(56, 189, 248));
        using var flaggedBrush = new SolidBrush(Color.FromArgb(251, 191, 36));
        using var flaggedPen = new Pen(Color.FromArgb(251, 191, 36), 1.5f);
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
        using var centroidBrush = new SolidBrush(Color.FromArgb(251, 191, 36));
        float ccx = X(cx), ccy = Y(cy);
        PointF[] diamond = { new(ccx, ccy - 6), new(ccx + 6, ccy), new(ccx, ccy + 6), new(ccx - 6, ccy) };
        g.FillPolygon(centroidBrush, diamond);
    }
}
