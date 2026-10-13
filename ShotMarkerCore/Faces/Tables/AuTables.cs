using static ShotMarker.Core.Faces.Tables.FaceTable;

namespace ShotMarker.Core.Faces.Tables;

/// <summary>
/// The Australian faces: the NRAA's Match Rifle target, the SSAA's Core and Mini Core, and the
/// SSAA Fly target.
///
/// Sources, all published by the governing body:
///   * NRAA Standard Shooting Rules V2.0, effective 1 January 2026, Chapter 5 Annex C — C.11
///     the Match Rifle ring table, C.12 the F-Class Super V, C.13 the Outer 2, C.8 the only
///     line thicknesses the NRAA publishes, C.2 the three frame sizes.
///     https://nraa.com.au/wp-content/uploads/2025/12/SSR-PART-1-7_-All-Chap_v2.0_Jan-26.pdf
///     (The ring tables were removed from SSR v8.0's chapters; V2.0 Annex C is where they live.)
///   * SSAA Combined Services Official National Rules No. 5 (Revised 2022), Annex 5B — Target 1
///     the Core target, Target 6 the Mini Core.
///     https://www.ssaawa.org.au/uploads/resources/Combined-Services-Rulebook-No-5.pdf
///   * SSAA Fly Shoot Official National Rules No. 1 (2017), 3.6 — the Fly target.
///     https://www.ssaawa.org.au/uploads/resources/Fly-Shoot-Rulebook.pdf
/// </summary>
internal static class AuTables
{
    public static IEnumerable<TargetFace> Faces()
    {
        yield return MatchRifle();
        yield return MatchRifleFClass();

        // Annex 5B publishes the Core target once, and the Mini Core once, with no distance
        // attached: "the same approved target" serves every Combined Services distance. The
        // four ids below are therefore two published specs carried at four nominal distances,
        // which is why each pair is built from one call.
        yield return Core("Core", "Core", "Core", CoreTarget);
        yield return Core("SSAACore", "SSAA Core", "SSAA Core", CoreTarget);
        yield return Core("MiniCore", "Mini-Core", "Mini-Core", MiniCoreTarget);
        yield return Core("SSAAMiniCore", "SSAA Mini Core", "SSAA Mini Core", MiniCoreTarget);

        yield return Fly();
    }

    // ---- NRAA Match Rifle -------------------------------------------------------------------

    /// <summary>C.11, verbatim: "In all cases, the Target used shall be the NRAA First Class
    /// Target, 2400 x 1800mm, but with the following dimensions marked on them which shall be
    /// the same for Imperial and Metric Distances" — Aiming Mark 1020, Centre Bull (V) 305,
    /// Bulls Eye (5) 610, Inner (4) 1220, Magpie (3) 1800, Outer (2) Rest of Target.
    ///
    /// The aiming mark is 1020 and the Inner is 1220, so the black edge falls *inside* the
    /// Inner and is not a scoring boundary: a hit at 900 and a hit at 1100 both score 4. It is
    /// carried as an unscored ring, which is what it is — the edge of the black, placed where
    /// it does not split a scoring zone.
    ///
    /// C.13 discards the First Class target's own "Hit (Score 1)" and makes everything outside
    /// the 1800 Magpie an Outer 2, so the Outer has no circle and the Magpie is the outermost
    /// one drawn.</summary>
    private static TargetFace MatchRifle() =>
        Concentric("AU_MR", "Australian Match Rifle", "Australian MR",
                   new[]
                   {
                       Z(305, "V"), Z(610, "5"), Z(1020, null), Z(1220, "4"), Z(1800, "3"),
                   },
                   aimingMarkMm: 1020,
                   ringLineMm: LongRangeLineMm,
                   boardWidthMm: 2400, boardHeightMm: 1800);

    /// <summary>C.12, verbatim: "When F Class Open or FTR are shot at State or National
    /// Championships, an ICFRA 1000yds Super V measuring 128mm shall be placed inside the
    /// central bull. The Super V shot shall be marked as an X on the competitor's score card."
    /// The scores are C.9's own "Centre (V/6)" row: the Super V is the X and every Target Rifle
    /// ring keeps its diameter and gains a point.
    ///
    /// 128 is the published figure. ShotMarker stores 127 — exactly 5.000 in — which is neither
    /// C.12's number nor C.9's, where the 800-1000 yds Super V also prints 128.</summary>
    private static TargetFace MatchRifleFClass() =>
        Concentric("AU_MRFC", "Australian Match Rifle FC)", "Australian MR (FC)",
                   new[]
                   {
                       Z(128, "X", FClassVeeLineMm), Z(305, "6"), Z(610, "5"),
                       Z(1020, null), Z(1220, "4"), Z(1800, "3"),
                   },
                   aimingMarkMm: 1020,
                   ringLineMm: LongRangeLineMm,
                   boardWidthMm: 2400, boardHeightMm: 1800);

    /// <summary>C.8 "Line Thicknesses – White lines for aiming marks" is the only line width the
    /// NRAA publishes, and for "800 to 100yds" (800 to 1000, the typo is the rulebook's) and
    /// "700 to 900m" it gives 4 mm for the F-Class V and 12 mm for every other line. The Match
    /// Rifle target is a long-range target on the First Class frame, so those are its widths.
    /// C.8 states them for the white lines inside the aiming mark; nothing is published for the
    /// lines outside it, so the same widths are used there rather than a second guess.</summary>
    private const double LongRangeLineMm = 12;

