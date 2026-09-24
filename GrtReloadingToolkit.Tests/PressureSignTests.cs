using GrtReloadingToolkit.Log;
using Xunit;
using static GrtReloadingToolkit.Log.PressureSign;

namespace GrtReloadingToolkit.Tests;

public sealed class PressureSignTests
{
    private static CaseHeadMeasurement M(double chargeGr, double diaMm) =>
        new() { FirearmId = 1, PowderId = 1, ChargeGr = chargeGr, HeadDiameterMm = diaMm };

    // ── BuildSeries ──────────────────────────────────────────────────────

    [Fact]
    public void BuildSeriesAveragesRepeatedReadingsAtTheSameChargeAndSorts()
    {
        var series = BuildSeries(new[]
        {
            M(40.0, 10.500), M(39.0, 10.480),
            M(40.0, 10.520), // second reading at 40.0gr -- should average with the first, not double-count
        });

        Assert.Equal(2, series.Count);
        Assert.Equal(39.0, series[0].ChargeGr);
        Assert.Equal(40.0, series[1].ChargeGr);
        Assert.Equal(10.510, series[1].AvgHeadDiameterMm, 6);
        Assert.Equal(2, series[1].ReadingCount);
        Assert.Equal(1, series[0].ReadingCount);
    }

    // ── Diagnose: honesty guards ─────────────────────────────────────────

    [Fact]
    public void DiagnoseReturnsEmptyBelowMinPoints()
    {
        var series = BuildSeries(new[] { M(39.0, 10.40), M(40.0, 10.42), M(41.0, 10.44) });
        Assert.True(series.Count < new PressureSignConfig().MinPoints);
        Assert.Empty(Diagnose(series, new PressureSignConfig()));
    }

    [Fact]
    public void DiagnoseFindsNothingOnALinearLadder()
    {
        // Constant 0.005 mm/gr the whole way -- no step is faster than the ladder's own average.
        var series = BuildSeries(new[]
        {
            M(39.0, 10.400), M(39.5, 10.4025), M(40.0, 10.405), M(40.5, 10.4075), M(41.0, 10.410),
        });
        Assert.Empty(Diagnose(series, new PressureSignConfig()));
    }

    [Fact]
    public void DiagnoseFlagsAStepThatAcceleratesWellBeyondThePriorAverage()
    {
        // Steady ~0.004 mm/gr for the first three steps, then a real jump to 0.020 mm/gr (5x) on the
        // last one -- the classic "pressure sign" shape: fine until it suddenly isn't.
        var series = BuildSeries(new[]
        {
            M(38.0, 10.400),
            M(38.5, 10.402), // +0.004/gr
            M(39.0, 10.404), // +0.004/gr
            M(39.5, 10.406), // +0.004/gr
            M(40.0, 10.416), // +0.020/gr -- the flagged step
        });

        var flags = Diagnose(series, new PressureSignConfig());
        var flag = Assert.Single(flags);
        Assert.Equal(39.5, flag.FromChargeGr);
        Assert.Equal(40.0, flag.ToChargeGr);
        Assert.Equal(0.020, flag.StepSlopeMmPerGr, 6);
        Assert.Equal(0.004, flag.BaselineSlopeMmPerGr, 6);
        Assert.Equal(5.0, flag.Ratio, 3);
    }

    [Fact]
    public void DiagnoseDoesNotFlagTheVeryFirstEligibleStepBeforeAPriorAverageExists()
    {
        // Only MinPriorSteps=2 prior slopes are required before judging a step -- the 3rd step (index
        // 2) is the first one ever judged; steps 1 and 2 themselves can never be flagged since there
        // is no baseline yet to compare them against.
        var series = BuildSeries(new[] { M(38.0, 10.00), M(38.5, 10.50), M(39.0, 10.60) });
        var cfg = new PressureSignConfig { MinPoints = 3 };
        // Step 1 (38.0->38.5) is a huge jump, but it's the very first slope recorded -- nothing to
        // compare it against yet, so it must never appear as a flag.
        var flags = Diagnose(series, cfg);
        Assert.DoesNotContain(flags, f => f.FromChargeGr == 38.0);
    }

    [Fact]
    public void DiagnoseUsesTheBaselineFloorWhenPriorStepsAreFlatOrShrinking()
    {
        // Prior average slope is slightly negative (case-to-case measurement noise) -- without a
        // floor, any small positive slope afterward would look like an infinite/huge ratio and flag
        // on noise alone. The floor keeps a merely-small positive step from flagging.
        var series = BuildSeries(new[]
        {
            M(38.0, 10.500),
            M(38.5, 10.499),  // slope -0.002
            M(39.0, 10.500),  // slope +0.002 -- prior average of these two is ~0.000, floored to 0.002
            M(39.5, 10.5015), // slope +0.003 -- ratio against the 0.002 floor is 1.5x, under the 2.0x threshold
        });
        var cfg = new PressureSignConfig();
        var flags = Diagnose(series, cfg);
        Assert.Empty(flags);
    }

    [Fact]
    public void DiagnoseCanFlagMoreThanOneStepInALongerLadder()
    {
        var series = BuildSeries(new[]
        {
            M(38.0, 10.400),
            M(38.5, 10.402),  // +0.004
            M(39.0, 10.404),  // +0.004
            M(39.5, 10.420),  // +0.032 -- first flag
            M(40.0, 10.422),  // +0.004 -- back to normal, must not flag
            M(40.5, 10.470),  // +0.096 -- second flag (way above the still-modest running average)
        });
        var flags = Diagnose(series, new PressureSignConfig());
        Assert.Equal(2, flags.Count);
        Assert.Contains(flags, f => f.FromChargeGr == 39.0 && f.ToChargeGr == 39.5);
        Assert.Contains(flags, f => f.FromChargeGr == 40.0 && f.ToChargeGr == 40.5);
    }
}
