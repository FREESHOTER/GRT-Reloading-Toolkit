using System.Drawing;
using GrtReloadingToolkit.Cards;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// <see cref="LabelSheet"/> owns the paging that <c>LabelForm.Print()</c> used to get wrong: it laid
/// the grid out inline and stopped at the bottom of page one, so anything past the first page was
/// dropped without a message. These tests walk the pages the way the print loop does and assert the
/// count that comes out the far end, because that -- not the geometry -- is what the user loses.
/// </summary>
public sealed class LabelSheetTests
{
    /// <summary>US Letter at the PrintDocument default of 1in margins, in hundredths of an inch,
    /// which is what <c>PrintPageEventArgs.MarginBounds</c> hands the print handler.</summary>
    private static readonly RectangleF Letter = new(100, 100, 650, 900);

    /// <summary>A4 (210x297mm) at the same 1in margins, in the same units.</summary>
    private static readonly RectangleF A4 = new(100, 100, 627, 969);

    /// <summary>Walks pages exactly as LabelForm's PrintPage handler does, returning every cell it
    /// would draw. Guarded against a page that yields nothing, which in the real handler would mean
    /// HasMorePages never goes false -- a print job that never ends.</summary>
    private static List<RectangleF> PrintAll(RectangleF margin, int count)
    {
        var drawn = new List<RectangleF>();
        int printed = 0, pages = 0;
        do
        {
            var page = LabelSheet.Page(margin, count - printed).ToList();
            Assert.True(page.Count > 0, "a page drew no labels: the print loop would never terminate");
            drawn.AddRange(page);
            printed += page.Count;
            Assert.True(++pages < 1000, "runaway print job");
        }
        while (printed < count);

        return drawn;
    }

    /// <summary>The bug, pinned to the dialog's own default. LabelForm's count box starts at 12;
    /// a Letter page holds 10, so two of them used to vanish on the very first print anyone tried.</summary>
    [Fact]
    public void TheDefaultCountOfTwelveNeedsTwoPagesOnLetter()
    {
        Assert.Equal(10, LabelSheet.PerPage(Letter));

        Assert.Equal(10, LabelSheet.Page(Letter, 12).Count());
        Assert.Equal(2, LabelSheet.Page(Letter, 2).Count());
        Assert.Equal(12, PrintAll(Letter, 12).Count);
    }

    /// <summary>A4 is taller relative to its width and fits one more row, so the same default of 12
    /// happens to land exactly on one page there. Worth pinning: it is the reason this went
    /// unnoticed for so long on a European desk.</summary>
    [Fact]
    public void A4HoldsTwelveOnASinglePage()
    {
        Assert.Equal(12, LabelSheet.PerPage(A4));
        Assert.Equal(12, LabelSheet.Page(A4, 12).Count());
    }

    /// <summary>The whole point: whatever the user asks for is what gets printed. 60 is the count
    /// box's maximum, and used to print 10.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(59)]
    [InlineData(60)]
    public void EveryLabelAskedForGetsPrinted(int count)
    {
        Assert.Equal(count, PrintAll(Letter, count).Count);
        Assert.Equal(count, PrintAll(A4, count).Count);
    }

    /// <summary>The last page is short, not padded out to a full sheet of blanks.</summary>
    [Fact]
    public void TheLastPageCarriesOnlyWhatIsLeft()
    {
        Assert.Single(LabelSheet.Page(Letter, 1));
        Assert.Equal(3, LabelSheet.Page(Letter, 13 - 10).Count());
    }

    /// <summary>Nothing left to print draws nothing -- the print handler calls this once more on a
    /// page it has already filled exactly, and must not start a stray row.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NothingRemainingDrawsNothing(int remaining) =>
        Assert.Empty(LabelSheet.Page(Letter, remaining));

    /// <summary>A page too short for a single row still yields one label rather than zero. Zero
    /// would leave the print loop unable to advance, so it would spool pages until the spooler or
    /// the paper gave out. A clipped label is the better failure.</summary>
    [Fact]
    public void APageTooShortForARowStillPrintsSomething()
    {
        var sliver = new RectangleF(100, 100, 650, 20); // cell height would be ~167
        Assert.True(LabelSheet.PerPage(sliver) >= 1);
        Assert.NotEmpty(LabelSheet.Page(sliver, 5));
        Assert.Equal(5, PrintAll(sliver, 5).Count);
    }

