using static ShotMarker.Core.Faces.Tables.FaceTable;

namespace ShotMarker.Core.Faces.Tables;

/// <summary>
/// The Swiss combined A target, in its ten-zone and five-zone divisions.
///
/// Sources, two of them, independent and in agreement:
///   * Schiessverordnung des VBS (SR 512.311), Anhang 2 "Scheibenordnung (Ordonnanzscheiben)"
///     §1, in force 1 January 2023 — the governing federal ordinance. Verbatim: "Die
///     kombinierte Scheibe A hat einen weissen Grund von mindestens 150 cm Breite und 165 cm
///     Höhe und als Zentrum einen schwarzen Kreis von 60 cm Durchmesser. Das Trefferfeld von
///     100 cm Durchmesser ist in fünf bzw. zehn gleichmässige Kreise eingeteilt."
///     https://fedlex.data.admin.ch/filestore/fedlex.data.admin.ch/eli/cc/2004/1/20230101/de/pdf-a/fedlex-data-admin-ch-eli-cc-2004-1-20230101-de-pdf-a-1.pdf
///   * SSV / Swiss Shooting "Technische Regeln Gewehr" DOK 1.10.4022 Rev 14 (9 July 2021),
///     Art. 27 — "Scheibe 10 — Durchmesser des Zehners 100 mm", "Innenzehn — Durchmesser
///     50mm", "Trefferfelddurchmesser 1000mm", "Schwarzdurchmesser 600mm".
///     https://www.swissshooting.ch/media/0xikjm12/dok-1104020_4029-d.pdf
///
/// So every figure here is published twice: a 1000 mm hit field divided into five or ten even
/// circles, a 600 mm black, a 100 mm ten and a 50 mm inner ten. Neither document publishes a
/// ring line width, so the house rule in <see cref="FaceTable"/> applies.
///
/// Art. 27.1 adds "Zugelassen sind Scheibenbilder gemäss Schiessverordnung VBS und ISSF", which
/// is why the A10 comes out diameter-identical to the ISSF 300 m target in
/// <see cref="MiscTables"/>: the two bodies authorise the same face.
///
/// Not here, and deliberately: Scheibe A100, whose hundred 10 mm zones are an inference from
/// the ordinance's "gleichmässige Kreise" pattern rather than a printed table (Art. 27.1.c
/// admits a Scheibe 100 but only "elektronisch ausgewertet laut Umrechnungstabelle", and does
/// not reproduce that table); and the kombinierte Feldscheibe B, whose four-field variant the
/// ordinance leaves undimensioned.
/// </summary>
internal static class SwissTables
{
    /// <summary>The published hit field, black and inner ten, in millimetres.</summary>
    private const double HitFieldMm = 1000;

    private const double BlackMm = 600;

    private const double InnerTenMm = 50;

    private const double TenMm = 100;

    /// <summary>Anhang 2 §1's white ground, which it gives as a minimum.</summary>
    private const double GroundWidthMm = 1500;

    private const double GroundHeightMm = 1650;

    /// <summary>200 yards is 182.88 m, so a 300 m face subtends the same angle at 200 yd when
    /// every dimension is multiplied by 182.88 / 300.</summary>
    private const double TwoHundredYardScale = 182.88 / 300.0;

    public static IEnumerable<TargetFace> Faces()
    {
        yield return TenZone();
        yield return FiveZone("SW_A5", "Swiss A5", "Swiss A5", 1.0);

        // Ours, not Switzerland's: the published 300 m five-zone face reduced to the same
        // angular size at 200 yards. No body publishes a 200 yd Swiss target; the scale factor
        // is the arithmetic above and nothing more is claimed for it. The white ground scales
        // with the rings, so the whole face stays angularly identical to the published one.
        yield return FiveZone("SW_A5_200Y", "Swiss A5 (200y)", "Swiss A5 (200y)",
                              TwoHundredYardScale);
    }

    /// <summary>"zehn gleichmässige Kreise" across the 1000 mm hit field — 100, 200 ... 1000 —
    /// with Art. 27's 50 mm Innenzehn inside the 100 mm ten.</summary>
    private static TargetFace TenZone()
    {
        var zones = new List<Zone> { Z(InnerTenMm, "X") };
        for (int score = 10; score >= 1; score--)
            zones.Add(Z(TenMm * (11 - score), score.ToString()));

        return Concentric("SW_A10", "Swiss A10", "Swiss A10",
                          zones,
                          aimingMarkMm: BlackMm,
                          ringLineMm: null,
                          boardWidthMm: GroundWidthMm, boardHeightMm: GroundHeightMm);
    }

    /// <summary>"fünf gleichmässige Kreise" across the same field — 200, 400, 600, 800, 1000.
    /// The SSV's own ring-pairing rule for Scheibe 5 ("Schusswert Zehn und Neun = 5, Acht und
    /// Sieben = 4 usw.") reaches the same five diameters from the ten-zone face, so the two
    /// published routes agree.</summary>
    private static TargetFace FiveZone(string id, string name, string shortName, double scale)
    {
        var zones = new List<Zone>();
        for (int score = 5; score >= 1; score--)
            zones.Add(Z(HitFieldMm / 5 * (6 - score) * scale, score.ToString()));

        return Concentric(id, name, shortName,
                          zones,
                          aimingMarkMm: BlackMm * scale,
                          ringLineMm: null,
                          boardWidthMm: GroundWidthMm * scale,
                          boardHeightMm: GroundHeightMm * scale);
    }
}
