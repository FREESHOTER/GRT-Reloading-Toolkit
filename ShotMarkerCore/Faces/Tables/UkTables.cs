using static ShotMarker.Core.Faces.Tables.FaceTable;

namespace ShotMarker.Core.Faces.Tables;

/// <summary>
/// The Bisley targets, from the NRA (UK) <b>Rules of Shooting 2026</b>, Appendix V (see paras
/// 221–222), which prints "Current Bisley Target Rifle (TR) Targets" and "Bisley F Class
/// Rifle (F Class) Targets" as two tables of millimetres — one column per distance, one row
/// per division, with the apparent diameter in minutes of angle under each figure.
/// <c>https://nra.org.uk/wp-content/uploads/2025/12/Rules-of-Shooting-2026.pdf</c>
///
/// Everything these faces draw is one of those printed millimetre figures. Four things to
/// name:
///
///   * <b>The TR dimensions changed on 1 January 2025</b> (Appendix V says so, and points at
///     the NRA Journal, Winter 2023, p. 52). Four of ShotMarker's Bisley TR faces are the
///     older target: its 300, 500, 600 and Long Range faces carry the pre-2025 inch figures
///     (3.3/5.5/11/16.5/22 in at 300 yards, where the current target is 78/130/260/390/560
///     mm). The faces here are the 2026 table. The F-Class table did not change: those
///     faces match ShotMarker's to the millimetre it rounded from.
///   * <b>The lowest zone is the frame, not a circle.</b> The TR table's Hit row is "Rectangle
///     measuring" 2997w x 1778h, 1778w x 1524h or 900w x 920h, and note b makes it the edge
///     of scoring: "shots clear outside the dimensions of the 'Hit' region of the targets ...
///     will count as misses". So a TR face has five circles and the frame is the 1. The
///     F-Class Hit row, by contrast, is a diameter, so an F-Class face has six circles.
///   * <b>The frame rectangles are published once.</b> Only the TR table prints them, for the
///     frames both disciplines shoot at a given distance, so <see cref="Frame"/> is what both
///     use. That reproduces ShotMarker's own 500/600/Long Range boards exactly; at 200–400
///     yards it is 900 x 920 against the 46-inch square ShotMarker drew, which is the same
///     pre-2025 target as its stale TR rings.
///   * <b>The F-Class V bull is white</b> — "a circular black aiming mark and a central white
///     V bull", and note c at long range: "a white circle, 5" in diameter, placed in the
///     centre of the X-ring as the F Class V bull" (the table's 128 mm). The TR V bull is
///     black, and note a gives the one line width in the appendix: "The TR/MR V bull line
///     will be continuous, and will be half the thickness of the other scoring rings." No
///     absolute thickness is published, so the other rings follow the house rule and the TR V
///     bull is half of it.
/// </summary>
internal static class UkTables
{
    /// <summary>One column of one table: the published diameters in millimetres.
    /// <paramref name="Hit"/> is the F-Class table's Hit diameter; on a TR column it is null,
    /// because there the Hit is the frame.</summary>
    private sealed record Column(
        string Suffix, string Label,
        double AimingMark, double V, double Bull, double Inner, double Magpie, double Outer,
        double? Hit = null);

    /// <summary>"Current Bisley Target Rifle (TR) Targets", as printed.</summary>
    private static readonly Column[] TargetRifle =
    {
        //       id     name            AimMk   V-Bull  Bull  Inner  Magpie  Outer
        new("200",  "200y",         406,    53.4,   89,    178,   267,    406),
        new("300",  "300y",         560,    78,    130,    260,   390,    560),
        new("400",  "400y",         813,   122,    203,    406,   610,    813),
        new("500",  "500y",         990,   150,    250,    660,   990,   1320),
        new("600",  "600y",         990,   192,    320,    660,   990,   1320),
        new("LR",   "Long Range",  1120,   351,    585,   1120,  1830,   2440),
    };

    /// <summary>"Bisley F Class Rifle (F Class) Targets", as printed. The aiming mark and the
    /// Outer are the same circle in every column, which is what the table says.</summary>
    private static readonly Column[] FClass =
    {
        //       id     name            AimMk   V-Bull  Bull  Inner  Magpie  Outer    Hit
        new("200",  "200y",         203,    25,     51,    102,   152,    203,    254),
        new("300",  "300y",         305,    38,     76,    152,   229,    305,    381),
        new("400",  "400y",         406,    51,    102,    203,   305,    406,    508),
        new("500",  "500y",         508,    64,    127,    254,   381,    508,    635),
        new("600",  "600y",         610,    76,    152,    305,   457,    610,    762),
        new("LR",   "Long Range",  1120,   128,    255,    510,   815,   1120,   1830),
    };

    /// <summary>The TR table's Hit row: the frame the target is pasted to, which the appendix
    /// publishes for a distance rather than for a discipline. 200, 300 and 400 yards share one
    /// rectangle, 500 and 600 the next, and Long Range its own.</summary>
    private static (double W, double H) Frame(string suffix) => suffix switch
    {
        "LR" => (2997, 1778),
        "500" or "600" => (1778, 1524),
        _ => (900, 920),
    };

    public static IEnumerable<TargetFace> Faces()
    {
        foreach (Column c in TargetRifle)
        {
            double line = c.Outer / 200;
            yield return Build("UKTR" + c.Suffix, "Bisley Target Rifle " + c.Label,
                               "TR " + c.Label, c,
                               // Note a: the V bull line is half the thickness of the others.
                               Z(c.V, "V", line / 2), line, whiteCentreMm: 0);
        }

        foreach (Column c in FClass)
            yield return Build("UKFC" + c.Suffix, "Bisley F-Class " + c.Label,
                               "FC " + c.Label, c,
                               Z(c.V, "V"), null, whiteCentreMm: c.V);
    }

    private static TargetFace Build(string id, string name, string shortName, Column c,
                                    Zone vBull, double? ringLineMm, double whiteCentreMm)
    {
        var zones = new List<Zone>
        {
            vBull, Z(c.Bull, "5"), Z(c.Inner, "4"), Z(c.Magpie, "3"), Z(c.Outer, "2"),
        };
        if (c.Hit is not null) zones.Add(Z(c.Hit.Value, "1"));

        (double w, double h) = Frame(c.Suffix);
        return Concentric(id, name, shortName, zones,
                          aimingMarkMm: c.AimingMark,
                          ringLineMm: ringLineMm,
                          boardWidthMm: w, boardHeightMm: h,
                          whiteCentreMm: whiteCentreMm);
    }
}
