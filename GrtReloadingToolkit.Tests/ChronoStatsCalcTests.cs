using GrtReloadingToolkit.ChronoStats;
using Xunit;

namespace GrtReloadingToolkit.Tests;

public class ChronoStatsCalcTests
{
    // a clean 5-shot string, no shot anywhere near being an outlier
    private static readonly double[] Clean = { 828, 831, 826, 830, 829 };

    [Fact]
    public void ChauvenetOutliers_FlagsNothingInACleanString()
    {
        var r = ChronoStatsCalc.ChauvenetOutliers(Clean);
        Assert.All(r, o => Assert.False(o.Flagged));
    }

    [Fact]
    public void ChauvenetOutliers_FlagsAnObviousBadShot()
    {
        // a double-registration / bounced-signal read is a classic chrono failure mode
        double[] withFlyer = { 828, 831, 826, 830, 829, 1450 };
        var r = ChronoStatsCalc.ChauvenetOutliers(withFlyer);
        Assert.True(r[5].Flagged);
        Assert.False(r[0].Flagged);
    }

    [Fact]
    public void Analyze_ExcludesFlaggedOutliersFromTheMean()
    {
        double[] withFlyer = { 828, 831, 826, 830, 829, 1450 };
        var withoutFlyer = ChronoStatsCalc.Analyze(Clean);
        var withFlyerResult = ChronoStatsCalc.Analyze(withFlyer);

        Assert.Equal(5, withFlyerResult.N);   // the 1450 was dropped
        Assert.Equal(withoutFlyer.Mean, withFlyerResult.Mean, 6);
    }

    [Fact]
    public void Analyze_ConfidenceIntervalContainsTheSampleMean()
    {
        var r = ChronoStatsCalc.Analyze(Clean, 0.95);
        Assert.InRange(r.Mean, r.CiLoMps, r.CiHiMps);
        Assert.True(r.CiMarginMps > 0);
    }

    [Fact]
    public void Analyze_WiderConfidenceNarrowsTheInterval()
    {
        var ci90 = ChronoStatsCalc.Analyze(Clean, 0.90);
        var ci99 = ChronoStatsCalc.Analyze(Clean, 0.99);
        Assert.True(ci90.CiMarginMps < ci99.CiMarginMps);
    }

    [Fact]
    public void RequiredSampleSize_ShrinksAsTheAllowedMarginGrows()
    {
        int nTight = ChronoStatsCalc.RequiredSampleSize(sampleSd: 8, marginMps: 2, confidence: 0.95);
        int nLoose = ChronoStatsCalc.RequiredSampleSize(sampleSd: 8, marginMps: 8, confidence: 0.95);
        Assert.True(nTight > nLoose);
    }

    [Fact]
    public void RequiredSampleSize_MoreShotsNeededForATighterConfidence()
    {
        int n95 = ChronoStatsCalc.RequiredSampleSize(sampleSd: 5, marginMps: 3, confidence: 0.95);
        int n99 = ChronoStatsCalc.RequiredSampleSize(sampleSd: 5, marginMps: 3, confidence: 0.99);
        Assert.True(n99 >= n95);
    }

    [Fact]
    public void Compare_SameDistributionIsNotFlaggedDifferent()
    {
        double[] a = { 828, 831, 826, 830, 829 };
        double[] b = { 827, 832, 825, 831, 828 };
        var r = ChronoStatsCalc.Compare(a, b);
        Assert.False(r.MeansDiffer);
        Assert.False(r.SpreadsDiffer);
    }

    [Fact]
    public void Compare_ClearlySeparatedStringsAreFlaggedDifferent()
    {
        double[] slow = { 800, 802, 799, 801, 800 };
        double[] fast = { 850, 852, 849, 851, 850 };
        var r = ChronoStatsCalc.Compare(slow, fast);
        Assert.True(r.MeansDiffer);
        Assert.True(r.TPValue < 0.01);
    }

    [Fact]
    public void Compare_MuchNoisierStringHasADifferentSpreadFlagged()
    {
        double[] tight = { 828, 829, 828, 829, 828, 829, 828, 829 };
        double[] loose = { 800, 850, 810, 840, 795, 855, 805, 845 };
        var r = ChronoStatsCalc.Compare(tight, loose);
        Assert.True(r.SpreadsDiffer);
    }
}
