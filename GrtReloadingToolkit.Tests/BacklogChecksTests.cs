using GrtPluginKit.Analysis;
using GrtReloadingToolkit.Athlon;
using GrtReloadingToolkit.ChronoStats;
using GrtReloadingToolkit.Ocw;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>Two checks of the list of controls that need no GRT: the confidence interval against the textbook, and irregular ladder steps.</summary>
public sealed class BacklogChecksTests
{
    /// <summary>
    /// The statistics window's "CI ± (mean)" is the one figure it computes that GRT does not show. For the four shots
    /// 810.3 / 803.5 / 805.8 / 806.9 the sample SD is 2.830 and the 95% t-interval has 3 degrees of freedom (t = 3.1824):
    /// 3.1824 x 2.830 / sqrt(4) = 4.503. The window printed +-4.5 for that string.
    /// </summary>
    [Fact]
    public void TheConfidenceIntervalIsTheTextbookTInterval()
    {
        double[] shots = { 810.3, 803.5, 805.8, 806.9 };
        var r = ChronoStatsCalc.Analyze(shots, 0.95);
        double sd = StringStats.From(shots).Sd;
        Assert.Equal(3.1824 * sd / 2.0, r.CiMarginMps, 2);
        Assert.Equal(4.50, r.CiMarginMps, 2);
    }

    [Fact]
    public void TenShotsUseTheirOwnTValue()
    {
        double[] shots = { 800, 803, 799, 801, 805, 798, 802, 800, 804, 797 };
        var r = ChronoStatsCalc.Analyze(shots, 0.95);
        double sd = StringStats.From(shots).Sd;
        Assert.Equal(2.2622 * sd / Math.Sqrt(10), r.CiMarginMps, 2);   // t(0.975, 9 df) = 2.2622
    }

    // ---- irregular steps
    private static AthlonString S(double charge, double mean)
    {
        var s = new AthlonString { SourceFile = $"c{charge}.xlsx", SourceUnit = "mps", ChargeGrains = charge };
        foreach (double d in new[] { -2.0, 1.0, 0.0, 2.0, -1.0 }) s.Shots.Add(new ShotMeta { VelocityMps = mean + d });
        return s;
    }

    [Fact]
    public void IrregularChargesKeepTheirOwnValuesAndComeOutInAscendingOrder()
    {
        var rows = new[] { S(40.1, 830), S(39.0, 810), S(41.5, 850), S(39.7, 820) }.Select(s => new ImportBuilder.Row(s, null, true)).ToList();
        var charges = ImportBuilder.Build(rows, null, false);
        Assert.Equal(new[] { 39.0, 39.7, 40.1, 41.5 }, charges.Select(c => Math.Round(c.ChargeGrains ?? 0, 2)).ToArray());
        Assert.Equal(new[] { 810.0, 820.0, 830.0, 850.0 }, charges.Select(c => Math.Round(c.Shots.Average(sh => sh.VelocityMps), 1)).ToArray());
    }

    [Fact]
    public void TheNodeIsNamedByTheRealChargesNotByTheirPlaceInTheList()
    {
        // charges 39.0, 39.7, 40.1, 40.2, 41.5: the flat spot (velocity and POI) lies on 39.7 / 40.1 / 40.2
        double[] x = { 39.0, 39.7, 40.1, 40.2, 41.5 };
        double[] mv = { 805, 821.0, 821.4, 821.2, 836 };
        double[] poi = { -0.8, 0.10, 0.12, 0.11, 1.0 };
        double[] grp = { 1.4, 0.5, 0.45, 0.5, 1.2 };
        var rows = x.Select((v, i) => new LadderStep
        {
            X = v, VelN = 5, MeanMps = mv[i], SdMps = 3.0, EsMps = 8, Impacts = 5, DistanceM = 100, PoiYMoa = poi[i], MeanRadiusMoa = grp[i], VertSpreadMoa = grp[i] * 1.8, GroupEsMoa = grp[i],
        }).ToList();
        var res = LadderAnalyzer.Analyze(rows, LadderMode.Charge, "gr");
        var node = res.BestNode;
        Assert.NotNull(node);
        Assert.Equal(39.7, node!.Low, 2);
        Assert.Equal(40.2, node.High, 2);
    }
}