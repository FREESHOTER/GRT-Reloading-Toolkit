namespace ShotMarker.Core.Faces.Tables;

/// <summary>
/// Shared machinery for the face tables in this folder.
///
/// Every face here is authored from the governing body's own published dimensions, written
/// out once per body with the citation in a comment beside it, and expanded by that body's
/// own published derivation rule. The code is the citation: a diameter with no rulebook
/// behind it is not in these tables at all.
///
/// Three things the rulebooks do NOT publish, decided here once and applied to every face
/// rather than guessed per face:
///
///   * <b>Fill.</b> A target's published "aiming mark" row is the black/white boundary, so
///     every ring at or inside it is filled black and everything outside it white. A handful
///     of bodies also publish a white mark inside the black — the Bisley F-Class white V
///     bull, the NRA A-32's white aiming dot — and inside that the face is white again. A
///     ring that IS one of those two boundaries needs no outline, because the change of fill
///     draws it, so it gets line 0. Where a body publishes either boundary without making it
///     a scoring boundary, <see cref="Concentric"/> inserts it as an unscored ring.
///   * <b>Numerals.</b> Each score is drawn once, at the radial midpoint of the band that
///     shows it, sized to 60% of that band's width so it cannot overflow the band it
///     labels; the innermost score goes at the centre, sized to 60% of its radius. White
///     inside the black, grey outside it, because grey-on-black cannot be read.
///   * <b>Frame.</b> Where a body publishes a target frame size it is used and cited. Where
///     none is published the frame is the outermost ring plus an eighth of its diameter all
///     round — enough white to show that the outer ring is the outer ring. Its edge is
///     drawn at the ring line width unless the body publishes a non-scoring border (as
///     ICFRA and the DCRA do), in which case that border is the edge.
///   * <b>Line width.</b> Where a body publishes one it is used. Where none is published
///     every ring is drawn at a two-hundredth of the outermost ring's diameter, which is
///     about what a printed target's lines come to at any distance.
/// </summary>
internal static class FaceTable
{
    public const double MmPerInch = 25.4;

    /// <summary>Inches to millimetres. Several bodies publish in inches only (NRA, DCRA,
    /// IBS, SABU's long range table); they are stored in their published unit and converted
    /// here, so the rulebook's own figures are what appears in the table.</summary>
    public static double In(double inches) => inches * MmPerInch;

    /// <summary>One scoring zone: the circle that bounds it and the score inside it.
    /// <paramref name="LineMm"/> overrides the face's ring line width for bodies that
    /// publish a different width for one ring (ISSF's inner ten, the NRAA's V).</summary>
    internal readonly record struct Zone(double DiamMm, string? Score, double? LineMm = null);

    public static Zone Z(double diamMm, string? score, double? lineMm = null) =>
        new(diamMm, score, lineMm);

    /// <summary>A zone published in inches.</summary>
    public static Zone ZIn(double diamIn, string? score, double? lineMm = null) =>
        new(In(diamIn), score, lineMm);

