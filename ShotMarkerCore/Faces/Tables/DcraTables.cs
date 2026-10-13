namespace ShotMarker.Core.Faces.Tables;

/// <summary>
/// The Dominion of Canada Rifle Association's own target system, from the DCRA Rulebook
/// 2024, Rule 3.01 "DCRA Targets" — Table A (yard distances) and Table B (metric
/// distances). Both tables publish diameters in INCHES and frame sizes in FEET (Table A
/// note 2, Table B note 2), so that is how they are written out below and
/// <see cref="FaceTable.In"/>/<c>Ft</c> convert them.
///
/// Three things the tables publish that are worth naming, because they are what makes these
/// faces differ from the ICFRA ones in <see cref="IcfraTables"/>:
///
///   * <b>The lowest zone is not a circle.</b> Both tables print "Hit (score 1) — RT", and
///     note 3 (Table B note 4) defines RT as "Rest of Target, less a 1-inch border all
///     around the edge". So a DCRA Target Rifle face has five circles, not six: the Hit
///     zone is bounded by the frame. The frame is drawn at the published Target Size with a
///     one-inch edge, which is that border.
///   * <b>F-Class is a published derivation, not a separate table.</b> Note 7: "F-Class
///     will use a wear centre that has a V-Bull of half the diameter of the TR V-Bull. The
///     TR V-Bull as below will be the F-Class bull with all other rings reduced in value by
///     one." Reducing every value by one is what gives F-Class its sixth circle — the TR
///     Outer becomes the F-Class 1 — where ICFRA's F-Class has none. That is the rule
///     <see cref="FClass"/> applies, and it is the only place a DCRA diameter here was not
///     read straight off a table.
///   * <b>Long Range is one target for both tables.</b> Table A note 1 ("all distances from
///     800 yards and more") and Table B note 1 ("700 metres to 900 metres") put both on the
///     same 12/24/48/72/96 column, so there is one Long Range face, not two.
///
/// Table A note 5 permits a 10x6 frame at long range and note 4 permits a 4x4 at 400 yards;
/// the Target Size row is what is drawn, since that is what the rulebook specifies rather
/// than merely allows.
///
/// Rule 3.01 publishes no ring line widths, so the house rule in <see cref="FaceTable"/>
/// applies. Note 8 also authorises the ICFRA system for DCRA events, which is why the
/// <see cref="IcfraTables"/> faces are shipped alongside these rather than instead of them.
/// </summary>
internal static class DcraTables
{
    /// <summary>Feet to millimetres. Both tables give frame sizes as WIDTH x HEIGHT in feet.</summary>
    private static double Ft(double feet) => FaceTable.In(feet * 12);

    /// <summary>
    /// One column of Table A or Table B: the five Target Rifle diameters, the aiming mark
    /// and the frame, all in the tables' own units.
    /// </summary>
    /// <param name="Suffix">The id suffix ShotMarker's library already uses for this face,
    /// so a stored shot group keeps resolving to the same target.</param>
    private sealed record Column(
        string Suffix, string Label,
        double VBull, double Bull, double Inner, double Magpie, double Outer,
        double AimingMark, double FrameWidthFt, double FrameHeightFt);

    /// <summary>Table A — YARD DISTANCES, plus the Long Range column both tables share.</summary>
    private static readonly Column[] Yards =
    {
        new("100y", "100y",  0.875,  1.75,  3.5,   5.25,   8,   8, 4, 4),
        new("200y", "200y",  1.75,   3.5,   7,    10.5,   16,  16, 4, 4),
        new("300y", "300y",  2.75,   5.5,  11,    16.5,   22,  22, 4, 4),
        new("400y", "400y",  4,      8,    15.25, 22.25,  30,  30, 6, 6),
        new("500y", "500y",  5.25,  10.5,  26,    39,     52,  39, 6, 6),
        new("600y", "600y",  6.5,   13,    26,    39,     52,  39, 6, 6),
        new("LR",   "Long Range", 12, 24,  48,    72,     96,  48, 8, 6),
    };

    /// <summary>
    /// Table B — METRIC DISTANCES. 100 m and 200 m are published as N/A ("targets are not
    /// yet available for this metric distance and the equivalent 'yard' target may be
    /// employed"), so there are no faces for them; a 100 m shoot uses DS100y/DF100y.
    /// </summary>
    private static readonly Column[] Metres =
    {
        new("300m", "300m",  3.125,  6.25, 11.25, 16.5,   22,  22, 4, 4),
        new("400m", "400m",  4.125,  8.25, 15.25, 22.25,  30,  30, 6, 6),
        new("500m", "500m",  5.875, 11.75, 26,    39,     52,  39, 6, 6),
        new("600m", "600m",  7.25,  14.5,  26,    39,     52,  39, 6, 6),
    };

    /// <summary>The one-inch non-scoring border of note 3: "RT means Rest of Target, less a
    /// 1-inch border all around the edge."</summary>
    private static readonly double BorderMm = FaceTable.In(1);

    public static IEnumerable<TargetFace> Faces()
    {
        foreach (Column c in Yards.Concat(Metres))
        {
            yield return TargetRifle(c);
            yield return FClass(c);
        }
    }

    /// <summary>Table A/B as printed: V-Bull 5V, Bull 5, Inner 4, Magpie 3, Outer 2. The
    /// Hit (1) row is RT, so it is the frame rather than a circle.</summary>
    private static TargetFace TargetRifle(Column c) =>
        FaceTable.Concentric(
            "DS" + c.Suffix,
            "DCRA Target Rifle " + c.Label,
            "TR " + c.Label,
            new[]
            {
                FaceTable.ZIn(c.VBull,  "V"),
                FaceTable.ZIn(c.Bull,   "5"),
                FaceTable.ZIn(c.Inner,  "4"),
                FaceTable.ZIn(c.Magpie, "3"),
                FaceTable.ZIn(c.Outer,  "2"),
            },
            FaceTable.In(c.AimingMark), null,
            Ft(c.FrameWidthFt), Ft(c.FrameHeightFt), BorderMm);

    /// <summary>Note 7's derivation: a wear centre half the TR V-Bull, then every TR ring
    /// one point lower — so the TR V-Bull becomes the bull and the TR Outer becomes the
    /// 1.</summary>
    private static TargetFace FClass(Column c) =>
        FaceTable.Concentric(
            "DF" + c.Suffix,
            "DCRA F-Class " + c.Label,
            "FC " + c.Label,
            new[]
            {
                FaceTable.ZIn(c.VBull / 2, "V"),
                FaceTable.ZIn(c.VBull,     "5"),
                FaceTable.ZIn(c.Bull,      "4"),
                FaceTable.ZIn(c.Inner,     "3"),
                FaceTable.ZIn(c.Magpie,    "2"),
                FaceTable.ZIn(c.Outer,     "1"),
            },
            FaceTable.In(c.AimingMark), null,
            Ft(c.FrameWidthFt), Ft(c.FrameHeightFt), BorderMm);
}
