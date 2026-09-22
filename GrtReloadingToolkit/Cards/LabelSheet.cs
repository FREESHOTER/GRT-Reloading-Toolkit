using System.Drawing;

namespace GrtReloadingToolkit.Cards;

/// <summary>
/// Where each box label sits on a printed page, and when the page is full. Pure geometry over the
/// printable rectangle -- no GDI+ -- so the paging invariant that matters ("every label the user
/// asked for gets printed, on as many pages as that takes") is testable without WinForms, the same
/// way <see cref="LoadCard"/> is testable while CardRenderer next door is not.
///
/// This exists because <c>LabelForm.Print()</c> used to lay labels out inline and simply stop at the
/// bottom of the first page: a count of 12 -- the dialog's own default -- silently printed 10 on
/// Letter and dropped the rest with no message. Paging is not a detail the drawing code can be
/// trusted to get right by accident, so it lives here on its own, with tests.
/// </summary>
public static class LabelSheet
{
    /// <summary>Labels per row. Two across is the only count that fits: a 78 mm label three across
    /// needs 234 mm of printable width, and Letter at 1 in margins gives 165 mm. The on-screen
    /// preview used to draw three across on a grid of its own, so it agreed with the printed sheet
    /// at no count at all; it now lays out through <see cref="Grid"/> like everything else.</summary>
    public const int Columns = 2;

    /// <summary>Printed label proportions, 78x40 mm. Only the ratio is used: the cell width comes
    /// from the page, so the labels scale with whatever paper the printer is loaded with.</summary>
    private const float WidthMm = 78f, HeightMm = 40f;

    /// <summary>Inset between the cell and the drawn label, in the page's own units, so adjacent
    /// labels don't share an edge under the scissors. Absolute rather than proportional -- 0.04 in
    /// of paper is what the scissors need, whatever size the cell works out to -- which is why the
    /// preview lays out in page units and scales the result, rather than laying out in pixels.</summary>
    private const float Inset = 4f;

    /// <summary>
    /// Printable width of a nominal page in the same hundredths of an inch
    /// <see cref="System.Drawing.Printing.PrintPageEventArgs.MarginBounds"/> reports: US Letter at
    /// 1 in margins. The preview has no printer to ask, and does not need one -- column count and
    /// cell shape are the same on every paper, and only the absolute size differs (A4 comes out
    /// 627 wide, 3.5% narrower, which no one can see on screen).
    /// </summary>
    public const float NominalPageWidth = 650f;

    /// <summary>Size of one grid cell on a page whose printable area is <paramref name="margin"/>.</summary>
    public static SizeF CellSize(RectangleF margin)
    {
        float w = margin.Width / Columns;
        return new SizeF(w, w * HeightMm / WidthMm);
    }

    /// <summary>
    /// How many labels one page holds. Never less than one: a page too short for even a single row
    /// would otherwise hold zero, and a caller paging until the count runs out would never advance
    /// and never stop printing. One clipped label beats an endless print job.
    /// </summary>
    public static int PerPage(RectangleF margin)
    {
        float cellHeight = CellSize(margin).Height;
        if (cellHeight <= 0) return Columns;
        return Math.Max(1, (int)(margin.Height / cellHeight)) * Columns;
    }

    /// <summary>
    /// The cells to draw on this page: as many as the page holds, or <paramref name="remaining"/> if
    /// fewer are left. Returns nothing when <paramref name="remaining"/> is zero or negative, so a
    /// caller that has already printed everything draws an empty page rather than a stray row.
    /// </summary>
    public static IEnumerable<RectangleF> Page(RectangleF margin, int remaining) =>
        Grid(margin, Math.Min(remaining, PerPage(margin)));

    /// <summary>
    /// The grid itself, <paramref name="count"/> cells filling rows left to right from the top left
    /// of <paramref name="area"/>, with no regard for where the area ends. <see cref="Page"/> caps
    /// the count so a page never overflows; the preview does not, because it lays every label out on
    /// one strip and lets the panel scroll. Both go through here so there is one definition of where
    /// a label sits -- the preview drawing its own grid is exactly how the two came to disagree.
    /// </summary>
    public static IEnumerable<RectangleF> Grid(RectangleF area, int count)
    {
        if (count <= 0) yield break;

        SizeF cell = CellSize(area);
        for (int i = 0; i < count; i++)
        {
            int row = i / Columns, col = i % Columns;
            yield return new RectangleF(
                area.X + col * cell.Width + Inset,
                area.Y + row * cell.Height + Inset,
                cell.Width - Inset * 2,
                cell.Height - Inset * 2);
        }
    }

    /// <summary>Rows a grid of <paramref name="count"/> labels needs, counting a part-full last row.</summary>
    public static int Rows(int count) => count <= 0 ? 0 : (count + Columns - 1) / Columns;

    /// <summary>
    /// The strip the on-screen preview lays out on: a nominal page's printable width, tall enough to
    /// hold every label at once. The preview scrolls rather than paginating, so this is deliberately
    /// not a page -- but it is the same width and the same cells, which is the whole point.
    /// </summary>
    public static RectangleF PreviewSheet(int count)
    {
        var sheet = new RectangleF(0, 0, NominalPageWidth, 0);
        return sheet with { Height = Rows(count) * CellSize(sheet).Height };
    }
}
