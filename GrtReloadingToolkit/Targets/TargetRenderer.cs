using System.Globalization;

namespace GrtReloadingToolkit.Targets;

public enum TargetKind { Ocw, Ladder }

/// <summary>What the user picked before printing. <see cref="PerPage"/> mirrors the reference tool's
/// own "X per pagina" field: 0 (or &gt;=NCharges) means everything on one sheet; otherwise the charges
/// split across several pages, each repeating the header/instructions, numbered "Pagina X/Y".</summary>
public sealed class TargetSpec
{
    public TargetKind Kind { get; set; } = TargetKind.Ocw;
    public int NCharges { get; set; } = 5;
    public IReadOnlyList<double>? ChargesGr { get; set; }
    public int PerPage { get; set; } = 0;

    /// <summary>Shots fired at each aim point before moving to the next: OCW's own round-robin isn't
    /// fixed at 3 passes, and neither is repeating a single Ladder/Audette point -- both range 1-5, so
    /// this is one shared field rather than a fixed constant baked into the instructional text.</summary>
    public int ShotsPerPoint { get; set; } = 3;
    public double? DistanceM { get; set; }
    public string? Firearm { get; set; }
    public string? Powder { get; set; }
    public string? Bullet { get; set; }
}

/// <summary>
/// Printable OCW and Ladder/Audette targets — the "before the range" counterpart to the Ladder/OCW
/// Analyzer, which only reads a group back afterward. Ported from Ballistic Lab v2's own generators
/// (<c>core/ocw_target.py</c>, <c>core/ladder_target.py</c>), checked line by line against that source
/// (not just its PDF output) and redrawn in GDI+ instead of reportlab so it fits this plugin's own
/// print/PNG pipeline rather than adding a PDF library dependency. Every mm offset and adaptive-sizing
/// formula below has a matching line in that Python source, called out in the comments, with two
/// deliberate exceptions: the Ladder/Audette ruler is centred on its own aim point rather than drawn
/// in a separate zone above it (the user's own explicit choice, checked directly against the
/// original), and a second unit ruler in inches runs alongside the mm one (the reference has no inch
/// ruler at all — this is a value-add, not a fidelity gap).
///
/// Every drawing call takes <paramref name="unitsPerMm"/> so the SAME layout code serves both a true
/// 1:1 print (a .NET PrintDocument's Graphics is in 1/100 inch, so unitsPerMm = 100/25.4) and an
/// on-screen preview bitmap at a chosen DPI (unitsPerMm = dpi/25.4). <paramref name="bounds"/> is the
/// FULL page (not pre-inset by the caller) -- this class owns every margin itself, exactly like the
/// Python source computes its own margins from page_w/page_h rather than being handed a drawable area.
///
/// The printed text is Italian throughout, not run through <c>Lang.T</c>: this is content on a piece of
/// paper taken to the range, not a UI string, matching the reference tool's own Italian text exactly
/// (down to the wording of "ZERO OTTICA", "USO", "LETTURA").
/// </summary>
public static class TargetRenderer
{
    private const double MmPerInch = 25.4;
    private const double MarginXMm = 15, MarginTopMm = 12, MarginBotMm = 8;
    private const double AimMarginXMmOcw = 38, AimMarginXMmLadder = 40;
    private const double AimDiscMinMmOcw = 12, AimDiscMaxMmOcw = 25;
    private const double AimDiscMinMmLadder = 15, AimDiscMaxMmLadder = 30;
    private const double GridStepMm = 10, GridHalfWMinMm = 10, GridHalfWMaxMm = 60, GridHalfVMaxMm = 80;
    private const double RulerHalfHeightMm = 50, RulerStepMm = 10;
    private const double ScaleBarMm = 100;

    /// <summary>Loaded once from the embedded copy (see the .csproj) rather than reaching into the
    /// live GRT install at draw time -- a plugin's own printed output should not break if the user's
    /// install layout ever moves that file. Null (not thrown) if the resource is ever missing, so a
    /// packaging mistake costs the logo, not the whole target.</summary>
    private static readonly Image? GrtLogo = LoadGrtLogo();

