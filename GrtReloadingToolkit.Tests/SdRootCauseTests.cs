using GrtReloadingToolkit.Log;
using GrtReloadingToolkit.Ocw;
using Xunit;
using static GrtReloadingToolkit.Log.SdRootCause;

namespace GrtReloadingToolkit.Tests;

public sealed class SdRootCauseTests
{
    // ── Leave-one-out ranking ────────────────────────────────────────────

    [Fact]
    public void LeaveOneOutRankingSortsByReductionDescendingAndFindsTheRealOutlier()
    {
        // Four identical shots plus one clear high flyer -- removing the flyer must drive the
        // remaining SD to exactly zero (a 100% reduction), and it must rank first.
        var v = new List<double> { 800, 800, 800, 800, 830 };

        var ranking = LeaveOneOutRanking(v);

        Assert.Equal(4, ranking[0].ShotIndex);
        Assert.Equal(0.0, ranking[0].SdWithoutMps, 9);
        Assert.Equal(100.0, ranking[0].ReductionPct, 9);
        Assert.True(ranking[0].ChauvenetFlagged);
        // Sorted descending: nothing should out-rank the shot that zeroes the SD out.
        Assert.All(ranking.Skip(1), r => Assert.True(r.ReductionMps <= ranking[0].ReductionMps));
    }

    [Fact]
    public void LeaveOneOutRankingRequiresAtLeastThreeShots()
    {
        var v = new List<double> { 800, 801 };
        Assert.Throws<SdRootCauseError>(() => LeaveOneOutRanking(v));
    }

    // ── Pattern classification (Chauvenet-based, not an invented percentage) ─

    [Theory]
    [InlineData(0, SdPatternKind.Systematic)]
    [InlineData(1, SdPatternKind.SingleOutlier)]
    [InlineData(2, SdPatternKind.PartialOutlier)]
    [InlineData(3, SdPatternKind.PartialOutlier)]
    public void ClassifySdPatternCountsChauvenetFlaggedShotsNotAnInventedPercentage(int flaggedCount, SdPatternKind expected)
    {
        var rows = Enumerable.Range(0, 5)
            .Select(i => new LeaveOneOutRow(i, 800, 800, 0, 0, ChauvenetFlagged: i < flaggedCount))
            .ToList();

        Assert.Equal(expected, ClassifySdPattern(rows));
    }

    // ── Skewness ─────────────────────────────────────────────────────────

    [Fact]
    public void SkewnessIsZeroForASymmetricSample()
    {
        var v = new List<double> { 1, 2, 3, 4, 5 };
        Assert.Equal(0.0, Skewness(v), 9);
    }

    [Fact]
    public void SkewnessIsPositiveForARightSkewedSample()
    {
        // One high flyer stretches the right tail -- textbook positive skew.
        var v = new List<double> { 1, 2, 3, 4, 20 };
        Assert.True(Skewness(v) > 1.0);
    }

    // ── Temperature correlations (thin wrappers over AdvancedDiagnostics.Pearson) ─

    [Fact]
    public void TempVelocityCorrelationIsPerfectForAnExactLinearRelationship()
    {
        var pairs = new List<(int, double, double)>
        {
            (0, 10, 800), (1, 15, 810), (2, 20, 820), (3, 25, 830),
        };
        Assert.Equal(1.0, TempVelocityCorrelation(pairs)!.Value, 6);
    }

    [Fact]
    public void TempPoiCorrelationIsPerfectForAnExactLinearRelationship()
    {
        var pairs = new List<(int, double, double)>
        {
            (0, 10, 0.2), (1, 20, 0.4), (2, 30, 0.6), (3, 40, 0.8),
        };
        Assert.Equal(1.0, TempPoiCorrelation(pairs)!.Value, 6);
    }

    // ── Shot/POI alignment ───────────────────────────────────────────────

    [Fact]
    public void AlignShotsWithGroupFailsOnACountMismatch()
    {
        var group = new TargetGroup { SourceFile = "test" };
        group.Impacts.Add(new Impact(0, 0));
        group.Impacts.Add(new Impact(1, 0));

        var result = AlignShotsWithGroup(shotCount: 5, group);

        Assert.False(result.Aligned);
        Assert.Empty(result.Pairs);
        Assert.Contains("5", result.Reason);
        Assert.Contains("2", result.Reason);
    }

