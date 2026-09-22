using GrtReloadingToolkit.Log;
using Xunit;
using static GrtReloadingToolkit.Log.AdvancedDiagnostics;

namespace GrtReloadingToolkit.Tests;

public sealed class AdvancedDiagnosticsTests
{
    [Fact]
    public void LinearSlopePerfectlyFitsAKnownLine()
    {
        // y = 2x + 1 exactly -- slope must come back as 2, not an approximation.
        var xs = new List<double> { 0, 1, 2, 3, 4 };
        var ys = new List<double> { 1, 3, 5, 7, 9 };

        Assert.Equal(2.0, AdvancedDiagnostics.LinearSlope(xs, ys), 9);
    }

    [Fact]
    public void LinearSlopeIsZeroForAFlatLine()
    {
        var xs = new List<double> { 0, 1, 2, 3 };
        var ys = new List<double> { 5, 5, 5, 5 };

        Assert.Equal(0.0, AdvancedDiagnostics.LinearSlope(xs, ys), 9);
    }

    // ── Fouling Tracker ──────────────────────────────────────────────────

    [Fact]
    public void FoulingTrendFlagsARisingRollingSd()
    {
        // Tight at the start, increasingly erratic at the end -- a textbook fouling signature.
        var v = new List<double> { 800, 800.5, 799.5, 800, 799, 795, 805, 790, 810, 785, 815 };

        var r = AdvancedDiagnostics.FoulingTrend(v, window: 5);

        Assert.True(r.SdTrendSlope > 0);
        Assert.Equal(v.Count, r.NShots);
        Assert.Equal(v.Count - 5 + 1, r.Points.Count);
    }

    [Fact]
    public void FoulingTrendRequiresMoreShotsThanTheWindow()
    {
        var v = new List<double> { 800, 801, 802 };
        Assert.Throws<AdvancedDiagnosticsError>(() => AdvancedDiagnostics.FoulingTrend(v, window: 5));
    }

    [Fact]
    public void FoulingTrendRejectsAWindowSmallerThanTwo()
    {
        var v = new List<double> { 800, 801, 802, 803 };
        Assert.Throws<AdvancedDiagnosticsError>(() => AdvancedDiagnostics.FoulingTrend(v, window: 1));
    }

    [Fact]
    public void FoulingTrendDetectsADecliningVelocity()
    {
        var v = Enumerable.Range(0, 12).Select(i => 800.0 - i * 2).ToList(); // steady -2 m/s per shot
        var r = AdvancedDiagnostics.FoulingTrend(v, window: 5);

        Assert.True(r.VelocityDeclining);
        Assert.True(r.VelocityTrendSlope < 0);
    }

    // ── Cold Bore ────────────────────────────────────────────────────────

    [Fact]
    public void ColdBoreDetectsAFasterFirstShotAsAnOutlier()
    {
        var v = new List<double> { 830, 800, 801, 799, 800, 800 };
        var flags = new List<bool> { true, false, false, false, false, false };

        var r = AdvancedDiagnostics.ColdBoreAnalysis(v, flags, zThreshold: 1.5);

        Assert.True(r.ColdBoreIsOutlier);
        Assert.True(r.ColdBoreFaster);
        Assert.Equal(1, r.NCold);
        Assert.Equal(5, r.NWarm);
    }

    [Fact]
    public void ColdBoreWithinWarmSpreadIsNotAnOutlier()
    {
        var v = new List<double> { 801, 800, 802, 799, 800, 801 };
        var flags = new List<bool> { true, false, false, false, false, false };

        var r = AdvancedDiagnostics.ColdBoreAnalysis(v, flags, zThreshold: 1.5);

        Assert.False(r.ColdBoreIsOutlier);
    }

    [Fact]
    public void ColdBoreRequiresMatchingLengths()
    {
        Assert.Throws<AdvancedDiagnosticsError>(() =>
            AdvancedDiagnostics.ColdBoreAnalysis(new List<double> { 1, 2, 3 }, new List<bool> { true, false }));
    }

    [Fact]
    public void ColdBoreRequiresAtLeastOneColdAndTwoWarmShots()
    {
        var v = new List<double> { 800, 801, 802 };
        var flags = new List<bool> { true, true, false }; // only 1 warm shot
        Assert.Throws<AdvancedDiagnosticsError>(() => AdvancedDiagnostics.ColdBoreAnalysis(v, flags));
    }

    // ── Primer Sensitivity ───────────────────────────────────────────────