    private static Image? LoadGrtLogo()
    {
        try
        {
            using Stream? s = typeof(TargetRenderer).Assembly.GetManifestResourceStream("GrtReloadingToolkit.grt-logo.png");
            return s is null ? null : Image.FromStream(s);
        }
        catch (Exception) { return null; }
    }

    /// <summary>How many sheets <see cref="Draw"/> needs for this spec.</summary>
    public static int PageCount(TargetSpec spec)
    {
        int perPage = spec.PerPage > 0 ? spec.PerPage : Math.Max(1, spec.NCharges);
        return Math.Max(1, (int)Math.Ceiling(spec.NCharges / (double)perPage));
    }

    public static void Draw(Graphics g, RectangleF bounds, TargetSpec spec, float unitsPerMm, int pageIndex = 0)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        float u = unitsPerMm;
        double pageWMm = bounds.Width / u, pageHMm = bounds.Height / u;
        float X(double mm) => bounds.X + (float)mm * u;
        float Y(double mm) => bounds.Y + (float)mm * u;

        using var black = new Pen(Color.Black, 0.25f * u);
        using var blackBrush = new SolidBrush(Color.Black);
        using var greyBrush = new SolidBrush(Color.FromArgb(85, 85, 85));
        using var greyBrush2 = new SolidBrush(Color.FromArgb(0x88, 0x88, 0x88));

        using var titleFont = new Font("Segoe UI", 6.2f * u, FontStyle.Bold, GraphicsUnit.Point);
        using var infoFont = new Font("Segoe UI", 3.5f * u, FontStyle.Regular, GraphicsUnit.Point);
        using var labelFont = new Font("Segoe UI", 3.6f * u, FontStyle.Bold, GraphicsUnit.Point);
        using var chargeFont = new Font("Segoe UI", 3.2f * u, FontStyle.Regular, GraphicsUnit.Point);
        using var tickFont = new Font("Segoe UI", 2.4f * u, FontStyle.Regular, GraphicsUnit.Point);
        using var zeroOtticaFont = new Font("Segoe UI", 2.8f * u, FontStyle.Bold, GraphicsUnit.Point);

        int n = Math.Max(1, spec.NCharges);
        int perPage = spec.PerPage > 0 ? spec.PerPage : n;
        int pageCount = PageCount(spec);
        int start = Math.Clamp(pageIndex, 0, pageCount - 1) * perPage;
        int count = Math.Min(perPage, n - start);
        bool isOcw = spec.Kind == TargetKind.Ocw;

        // ── Header ───────────────────────────────────────────────────────
        string kindTitle = isOcw ? "Bersaglio OCW (Optimal Charge Weight)" : "Bersaglio Ladder / Audette";

        string pageInfo = pageCount > 1
            ? FormattableString.Invariant($"Pagina {pageIndex + 1}/{pageCount} — {(isOcw ? "N" : "C")}{start + 1}..{(isOcw ? "N" : "C")}{start + count} di {n} — Stampa 1:1")
            : FormattableString.Invariant($"{n} {(isOcw ? "cariche" : "mire")} — Stampa 1:1 (no fit-to-page)");
        SizeF pageInfoSz = g.MeasureString(pageInfo, infoFont);

        // Logo size/position is fixed by the page-info text alone (top-right corner), never by the
        // title -- only the title is allowed to shrink below.
        float logoH = 0, logoW = 0, logoLeftPx = 0;
        if (GrtLogo is { } logoSz)
        {
            logoH = titleFont.GetHeight(g) * 1.4f;
            logoW = logoH * logoSz.Width / logoSz.Height;
            logoLeftPx = X(pageWMm - MarginXMm) - pageInfoSz.Width - 4 * u - logoW;
        }

