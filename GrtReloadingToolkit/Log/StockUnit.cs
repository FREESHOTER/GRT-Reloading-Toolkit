using GrtPluginKit.Grt;

namespace GrtReloadingToolkit.Log;

/// <summary>
/// The unit a lot is counted in, and the arithmetic between it and the unit the store holds.
///
/// Powder stock is kept in grams because that is what the ledger moves: <c>ApplyStock</c> turns a
/// charge in grains into grams and subtracts it, and <see cref="Component.CostPerUnit"/> is a
/// price per gram. Nobody buys powder in grams, though — it comes in one- and eight-pound jugs in
/// the US and half- and one-kilo tubs in Europe, and a shooter checking what is left thinks in
/// the unit on the label. So <see cref="Component.Unit"/> is what the lot is *counted* in and the
/// stored number stays grams; everything the user reads or types goes through here.
///
/// Pieces are their own unit with a factor of one, which is what lets a single table serve every
/// kind and keeps the conversion free of a per-kind branch.
/// </summary>
public static class StockUnit
{
    /// <summary>Primers, bullets, brass and barrels: counted, not weighed.</summary>
    public const string Pieces = "pcs";

    /// <summary>What powder stock is stored in, whatever the lot is counted in.</summary>
    public const string Gram = "g";

    // How many stored units one of these is worth. A grain is exactly 64.79891 mg and a pound is
    // exactly 453.59237 g, so both factors are exact to the digits written; the grain one is
    // GrtUnits.GrainsPerGram inverted rather than a second copy of the same constant.
    private static readonly Dictionary<string, double> PerUnit = new(StringComparer.OrdinalIgnoreCase)
    {
        [Pieces] = 1,
        [Gram] = 1,
        ["gr"] = 1 / GrtUnits.GrainsPerGram,
        ["lb"] = 453.59237,
        ["kg"] = 1000,
    };

    private static readonly string[] PowderUnits = { Gram, "gr", "lb", "kg" };
    private static readonly string[] PieceUnits = { Pieces };

    /// <summary>The units this kind may be counted in, the usual one first.</summary>
    public static IReadOnlyList<string> For(ComponentKind kind) =>
        kind == ComponentKind.Powder ? PowderUnits : PieceUnits;

    /// <summary>What a new lot of this kind starts out counted in.</summary>
    public static string DefaultFor(ComponentKind kind) => For(kind)[0];

    /// <summary>
    /// Stored units per counted unit. An unknown unit is worth one, so a row written before this
    /// existed, or a unit typed by hand, reads as itself instead of being scaled by a guess.
    /// </summary>
    public static double Factor(string? unit) =>
        unit is not null && PerUnit.TryGetValue(unit.Trim(), out double f) ? f : 1;

    /// <summary>A number the user typed, in <paramref name="unit"/>, as the store holds it.</summary>
    public static double ToStore(double shown, string? unit) => shown * Factor(unit);

    /// <summary>A stored number as the user counts it.</summary>
    public static double FromStore(double stored, string? unit) => stored / Factor(unit);

    /// <summary>
    /// Places to show. A pound of powder is spent about 0.006 lb at a time, so two places would
    /// round a round's worth of it away; grams and pieces never need that many.
    /// </summary>
    public static int Decimals(string? unit) => Factor(unit) >= 100 ? 3 : 1;

    /// <summary>The matching numeric format, for a grid cell or a label.</summary>
    public static string Format(string? unit) => "0." + new string('#', Decimals(unit));
}