    [Fact]
    public void AlignShotsWithGroupComputesRadiusFromTheImpactCentroidAndAlwaysStatesTheOrderCaveat()
    {
        // Centroid is (1, 1); radii are sqrt(2), sqrt(2), 2 in firing order.
        var group = new TargetGroup { SourceFile = "test" };
        group.Impacts.Add(new Impact(0, 0));
        group.Impacts.Add(new Impact(2, 0));
        group.Impacts.Add(new Impact(1, 3));

        var result = AlignShotsWithGroup(shotCount: 3, group);

        Assert.True(result.Aligned);
        Assert.Equal(Math.Sqrt(2), result.Pairs[0].RadiusMoa, 6);
        Assert.Equal(Math.Sqrt(2), result.Pairs[1].RadiusMoa, 6);
        Assert.Equal(2.0, result.Pairs[2].RadiusMoa, 6);
        // A matching count does not prove the firing order lines up -- the caveat must be present
        // even on a success, not only reserved for the failure case above.
        Assert.Contains("does not link", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    // ── Diagnose orchestration ───────────────────────────────────────────

    [Fact]
    public void DiagnoseWithNoOptionalInputsOnlyReturnsTheRanking()
    {
        var v = new List<double> { 800, 802, 798, 801, 799 };

        var report = Diagnose(v);

        Assert.Null(report.ColdBore);
        Assert.Null(report.ProgressiveDrift);
        Assert.Null(report.TempVelocityR);
        Assert.Null(report.TempPoiR);
        Assert.Equal(v.Count, report.Ranking.Count);
    }

    [Fact]
    public void DiagnoseWithColdBoreFlagsButNoTempsFillsColdBoreAndFallsBackToProgressiveDrift()
    {
        var v = new List<double> { 830, 800, 801, 799, 800, 800 };
        var coldBore = new List<bool> { true, false, false, false, false, false };

        var report = Diagnose(v, coldBoreFlags: coldBore);

        Assert.NotNull(report.ColdBore);
        Assert.True(report.ColdBore!.ColdBoreIsOutlier);
        // No manual temperature was supplied, so the only firing-order signal GRT's own data can
        // support (Fouling Tracker's trend) must stand in -- a regression here would leave a string
        // with real cold-bore data but no temperature entirely without a drift signal.
        Assert.NotNull(report.ProgressiveDrift);
        Assert.Null(report.TempVelocityR);
        Assert.Null(report.TempPoiR);
    }

    [Fact]
    public void DiagnoseWithManualTempsSkipsTheProgressiveDriftFallbackAndUsesRealCorrelation()
    {
        var v = new List<double> { 800, 805, 810, 815, 820, 825 };
        var temps = new List<(int, double)> { (0, 10), (1, 12), (2, 14), (3, 16), (4, 18), (5, 20) };

        var report = Diagnose(v, manualTemps: temps);

        Assert.NotNull(report.TempVelocityR);
        Assert.True(report.TempVelocityR > 0.9);
        // Real per-shot temperature was supplied -- the unmeasured fallback must not also fire and
        // double-report the same underlying drift as two different "causes".
        Assert.Null(report.ProgressiveDrift);
    }

    [Fact]
    public void DiagnoseWithManualTempsButNoShotGroupLeavesTempPoiRNull()
    {
        var v = new List<double> { 800, 805, 810, 815 };
        var temps = new List<(int, double)> { (0, 10), (1, 15), (2, 20), (3, 25) };

        var report = Diagnose(v, manualTemps: temps);

        Assert.NotNull(report.TempVelocityR);
        Assert.Null(report.TempPoiR);
    }

    [Fact]
    public void DiagnoseWithManualTempsAndAMatchingShotGroupFillsTempPoiR()
    {
        var v = new List<double> { 800, 805, 810, 815 };
        var temps = new List<(int, double)> { (0, 10), (1, 15), (2, 20), (3, 25) };
        var group = new TargetGroup { SourceFile = "test" };
        // y=0 for three shots, one flyer at y=10 -- centroid pulls toward it, so radius rises
        // with shot index the same way temperature does, giving a clear (not necessarily perfect)
        // positive correlation.
        group.Impacts.Add(new Impact(0, 0));
        group.Impacts.Add(new Impact(0, 0));
        group.Impacts.Add(new Impact(0, 0));
        group.Impacts.Add(new Impact(0, 10));

        var report = Diagnose(v, manualTemps: temps, shotGroup: group);

        Assert.NotNull(report.TempPoiR);
        Assert.True(report.TempPoiR > 0);
    }
}