        // The title shrinks (never the logo or page-info) to whatever room is left before that
        // corner. At the design size (A3) the fixed-size title string always clears it comfortably,
        // but on a narrower page -- e.g. a print that silently fell back to A4 because the selected
        // printer's PaperSizes has no A3 entry, see TargetForm.Print -- the same title takes up a
        // bigger fraction of the page and would otherwise run into the logo/page-info corner. That
        // silent A4 fallback plus this fixed-size title was exactly the overlap a live print showed
        // (correct on-screen preview, broken print) even though both call this same method.
        float titleAreaRightPx = (GrtLogo != null ? logoLeftPx : X(pageWMm - MarginXMm) - pageInfoSz.Width) - 4 * u;
        float availableTitlePx = titleAreaRightPx - X(MarginXMm);
        float titleNaturalW = g.MeasureString(kindTitle, titleFont).Width;
        double titleScale = titleNaturalW > availableTitlePx && availableTitlePx > 0
            ? Math.Clamp(availableTitlePx / titleNaturalW, 0.5, 1.0)
            : 1.0;
        using var scaledTitleFont = titleScale < 0.999
            ? new Font("Segoe UI", 6.2f * u * (float)titleScale, FontStyle.Bold, GraphicsUnit.Point)
            : null;
        Font activeTitleFont = scaledTitleFont ?? titleFont;

        g.DrawString(kindTitle, activeTitleFont, blackBrush, X(MarginXMm), Y(MarginTopMm - 4));
        // Below the title by the title's own rendered height, not a fixed mm offset -- a fixed offset
        // smaller than the actual title height is what put "Distanza: ... | Data: ..." underneath the
        // title's own letters instead of below them.
        double infoLineYMm = MarginTopMm - 4 + activeTitleFont.GetHeight(g) / u;

        g.DrawString(pageInfo, infoFont, greyBrush, X(pageWMm - MarginXMm) - pageInfoSz.Width, Y(MarginTopMm - 4));

        // GRT's own logo, top-right -- placed to the left of the page-info text (measured above) so
        // the two never collide regardless of how long that text is. This plugin only ever ships
        // inside a GRT install, so crediting the host app on its own printed output is the point.
        if (GrtLogo is { } logo)
            g.DrawImage(logo, logoLeftPx, Y(MarginTopMm - 4), logoW, logoH);