    /// <summary>Cells tile without overlapping and stay inside the printable area, so two labels
    /// never print on top of each other, none runs off the right edge, and the last row does not
    /// spill past the bottom margin into the unprintable strip.</summary>
    [Fact]
    public void CellsTileInsideTheMarginsWithoutOverlapping()
    {
        var page = LabelSheet.Page(Letter, LabelSheet.PerPage(Letter)).ToList();

        foreach (var cell in page)
        {
            Assert.True(cell.Left >= Letter.Left, "label starts left of the margin");
            Assert.True(cell.Right <= Letter.Right, "label runs past the right margin");
            Assert.True(cell.Top >= Letter.Top, "label starts above the margin");
            Assert.True(cell.Bottom <= Letter.Bottom, "label runs past the bottom margin");
            Assert.True(cell.Width > 0 && cell.Height > 0, "label has no area");
        }

        for (int i = 0; i < page.Count; i++)
            for (int j = i + 1; j < page.Count; j++)
                Assert.False(page[i].IntersectsWith(page[j]), $"labels {i} and {j} overlap");
    }

    /// <summary>
    /// The preview strip is the printed page: same columns, same cells, same cutting gutter, offset
    /// only by where the margin starts. LabelForm used to draw the preview on a 3-across grid of its
    /// own, which is three columns the page cannot fit, at 2.13:1 instead of the label's 1.95:1 --
    /// so the picture on screen (and the PNG saved from it) matched the printout at no count at all.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    [InlineData(10)]
    public void ThePreviewGridIsThePrintedGrid(int count)
    {
        var preview = LabelSheet.Grid(LabelSheet.PreviewSheet(count), count).ToList();
        var printed = LabelSheet.Page(Letter, count).ToList();

        Assert.Equal(printed.Count, preview.Count);
        for (int i = 0; i < preview.Count; i++)
        {
            // PreviewSheet sits at the origin; the page's printable area starts at the margin.
            Assert.Equal(printed[i].X - Letter.X, preview[i].X, 3);
            Assert.Equal(printed[i].Y - Letter.Y, preview[i].Y, 3);
            Assert.Equal(printed[i].Width, preview[i].Width, 3);
            Assert.Equal(printed[i].Height, preview[i].Height, 3);
        }
    }

    /// <summary>
    /// The paper cap lives in <see cref="LabelSheet.Page"/> alone: <see cref="LabelSheet.Grid"/> lays
    /// out what it is asked for and lets it run off the bottom. That split is what lets the preview
    /// put all 60 labels on one scrolling strip while the print loop still stops at each page break
    /// -- the same 60 labels, 10 to a Letter page, carried across six pages.
    /// </summary>
    [Fact]
    public void OnlyPageCapsToThePaperNotGrid()
    {
        Assert.Equal(60, LabelSheet.Grid(Letter, 60).Count());
        Assert.True(LabelSheet.Grid(Letter, 60).Last().Bottom > Letter.Bottom,
            "Grid is supposed to overflow the area; a Grid that fits has capped like Page");

        Assert.Equal(10, LabelSheet.Page(Letter, 60).Count());
        Assert.Equal(60, PrintAll(Letter, 60).Count);
    }

    /// <summary>
    /// The strip is tall enough for the labels drawn on it -- not a truism, since the height comes
    /// out of float arithmetic that <see cref="LabelSheet.PerPage"/> then floors. A strip one row
    /// short would clip the bottom row out of the preview and the saved PNG.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(59)]
    [InlineData(60)]
    public void ThePreviewStripHoldsEveryLabelItIsAskedFor(int count)
    {
        var sheet = LabelSheet.PreviewSheet(count);

        Assert.True(LabelSheet.PerPage(sheet) >= count,
            $"the preview strip holds {LabelSheet.PerPage(sheet)} of {count} labels");
        foreach (var cell in LabelSheet.Grid(sheet, count))
            Assert.True(cell.Bottom <= sheet.Bottom, "a label hangs off the bottom of the preview");
    }

    /// <summary>A part-full last row still takes a whole row, so an odd count is not a row short.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(12, 6)]
    [InlineData(59, 30)]
    public void RowsCountsAPartFullLastRow(int count, int expected) =>
        Assert.Equal(expected, LabelSheet.Rows(count));

    /// <summary>Nothing asked for, nothing laid out -- the preview of a zero count is blank rather
    /// than a stray first cell.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AnEmptyGridDrawsNothing(int count) =>
        Assert.Empty(LabelSheet.Grid(LabelSheet.PreviewSheet(count), count));

    /// <summary>The cell keeps the label's 78x40 mm proportions on any paper, which is why the
    /// preview can use a nominal page width without knowing what is in the printer's tray.</summary>
    [Theory]
    [InlineData(650)] // Letter printable width
    [InlineData(627)] // A4
    [InlineData(NominalWidth)]
    public void CellsKeepTheLabelsProportionsOnAnyPaper(float width)
    {
        SizeF cell = LabelSheet.CellSize(new RectangleF(0, 0, width, 0));

        Assert.Equal(40f / 78f, cell.Height / cell.Width, 4);
    }

    private const float NominalWidth = LabelSheet.NominalPageWidth;
}
