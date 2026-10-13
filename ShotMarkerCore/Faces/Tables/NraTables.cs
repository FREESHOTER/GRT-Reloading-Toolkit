namespace ShotMarker.Core.Faces.Tables;

/// <summary>
/// The NRA's own targets, from three rulebooks, all three of which print each target as two
/// columns — "Aiming Black (inches)" and "Rings in White (inches)" — so every face below
/// carries its published ring list and the diameter where the black stops, and nothing else
/// had to be decided:
///
///   * <b>2024 NRA High Power Rifle Rulebook</b>, §4.2–4.7 (the SR/MR family) and §4.8–4.10
///     (the C-1/C-2/C-3 300 m International targets, published in centimetres and inches).
///   * <b>2024 NRA F-Class Rulebook</b>, §4.4–4.7 (the four F-Class target centres). Its
///     note on page 10 is the whole F-Class rule: "the new X-rings are valued 10-X. The old
///     X-ring becomes the new 10-ring, the old 10-ring becomes the new 9-ring, etc." — but
///     each centre is also printed in full, so each is written out rather than derived.
///   * <b>2020 NRA Smallbore Rifle Rulebook</b>, §4.4/§4.6/§4.12 (A-32, A-23/5, A-33),
///     whose §4 states "The dimensions given are to the outside edge of the scoring rings".
///
/// Four things worth naming:
///
///   * <b>Two lowest zones are squares, not circles.</b> HP §4.7 ends the No. LR target at
///     "6 area ... 72x72 square" and F-Class §4.7 ends the LRFC at "5 area ... 72x72 Sq".
///     That square is the target, so it is the frame: a 72-inch board with the 7 (or 6) ring
///     as the outermost circle is the published face exactly.
///   * <b>The C-3 rulebook has a typo.</b> §4.10 prints the 5 ring as 22.60 in. Every other
///     C-3 ring is exactly 0.9144 of its C-1 counterpart (36 in standing in for 100 cm),
///     which puts the 5 ring at 21.60 — and 21.60 is also what the ring either side of it
///     requires. 21.60 is used.
///   * <b>Paper size is published for exactly one target.</b> §4.3(d) gives the SR-5 as
///     "paper size 28 x 28 inches", and §4.7's two squares are their own frames. Every other
///     NRA frame here follows the house rule in <see cref="FaceTable"/>.
///   * <b>Three of these are printed cards of several bullseyes.</b> §4.4 names the A-32 as
///     "6 bullseyes (1 for sighting)", §4.6 the A-23/5 as "5 bullseyes" and §4.12 the A-33 as
///     "3 bullseyes" — the count and nothing else. No NRA rulebook publishes the card size or
///     where on it the bulls sit; it publishes only the ring diameters, which are the same for
///     each bull. They are therefore authored as the published bullseye, centred, so every
///     dimension on the face is one the rulebook states. The shot coordinates a face is drawn
///     under are measured from one aiming centre anyway, so a card of identical bulls and a
///     single bull plot the same group; what a measured official card would add is the paper
///     around it.
/// </summary>
internal static class NraTables
{
    /// <summary>One published ring: diameter in the rulebook's own inches, and the score
    /// printed beside it.</summary>
    private sealed record R(double DiamIn, string? Score);

    /// <summary>
    /// A target as the rulebook prints it: the ring list, and the diameter at which the
    /// "Aiming Black" column stops.
    /// </summary>
    /// <param name="aimingBlackIn">The last diameter in the Aiming Black column.</param>
    /// <param name="boardIn">The published paper size, or null for the house rule.</param>
    private static TargetFace Nra(string id, string name, string shortName,
                                  double aimingBlackIn, double? boardIn, params R[] rings) =>
        FaceTable.Concentric(
            id, name, shortName,
            rings.Select(r => FaceTable.ZIn(r.DiamIn, r.Score)).ToArray(),
            FaceTable.In(aimingBlackIn), null,
            boardIn is null ? null : FaceTable.In(boardIn.Value),
            boardIn is null ? null : FaceTable.In(boardIn.Value));

    /// <summary>§4.8's C-1 is published in centimetres; the other two C targets in inches.</summary>
    private static TargetFace NraCm(string id, string name, string shortName,
                                    double aimingBlackCm, params (double DiamCm, string Score)[] rings) =>
        FaceTable.Concentric(
            id, name, shortName,
            rings.Select(r => FaceTable.Z(r.DiamCm * 10, r.Score)).ToArray(),
            aimingBlackCm * 10, null);

    // HP §4.7(a) "NRA No. LR" and F-Class §4.7 "NRA No. LRFC", kept as named lists because
    // the two reduced faces further down are scalings of them and must not drift.
    private static readonly R[] LongRange =
    {
        new(10, "X"), new(20, "10"), new(30, "9"), new(44, "8"), new(60, "7"),
    };

    private static readonly R[] LongRangeFClass =
    {
        new(5, "X"), new(10, "10"), new(20, "9"), new(30, "8"), new(44, "7"), new(60, "6"),
    };