    /// <summary>
    /// Builds a face from a body's published ring table: zones smallest first, the published
    /// aiming-mark diameter, and the published ring line width.
    /// </summary>
    /// <param name="aimingMarkMm">The published aiming mark (the black). 0 means the body
    /// publishes none, and the whole face is drawn on white.</param>
    /// <param name="ringLineMm">The published ring line width, or null where the body
    /// publishes none.</param>
    /// <param name="boardWidthMm">The published frame width, or null to size it from the
    /// outermost ring.</param>
    /// <param name="boardLineMm">The published non-scoring border, or null to draw the
    /// frame edge at the ring line width.</param>
    /// <param name="whiteCentreMm">A published white mark inside the black, or 0 where the
    /// body publishes none.</param>
    public static TargetFace Concentric(
        string id, string name, string shortName,
        IReadOnlyList<Zone> zones,
        double aimingMarkMm,
        double? ringLineMm,
        double? boardWidthMm = null,
        double? boardHeightMm = null,
        double? boardLineMm = null,
        IReadOnlyList<TargetPoly>? polys = null,
        IReadOnlyList<TargetText>? extraTexts = null,
        double whiteCentreMm = 0)
    {
        if (zones.Count == 0) throw new ArgumentException("no zones", nameof(zones));
        for (int i = 1; i < zones.Count; i++)
            if (zones[i].DiamMm <= zones[i - 1].DiamMm)
                throw new ArgumentException(
                    $"{id}: zones must be ordered smallest first ({zones[i - 1].DiamMm} then {zones[i].DiamMm})",
                    nameof(zones));

        // A published fill boundary that is not itself a scoring boundary still has to be
        // drawn: it is where the face turns from black to white, or back to white again.
        var all = new List<Zone>(zones);
        foreach (double boundary in new[] { whiteCentreMm, aimingMarkMm })
            if (boundary > 0 && !all.Any(z => Same(z.DiamMm, boundary)))
            {
                int at = all.FindIndex(z => z.DiamMm > boundary);
                all.Insert(at < 0 ? all.Count : at, new Zone(boundary, null));
            }

        double outer = all[^1].DiamMm;
        double line = ringLineMm ?? outer / 200;

        var rings = all.Select(z => new TargetRing(
            z.DiamMm,
            z.DiamMm <= aimingMarkMm + Tolerance && z.DiamMm > whiteCentreMm + Tolerance ? "b" : "w",
            Same(z.DiamMm, aimingMarkMm) || Same(z.DiamMm, whiteCentreMm) ? 0 : z.LineMm ?? line,
            z.Score)).ToArray();

        double w = boardWidthMm ?? outer * 1.25;
        double h = boardHeightMm ?? boardWidthMm ?? outer * 1.25;

        return new TargetFace(id, name, shortName, w, h, boardLineMm ?? line,
                              rings,
                              polys ?? Array.Empty<TargetPoly>(),
                              Numerals(rings).Concat(extraTexts ?? Array.Empty<TargetText>()).ToArray());
    }

    /// <summary>The score numerals for a ring list, by the rule in the class comment.
    /// Public because the handful of faces that build their rings directly (the IBS group
    /// targets, which are not concentric) still want the same numerals.</summary>
    public static IReadOnlyList<TargetText> Numerals(IReadOnlyList<TargetRing> rings)
    {
        var texts = new List<TargetText>();
        var ordered = rings.OrderBy(r => r.DiamMm).ToList();
        double inner = 0;
        foreach (TargetRing r in ordered)
        {
            double outer = r.DiamMm / 2;
            if (!string.IsNullOrEmpty(r.Score))
                texts.Add(new TargetText(
                    inner == 0 ? 0 : (inner + outer) / 2,
                    0,
                    0.6 * (outer - inner),
                    r.Color == "b" ? "wl" : "gl",
                    r.Score!));
            inner = outer;
        }
        return texts;
    }

    /// <summary>A square, centred on the face — how every body that publishes one publishes
    /// an aiming square (IBS) or a scoring square (the NRA's long range outer).</summary>
    public static TargetPoly Square(double sideMm, string color, double lineMm,
                                    double xMm = 0, double yMm = 0) =>
        Rect(sideMm, sideMm, color, lineMm, xMm, yMm);

    /// <summary>A rectangle, for the bodies that publish a printed border box on the card
    /// rather than a square aiming mark (IBS).</summary>
    public static TargetPoly Rect(double widthMm, double heightMm, string color, double lineMm,
                                  double xMm = 0, double yMm = 0)
    {
        double w = widthMm / 2, h = heightMm / 2;
        return new TargetPoly(color, lineMm, new[]
        {
            new TargetPoint(xMm - w, yMm - h),
            new TargetPoint(xMm + w, yMm - h),
            new TargetPoint(xMm + w, yMm + h),
            new TargetPoint(xMm - w, yMm + h),
        });
    }

    /// <summary>Published diameters are exact numbers, but a conversion from inches lands a
    /// hair off, so "is this ring the aiming mark" is asked to a tenth of a millimetre.</summary>
    private const double Tolerance = 0.05;

    private static bool Same(double a, double b) => Math.Abs(a - b) < Tolerance;
}
