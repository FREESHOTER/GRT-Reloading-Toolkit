using static ShotMarker.Core.Faces.Tables.FaceTable;

namespace ShotMarker.Core.Faces.Tables;

/// <summary>
/// The faces that belong to no single body: the ISSF 300 m target and the scalings of it that
/// ShotMarker shipped under ISSF names, the plain dots and ring grids, and the one ringless
/// figure face.
///
/// The ISSF 300 m rifle target is published, and by three independent bodies in the same
/// numbers — which is why it needs no ISSF document of its own:
///   * NRA (UK) Rules of Shooting 2026, Appendix V, "ISSF 300 Metre": "White with circular
///     black aiming mark. 10 ring diameter 10 cms, 9 ring 20 cms and so on to 1 ring 100 cms.
///     Black all inside the 5 ring (60 cms)."
///     https://nra.org.uk/wp-content/uploads/2025/12/Rules-of-Shooting-2026.pdf
///   * Schiessverordnung des VBS (SR 512.311) Anhang 2 §1 — a 1000 mm hit field in ten even
///     circles with a 600 mm black, as the Swiss combined A target.
///   * SSV "Technische Regeln Gewehr" DOK 1.10.4022 Art. 27, which gives the same figures and
///     adds the 50 mm Innenzehn that the other two leave out, and expressly admits the ISSF
///     face alongside its own: "Zugelassen sind Scheibenbilder gemäss Schiessverordnung VBS
///     und ISSF".
/// So <see cref="SwissTables"/>'s A10 and this face come out diameter-identical, as they
/// should: they are the same printed target.
///
/// The other three ISSF-named faces are NOT published targets. ISSF has no 100 m or 200 m
/// rifle target at all, so "ISSF C50" and "ISSF C200" are misnomers inherited from the file
/// this table replaces; all three are exact scalings of the 300 m face and are authored here
/// as ours, with the scale factor stated. One of them is corroborated anyway: SSAA Combined
/// Services Official National Rules No.5 (Revised 2022), Annex 5B Target 7, publishes
/// 25/50/100/150/200/250/300/350/400/450/500 mm scored X/10...1 — the half-scale face exactly,
/// though it publishes no aiming mark, so the black is ours by the scaling.
///
/// The dots and ring grids are our own faces and are published by nobody. They are a dot or an
/// even inch grid on a plain card, which is all they have ever been. <see cref="HalfMoaGrid"/>
/// is the same kind of face: the one ShotMarker ships as "Austria 600", which no Austrian body
/// publishes and which is provably a half-MOA grid.
/// </summary>
internal static class MiscTables
{
    /// <summary>The published ISSF/A10 figures: a 1000 mm hit field in ten even circles, a
    /// 600 mm black, and the SSV's 50 mm inner ten.</summary>
    private const double IssfFieldMm = 1000;

    private const double IssfBlackMm = 600;

    private const double IssfInnerTenMm = 50;

    /// <summary>Our own faces get a plain square card, the size of the backer they are shot
    /// on, with a 2 mm edge. Nothing published bears on them.</summary>
    private const double CardEdgeMm = 2;

    public static IEnumerable<TargetFace> Faces()
    {
        yield return Issf("ISSF300", "ISSF 300m", "ISSF 300m", 1.0, innerTen: true);

        // Ours: exact scalings of the face above. The scale factor is the whole derivation.
        yield return Issf("ISSFC50", "ISSF C50", "ISSF C50", 0.5, innerTen: true);
        yield return Issf("ISSF300x15", "ISSF 300m +50%", "ISSF 300m +50%", 1.5, innerTen: true);
        // The 0.8 scaling drops the inner ten, as the file this replaces did: its 10 ring is
        // 80 mm and an inner ten would be 40.
        yield return Issf("ISSFC200", "ISSF C200", "ISSF C200", 0.8, innerTen: false);

        foreach (TargetFace f in Dots()) yield return f;
        foreach (TargetFace f in Grids()) yield return f;
        yield return HalfMoaGrid();
        yield return FigureDF();
    }

    /// <summary>The ISSF 300 m target at a scale: ten even circles across the hit field, the
    /// black at the 5 ring, and optionally the 50 mm inner ten.</summary>
    private static TargetFace Issf(string id, string name, string shortName,
                                   double scale, bool innerTen)
    {
        var zones = new List<Zone>();
        if (innerTen) zones.Add(Z(IssfInnerTenMm * scale, "X"));
        for (int score = 10; score >= 1; score--)
            zones.Add(Z(IssfFieldMm / 10 * (11 - score) * scale, score.ToString()));

        // No body among the three publishes a ring line width or a frame for this face, so
        // both come from the house rule.
        return Concentric(id, name, shortName, zones,
                          aimingMarkMm: IssfBlackMm * scale,
                          ringLineMm: null);
    }

    /// <summary>A black dot on a card: our own faces, no scoring rings, nothing to cite.
    /// Diameters in inches, card in inches.</summary>
    private static readonly (string Id, string Name, string Short, double DiamIn, double CardIn)[]
        DotSpecs =
    {
        ("Dot14", "1/4\" Dot", "1/4\" Dot", 0.25, 24),
        ("Dot1",  "1\" Dot",   "1\" Dot",   1.0,  24),
        ("Dot2",  "2\" Dot",   "2\" Dot",   2.0,  36),
        ("Dot3",  "3\" Dot",   "3\" Dot",   3.0,  48),
    };

