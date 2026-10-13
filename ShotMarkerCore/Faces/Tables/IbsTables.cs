using static ShotMarker.Core.Faces.Tables.FaceTable;

namespace ShotMarker.Core.Faces.Tables;

/// <summary>
/// The International Benchrest Shooters targets.
///
/// Sources:
///   * <b>IBS Rulebook for the Score and Group Disciplines</b>, Rule V.A.1 "Heavy Bench Rifle
///     Competition", which specifies the 100, 200 and 300 yard group targets (IBS #1, #2, #3)
///     completely in text: every ring, the aiming square, the extra bullseyes, the printed
///     border and the card.
///     https://internationalbenchrest.com/documents/folder/rules/download/rulebook_for_score_and_group_disciplines
///   * <b>IBS 600-yd Rulebook 2026</b>, "600 Yard Course of Fire": "The Official Target shall
///     be the Official 1000 yard target with the scoring rings reduced 60%."
///     https://internationalbenchrest.com/app/ibs/files-module/local/documents/IBS%20600-yd.%20Rulebook%20-2026-.pdf
///   * <b>IBS, "The Disciplines"</b>, under "A Word about the Target", for the 1000 yard rings
///     and paper: "The 10-ring is 7 inches. Additional rings add 3 inches a side; i.e., the
///     9-ring measures 13 inches, the eight-ring 19 inches, the 7-ring is 25 inches, the
///     6-ring is 31 inches, and the 5-ring 37 inches. The paper is 42 inches square." The
///     1000-yd rulebook itself publishes no dimensions — §5.1.22 says only that targets "are
///     designated by the IBS 1,000 Yard Rules Committee" — so the association's own page is
///     the citable source. https://internationalbenchrest.com/about/disciplines
///
/// Four things to know about these faces:
///
///   * <b>No X ring at 600 and 1000 yards.</b> IBS publishes no X-ring diameter: not in the
///     600-yd rulebook, not in the 1000-yd rulebook, not on the page above. X count is the
///     published first tiebreaker for a score tie, so dropping the circle means an import
///     cannot count X — said plainly rather than drawn from a guessed diameter.
///   * <b>No aiming square at 600 and 1000 yards</b>, for the same reason. The 600-yd rule
///     reduces an aiming square the 1000-yd target's own specification never gives a size for.
///   * <b>One target per face.</b> V.A.1 publishes a card carrying a record target and a
///     sighter target of the same design below it, and the 600 yd sheet is shot three up.
///     A face's origin is the aiming centre a shot's coordinates are measured from, so each
///     face here is one published target centred on that origin. IBS600Bx3 is therefore the
///     same geometry as IBS600BR: the three-up spacing is not published anywhere, and a group
///     shot at the left or right target of a three-up sheet will plot off-centre.
///   * <b>Line-only rings.</b> The group target's extra bullseyes overlap the record rings, so
///     these rings are drawn as lines rather than filled discs — a white fill would rub out
///     whatever line it covers. IBS publishes no black aiming mark on any of these targets
///     ("All of the printing of the target shall be done in black" is as far as it goes), so
///     nothing here is filled.
/// </summary>
internal static class IbsTables
{
    public static IEnumerable<TargetFace> Faces()
    {
        // "Overall size of the 200-yard/meter target (designated as IBS #2) will be
        // approximately 8x8 inches with a black borderline one-half inch from the edge. The
        // printing of the 300-yard target (designated as IBS #3) shall be 18x18 inches with
        // the border one-half inch from the edge." At 100 yds the border is published as its
        // own box: "a black border line forming a rectangle 3.5 x 4.75 inches in size, which
        // will be centered on a target card approximately 8x8 inches".
        yield return Group("IBS100BR", "IBS 100yd BR", "IBS 100 BR", 1, 8, 3.5, 4.75);
        yield return Group("IBS200BR", "IBS 200yd BR", "IBS 200 BR", 2, 8, 7, 7);
        yield return Group("IBS300BR", "IBS 300yd BR", "IBS 300 BR", 3, 18, 17, 17);

        // 1000 yds as published, and 600 yds as the published derivation from it: rings
        // "reduced 60%", i.e. four tenths of the 1000 yd ring. The two reductions in that rule
        // differ deliberately — rings to 40%, the aiming square to 60%, "to maintain the same
        // sight picture as if shooting at a 1000 yard target" — so 600 yds is scored
        // angularly tighter than 1000. The association's site says instead that the 600 yd
        // target is "a 1000 yard target reduced by 40%"; the rulebook is the governing text.
        double[] thousand = { 7, 13, 19, 25, 31, 37 };
        yield return Long("IBS1000BR", "IBS 1000yd BR", "IBS 1000 BR", thousand, In(42));
        yield return Long("IBS600BR", "IBS 600yd BR", "IBS 600 BR", Scale(thousand, 0.4), null);
        yield return Long("IBS600Bx3", "IBS 600yd BR x3", "IBS 600 BR x3", Scale(thousand, 0.4), null);
    }

