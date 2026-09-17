using System.Globalization;
using GrtPluginKit.Grt;
using QRCoder;

namespace GrtReloadingToolkit.Cards;

internal static class CardRenderer
{
    /// <summary>The units GRT is showing, so a printed card reads like the load it came from.</summary>
    private static GrtUnits U => GrtUnits.Current;

    public static Bitmap QrBitmap(string text, int px = 8)
    {
        using var gen = new QRCodeGenerator();
        using QRCodeData data = gen.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        byte[] png = new PngByteQRCode(data).GetGraphic(px, new byte[] { 0, 0, 0 }, new byte[] { 255, 255, 255 }, true);
        using var ms = new MemoryStream(png);
        return new Bitmap(ms);
    }

    /// <summary>One line, clipped with an ellipsis rather than wrapped or spilled past its box.</summary>
    private static StringFormat OneLine() => new(StringFormat.GenericDefault)
    {
        Trimming = StringTrimming.EllipsisCharacter,
        FormatFlags = StringFormatFlags.NoWrap,
    };

    /// <summary>
    /// Segoe UI at <paramref name="pt"/>, or the largest size below it that still fits
    /// <paramref name="width"/>, down to a floor of 55% of <paramref name="pt"/>.
    ///
    /// The header used to be two bare DrawString(text, font, brush, x, y) calls, which have no box
    /// and so neither wrap nor shrink: at A6 preview scale the charge line is ~91pt, and "Hodgdon
    /// H4831SC" measures over 1000px against ~909px of usable width, so the powder name - the one thing
    /// the card exists to state - ran off the edge and was clipped mid-word. Shrinking beats
    /// wrapping here because the header is two lines by design (caliber, then charge and powder)
    /// and a wrapped third line would push every row below it down. The floor stops one absurd
    /// component name from rendering the header at 8pt; past it, OneLine's ellipsis takes over.
    /// </summary>
    private static Font Fit(Graphics g, string text, float pt, FontStyle style, float width, StringFormat fmt)
    {
        var font = new Font("Segoe UI", pt, style, GraphicsUnit.Point);
        if (string.IsNullOrWhiteSpace(text)) return font;

        float floor = pt * 0.55f;
        while (g.MeasureString(text, font, int.MaxValue, fmt).Width > width && font.SizeInPoints > floor)
        {
            float next = Math.Max(font.SizeInPoints * 0.94f, floor);
            font.Dispose();
            font = new Font("Segoe UI", next, style, GraphicsUnit.Point);
        }
        return font;
    }

    /// <summary>Full recipe card. bounds in the graphics' own units (1/100 in for print, px for bitmap).</summary>
    public static void DrawRecipeCard(Graphics g, RectangleF b, LoadCard c, Bitmap qr)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        float u = Math.Min(b.Width, b.Height) / 100f;      // scale unit

        using var pen = new Pen(Color.Black, 1.2f * u);
        g.DrawRectangle(pen, b.X, b.Y, b.Width, b.Height);

        float pad = 5 * u;
        float x = b.X + pad, y = b.Y + pad;
        float textW = b.Width - 2 * pad;

        using var one = OneLine();
        using var hFont = Fit(g, c.Title, 7.5f * u, FontStyle.Bold, textW, one);
        using var chFont = Fit(g, c.ChargeLine, 9f * u, FontStyle.Bold, textW, one);
        using var qFont = new Font("Segoe UI", 2.6f * u, FontStyle.Regular, GraphicsUnit.Point);
        using var black = new SolidBrush(Color.Black);
        using var grey = new SolidBrush(Color.FromArgb(90, 90, 90));

        g.DrawString(c.Title, hFont, black, new RectangleF(x, y, textW, hFont.GetHeight(g) * 1.2f), one);
        y += hFont.GetHeight(g) + 1 * u;
        g.DrawString(c.ChargeLine, chFont, black, new RectangleF(x, y, textW, chFont.GetHeight(g) * 1.2f), one);
        y += chFont.GetHeight(g) + 2 * u;

