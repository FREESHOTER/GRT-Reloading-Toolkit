using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

public sealed class PressureTrendTests
{
    [Fact]
    public void ARequiresAtLeastThreeCharges()
    {
        Assert.Throws<PressureTrend.PressureTrendError>(() =>
            PressureTrend.AnalyzePressureTrend(new List<double> { 39, 40 }, new List<double> { 800, 810 }));
    }

    [Fact]
    public void MismatchedLengthsThrow()
    {
        Assert.Throws<PressureTrend.PressureTrendError>(() =>
            PressureTrend.AnalyzePressureTrend(new List<double> { 39, 40, 41 }, new List<double> { 800, 810 }));
    }

    [Fact]
    public void ChargesAreReorderedAscendingRegardlessOfTestOrder()
    {
        // Fed out of order -- must still analyze low-to-high charge.
        var r = PressureTrend.AnalyzePressureTrend(
            new List<double> { 41, 39, 40 }, new List<double> { 820, 800, 810 });

        Assert.Equal(new List<double> { 39, 40, 41 }, r.ChargesGr);
        Assert.Equal(new List<double> { 800, 810, 820 }, r.VelocitiesMps);
    }

    [Fact]
    public void ASteadyLinearRiseIsReportedAsNormal()
    {
        // Perfectly even 10 m/s per grain the whole way -- no flattening, no jump.
        var charges = new List<double> { 39, 39.5, 40, 40.5, 41 };
        var velocities = new List<double> { 800, 805, 810, 815, 820 };

        var r = PressureTrend.AnalyzePressureTrend(charges, velocities);

        Assert.False(r.FlatteningTrend);
        Assert.Empty(r.VelocityJumps);
        Assert.Equal("normal", r.PressureLevel);
    }

    [Fact]
    public void AFlatteningLastSlopeSignalsApproachingTheLimit()
    {
        // Even 10 m/s/gr for the first three steps, then the last step barely moves at all --
        // the classic pressure-plateau shape.
        var charges = new List<double> { 39, 39.5, 40, 40.5, 41 };
        var velocities = new List<double> { 800, 805, 810, 815, 815.5 };

        var r = PressureTrend.AnalyzePressureTrend(charges, velocities);

        Assert.True(r.FlatteningTrend);
        Assert.True(r.PressureLevel is "warning" or "critical");
    }

    [Fact]
    public void ASuddenSlopeSpikeIsFlaggedAsAVelocityJump()
    {
        // Even 5 m/s/gr, then one abnormally large jump between two charges (dirty data or a real
        // ignition instability), then back to normal.
        var charges = new List<double> { 39, 39.5, 40, 40.5, 41, 41.5 };
        var velocities = new List<double> { 800, 802.5, 805, 830, 832.5, 835 };

        var r = PressureTrend.AnalyzePressureTrend(charges, velocities);

        Assert.NotEmpty(r.VelocityJumps);
        Assert.Equal(40.5, r.VelocityJumps[0].ChargeGr);
    }

    [Fact]
    public void ALastSlopeWellAboveTheMeanReadsAsAmpleMargin()
    {
        // Two modest, similar early steps, then a much larger last step -- the last slope ends up
        // well above the mean (this heuristic's definition of a safe margin), the mirror image of
        // the flattening case above.
        var charges = new List<double> { 39, 39.5, 40, 40.5 };
        var velocities = new List<double> { 800, 805, 810, 830 };

        var r = PressureTrend.AnalyzePressureTrend(charges, velocities);

        Assert.Equal("safe", r.PressureLevel);
    }
}