    private static double[] Scale(double[] ringsIn, double by) =>
        ringsIn.Select(d => d * by).ToArray();

    /// <summary>
    /// A group target. V.A.1 in full: "shall have a ten ring of one half inch outside
    /// diameter. The nine ring shall be one-inch diameter. Succeeding rings shall increase
    /// one-half inch in outside diameter to and including the six ring. The aiming square
    /// shall be one-inch square and placed tangent the eight ring at the twelve o'clock
    /// position. Thickness of the sides of the aiming square will be one-quarter inch. ... The
    /// target is to have an additional 10 and 9 ring placed in the third and forth quadrants
    /// of the target. The additional nine ring is to be tangent with the eight ring. ...
    /// Targets for the longer ranges shall be increased in direct proportion to the range in
    /// all ring and, aiming square dimensions, except in ring thickness".
    /// </summary>
    /// <param name="scale">The published "direct proportion to the range": 1, 2 or 3.</param>
    /// <param name="cardIn">The published card size, in inches.</param>
    private static TargetFace Group(string id, string name, string shortName,
                                    double scale, double cardIn,
                                    double borderWidthIn, double borderHeightIn)
    {
        (double DiamIn, string Score)[] zones =
        {
            (0.5, "10"), (1.0, "9"), (1.5, "8"), (2.0, "7"), (2.5, "6"),
        };

        // The one dimension V.A.1 explicitly does NOT scale with the range is ring thickness,
        // so the house line width is taken from the 100 yd target and used at all three.
        double line = In(2.5) / 200;

        var record = zones
            .Select(z => new TargetRing(In(z.DiamIn * scale), "bl", line, z.Score))
            .ToArray();

        // The extra 10 and 9: tangency puts their centres 0.75 + 0.5 = 1.25 in (x the range
        // proportion) from the aiming centre, and the quadrant names the direction. The angle
        // within the quadrant is the one thing the rule leaves open; the bisector is the only
        // placement that makes the pair mirror images and keeps both clear of the rings above.
        double off = In(1.25 * scale) / Math.Sqrt(2);
        var extra = new[] { -1.0, 1.0 }.SelectMany(sx => new[]
        {
            new TargetRing(In(0.5 * scale), "bl", line, "10", sx * off, -off),
            new TargetRing(In(1.0 * scale), "bl", line, "9", sx * off, -off),
        });

        // The aiming square is a 1 in square with quarter-inch sides, which is a 1 in black
        // square with a half-inch white one inside it; both scale with the range. Tangent to
        // the 8 ring at twelve o'clock puts its centre 0.75 + 0.5 = 1.25 in above the aiming
        // centre. Where the aiming centre sits on the card is not published, and the centre of
        // the card is the only reading the text supports — "centered on a target card". It is
        // also corroborated: at 200 yds the square's outer edge, 1.75 in x 2 = 3.5 in up, lands
        // exactly on the published border line half an inch inside an 8 in card.
        var polys = new[]
        {
            Rect(In(borderWidthIn), In(borderHeightIn), "bl", line),
            Square(In(1.0 * scale), "b", 0, 0, In(1.25 * scale)),
            Square(In(0.5 * scale), "w", 0, 0, In(1.25 * scale)),
        };

        // Numerals for the record rings only: each score is drawn once, and the extra pair
        // repeats two scores the record target already carries.
        return new TargetFace(id, name, shortName, In(cardIn), In(cardIn), line,
                              record.Concat(extra).ToArray(), polys, Numerals(record));
    }

    /// <summary>A long-range target: six concentric rings scoring 10 down to 5, no aiming
    /// mark and no X ring, because that is all IBS publishes.</summary>
    private static TargetFace Long(string id, string name, string shortName,
                                   double[] ringsIn, double? paperMm) =>
        Concentric(id, name, shortName,
                   ringsIn.Select((d, i) => ZIn(d, (10 - i).ToString())).ToArray(),
                   aimingMarkMm: 0,
                   ringLineMm: null,
                   boardWidthMm: paperMm, boardHeightMm: paperMm);
}
