using static ShotMarker.Core.Faces.Tables.FaceTable;

namespace ShotMarker.Core.Faces.Tables;

/// <summary>
/// The Norwegian F-Class targets of Norges Benkeskytterforbund, the NBSF.
///
/// The NBSF publishes no numeric ring table. What it publishes is the approved target artwork
/// itself, as vector PDFs on its own site under "Skiver nedlastbare", the F-Class set labelled
/// "godkjent paa GF 2016":
///   https://www.benchrest.no/skiver-nedlastbar/
///   200 m: https://www.benchrest.no/wp-content/uploads/2016/07/F-Class_200m_A3.pdf
///   300 m: https://www.benchrest.no/wp-content/uploads/2016/07/F-Class_300m_50x70.pdf
///   500 m: https://www.benchrest.no/wp-content/uploads/2016/05/F-Class_500m_fullsize.pdf
/// A vector PDF carries exact coordinates, so reading those files is measurement of a published
/// specification rather than estimation, and all three resolve to one exact design grid — which
/// is what makes them citable as a table.
///
/// What the artwork says, consistent to 0.01 mm across all three files: the X ring and the 10
/// ring are filled circles, so their edge is the nominal diameter; rings 9 to 6 are stroked at
/// 3 pt and their centrelines measure the nominal diameter minus one 3 pt line, i.e. the line's
/// OUTER edge is the nominal diameter — the usual convention, a hit touching the line taking
/// the higher value. Measured 200 m centrelines 115.30 / 173.50 / 231.70 / 289.90 plus 1.058
/// give 116.4 / 174.6 / 232.8 / 291.0.
///
/// THE GRID: base x {1, 2, 4, 6, 8, 10} for X / 10 / 9 / 8 / 7 / 6, the base being half a
/// minute of angle at the distance, to 0.05 mm. It is pinned by the filled 10 ring, which
/// measures exactly twice the base in every file (58.200 / 87.300 / 145.500). The drawn X
/// circle measures 29.1 / 43.7 / 72.8, the artwork having rounded it up to 0.1 mm; the grid
/// value is what is stored.
///
/// ShotMarker's own figures are wrong on all three faces and wrong in both directions, because
/// it rounded the base to a whole millimetre (43.65 to 44, 72.75 to 73) and then multiplied,
/// compounding the error tenfold by the 6 ring: its 300 m and 500 m scoring rings are every one
/// larger than the approved target, so scores read high. Worth reporting upstream.
///
/// The one thing the palette cannot carry: in the artwork the aiming disc and the rings are
/// printed cyan, not black. The renderer has no colour, so the disc is drawn as the dark aiming
/// mark with the white X inside it, which is what the old file did too.
///
/// Not here: the 500 m UNL face, whose artwork disagrees with itself by about a millimetre and
/// resolves to no exact grid, and a 1000 m face, which the NBSF does not publish at all.
/// </summary>
internal static class NorwayTables
{
    /// <summary>The rings are stroked at 3 points in the artwork, and a point is a 72nd of an
    /// inch.</summary>
    private static readonly double RingLineMm = In(3.0 / 72.0);

    /// <summary>One approved file: its half-MOA base and the paper it is drawn on, both read
    /// from the PDF — the media boxes are 297 x 420 (A3), 460 x 610 and 557 x 642 mm.</summary>
    private sealed record Sheet(string Id, string Name, string ShortName,
                                double BaseMm, double PaperWidthMm, double PaperHeightMm);

    private static readonly Sheet[] Sheets =
    {
        new("NOR200", "Norway F-Class 200", "NOR FC 200", 29.10, 297, 420),
        new("NOR300", "Norway F-Class 300", "NOR FC 300", 43.65, 460, 610),
        new("NOR500", "Norway F-Class 500", "NOR FC 500", 72.75, 557, 642),
    };

    /// <summary>The grid's multipliers, X outwards.</summary>
    private static readonly int[] Steps = { 1, 2, 4, 6, 8, 10 };

    private static readonly string[] Scores = { "X", "10", "9", "8", "7", "6" };

    public static IEnumerable<TargetFace> Faces()
    {
        foreach (Sheet s in Sheets)
            yield return Concentric(
                s.Id, s.Name, s.ShortName,
                Steps.Select((m, i) => Z(s.BaseMm * m, Scores[i])).ToArray(),
                // The filled 10 ring is the aiming mark and the filled X ring is the white
                // centre inside it, so both are drawn by their change of fill.
                aimingMarkMm: s.BaseMm * 2,
                ringLineMm: RingLineMm,
                boardWidthMm: s.PaperWidthMm, boardHeightMm: s.PaperHeightMm,
                whiteCentreMm: s.BaseMm);
    }
}