    [Fact]
    public void PrimerSensitivityRanksLotsByAscendingSd()
    {
        var groups = new Dictionary<string, IReadOnlyList<double>>
        {
            ["CCI BR2"] = new List<double> { 800, 801, 799, 800, 801 },       // tight
            ["Federal 210M"] = new List<double> { 795, 810, 790, 815, 800 }, // loose
        };

        var r = AdvancedDiagnostics.PrimerSensitivity(groups);

        Assert.Equal("CCI BR2", r.BestPrimerLot);
        Assert.Equal("CCI BR2", r.Lots[0].PrimerLot);
        Assert.True(r.PrimerSensitivityDetected);
    }

    [Fact]
    public void PrimerSensitivityIgnoresLotsWithFewerThanThreeShots()
    {
        var groups = new Dictionary<string, IReadOnlyList<double>>
        {
            ["A"] = new List<double> { 800, 801, 799, 800 },
            ["B"] = new List<double> { 800, 801 }, // only 2 -- excluded
        };

        Assert.Throws<AdvancedDiagnosticsError>(() => AdvancedDiagnostics.PrimerSensitivity(groups));
    }

    [Fact]
    public void PrimerSensitivityRequiresAtLeastTwoLots()
    {
        var groups = new Dictionary<string, IReadOnlyList<double>> { ["A"] = new List<double> { 800, 801, 802 } };
        Assert.Throws<AdvancedDiagnosticsError>(() => AdvancedDiagnostics.PrimerSensitivity(groups));
    }

    // ── Neck Tension Correlation ─────────────────────────────────────────

    [Fact]
    public void NeckTensionCorrelationFindsAPerfectPositiveCorrelation()
    {
        var points = new List<NeckTensionPoint>
        {
            new(0.02, EsMps: 5), new(0.04, EsMps: 10), new(0.06, EsMps: 15), new(0.08, EsMps: 20),
        };

        var r = AdvancedDiagnostics.NeckTensionCorrelation(points);

        Assert.Equal(1.0, r.CorrelationEs!.Value, 6);
        Assert.Null(r.CorrelationSd);
    }

    [Fact]
    public void NeckTensionCorrelationFindsAPerfectNegativeCorrelation()
    {
        var points = new List<NeckTensionPoint>
        {
            new(0.02, SdMps: 20), new(0.04, SdMps: 15), new(0.06, SdMps: 10), new(0.08, SdMps: 5),
        };

        var r = AdvancedDiagnostics.NeckTensionCorrelation(points);

        Assert.Equal(-1.0, r.CorrelationSd!.Value, 6);
    }

    /// <summary>The regression the old software had: a point missing ES must not shift which
    /// tension value gets paired with which OTHER point's ES. Point #2 here has no ES at all, so
    /// it must be dropped from the ES pairing entirely, not silently paired with point #3's value.</summary>
    [Fact]
    public void NeckTensionCorrelationNeverPairsAPointsTensionWithAnotherPointsDispersion()
    {
        var points = new List<NeckTensionPoint>
        {
            new(0.02, EsMps: 5),
            new(0.04, EsMps: null), // ES not measured for this one
            new(0.06, EsMps: 15),
            new(0.08, EsMps: 20),
        };

        var r = AdvancedDiagnostics.NeckTensionCorrelation(points);

        // Only 3 real (tension, ES) pairs exist: (0.02,5) (0.06,15) (0.08,20) -- still a clean
        // straight line (ES = 250*tension - 0), so correlation must still read as +1, not skewed
        // by a wrong pairing that a naive zip of two independently-filtered lists would produce.
        Assert.Equal(1.0, r.CorrelationEs!.Value, 6);
    }

    [Fact]
    public void NeckTensionCorrelationRequiresAtLeastThreePoints()
    {
        var points = new List<NeckTensionPoint> { new(0.02, EsMps: 5), new(0.04, EsMps: 10) };
        Assert.Throws<AdvancedDiagnosticsError>(() => AdvancedDiagnostics.NeckTensionCorrelation(points));
    }

    [Fact]
    public void NeckTensionCorrelationReportsTheOptimalTensionAsTheLowestDispersionPoint()
    {
        var points = new List<NeckTensionPoint>
        {
            new(0.02, EsMps: 12), new(0.04, EsMps: 4), new(0.06, EsMps: 9), new(0.08, EsMps: 15),
        };

        var r = AdvancedDiagnostics.NeckTensionCorrelation(points);

        Assert.Equal(0.04, r.OptimalTensionEs);
    }
}
