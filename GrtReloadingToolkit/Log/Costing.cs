using System.Globalization;

namespace GrtReloadingToolkit.Log;

/// <summary>
/// What the inventory counts money in. The toolkit was written in euros and hardcoded "EUR" in
/// four places, so every shooter outside the eurozone retyped it on every lot they added.
/// </summary>
public static class Money
{
    /// <summary>
    /// The currency a new component starts in, read from the machine's Windows region.
    /// <c>ISOCurrencySymbol</c> is the three-letter code the column stores, so no mapping is
    /// needed. Falls back to EUR, which is what the column has always defaulted to.
    /// </summary>
    public static string Default { get; } = Local();

    /// <summary>
    /// Offered in the dropdown, local one first. The box stays editable, because this list is a
    /// convenience and not a whitelist -- a shooter buying in NOK or ZAR types it and it sticks.
    /// </summary>
    public static IReadOnlyList<string> Common { get; } =
        new[] { Default }.Concat(new[] { "EUR", "USD", "GBP", "CHF", "CAD", "AUD", "SEK", "NOK", "DKK", "CZK", "PLN" })
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // RegionInfo.CurrentRegion throws on a machine running the invariant culture (a locked-down
    // server, or DOTNET_SYSTEM_GLOBALIZATION_INVARIANT), which is not a reason to fail to open
    // an inventory window.
    private static string Local()
    {
        try
        {
            string c = RegionInfo.CurrentRegion.ISOCurrencySymbol;
            return c.Length == 3 ? c.ToUpperInvariant() : "EUR";
        }
        catch (Exception) { return "EUR"; }
    }
}

public sealed record CostBreakdown(double Powder, double Primer, double Brass, double Bullet, string Currency, IReadOnlyList<string> Currencies)
{
    /// <summary>
    /// The sum of the four lines. Only a cost when <see cref="Mixed"/> is false: adding a dollar
    /// to a euro gives a number and not an amount, so every display goes through
    /// <see cref="PerRoundText"/> rather than formatting this itself.
    /// </summary>
    public double PerRound => Powder + Primer + Brass + Bullet;

    /// <summary>
    /// The lots that priced this round were bought in more than one currency. Nothing here
    /// converts between them -- a rate is a fact about a day, not about a component -- so the
    /// honest answer is to name the mismatch instead of totalling through it.
    /// </summary>
    public bool Mixed => Currencies.Count > 1;

    /// <summary>What goes where the cost would have gone: "mixed EUR/USD".</summary>
    public string MixedNote => "mixed " + string.Join("/", Currencies);

    /// <summary>
    /// The cost of one round, or why there is not one. Every window that shows a cost per round
    /// shows this, so none of them can print a total across two currencies.
    /// </summary>
    public string PerRoundText => Mixed ? MixedNote
        : PerRound > 0 ? Costing.Format(PerRound, Currency)
        : "";
}

public static class Costing
{
    private const double GrainToGram = 0.06479891;

    /// <summary>Cost of one loaded round for this entry, using the referenced component lots.</summary>
    public static CostBreakdown PerRound(JournalEntry e, Func<long, Component?> lookup)
    {
        Component? pw = e.PowderId is { } a ? lookup(a) : null;
        Component? pr = e.PrimerId is { } b ? lookup(b) : null;
        Component? br = e.BrassId is { } c ? lookup(c) : null;
        Component? bu = e.BulletId is { } d ? lookup(d) : null;

        double powder = pw is not null ? e.ChargeGr * GrainToGram * pw.CostPerUnit : 0; // CostPerUnit = per gram
        double primer = pr?.CostPerUnit ?? 0;
        double bullet = bu?.CostPerUnit ?? 0;
        double brass = br?.CostPerUnit ?? 0; // already amortised over ExpectedUses

        // The currencies that money is actually being added in. A lot with no price on it names a
        // currency too, but nothing of its is in the sum, so counting it would report a mismatch
        // that costs the user nothing -- only a line that contributes gets a say.
        var currencies = new[] { (powder, pw), (primer, pr), (brass, br), (bullet, bu) }
            .Where(t => t.Item1 > 0 && t.Item2 is not null)
            .Select(t => Name(t.Item2!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Whichever priced component names one first labels the total. With one currency that is
        // simply the currency; with several the total is not shown at all, so it is only a label
        // for the lines.
        string cur = currencies.Count > 0 ? currencies[0]
            : pw is not null ? Name(pw)
            : pr is not null ? Name(pr)
            : bu is not null ? Name(bu)
            : br is not null ? Name(br)
            : Money.Default;

        return new CostBreakdown(powder, primer, brass, bullet, cur, currencies);

        // An older row can hold an empty currency, which is not a third currency -- it is a lot
        // added before the column had a default.
        static string Name(Component c) =>
            string.IsNullOrWhiteSpace(c.Currency) ? Money.Default : c.Currency.Trim().ToUpperInvariant();
    }

    public static string Format(double v, string currency) => $"{v:0.000} {currency}";
}
