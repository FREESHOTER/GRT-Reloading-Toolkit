using static ShotMarker.Core.Faces.Tables.FaceTable;

namespace ShotMarker.Core.Faces.Tables;

/// <summary>
/// ICFRA — the International Confederation of Fullbore Rifle Associations — publishes the
/// international match targets every fullbore body's own faces descend from.
///
/// Sources, both published as PDFs by ICFRA:
///   * ICFRA TR Technical Rules 2024, Annex T/D — D1.2 frame sizes, D2.1 standard aiming
///     marks, D3 the short-range match target, D3.1 the supervee, D4 the long-range target.
///     https://icfra.com/wp-content/uploads/2025/03/TR_Technical_rules-2024.pdf
///   * ICFRA F-Class Technical Rules 2023 Rev 2, Annex F/B — B2.1 the short-range F-Class
///     target, B2.3 the 400 yd/m substitution, B3.1 the long-range F-Class target.
///     https://icfra.com/wp-content/uploads/2024/11/FC_Rules_2023-Rev-2.pdf
///
/// Both tables print "Hit — ROT": a shot outside the outermost circle still scores, so the
/// lowest zone is the rest of the target and has no circle to draw. It is therefore absent
/// from these faces, which is the one visible difference from ShotMarker's own: ShotMarker
/// drew a circle round the lowest zone on the F-Class faces (the aiming mark on the 300/400
/// faces, an undocumented 1320/1830 on the rest) and scored it, which ends the zone where
/// the rulebook does not.
/// </summary>
internal static class IcfraTables
{
    /// <summary>One distance's column. <paramref name="Suffix"/> ends the face id,
    /// <paramref name="Label"/> ends its name. Frames are D1.2: 1.2 x 1.2 m at 300 yds/m,
    /// 1.8 x 1.8 m from 400 m to 700 yds, 1.8 m high x 2.4 m wide at long range.</summary>
    private sealed record Column(
        string Suffix, string Label,
        double AimingMark, double V, double Bull, double Inner, double Magpie, double Outer,
        double FrameWidth, double FrameHeight);

    /// <summary>D3 (short range) and D4 (long range), in mm to the outer edge of the
    /// scoring line, exactly as printed. The 600 m column is also the 700 yd target (D3's
    /// footnote); D4 covers 700 m to 1000 yds.</summary>
    private static readonly Column[] TargetRifle =
    {
        //         id      name          AimMk    V-Bull  Bull   Inner  Magpie  Outer   frame
        new("300y",  "300y",        560,      65,    130,   260,    390,   560,  1200, 1200),
        new("400y",  "400y",        745,      85,    175,   350,    520,   745,  1800, 1800),
        new("500y",  "500y",        915,     130,    260,   600,    915,  1320,  1800, 1800),
        new("600y",  "600y",        915,     145,    290,   600,    915,  1320,  1800, 1800),
        new("300m",  "300m",        600,      70,    140,   280,    420,   600,  1200, 1200),
        new("400m",  "400m",        800,      95,    185,   375,    560,   800,  1800, 1800),
        new("500m",  "500m",       1000,     145,    290,   660,   1000,  1320,  1800, 1800),
        new("600m",  "600m/700y",  1000,     160,    320,   660,   1000,  1320,  1800, 1800),
        new("LR",    "Long Range", 1120,     255,    510,   815,   1120,  1830,  2400, 1800),
    };

