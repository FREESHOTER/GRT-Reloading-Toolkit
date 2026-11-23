using System.Drawing.Imaging;
using System.Globalization;
using GrtReloadingToolkit.Log;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Leave-one-out SD contribution, one bar per shot in firing order. No bar-chart pattern existed
/// yet in this plugin, so this is new drawing logic — but it follows <see cref="BarrelChart"/>/
/// <see cref="FindBaChart"/>'s conventions exactly (dark navy background, Segoe UI 8f, bordered axis
/// rectangle, the same blue used for every other chart's data points). A bar can go negative:
/// removing a perfectly ordinary shot sometimes RAISES the sample SD slightly (fewer points, same
/// relative spread), so the zero line sits at mid-height rather than at the bottom.
/// </summary>
internal sealed class SdRootCauseChart : Panel
{
    private IReadOnlyList<SdRootCause.LeaveOneOutRow>? _rows;

    public SdRootCauseChart()
    {
        DoubleBuffered = true;
        BackColor = ChartPalette.Dark.Back;
    }

    public void SetData(IReadOnlyList<SdRootCause.LeaveOneOutRow> rows) { _rows = rows; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Render(e.Graphics, Width, Height, ChartPalette.Dark);
    }

    /// <summary>Renders the current ranking to a stand-alone PNG (for the GRT report gallery), same
    /// pattern as <see cref="Ocw.LadderChart.RenderPng()"/>.</summary>
    /// <param name="print">white ground for paper (<see cref="ChartPalette.Print"/>).</param>
    public byte[]? RenderPng(int width = 900, int height = 420, bool print = false)
    {
        if (_rows is null || _rows.Count == 0) return null;
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

        if (_rows is null || _rows.Count == 0)
        {
            g.DrawString(Lang.T("Run the analysis first."), fnt, tb, 10, 10);
            return;
        }

        var byShot = _rows.OrderBy(r => r.ShotIndex).ToList();

        float ml = 52, mr = 16, mt = 16, mb = 26;
        float pw = width - ml - mr, ph = height - mt - mb;
        if (pw < 40 || ph < 30) return;

        double maxAbs = Math.Max(0.01, byShot.Max(r => Math.Abs(r.ReductionMps)));
        float zeroY = mt + ph / 2f;
        float scale = (ph / 2f) / (float)maxAbs;

        using var axisPen = new Pen(c.Axis);
        g.DrawRectangle(axisPen, ml, mt, pw, ph);
        g.DrawLine(axisPen, ml, zeroY, ml + pw, zeroY);

        var ci = CultureInfo.InvariantCulture;
        g.DrawString(string.Format(ci, "+{0:0.0}", maxAbs), fnt, tb, 4, mt - 3);
        g.DrawString(string.Format(ci, "-{0:0.0}", maxAbs), fnt, tb, 4, mt + ph - 12);

        float barW = pw / byShot.Count;
        using var normalBrush = new SolidBrush(c.Primary);
        // Chauvenet-flagged shots reuse the amber already used for every trend line in this plugin,
        // rather than adding a third colour to the palette.
        using var flaggedBrush = new SolidBrush(c.Accent);
        for (int i = 0; i < byShot.Count; i++)
        {
            var r = byShot[i];
            float x = ml + i * barW + barW * 0.15f;
            float w = barW * 0.7f;
            float barH = (float)Math.Abs(r.ReductionMps) * scale;
            float y = r.ReductionMps >= 0 ? zeroY - barH : zeroY;
            g.FillRectangle(r.ChauvenetFlagged ? flaggedBrush : normalBrush, x, y, w, Math.Max(1f, barH));
            g.DrawString((r.ShotIndex + 1).ToString(ci), fnt, tb, ml + i * barW + barW / 2 - 6, mt + ph + 4);
        }
    }
}
