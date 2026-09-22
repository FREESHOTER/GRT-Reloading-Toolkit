using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

public sealed class BrassLifeTests
{
    private static Component Brass(double qtyInitial = 100, int expectedUses = 6,
        int? annealEvery = 3, double? annealedAt = null) => new()
    {
        Kind = ComponentKind.Brass, QtyInitial = qtyInitial, ExpectedUses = expectedUses,
        AnnealEveryUses = annealEvery, AnnealedAtUses = annealedAt,
    };

    [Fact]
    public void AverageUsesIsRoundsFiredDividedByPieces()
    {
        // 100 pieces, 250 rounds logged against the lot -> 2.5 average firings per case.
        var s = BrassLife.Compute(Brass(qtyInitial: 100), totalRoundsFired: 250);

        Assert.Equal(2.5, s.AvgUsesPerCase);
    }

    [Fact]
    public void NeverAnnealedCountsEverythingFiredTowardTheFirstReminder()
    {
        var s = BrassLife.Compute(Brass(qtyInitial: 100, annealEvery: 3, annealedAt: null), totalRoundsFired: 350);

        Assert.Equal(3.5, s.AvgUsesPerCase);
        Assert.Equal(3.5, s.UsesSinceAnneal); // nothing subtracted -- never annealed
        Assert.True(s.AnnealDue);
    }

    [Fact]
    public void AnnealingResetsTheCounterFromWhereItWasMarked()
    {
        // Annealed once already at 3.0 average uses; 50 more rounds on a 100-piece lot only adds
        // 0.5 more average uses since then -- not due again yet even though the LIFETIME total (3.5)
        // would look due if the reset were ignored.
        var s = BrassLife.Compute(Brass(qtyInitial: 100, annealEvery: 3, annealedAt: 3.0), totalRoundsFired: 350);

        Assert.Equal(3.5, s.AvgUsesPerCase);
        Assert.Equal(0.5, s.UsesSinceAnneal);
        Assert.False(s.AnnealDue);
    }

    [Fact]
    public void NoAnnealIntervalSetNeverFlagsDue()
    {
        var s = BrassLife.Compute(Brass(qtyInitial: 100, annealEvery: null), totalRoundsFired: 1000);

        Assert.False(s.AnnealDue);
    }

    [Fact]
    public void RetirementIsFlaggedAtOrAboveExpectedUses()
    {
        // 100 pieces, expected 6 uses, 600 rounds logged -> exactly at the retirement line.
        var s = BrassLife.Compute(Brass(qtyInitial: 100, expectedUses: 6), totalRoundsFired: 600);

        Assert.True(s.RetirementDue);
    }

    [Fact]
    public void BelowExpectedUsesIsNotRetirementDue()
    {
        var s = BrassLife.Compute(Brass(qtyInitial: 100, expectedUses: 6), totalRoundsFired: 599);

        Assert.False(s.RetirementDue);
    }

    [Fact]
    public void ZeroPiecesNeverDividesByZero()
    {
        var s = BrassLife.Compute(Brass(qtyInitial: 0), totalRoundsFired: 100);

        Assert.Equal(0, s.AvgUsesPerCase);
        Assert.False(s.AnnealDue);
    }

    [Fact]
    public void NoRoundsFiredYetIsNotDueForAnythingWithAFreshLot()
    {
        var s = BrassLife.Compute(Brass(), totalRoundsFired: 0);

        Assert.Equal(0, s.AvgUsesPerCase);
        Assert.False(s.AnnealDue);
        Assert.False(s.RetirementDue);
    }
}
