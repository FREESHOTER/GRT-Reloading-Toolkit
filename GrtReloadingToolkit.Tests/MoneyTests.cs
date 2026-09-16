using System.Globalization;
using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The inventory hardcoded "EUR" in four places, so a shooter outside the eurozone retyped it
/// on every lot. These pin the shape of the replacement, not this machine's own currency.
/// </summary>
public class MoneyTests
{
    [Fact]
    public void TheDefaultIsAThreeLetterIsoCode()
    {
        // The column stores the code verbatim and the card, the grid and the cost-per-round line
        // all print it straight through, so a symbol or a localised name would leak into all three.
        Assert.Equal(3, Money.Default.Length);
        Assert.Equal(Money.Default.ToUpperInvariant(), Money.Default);
        Assert.All(Money.Default, ch => Assert.True(char.IsAsciiLetterUpper(ch)));
    }

    [Fact]
    public void TheLocalCurrencyIsOfferedFirstAndOnlyOnce()
    {
        // It is the one the user wants on nearly every lot, and a second copy further down the
        // list would sit there looking like a different currency.
        Assert.Equal(Money.Default, Money.Common[0]);
        Assert.Equal(Money.Common.Count, Money.Common.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void TheEurozoneDefaultIsStillThere()
    {
        // FREESHOTER and every other EUR user must still find their currency in the list, even
        // though it is no longer what a new component starts in.
        Assert.Contains("EUR", Money.Common);
        Assert.Contains("USD", Money.Common);
    }

    [Fact]
    public void ACostIsLabelledByWhicheverComponentNamesACurrency()
    {
        // Costing does not convert -- it labels. That is fine for one currency and wrong for two,
        // which is why the dropdown defaults everything to the same one.
        var e = new JournalEntry { ChargeGr = 41.3, PowderId = 1 };
        var powder = new Component { Id = 1, Kind = ComponentKind.Powder, QtyInitial = 454, CostTotal = 45.4, Currency = "USD" };
        Assert.Equal("USD", Costing.PerRound(e, _ => powder).Currency);
        Assert.Equal(Money.Default, Costing.PerRound(new JournalEntry(), _ => null).Currency);
    }
}