    /// <summary>
    /// B2.1 (short range) and B3.1 (long range), as printed — including the supervees ICFRA
    /// prints as whole millimetres (32 for 65/2, 72 for 145/2). B2.1's own description is
    /// "a V-bull ½ the size of the TR V-bull within the ICFRA International Match Target for
    /// short range, with the original v-bull counting bull-5 and so on", i.e. every Target
    /// Rifle ring keeps its diameter and loses a point, which is what the printed columns do.
    ///
    /// 400 yds and 400 m are the one judgement call in this file. B2.1 prints no 400 column,
    /// and ICFRA gives two ways to get one: B2.3 permits enlarging the 300 target x4/3, while
    /// B2.1's rule applies perfectly well to D3's explicit 400 columns. Taken here as B2.1 on
    /// D3 (the rows below), because D3 publishes the 400 distances outright so nothing needs
    /// substituting, because x4/3 lands the aiming mark on 746.67 where D3 publishes 745, and
    /// because this is the only reading under which the F-Class and Target Rifle 400 faces
    /// agree about the diameter of the same physical ring. D3's 400 column is also reprinted,
    /// unchanged, by a second body: the NRA (UK) Rules of Shooting 2026, Appendix V, gives
    /// the ICFRA TR table with 745/85/175/350/520/745 at 400 yards, which is the TargetRifle
    /// row above. The supervee is then B2.1's rule
    /// exactly — half of D3's V-bull, 42.5 and 47.5 — rather than a rounding ICFRA has not
    /// printed.
    /// </summary>
    private static readonly Column[] FClass =
    {
        //         id      name          Black     V-Bull  Bull   Inner  Magpie  Outer   frame
        new("300y",  "300y",        560,    32,     65,    130,    260,   390,  1200, 1200),
        new("400y",  "400y",        745,    42.5,   85,    175,    350,   520,  1800, 1800),
        new("500y",  "500y",        915,    65,    130,    260,    600,   915,  1800, 1800),
        new("600y",  "600y",        915,    72,    145,    290,    600,   915,  1800, 1800),
        new("300m",  "300m",        600,    35,     70,    140,    280,   420,  1200, 1200),
        new("400m",  "400m",        800,    47.5,   95,    185,    375,   560,  1800, 1800),
        new("500m",  "500m",       1000,    72,    145,    290,    660,  1000,  1800, 1800),
        new("600m",  "600m/700y",  1000,    80,    160,    320,    660,  1000,  1800, 1800),
        new("LR",    "Long Range", 1118,   128,    254,    508,    815,  1118,  2400, 1800),
    };

    /// <summary>D4 and B3.1 both require a 25 mm non-scoring zone drawn round the outer edge
    /// of a target on a wooden frame, which is the only edge marking ICFRA publishes.</summary>
    private const double FrameLineMm = 25;

    public static IEnumerable<TargetFace> Faces()
    {
        foreach (Column c in TargetRifle)
            yield return Build("IS" + c.Suffix, "ICFRA Target Rifle " + c.Label, "TR " + c.Label,
                               c, new[] { "V", "5", "4", "3", "2" });

        foreach (Column c in FClass)
            yield return Build("IF" + c.Suffix, "ICFRA F-Class " + c.Label, "FC " + c.Label,
                               c, new[] { "V", "5", "4", "3", "2" });

        // ShotMarker's second F-Class category is the pre-2009 scoring of the SAME physical
        // target: IFX300y and IF300y are diameter-identical in its own file, and both match
        // B2.1. So the geometry below is B2.1/B3.1 as above and only the labels differ. No
        // reachable ICFRA publication prints the 2008 labels — they are what the category is
        // named for, and nothing but labels rests on them.
        foreach (Column c in FClass)
            yield return Build("IFX" + c.Suffix, "ICFRA-2008 F-Class " + c.Label, "FC " + c.Label,
                               c, new[] { "X", "6", "5", "4", "3" });
    }

    private static TargetFace Build(string id, string name, string shortName,
                                    Column c, string[] scores) =>
        Concentric(id, name, shortName,
                   new[]
                   {
                       Z(c.V, scores[0]), Z(c.Bull, scores[1]), Z(c.Inner, scores[2]),
                       Z(c.Magpie, scores[3]), Z(c.Outer, scores[4]),
                   },
                   aimingMarkMm: c.AimingMark,
                   ringLineMm: null,
                   boardWidthMm: c.FrameWidth, boardHeightMm: c.FrameHeight,
                   boardLineMm: FrameLineMm);
}