    private const double FClassVeeLineMm = 4;

    // ---- SSAA Combined Services Core --------------------------------------------------------

    /// <summary>One of Annex 5B's two Core targets: the published frame, ring line width and
    /// ring table.</summary>
    private sealed record CoreSpec(double FrameMm, double LineMm, double[] Circles);

    /// <summary>Target 1, Core Target (1200mm X 1200mm): "Scoring Rings: 5mm with the outside
    /// edge to be on the outside of the scoring dimensions listed below" — Central 150 = 5.1,
    /// Bull 300 = 5, Inner 600 = 4, Magpie 900 = 3, Outer 1200 = 2, Rest of Target = 1.
    /// SSAA writes the super-vee as "5.1"; it is the V.</summary>
    private static readonly CoreSpec CoreTarget =
        new(1200, 5, new double[] { 150, 300, 600, 900, 1200 });

    /// <summary>Target 6, Mini Core Target (600mm X 600mm): "Scoring Rings: 3mm ..." — Central
    /// 75 = 5.1, Bull 150 = 5, Inner 300 = 4, Magpie 450 = 3, Outer 600 = 2, Rest = 1. Half the
    /// Core target in every published figure, including the line width.</summary>
    private static readonly CoreSpec MiniCoreTarget =
        new(600, 3, new double[] { 75, 150, 300, 450, 600 });

    /// <summary>
    /// Both Core targets print an Outer circle and score the rest of the target 1, so unlike the
    /// ICFRA and Match Rifle faces the outermost circle is published and drawn.
    ///
    /// One thing Annex 5B does NOT publish, and the one judgement in this file: the aiming mark.
    /// Target 1 and Target 6 say only "Aiming Mark: Black with Centre to bottom of the aiming
    /// mark", and the mark itself is a figure shown as a picture — no diameter, no outline, no
    /// dimension of any kind. So no black is drawn here: the published rings are complete and
    /// correct, and the mark is left out rather than traced off a picture or guessed at a
    /// diameter. ShotMarker did it both ways in one file — a traced silhouette on Core and
    /// Mini-Core, a black 300/150 bull on the SSAA pair — and the rulebook publishes neither.
    /// </summary>
    private static TargetFace Core(string id, string name, string shortName, CoreSpec s) =>
        Concentric(id, name, shortName,
                   new[]
                   {
                       Z(s.Circles[0], "V"), Z(s.Circles[1], "5"), Z(s.Circles[2], "4"),
                       Z(s.Circles[3], "3"), Z(s.Circles[4], "2"),
                   },
                   aimingMarkMm: 0,
                   ringLineMm: s.LineMm,
                   boardWidthMm: s.FrameMm, boardHeightMm: s.FrameMm);

    // ---- SSAA Fly ---------------------------------------------------------------------------

    /// <summary>
    /// 3.6.1, which publishes this target to the line weight:
    ///   (i)   "overall dimensions of the target board are: Height 23.4 inches (59.5 cm) and
    ///         width 16.5 inches (42 cm)"
    ///   (iv)  "The 10 ring shall measure 46.7 mm outside diameter (OD)"
    ///   (v)   "Scoring rings line thickness shall each be 0.44 mm for the 1 to 10 score rings.
    ///         The outer zero / one score ring shall be 1.76 mm thick"
    ///   (vi)  "The annular dimensions of each of the 9 to 2 rings shall be 9.1 mm inside
    ///         (9.6mm CTC, 10.0 mm OD)" — 9.6 mm centre-to-centre radially is 19.2 mm of
    ///         diameter per ring, which is the step below.
    ///   (viii) "The total scoring area from outside to outside of the one ring is 220.0mm"
    ///
    /// The 1 ring is the one documented judgement. (vii) describes its annulus as "10.5 mm
    /// outside to outside (8.3mm ID, 10.0 mm CTC)", which is internally impossible — 8.3 + 0.44
    /// + 1.76 = 10.5 puts the centre-to-centre at 9.4, not the 10.0 copied from the row above —
    /// and deriving from it would give 221.3. (viii) states the outermost scoring boundary
    /// outright, so 220.0 is used. ShotMarker stores 219.5, outside the rulebook's own ±0.2 mm.
    ///
    /// 3.6 (iii) gives the Fly itself as "total height of 22.3 mm and a width of 20.0 mm" and
    /// nothing else: it is a drawing of a fly, so there is no outline to author from those two
    /// figures and none is drawn. (Nothing is lost against the old file, which carried the Fly
    /// in a "poly_top" the renderer never read.)
    ///
    /// 3.6.2: "The same approved target is used for 500 metre centrefire and 200 yard rimfire",
    /// so this one face serves both.
    /// </summary>
    private static TargetFace Fly()
    {
        const double ten = 46.7;
        const double step = 19.2;
        var zones = new List<Zone> { Z(ten, "10") };
        for (int score = 9; score >= 2; score--)
            zones.Add(Z(ten + step * (10 - score), score.ToString()));
        zones.Add(Z(220.0, "1", OuterRingLineMm));

        return Concentric("SSAAFly", "SSAA Fly", "SSAA Fly",
                          zones,
                          aimingMarkMm: 0,
                          ringLineMm: RingLineMm,
                          boardWidthMm: In(16.5), boardHeightMm: In(23.4));
    }

    private const double RingLineMm = 0.44;

    private const double OuterRingLineMm = 1.76;
}
