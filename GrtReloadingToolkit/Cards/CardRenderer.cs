using System.Globalization;
using QRCoder;

namespace GrtReloadingToolkit.Cards;

internal static class CardRenderer
{
    public static Bitmap QrBitmap(string text, int px = 8)
    {
        using var gen = new QRCodeGenerator();
        using QRCodeData data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        byte[] png = new PngByteQRCode(data).GetGraphic(px, new byte[] { 0, 0, 0 }, new byte[] { 255, 255, 255 }, true);
        using var ms = new MemoryStream(png);
        return new Bitmap(ms);
    }

    /// <summary>Full recipe card. bounds in the graphics' own units (1/100 in for print, px for bitmap).</summary>
    public static void DrawRecipeCard(Graphics g, RectangleF b, LoadCard c, Bitmap qr)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        float u = Math.Min(b.Width, b.Height) / 100f;      // scale unit

        using var pen = new Pen(Color.Black, 1.2f * u);
        g.DrawRectangle(pen, b.X, b.Y, b.Width, b.Height);

        using var hFont = new Font("Segoe UI", 7.5f * u, FontStyle.Bold, GraphicsUnit.Point);
        using var kFont = new Font("Segoe UI", 3.4f * u, FontStyle.Bold, GraphicsUnit.Point);
        using var vFont = new Font("Segoe UI", 3.6f * u, FontStyle.Regular, GraphicsUnit.Point);
        using var chFont = new Font("Segoe UI", 9f * u, FontStyle.Bold, GraphicsUnit.Point);
        using var black = new SolidBrush(Color.Black);
        using var grey = new SolidBrush(Color.FromArgb(90, 90, 90));

        float pad = 5 * u;
        float x = b.X + pad, y = b.Y + pad;
        g.DrawString(c.Title, hFont, black, x, y);
        y += hFont.GetHeight(g) + 1 * u;
        g.DrawString(c.ChargeLine, chFont, black, x, y);
        y += chFont.GetHeight(g) + 2 * u;

        (string k, string? v)[] rows =
        {
            ("Firearm", c.Firearm),
            ("Bullet", c.Bullet + (c.BulletGr is { } bg ? "  " + bg.ToString("0.#", CultureInfo.InvariantCulture) + " gr" : "")),
            ("Primer", c.Primer),
            ("Brass", c.Brass),
            ("COAL", c.CoalMm is { } o ? o.ToString("0.00", CultureInfo.InvariantCulture) + " mm" : null),
            ("CBTO", c.CbtoMm is { } t ? t.ToString("0.00", CultureInfo.InvariantCulture) + " mm" : null),
            ("MV / SD", c.MvMs is { } mv ? mv.ToString("0", CultureInfo.InvariantCulture) + " m/s" + (c.SdMs is { } sd ? "  SD " + sd.ToString("0.0", CultureInfo.InvariantCulture) : "") : null),
            ("Cost / round", string.IsNullOrWhiteSpace(c.CostPerRound) ? null : c.CostPerRound),
            ("Date", c.Date),
            ("Notes", string.IsNullOrWhiteSpace(c.LotNote) ? null : c.LotNote),
        };
        float labelW = 20 * u;
        float qrSize = Math.Min(b.Width * 0.30f, b.Height * 0.42f);
        foreach (var (k, v) in rows)
        {
            if (string.IsNullOrWhiteSpace(v)) continue;
            g.DrawString(k, kFont, grey, x, y);
            g.DrawString(v, vFont, black, x + labelW, y);
            y += vFont.GetHeight(g) + 1.4f * u;
        }

        // QR bottom-right
        var qrRect = new RectangleF(b.Right - pad - qrSize, b.Bottom - pad - qrSize, qrSize, qrSize);
        g.DrawImage(qr, qrRect);
        using var qFont = new Font("Segoe UI", 2.6f * u, FontStyle.Regular, GraphicsUnit.Point);
        g.DrawString("scan for full recipe", qFont, grey, qrRect.X, qrRect.Y - qFont.GetHeight(g) - 0.5f * u);
    }

    /// <summary>Small ammo-box label.</summary>
    public static void DrawBoxLabel(Graphics g, RectangleF b, LoadCard c, Bitmap qr)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        float u = Math.Min(b.Width, b.Height) / 40f;

        using var pen = new Pen(Color.Black, 0.8f * u);
        g.DrawRectangle(pen, b.X, b.Y, b.Width, b.Height);

        using var big = new Font("Segoe UI", 5f * u, FontStyle.Bold, GraphicsUnit.Point);
        using var mid = new Font("Segoe UI", 3.6f * u, FontStyle.Bold, GraphicsUnit.Point);
        using var sm = new Font("Segoe UI", 3f * u, FontStyle.Regular, GraphicsUnit.Point);
        using var black = new SolidBrush(Color.Black);

        float qrSize = b.Height - 2 * u;
        g.DrawImage(qr, new RectangleF(b.Right - qrSize - u, b.Y + u, qrSize, qrSize));

        float x = b.X + 2 * u, y = b.Y + 1.5f * u;
        float textW = b.Width - qrSize - 5 * u;
        g.DrawString(c.Title, big, black, new RectangleF(x, y, textW, big.GetHeight(g) * 1.2f));
        y += big.GetHeight(g) + 0.5f * u;
        g.DrawString(c.ChargeLine, mid, black, new RectangleF(x, y, textW, mid.GetHeight(g) * 1.2f));
        y += mid.GetHeight(g) + 0.4f * u;
        string b3 = c.Bullet + (c.BulletGr is { } bg ? " " + bg.ToString("0.#", CultureInfo.InvariantCulture) + "gr" : "");
        if (!string.IsNullOrWhiteSpace(b3)) { g.DrawString(b3, sm, black, new RectangleF(x, y, textW, sm.GetHeight(g) * 1.2f)); y += sm.GetHeight(g) + 0.3f * u; }
        g.DrawString($"{c.Primer}   {c.Date}".Trim(), sm, black, new RectangleF(x, y, textW, sm.GetHeight(g) * 1.2f));
    }
}
