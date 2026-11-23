using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// What the new charts have in common: the dark look of the program (or white for paper), a picture that can be copied or saved with a right click, and a few
/// helpers for drawing. Same <c>OnPaint</c> + <c>RenderPng</c> pattern as the older charts of the program.
/// </summary>
internal abstract class ChartBase : Panel
{
    /// <summary>The colours of the series or of the causes, bright enough on the dark ground and still told apart on paper.</summary>
    public static readonly Color[] Series =
    {
        Color.FromArgb(56, 189, 248), Color.FromArgb(251, 191, 36), Color.FromArgb(52, 211, 153), Color.FromArgb(244, 114, 182),
        Color.FromArgb(167, 139, 250), Color.FromArgb(251, 146, 60), Color.FromArgb(45, 212, 191), Color.FromArgb(248, 113, 113),
    };

    protected ChartBase()
    {
        DoubleBuffered = true;
        BackColor = ChartPalette.Dark.Back;
        var menu = new ContextMenuStrip();
        menu.Items.Add(Lang.T("Copy picture"), null, (_, _) => { if (RenderPng(1200, 900) is { } png) { using var ms = new MemoryStream(png); Clipboard.SetImage(Image.FromStream(ms)); } });
        menu.Items.Add(Lang.T("Save picture…"), null, (_, _) => SavePicture());
        ContextMenuStrip = menu;
    }

    /// <summary>True when there is something to draw; without it the chart says what it is waiting for.</summary>
    public abstract bool HasData { get; }

    /// <summary>What the chart waits for, in the language of the window.</summary>
    protected abstract string Waiting { get; }

    protected abstract void Draw(Graphics g, int w, int h, ChartPalette p);

    private void Render(Graphics g, int w, int h, ChartPalette p)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.Clear(p.Back);
        if (!HasData)
        {
            using var f = new Font("Segoe UI", Math.Max(9f, Math.Min(w, h) / 38f));
            using var b = new SolidBrush(p.TextDim);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(Waiting, f, b, new RectangleF(12, 12, w - 24, h - 24), sf);
            return;
        }
        Draw(g, w, h, p);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Render(e.Graphics, Width, Height, ChartPalette.Dark);
    }

    protected override void OnResize(EventArgs eventargs)
    {
        base.OnResize(eventargs);
        Invalidate();
    }

    /// <summary>The picture as a PNG, for the clipboard, a file or a report.</summary>
    public byte[]? RenderPng(int width = 900, int height = 700, bool print = false)
    {
        if (!HasData) return null;
        using var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) Render(g, width, height, ChartPalette.For(print));
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private void SavePicture()
    {
        if (RenderPng(1400, 1000) is not { } png) return;
        using var d = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = "grafico.png" };
        if (d.ShowDialog(FindForm()) == DialogResult.OK) File.WriteAllBytes(d.FileName, png);
    }

    // ---- helpers ----------------------------------------------------------------------------------------------------------------------

    /// <summary>A round step (1, 2, 2.5, 5 times a power of ten) that makes about <paramref name="target"/> divisions of <paramref name="range"/>.</summary>
    public static double NiceStep(double range, int target = 5)
    {
        if (range <= 0 || double.IsNaN(range)) return 1;
        double raw = range / Math.Max(1, target), pow = Math.Pow(10, Math.Floor(Math.Log10(raw))), f = raw / pow;
        double nice = f < 1.5 ? 1 : f < 2.25 ? 2 : f < 3.5 ? 2.5 : f < 7 ? 5 : 10;
        return nice * pow;
    }

    protected static string Num(double v, double step)
    {
        // as many decimals as the step needs (0.25 needs two), none for a whole step
        return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }

    protected static new void Text(Graphics g, string s, Font f, Color c, float x, float y, StringAlignment h = StringAlignment.Near, StringAlignment v = StringAlignment.Near)
    {
        using var b = new SolidBrush(c);
        using var sf = new StringFormat { Alignment = h, LineAlignment = v };
        g.DrawString(s, f, b, x, y, sf);
    }

    /// <summary>The text cut with an ellipsis so that it is not wider than <paramref name="maxWidth"/> pixels in this font.</summary>
    protected static string Fit(Graphics g, string text, Font f, float maxWidth)
    {
        if (g.MeasureString(text, f).Width <= maxWidth) return text;
        int n = text.Length;
        while (n > 1 && g.MeasureString(text[..n] + "…", f).Width > maxWidth) n--;
        return text[..n].TrimEnd() + "…";
    }

    /// <summary>A legend line along the bottom: a dot and a name for each entry, wrapped to the width.</summary>
    protected static float Legend(Graphics g, IReadOnlyList<(string Name, Color Color)> items, Font f, Color text, float x, float y, float width)
    {
        float cx = x, cy = y, lineH = f.GetHeight(g) + 4;
        foreach (var (name, color) in items)
        {
            float w = g.MeasureString(name, f).Width + 22;
            if (cx + w > x + width && cx > x) { cx = x; cy += lineH; }
            using (var b = new SolidBrush(color)) g.FillEllipse(b, cx, cy + lineH / 2 - 5, 9, 9);
            Text(g, name, f, text, cx + 14, cy + 1);
            cx += w + 6;
        }
        return cy + lineH;
    }
}
