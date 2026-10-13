using static ShotMarker.Core.Faces.Tables.FaceTable;

namespace ShotMarker.Core.Faces.Tables;

/// <summary>
/// The South African targets, from the <b>SABU Rulebook, Revised December 2020</b>
/// (<c>https://www.bpsu.co.za/SABU-Rulebook-Revised-Dec-2020.pdf</c>):
///
///   * <b>Appendix D</b>, "Target Dimensions" — section A the frame sizes, B the short-range
///     scoring rings in millimetres, C the long-range rings. Rule 16.1 points at it: "Overall
///     current target sizes and scoring-ring dimensions are given in Appendix D."
///   * <b>Rule FC5</b> and <b>Annex FC/A</b> (A2.1, A3) — the F-Class targets, which SABU
///     does not tabulate because it publishes the rule that makes them instead.
///
/// The derivation rule is printed three times, in A2.1, FC5.1 and FC5.2, and
/// <see cref="FClass"/> below is that rule and nothing else: "targets will be as per SABU TR
/// Targets, again scored one ring tighter, with a new V-Bull, half the diameter of the TR
/// V-Bull". At long range FC5.1 names the new V-bull outright — "A new 128mm (5”) V-Bull is
/// centred in the TR 254mm (10”) V-Bull, which becomes the F-Class Bull" — and half of the
/// TR V-bull is the same 5 inches, so both readings agree to the millimetre.
///
/// Appendix D's own "2" column reads ROT at every distance — rest of target — so on a SABU
/// Target Rifle face the lowest zone has no circle, and on an F-Class face (everything one
/// ring tighter) the 1 does.
///
/// The long-range set is imperial: 10, 20, 44 and 60 inches, with Appendix D printing the
/// converted 1117,6 mm and Annex A3's own note saying so — "The small differences in the
/// metric dimensions compared with those in Appendix D result from rounding off of conversions
/// from the original Imperial dimensions". It is stored here in inches for that reason.
///
/// A1.1 is the reason nothing special is done about the F-Class V-bull: A3 prints it as a
/// "white ring", which is what a black-filled ring inside the aiming mark already draws.
/// SABU's copy of ICFRA's frame footnote says a 25 mm non-scoring border <i>may</i> be used
/// where ICFRA's own rules say must, so these faces do not draw one.
/// </summary>
internal static class SabuTables
{
    /// <summary>One SABU Target Rifle target: the published aiming mark, the four published
    /// circles (V-Bull, Bull, 4, 3 — the 2 being ROT), and the Appendix D.A frame.</summary>
    private sealed record Target(
        string Suffix, string Label, double AimingMark, double[] Circles,
        double FrameW, double FrameH);

    /// <summary>Appendix D.B (300 m and 500/600 m) and D.C (700/800/900 m), with the frames
    /// from D.A: 120 x 120 cm at 300, 180 x 180 at 500/600, 240 wide x 180 high at long
    /// range.</summary>
    private static readonly Target[] Targets =
    {
        new("SR", "Short Range", 600,     new[] { 70.0, 140, 370, 600 },           1200, 1200),
        new("MR", "Mid Range",  1000,     new[] { 160.0, 320, 660, 1000 },         1800, 1800),
        new("LR", "Long Range", In(44),   new[] { In(10), In(20), In(44), In(60) }, 2400, 1800),
    };

    /// <summary>
    /// The two short-range V-X faces are the ICFRA F-Class target, which rule FC5.2 is what
    /// puts in a South African rulebook: "For International Matches under full ICFRA
    /// jurisdiction, the ICFRA 300m target will be used. This has an additional scoring zone
    /// in the black aiming mark. (The SABU 500/600m target is identical to the ICFRA one for
    /// 600m)." The diameters are therefore ICFRA F-Class Annex B2.1's printed columns — see
    /// <see cref="IcfraTables"/>, where the same two columns build IF300y and IF600y — and
    /// what SABU changes is the scoring: V and X both count, so the innermost two zones read
    /// X and V where ICFRA's 2014 rules read V and 5.
    /// </summary>
    private static readonly Target[] IcfraShortRange =
    {
        new("SR", "Short Range", 560, new[] { 32.0, 65, 130, 260, 390 }, 1200, 1200),
        new("MR", "Mid Range",   915, new[] { 72.0, 145, 290, 600, 915 }, 1800, 1800),
    };

    public static IEnumerable<TargetFace> Faces()
    {
        foreach (Target t in Targets)
        {
            yield return Build("SA" + t.Suffix + "TR", "SABU Target Rifle " + t.Label,
                               "TR " + t.Label, t, t.Circles, new[] { "V", "5", "4", "3" });
            yield return Build("SA" + t.Suffix + "FC", "SABU F-Class " + t.Label,
                               "FC " + t.Label, t, FClass(t), new[] { "V", "5", "4", "3", "2" });
        }

        foreach (Target t in IcfraShortRange)
            yield return Build("SA" + t.Suffix + "VX", "SABU " + t.Label + " V-X",
                               "SABU " + t.Suffix + " V-X", t, t.Circles,
                               new[] { "X", "V", "5", "4", "3" });

        // The long-range V-X target is the long-range F-Class target under the other scoring:
        // one face, two sets of labels.
        Target lr = Targets[2];
        yield return Build("SALRVX", "SABU Long Range V-X", "SABU LR V-X", lr, FClass(lr),
                           new[] { "X", "V", "5", "4", "3" });
    }

    /// <summary>A2.1 / FC5.1 / FC5.2: a new V-bull half the diameter of the Target Rifle
    /// V-bull, in front of every TR circle scored one ring tighter.</summary>
    private static double[] FClass(Target t) =>
        new[] { t.Circles[0] / 2 }.Concat(t.Circles).ToArray();

    private static TargetFace Build(string id, string name, string shortName, Target t,
                                    double[] circles, string[] scores) =>
        Concentric(id, name, shortName,
                   circles.Select((d, i) => Z(d, scores[i])).ToArray(),
                   aimingMarkMm: t.AimingMark,
                   ringLineMm: null,
                   boardWidthMm: t.FrameW, boardHeightMm: t.FrameH);
}
