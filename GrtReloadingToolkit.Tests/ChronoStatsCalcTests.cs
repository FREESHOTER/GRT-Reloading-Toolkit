using System;
using System.Linq;
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

    private static string Report(double confidence) =>
        ChronoStatsCalc.BuildTableReport(
            new[] { ("39.2 gr (Ladder)", ChronoStatsCalc.Analyze(Clean, confidence)) },
            3.0, confidence);

    /// <summary>The table's column header used to say "95%" whatever level was asked for, so at 90
    /// or 99 it contradicted the sentence directly above it, which has always been correct.</summary>
    [Theory]
    [InlineData(0.90, 90)]
    [InlineData(0.95, 95)]
    [InlineData(0.99, 99)]
    public void TableReport_ColumnHeaderNamesTheConfidenceLevelActuallyUsed(double confidence, int shown)
    {
        string r = Report(confidence);
        Assert.Contains($"{shown}% CI on mean", r);
        foreach (int other in new[] { 90, 95, 99 })
            if (other != shown) Assert.DoesNotContain($"{other}% CI on mean", r);
    }

    /// <summary>The header is fixed-width and hand-aligned to the row format, so a change to it has
    /// to keep the column rules where they were at every confidence level.</summary>
    [Theory]
    [InlineData(0.90)]
    [InlineData(0.95)]
    [InlineData(0.99)]
    public void TableReport_ColumnRulesStayAlignedWithTheRows(double confidence)
    {
        var lines = Report(confidence).Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        string header = lines.Single(l => l.Contains("CI on mean"));
        string row = lines.Single(l => l.StartsWith("39.2 gr", StringComparison.Ordinal));
        Assert.Equal(PipeColumns(header), PipeColumns(row));
    }

    private static IReadOnlyList<int> PipeColumns(string line) =>
        line.Select((c, i) => (c, i)).Where(t => t.c == '|').Select(t => t.i).ToList();
}