        var infoParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(spec.Firearm)) infoParts.Add("Arma: " + spec.Firearm);
        if (!string.IsNullOrWhiteSpace(spec.Powder)) infoParts.Add("Polvere: " + spec.Powder);
        if (!string.IsNullOrWhiteSpace(spec.Bullet)) infoParts.Add("Palla: " + spec.Bullet);
        if (spec.DistanceM is { } d) infoParts.Add(FormattableString.Invariant($"Distanza: {d:0.#} m"));
        infoParts.Add("Data: " + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        g.DrawString(string.Join(" | ", infoParts), infoFont, greyBrush, X(MarginXMm), Y(infoLineYMm));

        // ── ZERO OTTICA + instructions, both centred on the page ────────
        // refYMm used to be a fixed offset from the top, independent of how tall the header (title +
        // info line) actually rendered -- close enough not to touch at the A3 this was designed for,
        // but the same fixed gap collided with the info line's own text on a narrower page (A4, or
        // any print that fell back to it), which is exactly the "ZERO OTTICA" overlap a live print
        // showed. Anchoring it to the header's own measured bottom, like infoLineYMm already does for
        // the title, keeps it clear of the header at any page width.
        double zeroLabelHMm = zeroOtticaFont.GetHeight(g) / u;
        double headerBottomMm = infoLineYMm + infoFont.GetHeight(g) / u;
        double refYMm = Math.Max(MarginTopMm + (isOcw ? 18 : 20), headerBottomMm + 4 + 7 + zeroLabelHMm);
        float refX = X(pageWMm / 2);
        using (var bluePen = new Pen(Color.FromArgb(0x00, 0x66, 0xcc), 0.5f * u))
        {
            g.DrawLine(bluePen, refX - 5 * u, Y(refYMm), refX + 5 * u, Y(refYMm));
            g.DrawLine(bluePen, refX, Y(refYMm) - 5 * u, refX, Y(refYMm) + 5 * u);
        }
        using (var blueBrush = new SolidBrush(Color.FromArgb(0x00, 0x66, 0xcc)))
            g.DrawString("ZERO OTTICA", zeroOtticaFont, blueBrush, refX - g.MeasureString("ZERO OTTICA", zeroOtticaFont).Width / 2f, Y(refYMm) - 7 * u - zeroOtticaFont.GetHeight(g));

        int shots = Math.Clamp(spec.ShotsPerPoint, 1, 5);
        string[] instrLines = isOcw
            ? new[]
            {
                FormattableString.Invariant($"USO (metodo OCW round-robin): stampa 1:1 (no fit-to-page). Spara 1 colpo alla volta su ogni mira in ORDINE DI CARICA CRESCENTE (da N1 a destra), poi RIPETI il giro: {shots} passate = {shots} colpi per mira. Lascia raffreddare la canna."),
                "LETTURA: cerca le mire i cui GRUPPI cadono nello stesso punto rispetto alla mira (stesso scostamento su griglia). La carica al CENTRO di quella serie è la tua OCW.",
            }
            : new[]
            {
                FormattableString.Invariant($"USO: stampa a scala 1:1 (no fit-to-page). Spara {shots} colpo/i su ogni punto di mira in ORDINE DI CARICA CRESCENTE da sinistra a destra. Leggi il ΔY del foro sulla scala mm sopra al mira: 2-3 ΔY simili consecutivi → nodo."),
            };
        double instrYMm = refYMm + (isOcw ? 8 : 9);
        foreach (string line in instrLines)
        {
            SizeF sz = g.MeasureString(line, infoFont, (int)(pageWMm * u));
            g.DrawString(line, infoFont, greyBrush, new RectangleF(X(0), Y(instrYMm), bounds.Width, sz.Height), new StringFormat { Alignment = StringAlignment.Center });
            instrYMm += sz.Height / u + 0.8;
        }

        // ── Aim-point row: edge-anchored spacing, matching the reference's own x_positions --
        // point i sits at aimMarginX + i*colSpacing, so the first and last points land exactly at
        // the margins rather than centred inside N equal cells (a different formula, not just a
        // different constant, from an earlier version of this file).
        double aimMarginXMm = isOcw ? AimMarginXMmOcw : AimMarginXMmLadder;
        double availWMm = pageWMm - 2 * aimMarginXMm;
        double colSpacingMm = count > 1 ? availWMm / (count - 1) : availWMm;
        double[] xPositionsMm = count == 1
            ? new[] { pageWMm / 2 }
            : Enumerable.Range(0, count).Select(i => aimMarginXMm + i * colSpacingMm).ToArray();

        double discMm = isOcw
            ? Math.Clamp(colSpacingMm - 6, AimDiscMinMmOcw, AimDiscMaxMmOcw)
            : Math.Clamp(colSpacingMm - 3, AimDiscMinMmLadder, AimDiscMaxMmLadder);

        double rowYMm;
        double gridHalfWMm = 0, gridHalfHMm = 0;
        if (isOcw)
        {
            // Grid width comes from column spacing (so neighbours never overlap); grid height comes
            // from whatever vertical room is left between the instructions and the label block below
            // -- the two are independent, so the grid is a rectangle, not a square, exactly like the
            // reference (its own height commonly hits the 80 mm cap on a normal single-page layout,
            // while its width shrinks as more charges share the sheet).
            gridHalfWMm = Math.Clamp(Math.Floor((colSpacingMm / 2 - 4) / GridStepMm) * GridStepMm, GridHalfWMinMm, GridHalfWMaxMm);
            double zoneTopMm = instrYMm + 4;
            double zoneBotMm = pageHMm - MarginBotMm - 16;
            double availHalfVMm = (zoneBotMm - zoneTopMm) / 2 - 6;
            gridHalfHMm = Math.Clamp(Math.Floor(availHalfVMm / GridStepMm) * GridStepMm, GridStepMm, GridHalfVMaxMm);
            rowYMm = (zoneTopMm + zoneBotMm) / 2;

            // Dashed connector across the whole row, drawn before the grids so they overlay it --
            // purely a reading aid linking the sequence, not a measurement.
            using var dashPen = new Pen(Color.FromArgb(0xbb, 0xbb, 0xbb), 0.35f * u) { DashPattern = new float[] { 2.5f, 2.5f } };
            g.DrawLine(dashPen, X(aimMarginXMm - 12), Y(rowYMm), X(pageWMm - aimMarginXMm + 12), Y(rowYMm));
        }
        else
        {
            // Ladder/Audette: the user's own explicit choice keeps the ruler centred on the aim
            // point (not the reference's separate zone above it), so this only needs a row Y that
            // clears the header by the ruler's own half-height.
            rowYMm = instrYMm + 20 + RulerHalfHeightMm;
        }

        for (int col = 0; col < count; col++)
        {
            int i = start + col; // global charge index, so N/Carica numbering doesn't reset per page
            float cx = X(xPositionsMm[col]);
            float cy = Y(rowYMm);
            string chargeLabel = spec.ChargesGr is { } charges && i < charges.Count
                ? charges[i].ToString("0.00", CultureInfo.InvariantCulture) + " gr"
                : "______ gr";

            if (isOcw)
            {
                DrawOcwPoint(g, cx, cy, discMm, gridHalfWMm, gridHalfHMm, u, black, blackBrush, tickFont);
                string nLabel = "N" + (i + 1).ToString(CultureInfo.InvariantCulture);
                g.DrawString(nLabel, labelFont, blackBrush, cx - g.MeasureString(nLabel, labelFont).Width / 2f, cy - (float)gridHalfHMm * u - labelFont.GetHeight(g) - 4 * u);

                float labelY = cy + (float)gridHalfHMm * u + 6 * u;
                string stepLabel = "Carica " + (i + 1).ToString(CultureInfo.InvariantCulture);
                g.DrawString(stepLabel, labelFont, blackBrush, cx - g.MeasureString(stepLabel, labelFont).Width / 2f, labelY);
                labelY += labelFont.GetHeight(g) * 1.15f;
                g.DrawString(chargeLabel, chargeFont, greyBrush, cx - g.MeasureString(chargeLabel, chargeFont).Width / 2f, labelY);
            }
            else
            {
                DrawLadderPoint(g, cx, cy, discMm, u, black, blackBrush, tickFont);
                float labelY = cy + (float)(RulerHalfHeightMm + discMm) * u + 6 * u;
                string stepId = "C" + (i + 1).ToString(CultureInfo.InvariantCulture);
                g.DrawString(stepId, labelFont, blackBrush, cx - g.MeasureString(stepId, labelFont).Width / 2f, labelY);
                labelY += labelFont.GetHeight(g) * 1.15f;
                g.DrawString(chargeLabel, chargeFont, greyBrush, cx - g.MeasureString(chargeLabel, chargeFont).Width / 2f, labelY);
            }
        }

        // ── Scale-verification bar + footer metadata ────────────────────
        double barYMm = pageHMm - MarginBotMm;
        float barX0 = X(MarginXMm), barX1 = X(MarginXMm + ScaleBarMm);
        g.DrawLine(black, barX0, Y(barYMm), barX1, Y(barYMm));
        g.DrawLine(black, barX0, Y(barYMm) - 1.5f * u, barX0, Y(barYMm) + 1.5f * u);
        g.DrawLine(black, barX1, Y(barYMm) - 1.5f * u, barX1, Y(barYMm) + 1.5f * u);
        g.DrawString(FormattableString.Invariant($"{ScaleBarMm:0} mm / {ScaleBarMm / MmPerInch:0.00} in — verifica scala (stampa 1:1)"),
            tickFont, blackBrush, barX0, Y(barYMm) - tickFont.GetHeight(g) - 1 * u);

        string meta = isOcw
            ? FormattableString.Invariant($"Pagina {pageWMm:0}×{pageHMm:0} mm — mira ⌀{discMm:0} mm, griglia {GridStepMm:0} mm")
            : FormattableString.Invariant($"Pagina {pageWMm:0}×{pageHMm:0} mm — mira ⌀{discMm:0} mm, scala ±{RulerHalfHeightMm:0} mm");
        g.DrawString(meta, tickFont, greyBrush2, X(pageWMm - MarginXMm) - g.MeasureString(meta, tickFont).Width, Y(barYMm) - tickFont.GetHeight(g));
    }

    /// <summary>OCW: a shared 10 mm grid around the aim point (blue centre lines, pale blue steps,
    /// matching the reference's own <c>#3aa0d8</c>/<c>#d8e6ee</c>), the shooter fires a round-robin
    /// group here (one shot per aim point per pass, several passes) and compares where the GROUP
    /// centres relative to the grid, not one shot at a time.</summary>
    private static void DrawOcwPoint(Graphics g, float cx, float cy, double discMm, double halfWMm, double halfHMm, float u, Pen linePen, Brush brush, Font tickFont)
    {
        using var centerPen = new Pen(Color.FromArgb(0x3a, 0xa0, 0xd8), 0.3f * u);
        using var gridPen = new Pen(Color.FromArgb(0xd8, 0xe6, 0xee), 0.15f * u);
        using var borderPen = new Pen(Color.FromArgb(0x9b, 0xbf, 0xd2), 0.3f * u);

        for (double mm = -halfWMm; mm <= halfWMm + 1e-6; mm += GridStepMm)
        {
            float x = cx + (float)mm * u;
            g.DrawLine(Math.Abs(mm) < 1e-6 ? centerPen : gridPen, x, cy - (float)halfHMm * u, x, cy + (float)halfHMm * u);
        }
        for (double mm = -halfHMm; mm <= halfHMm + 1e-6; mm += GridStepMm)
        {
            float y = cy + (float)mm * u;
            g.DrawLine(Math.Abs(mm) < 1e-6 ? centerPen : gridPen, cx - (float)halfWMm * u, y, cx + (float)halfWMm * u, y);
        }
        g.DrawRectangle(borderPen, cx - (float)halfWMm * u, cy - (float)halfHMm * u, (float)halfWMm * 2 * u, (float)halfHMm * 2 * u);

        DrawInchTicks(g, cx - (float)halfWMm * u - 5 * u, cy, halfHMm, u, linePen, tickFont, brush);
        DrawAimDisc(g, cx, cy, discMm, u, linePen, Color.FromArgb(0xff, 0x6a, 0x00));
    }

    /// <summary>Ladder/Audette: one shot per aim point, so only a vertical ruler is needed to read the
    /// impact's vertical offset -- no horizontal grid, and a bigger disc since this is shot at
    /// distance (~200-300 m), not 100 m. Kept centred on the aim point rather than the reference's own
    /// separate zone above it -- the user's explicit choice after comparing both directly.</summary>
    private static void DrawLadderPoint(Graphics g, float cx, float cy, double discMm, float u, Pen linePen, Brush brush, Font tickFont)
    {
        using var tickPen = new Pen(Color.FromArgb(0x88, 0x88, 0x88), 0.18f * u);
        g.DrawLine(tickPen, cx, cy - (float)RulerHalfHeightMm * u, cx, cy + (float)RulerHalfHeightMm * u);
        using var redPen = new Pen(Color.FromArgb(0xcc, 0x00, 0x00), 0.35f * u);
        using var redBrush = new SolidBrush(Color.FromArgb(0xcc, 0x00, 0x00));
        using var tickBrush = new SolidBrush(Color.FromArgb(0x44, 0x44, 0x44));
        for (double mm = -RulerHalfHeightMm; mm <= RulerHalfHeightMm; mm += RulerStepMm / 2)
        {
            float y = cy + (float)mm * u;
            bool major = Math.Abs(mm % RulerStepMm) < 1e-6;
            float tickW = major ? 2.5f * u : 1.2f * u;
            g.DrawLine(tickPen, cx - tickW, y, cx + tickW, y);
            if (major && Math.Abs(mm) > 1e-6)
                g.DrawString(Math.Abs(mm).ToString("0", CultureInfo.InvariantCulture), tickFont, tickBrush, cx + tickW + 1 * u, y - tickFont.GetHeight(g) / 2f);
        }
        // Zero gets its own red "0" and red centre line -- the read-off reference every other tick's
        // +/- delta is relative to. It sits at the same height as the disc drawn on top afterward, so
        // its label needs to clear the disc's own radius, not just the tick.
        g.DrawLine(redPen, cx - 5 * u, cy, cx + 5 * u, cy);
        g.DrawString("0", tickFont, redBrush, cx + (float)discMm / 2f * u + 2 * u, cy - tickFont.GetHeight(g) / 2f);

        DrawInchTicks(g, cx - 10 * u, cy, RulerHalfHeightMm, u, linePen, tickFont, brush);
        DrawAimDisc(g, cx, cy, discMm, u, linePen, Color.FromArgb(0xe6, 0x00, 0x00));
    }

    /// <summary>The aim disc every target type shares: a solid colour circle, a white crosshair on
    /// top (an aim point without one is a lot harder to hold a crosshair steady on than the reference
    /// target's own reticle-style discs), and a small white centre dot -- the reference draws that
    /// dot too, and without it the crosshair's own centre is the one place with no ink at all.</summary>
    private static void DrawAimDisc(Graphics g, float cx, float cy, double discMm, float u, Pen linePen, Color fill)
    {
        using var discBrush = new SolidBrush(fill);
        float r = (float)discMm / 2f * u;
        g.FillEllipse(discBrush, cx - r, cy - r, 2 * r, 2 * r);
        g.DrawEllipse(linePen, cx - r, cy - r, 2 * r, 2 * r);
        using (var whitePen = new Pen(Color.White, 0.3f * u))
        {
            float armR = Math.Min(r * 0.5f, 6f * u);
            g.DrawLine(whitePen, cx - armR, cy, cx + armR, cy);
            g.DrawLine(whitePen, cx, cy - armR, cx, cy + armR);
        }
        using var whiteBrush = new SolidBrush(Color.White);
        float dotR = 0.4f * u;
        g.FillEllipse(whiteBrush, cx - dotR, cy - dotR, 2 * dotR, 2 * dotR);
    }

    /// <summary>A separate inch ruler alongside the mm one (never overlaid on it): half-inch ticks
    /// out to the same physical half-height as the mm scale it sits next to. The reference has no
    /// inch ruler at all -- this is the one dimension added on top of it, per explicit request.</summary>
    private static void DrawInchTicks(Graphics g, float x, float cy, double halfHeightMm, float u, Pen pen, Font tickFont, Brush brush)
    {
        double halfHeightIn = halfHeightMm / MmPerInch;
        g.DrawLine(pen, x, cy - (float)(halfHeightIn * MmPerInch) * u, x, cy + (float)(halfHeightIn * MmPerInch) * u);
        for (double inch = -Math.Floor(halfHeightIn * 2) / 2; inch <= halfHeightIn; inch += 0.5)
        {
            float y = cy + (float)(inch * MmPerInch) * u;
            float tickW = Math.Abs(inch - Math.Round(inch)) < 1e-6 ? 2.5f * u : 1.2f * u;
            g.DrawLine(pen, x - tickW, y, x, y);
            if (Math.Abs(inch - Math.Round(inch)) < 1e-6 && inch != 0)
                g.DrawString(Math.Abs(inch).ToString("0", CultureInfo.InvariantCulture) + "\"", tickFont, brush, x - tickW - 8 * u, y - tickFont.GetHeight(g) / 2f);
        }
    }
}