    // HP §4.6(a) "MR-1 target".
    private static readonly R[] Mr1 =
    {
        new(6, "X"), new(12, "10"), new(18, "9"), new(24, "8"),
        new(36, "7"), new(48, "6"), new(60, "5"),
    };

    /// <summary>Every ring of a published target at one scale factor, for the three reduced
    /// faces below. Nothing else in this folder scales a ring.</summary>
    private static R[] Scale(R[] rings, double by) =>
        rings.Select(r => new R(r.DiamIn * by, r.Score)).ToArray();

    public static IEnumerable<TargetFace> Faces()
    {
        // ---- High Power §4.2, 100 yards ----
        yield return Nra("NRA_SR1", "NRA SR-1", "SR-1 (100)", 6.35, null,
            new(1.35, "X"), new(3.35, "10"), new(6.35, "9"),
            new(9.35, "8"), new(12.35, "7"), new(15.35, "6"), new(18.35, "5"));
        yield return Nra("NRA_SR21", "NRA SR-21", "SR-21 (100)", 6.12, null,
            new(0.79, "X"), new(2.12, "10"), new(4.12, "9"), new(6.12, "8"),
            new(8.12, "7"), new(10.12, "6"), new(12.12, "5"));
        yield return Nra("NRA_MR31", "NRA MR-31", "MR-31 (100)", 5.75, null,
            new(0.75, "X"), new(1.75, "10"), new(2.75, "9"), new(3.75, "8"), new(5.75, "7"),
            new(7.75, "6"), new(9.75, "5"));

        // ---- High Power §4.3, 200 yards ----
        yield return Nra("NRA_SR", "NRA SR", "SR (200)", 13, null,
            new(3, "X"), new(7, "10"), new(13, "9"),
            new(19, "8"), new(25, "7"), new(31, "6"), new(37, "5"));
        yield return Nra("NRA_SR42", "NRA SR-42", "SR-42 (200)", 12.56, null,
            new(1.90, "X"), new(4.56, "10"), new(8.56, "9"), new(12.56, "8"),
            new(16.56, "7"), new(20.56, "6"), new(24.56, "5"));
        yield return Nra("NRA_MR52", "NRA MR-52", "MR-52 (200)", 11.79, null,
            new(1.79, "X"), new(3.79, "10"), new(5.79, "9"), new(7.79, "8"), new(11.79, "7"),
            new(15.79, "6"), new(19.79, "5"));
        // §4.3(d): "Same scoring ring dimensions as No. SR Military with scoring rings
        // through 7 only; paper size 28 x 28 inches."
        yield return Nra("NRA_SR5", "NRA SR-5", "SR-5 (200)", 13, 28,
            new(3, "X"), new(7, "10"), new(13, "9"), new(19, "8"), new(25, "7"));

        // ---- High Power §4.4, 300 yards ----
        // §4.4(a): "Enlarged aiming black ... Scoring rings the same as the SR target, with
        // the 8-ring in the aiming black" — which is the only difference from the SR.
        yield return Nra("NRA_SR3", "NRA SR-3", "SR-3 (300)", 19, null,
            new(3, "X"), new(7, "10"), new(13, "9"), new(19, "8"),
            new(25, "7"), new(31, "6"), new(37, "5"));
        yield return Nra("NRA_MR63", "NRA MR-63", "MR-63 (300)", 17.85, null,
            new(2.85, "X"), new(5.85, "10"), new(8.85, "9"), new(11.85, "8"), new(17.85, "7"),
            new(23.85, "6"), new(29.85, "5"));
        yield return Nra("NRA_MR63FC", "NRA MR-63FC", "MR-63FC (300)", 17.85, null,
            new(1.42, "X"), new(2.85, "10"), new(5.85, "9"), new(8.85, "8"), new(11.85, "7"),
            new(17.85, "6"), new(23.85, "5"));

        // ---- §4.5, 500 yards. The rulebook calls it MR-65; ShotMarker files name it "NRA
        // MR", so that is the name a stored shoot looks up. ----
        yield return Nra("NRA_MR", "NRA MR", "MR (500)", 30, null,
            new(5, "X"), new(10, "10"), new(15, "9"), new(20, "8"), new(25, "7"),
            new(30, "6"), new(36, "5"));
        // F-Class §4.5 prints its white column as "(None": every ring is in the black.
        yield return Nra("NRA_MR65FC", "NRA MR-65FC", "MR-65FC (500)", 30, null,
            new(2.50, "X"), new(5, "10"), new(10, "9"), new(15, "8"), new(20, "7"),
            new(25, "6"), new(30, "5"));

        // ---- §4.6, 600 yards ----
        yield return Nra("NRA_MR1", "NRA MR-1", "MR-1 (600)", 36, null, Mr1);
        yield return Nra("NRA_MR1FC", "NRA MR-1FC", "MR-1FC (600)", 36, null,
            new(3, "X"), new(6, "10"), new(12, "9"), new(18, "8"), new(24, "7"),
            new(36, "6"), new(48, "5"));

        // ---- §4.7, 800/900/1000 yards. One target for all three distances, and the lowest
        // zone is the 72-inch square the rings are printed on. ----
        yield return Nra("NRA_LR", "NRA Long Range", "LR (800-1000)", 44, 72, LongRange);
        yield return Nra("NRA_LRFC", "NRA Long Range FC", "LRFC (800-1000)", 44, 72, LongRangeFClass);

        // ---- High Power §4.8–4.10, the 300 m International targets. "The scoring rings 5
        // through inner 10 are black", so the black ends at the 5 ring on all three. ----
        yield return NraCm("NRA_C1", "NRA C-1", "C-1 (300m)", 60,
            (5, "X"), (10, "10"), (20, "9"), (30, "8"), (40, "7"), (50, "6"),
            (60, "5"), (70, "4"), (80, "3"), (90, "2"), (100, "1"));
        yield return Nra("NRA_C2", "NRA C-2", "C-2 (200y)", 14.40, null,
            new(1.20, "X"), new(2.40, "10"), new(4.80, "9"), new(7.20, "8"), new(9.60, "7"),
            new(12.00, "6"), new(14.40, "5"), new(16.80, "4"), new(19.20, "3"),
            new(21.60, "2"), new(24.00, "1"));
        yield return Nra("NRA_C3", "NRA C-3", "C-3 (300y)", 21.60, null,
            new(1.80, "X"), new(3.60, "10"), new(7.20, "9"), new(10.80, "8"), new(14.40, "7"),
            new(18.00, "6"), new(21.60, "5"), new(25.20, "4"), new(28.80, "3"),
            new(32.40, "2"), new(36.00, "1"));

        // ---- Smallbore §4.12: "ISSF 300 meter target reduced to 100 yards ... 4 through 10
        // rings black." No X ring is published for the A-33. ----
        yield return Nra("NRA_A33", "NRA A-33", "NRA A-33", 8.245, null,
            new(1.045, "10"), new(2.245, "9"), new(3.445, "8"), new(4.645, "7"),
            new(5.845, "6"), new(7.045, "5"), new(8.245, "4"), new(9.445, "3"),
            new(10.645, "2"), new(11.845, "1"));
        // Smallbore §4.6: "7 through 10 rings black." The outermost published ring, 5.89 in,
        // scores 5 — ShotMarker's library scores it 6, the same as the 4.89 ring.
        yield return Nra("NRA_A23_5", "NRA A-23/5", "NRA A-23/5 (50)", 3.89, null,
            new(0.39, "X"), new(0.89, "10"), new(1.89, "9"), new(2.89, "8"), new(3.89, "7"),
            new(4.89, "6"), new(5.89, "5"));
        yield return A32();

        // ---- Ours, not the NRA's: reductions of a published NRA target, named for what
        // they are. Each is the parent's own ring list at one scale factor and nothing else,
        // so the parent rulebook row is the only dimension involved. ----
        //
        // There is no NRA 800-yard target: §4.7's No. LR is used unchanged at 800, 900 and
        // 1000 yards. These two are 0.8 reductions of it — the scale that makes a 1000-yard
        // target subtend the same angle at 800 — including its 72-inch square, which becomes
        // 57.6.
        yield return Nra("NRA_LR_800", "NRA LR at 0.8 Scale", "NRA LR x0.8",
                         44 * 0.8, 72 * 0.8, Scale(LongRange, 0.8));
        yield return Nra("NRA_LRFC_800", "NRA LFRC at 0.8 Scale", "NRA LRFC x0.8",
                         44 * 0.8, 72 * 0.8, Scale(LongRangeFClass, 0.8));
        // Two thirds of the 600-yard MR-1, which is the right reduction for 400 yards.
        yield return Nra("NRA_MR1_400", "NRA MR-1 at 2/3 Scale", "MR-1 x2/3",
                         36 * 2 / 3.0, null, Scale(Mr1, 2 / 3.0));
        // The FCSA publishes no rulebook that could be found, and the rings a ShotMarker
        // file carries under this name are the NRA MR-1's exactly, so that published row is
        // what is drawn. The face exists so a file naming "FCSA MR-1" still resolves; no
        // dimension here is claimed to be the FCSA's.
        yield return Nra("FCSA_MR1", "FCSA MR-1", "FCSA MR-1", 36, null, Mr1);
    }

    /// <summary>
    /// Smallbore §4.4, the 50-foot Light Rifle target: "8 through 10 rings black, with white
    /// dot in center of 10 ring", then "Aiming dot .218 inch". That white dot is the one
    /// published white centre among the NRA's targets; it carries no score of its own — it
    /// sits inside the 10 ring and is there to aim at.
    /// </summary>
    private static TargetFace A32() =>
        FaceTable.Concentric(
            "NRA_A32", "NRA A-32", "NRA A-32 (50ft)",
            new[]
            {
                FaceTable.ZIn(0.439, "10"), FaceTable.ZIn(1.187, "9"), FaceTable.ZIn(1.874, "8"),
                FaceTable.ZIn(2.656, "7"), FaceTable.ZIn(3.374, "6"),
            },
            aimingMarkMm: FaceTable.In(1.874),
            ringLineMm: null,
            whiteCentreMm: FaceTable.In(0.218));
}