    private static IEnumerable<TargetFace> Dots()
    {
        foreach (var d in DotSpecs)
            yield return Concentric(
                d.Id, d.Name, d.Short,
                // One unscored zone, which is the dot: it carries no score, so it draws no
                // numeral, and being the aiming mark it needs no outline either.
                new[] { Z(In(d.DiamIn), null) },
                aimingMarkMm: In(d.DiamIn),
                ringLineMm: null,
                boardWidthMm: In(d.CardIn), boardHeightMm: In(d.CardIn),
                boardLineMm: CardEdgeMm);
    }

    /// <summary>An even inch grid: a black X ring then 2, 4, 6 and 8 times that diameter,
    /// scored X and 10 down to 7. Ours, as the dots are.</summary>
    private static readonly (string Id, string Name, string Short, double StepIn, double CardIn)[]
        GridSpecs =
    {
        ("Rings1", "1\" Rings", "1\" Rings", 1, 24),
        ("Rings2", "2\" Rings", "2\" Rings", 2, 36),
        ("Rings3", "3\" Rings", "3\" Rings", 3, 48),
    };

    private static readonly int[] GridSteps = { 1, 2, 4, 6, 8 };

    private static readonly string[] GridScores = { "X", "10", "9", "8", "7" };

    private static IEnumerable<TargetFace> Grids()
    {
        foreach (var g in GridSpecs)
            yield return Concentric(
                g.Id, g.Name, g.Short,
                GridSteps.Select((m, i) => Z(In(g.StepIn * m), GridScores[i])).ToArray(),
                aimingMarkMm: In(g.StepIn),
                // A millimetre of line for every inch of X ring, so the three faces look alike
                // at their three distances.
                ringLineMm: g.StepIn,
                boardWidthMm: In(g.CardIn), boardHeightMm: In(g.CardIn),
                boardLineMm: CardEdgeMm);
    }

    /// <summary>
    /// The face ShotMarker ships as "Austria 600": five rings at 87.3, 174.6, 261.9, 349.2 and
    /// 436.5 mm, scored 10 down to 6.
    ///
    /// No Austrian body publishes it. The OeSB's own target sheet (zielsport.at,
    /// Scheiben-GK.pdf) publishes the ISSF 25/50/100/300 m faces and nothing at 600 m, and the
    /// DSB and BDS catalogues have no such target either. So by the standing rule it could only
    /// be dropped — except that its geometry is not artwork to be traced: it is a stated rule.
    /// Half a minute of angle at 600 m is 600000 x tan(0.5/60 deg) = 87.266 mm, which is 87.3 mm
    /// at 0.1 mm, and every ring is an exact multiple of it. The face is therefore authored here
    /// as ours, from the rule, and named for what it provably is rather than for a country that
    /// does not publish it.
    ///
    /// <c>Id</c> and <c>ShortName</c> stay as the device writes them. They are not a claim about
    /// Austria, they are the lookup key: <c>ImportJob</c> resolves a face by id and
    /// <c>SmCsvReader</c> by display name, so changing either would quietly drop this face to
    /// <c>Generic</c> for anyone who actually shoots it.
    ///
    /// The 800 mm card is the extent ShotMarker drew it on, kept because it is generous: the
    /// outermost ring is 436.5 mm across, and a board sized to the rings alone would put a wide
    /// shot off the picture. Nothing is published that bears on the frame either way.
    /// </summary>
    private static TargetFace HalfMoaGrid() =>
        Concentric(
            "AT_600", "600 m half-MOA grid", "Austria 600",
            Enumerable.Range(1, 5)
                      .Select(step => Z(HalfMoaAt600Mm * step, (11 - step).ToString()))
                      .ToArray(),
            // No aiming mark is published, so the innermost ring is the mark, exactly as it is
            // on our own inch grids above.
            aimingMarkMm: HalfMoaAt600Mm,
            ringLineMm: null,
            boardWidthMm: HalfMoaCardMm, boardHeightMm: HalfMoaCardMm,
            boardLineMm: CardEdgeMm);

    /// <summary>Half a minute of angle at 600 m, to 0.1 mm: 600000 x tan(0.5/60 deg) = 87.266.
    /// Rounded once and then multiplied, which is how the five published-looking figures in the
    /// file this table replaces were themselves produced.</summary>
    private const double HalfMoaAt600Mm = 87.3;

    /// <summary>The square card the half-MOA grid is drawn on.</summary>
    private const double HalfMoaCardMm = 800;

    /// <summary>
    /// Target Rifle D / F: a figure target, and the one face here with no scoring rings at all
    /// — the whole figure scores 1.
    ///
    /// Nobody publishes its outline. The NRA (UK) Rules of Shooting 2026 Appendix V names the
    /// Service figure targets (Fig 11/59, Fig 12/59, Figure 14) and gives the circles drawn on
    /// them, but not the silhouette; the file this table replaces carried a traced 45-point
    /// drawing of it, which is not a published dimension and is not reproduced. So the face is
    /// its frame, which IS published: ICFRA TR Technical Rules 2024 Annex D, D1.2 gives
    /// 1.2 x 1.2 m at 300 yds, the distance this face is shot at.
    ///
    /// Nothing is lost in scoring: faces are drawn, never scored — the scores come from the
    /// ShotMarker CSV — and this one had no ring to hit or miss in the first place.
    /// </summary>
    private static TargetFace FigureDF() =>
        new("TRD", "Target Rifle D / F", "D / F",
            1200, 1200, CardEdgeMm,
            Array.Empty<TargetRing>(),
            Array.Empty<TargetPoly>(),
            Array.Empty<TargetText>());
}
