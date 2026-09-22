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
    /// <summary>Labels per row. Two across suits the 78x40 mm label on both Letter and A4; the
    /// on-screen preview in LabelForm uses three across because it lays out to a wide panel rather
    /// than to a portrait page.</summary>
    public const int Columns = 2;

    /// <summary>Printed label proportions, 78x40 mm. Only the ratio is used: the cell width comes
    /// from the page, so the labels scale with whatever paper the printer is loaded with.</summary>
    private const float WidthMm = 78f, HeightMm = 40f;

    /// <summary>Inset between the cell and the drawn label, in the page's own units, so adjacent
    /// labels don't share an edge under the scissors.</summary>
    private const float Inset = 4f;

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
    public static IEnumerable<RectangleF> Page(RectangleF margin, int remaining)
    {
        if (remaining <= 0) yield break;

        SizeF cell = CellSize(margin);
        int n = Math.Min(remaining, PerPage(margin));
        for (int i = 0; i < n; i++)
        {
            int row = i / Columns, col = i % Columns;
            yield return new RectangleF(
                margin.X + col * cell.Width + Inset,
                margin.Y + row * cell.Height + Inset,
                cell.Width - Inset * 2,
                cell.Height - Inset * 2);
        }
    }
}