        (string k, string? v)[] rows =
        {
            ("Firearm", c.Firearm),
            ("Barrel", c.BarrelLine),
            ("Bullet", c.Bullet + (c.BulletGr is { } bg ? "  " + U.BulletMass(bg) : "")),
            ("Primer", c.Primer),
            ("Brass", c.Brass),
            ("COAL", c.CoalMm is { } o ? U.Length(o) : null),
            ("CBTO", c.CbtoMm is { } t ? U.Length(t) : null),
            ("MV / SD", c.MvMs is { } mv ? U.Velocity(mv) + (c.SdMs is { } sd ? "  SD " + U.VelocitySd(sd) : "") : null),
            ("Cost / round", string.IsNullOrWhiteSpace(c.CostPerRound) ? null : c.CostPerRound),
            ("Date", c.Date),
            ("Notes", string.IsNullOrWhiteSpace(c.LotNote) ? null : c.LotNote),
        };
        // The rows had no floor under them: each one simply advanced y by a fixed pitch, so a card
        // with enough of them filled in walked straight down through the QR caption and into the QR
        // itself. At the A6 render there is 658px between the header and the caption and a row is
        // 78.6px, so it took nine rows -- which a card with a barrel, a cost line and a lot note has.
        // The rows now know where they have to stop and take a single scale between them, so the
        // block stays proportioned rather than one row being squeezed. A sparse card scales by 1 and
        // is untouched; the 55% floor is far below what even eleven rows need.
        float qrSize = Math.Min(b.Width * 0.30f, b.Height * 0.42f);
        float rowsBottom = b.Bottom - pad - qrSize - qFont.GetHeight(g) - 1.5f * u;
        int shown = rows.Count(r => !string.IsNullOrWhiteSpace(r.v));
        float scale = 1f;
        if (shown > 0)
        {
            // A font's height is linear in its point size, so scaling the size and the gap together
            // scales the pitch by exactly the same factor.
            using var probe = new Font("Segoe UI", 3.6f * u, FontStyle.Regular, GraphicsUnit.Point);
            scale = Math.Clamp((rowsBottom - y) / (shown * (probe.GetHeight(g) + 1.4f * u)), 0.55f, 1f);
        }
        using var kFont = new Font("Segoe UI", 3.4f * u * scale, FontStyle.Bold, GraphicsUnit.Point);
        using var vFont = new Font("Segoe UI", 3.6f * u * scale, FontStyle.Regular, GraphicsUnit.Point);

        // The label column was a flat 20u, which is narrower than the labels put in it: at the A6
        // render "Cost / round" measures 293px into 202px of column, so it ran 91px under its own
        // value, and "MV / SD" at 200px cleared its value by two pixels -- a gap only in arithmetic.
        // The labels are literals but their widths are a font's business, not a constant's, so the
        // column is the widest label actually drawn plus a real gap. The old 20u stays as a floor,
        // which is what a sparse card with only short labels still gets.
        float labelW = 20 * u * scale;
        foreach (var (k, v) in rows)
            if (!string.IsNullOrWhiteSpace(v))
                labelW = Math.Max(labelW, g.MeasureString(k, kFont, int.MaxValue, one).Width + 1.5f * u * scale);

        foreach (var (k, v) in rows)
        {
            if (string.IsNullOrWhiteSpace(v)) continue;
            g.DrawString(k, kFont, grey, x, y);
            // The labels are a fixed set of short literals, but the values are component names off
            // the user's inventory and have the same room to overrun as the header did.
            g.DrawString(v, vFont, black, new RectangleF(x + labelW, y, textW - labelW, vFont.GetHeight(g) * 1.2f), one);
            y += vFont.GetHeight(g) + 1.4f * u * scale;
        }

        // QR bottom-right
        var qrRect = new RectangleF(b.Right - pad - qrSize, b.Bottom - pad - qrSize, qrSize, qrSize);
        g.DrawImage(qr, qrRect);
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

        float qrSize = b.Height - 2 * u;
        g.DrawImage(qr, new RectangleF(b.Right - qrSize - u, b.Y + u, qrSize, qrSize));

        float x = b.X + 2 * u, y = b.Y + 1.5f * u;
        float textW = b.Width - qrSize - 5 * u;

        // These already drew into a box, so they wrapped instead of spilling - but the box is only
        // 1.2 lines tall, so a long name lost its second half to the clip just as invisibly.
        using var one = OneLine();
        using var big = Fit(g, c.Title, 5f * u, FontStyle.Bold, textW, one);
        using var mid = Fit(g, c.ChargeLine, 3.6f * u, FontStyle.Bold, textW, one);
        using var sm = new Font("Segoe UI", 3f * u, FontStyle.Regular, GraphicsUnit.Point);
        using var black = new SolidBrush(Color.Black);

        g.DrawString(c.Title, big, black, new RectangleF(x, y, textW, big.GetHeight(g) * 1.2f), one);
        y += big.GetHeight(g) + 0.5f * u;
        g.DrawString(c.ChargeLine, mid, black, new RectangleF(x, y, textW, mid.GetHeight(g) * 1.2f), one);
        y += mid.GetHeight(g) + 0.4f * u;
        string b3 = c.Bullet + (c.BulletGr is { } bg ? " " + bg.ToString("0.#", CultureInfo.InvariantCulture) + "gr" : "");
        if (!string.IsNullOrWhiteSpace(b3)) { g.DrawString(b3, sm, black, new RectangleF(x, y, textW, sm.GetHeight(g) * 1.2f), one); y += sm.GetHeight(g) + 0.3f * u; }
        g.DrawString($"{c.Primer}   {c.Date}".Trim(), sm, black, new RectangleF(x, y, textW, sm.GetHeight(g) * 1.2f), one);
    }
}
