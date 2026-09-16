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

public sealed record CostBreakdown(double Powder, double Primer, double Brass, double Bullet, string Currency)
{
    public double PerRound => Powder + Primer + Brass + Bullet;
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

        // Whichever component names one wins: this labels a total, it does not convert. A lot
        // bought in USD and a lot in EUR added together would be mislabelled -- keep one currency.
        string cur = pw?.Currency ?? pr?.Currency ?? bu?.Currency ?? br?.Currency ?? Money.Default;

        double powder = pw is not null ? e.ChargeGr * GrainToGram * pw.CostPerUnit : 0; // CostPerUnit = per gram
        double primer = pr?.CostPerUnit ?? 0;
        double bullet = bu?.CostPerUnit ?? 0;
        double brass = br?.CostPerUnit ?? 0; // already amortised over ExpectedUses

        return new CostBreakdown(powder, primer, brass, bullet, cur);
    }

    public static string Format(double v, string currency) => $"{v:0.000} {currency}";
}
