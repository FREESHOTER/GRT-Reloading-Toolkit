using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

public sealed class BaStalenessTests
{
    [Fact]
    public void NoLastCalibratedValueMeansNothingToCompareAgainst()
    {
        Assert.Null(BaStaleness.Check(0.48, null));
    }

    [Fact]
    public void NoFileBaMeansNothingToCheck()
    {
        Assert.Null(BaStaleness.Check(null, 0.48));
    }

    [Fact]
    public void AZeroOrNegativeValueIsTreatedAsMissing()
    {
        Assert.Null(BaStaleness.Check(0, 0.48));
        Assert.Null(BaStaleness.Check(0.48, -1));
    }

    [Fact]
    public void ASmallDifferenceIsNotFlagged()
    {
        // 0.48 vs 0.50 -> 4% under the 5% default threshold.
        var r = BaStaleness.Check(0.48, 0.50)!;
        Assert.False(r.Stale);
        Assert.Equal(4.0, r.DiffPct, 1);
    }

    [Fact]
    public void ALargeDifferenceIsFlagged()
    {
        // 0.40 vs 0.50 -> 20% over the default threshold.
        var r = BaStaleness.Check(0.40, 0.50)!;
        Assert.True(r.Stale);
        Assert.Equal(20.0, r.DiffPct, 1);
    }

    [Fact]
    public void TheThresholdIsExclusive()
    {
        // Exactly 5% must not be flagged -- "> threshold", not ">=".
        var r = BaStaleness.Check(0.525, 0.50, thresholdPct: 5.0)!;
        Assert.False(r.Stale);
    }

    [Fact]
    public void ACustomThresholdIsHonored()
    {
        var r = BaStaleness.Check(0.48, 0.50, thresholdPct: 2.0)!;
        Assert.True(r.Stale); // 4% > 2% threshold
    }

    [Fact]
    public void TheComparisonIsDirectionAgnostic()
    {
        // A file Ba HIGHER than the calibrated one is just as much a mismatch as lower.
        var r = BaStaleness.Check(0.60, 0.50)!;
        Assert.True(r.Stale);
        Assert.Equal(20.0, r.DiffPct, 1);
    }
}
