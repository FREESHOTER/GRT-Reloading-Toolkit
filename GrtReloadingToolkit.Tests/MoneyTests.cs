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
    public void ACostIsLabelledByWhicheverPricedComponentNamesACurrency()
    {
        // Costing does not convert -- it labels. With one currency that label is simply the
        // currency the lots were bought in.
        var e = new JournalEntry { ChargeGr = 41.3, PowderId = 1 };
        var powder = new Component { Id = 1, Kind = ComponentKind.Powder, QtyInitial = 454, CostTotal = 45.4, Currency = "USD" };
        var cb = Costing.PerRound(e, _ => powder);
        Assert.Equal("USD", cb.Currency);
        Assert.False(cb.Mixed);
        Assert.Equal(Money.Default, Costing.PerRound(new JournalEntry(), _ => null).Currency);
    }

    [Fact]
    public void TwoCurrenciesInOneRoundAreNamedRatherThanAddedUp()
    {
        // A dollar of powder and a euro of bullet summed to 2 of whichever currency was named
        // first. The sum is still there for the caller that wants the components, but nothing
        // prints it: a number in neither currency is worse than no number.
        var e = new JournalEntry { ChargeGr = 41.3, PowderId = 1, BulletId = 2 };
        var powder = new Component { Id = 1, Kind = ComponentKind.Powder, QtyInitial = 454, CostTotal = 45.4, Currency = "USD" };
        var bullet = new Component { Id = 2, Kind = ComponentKind.Bullet, QtyInitial = 100, CostTotal = 60, Currency = "EUR" };
        var cb = Costing.PerRound(e, id => id == 1 ? powder : bullet);

        Assert.True(cb.Mixed);
        Assert.Equal("mixed USD/EUR", cb.MixedNote);
        Assert.Equal(cb.MixedNote, cb.PerRoundText);
        Assert.DoesNotContain(cb.PerRound.ToString("0.000", CultureInfo.InvariantCulture), cb.PerRoundText);
    }

    [Fact]
    public void ALotWithNoPriceOnItDoesNotMakeARoundMixed()
    {
        // It names a currency, but none of its money is in the sum, so reporting a mismatch
        // would cost the user a cost-per-round they were entitled to.
        var e = new JournalEntry { ChargeGr = 41.3, PowderId = 1, BulletId = 2 };
        var powder = new Component { Id = 1, Kind = ComponentKind.Powder, QtyInitial = 454, CostTotal = 45.4, Currency = "USD" };
        var bullet = new Component { Id = 2, Kind = ComponentKind.Bullet, QtyInitial = 100, CostTotal = 0, Currency = "EUR" };
        var cb = Costing.PerRound(e, id => id == 1 ? powder : bullet);

        Assert.False(cb.Mixed);
        Assert.Equal("USD", cb.Currency);
        Assert.Contains("USD", cb.PerRoundText);
    }

    [Fact]
    public void AnEmptyCurrencyIsTheDefaultAndNotAThirdCurrency()
    {
        // Lots added before the column had a default carry "". Treating that as its own currency
        // would report a mismatch against the very currency it means.
        var e = new JournalEntry { ChargeGr = 41.3, PowderId = 1, BulletId = 2 };
        var powder = new Component { Id = 1, Kind = ComponentKind.Powder, QtyInitial = 454, CostTotal = 45.4, Currency = "" };
        var bullet = new Component { Id = 2, Kind = ComponentKind.Bullet, QtyInitial = 100, CostTotal = 60, Currency = Money.Default };
        var cb = Costing.PerRound(e, id => id == 1 ? powder : bullet);

        Assert.False(cb.Mixed);
        Assert.Equal(Money.Default, cb.Currency);
    }
}
