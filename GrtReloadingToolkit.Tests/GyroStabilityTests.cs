using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The reference numbers are what the old Ballistic Lab's Python implementation of the same rule gives for the same bullets
/// (6.5 mm 139 gr in 1:8 at 2723 ft/s, 7 mm 180 gr in 1:8.5, .308 168 gr in 1:12); that code was itself checked against the
/// formula written out by hand, so two independent implementations agree here.
/// </summary>
public class GyroStabilityTests
{
    private static GyroStabilityResult Run(double m, double l, double d, double twist, double v, double c = 15, double alt = 0)
    {
        var r = GyroStability.Compute(m, l, d, twist, v, c, alt, out string? err);
        Assert.Null(err);
        return r!;
    }

    [Fact]
    public void ASixPointFiveOnTheStandardDay()
    {
        var r = Run(139, 1.36, 0.264, 8.0, 2723);
        Assert.Equal(1.722, r.Sg, 2);
        Assert.Equal(1.74, r.SgStandard, 2);
        Assert.Equal(0.9907, r.VelocityFactor, 3);
        Assert.Equal(StabilityLevel.Stable, r.Level);
    }

    [Fact]
    public void OtherBulletsMatchTheReferenceImplementation()
    {
        Assert.Equal(1.669, Run(180, 1.522, 0.284, 8.5, 2891).Sg, 2);
        Assert.Equal(1.707, Run(168, 1.215, 0.308, 12.0, 2650).Sg, 2);
    }

    [Fact]
    public void ColdAirLowersTheMarginAndAltitudeRaisesIt()
    {
        double std = Run(139, 1.36, 0.264, 8.0, 2723).Sg;
        Assert.Equal(1.573, Run(139, 1.36, 0.264, 8.0, 2723, -10).Sg, 2);
        Assert.True(Run(139, 1.36, 0.264, 8.0, 2723, -10).Sg < std);
        Assert.Equal(2.196, Run(139, 1.36, 0.264, 8.0, 2723, 15, 2000).Sg, 2);
    }

    [Fact]
    public void SgFallsWithTheSquareOfTheTwistLength()
    {
        double a = Run(139, 1.36, 0.264, 8.0, 2723).Sg, b = Run(139, 1.36, 0.264, 10.0, 2723).Sg;
        Assert.Equal(100.0 / 64.0, a / b, 6);
    }

    [Fact]
    public void TheTwistForAWantedSgGivesThatSgBack()
    {
        var r = Run(139, 1.36, 0.264, 8.0, 2723, -5, 500);
        Assert.Equal(1.5, Run(139, 1.36, 0.264, r.TwistFor15In, 2723, -5, 500).Sg, 6);
        Assert.Equal(1.4, Run(139, 1.36, 0.264, r.TwistFor14In, 2723, -5, 500).Sg, 6);
        Assert.True(r.TwistFor14In > r.TwistFor15In);   // a slower twist is enough for the lower target
    }

    [Theory]
    [InlineData(1.6, StabilityLevel.Stable)]
    [InlineData(1.5, StabilityLevel.Stable)]
    [InlineData(1.45, StabilityLevel.Adequate)]
    [InlineData(1.2, StabilityLevel.Marginal)]
    [InlineData(0.9, StabilityLevel.Unstable)]
    public void Levels(double sg, StabilityLevel expected) => Assert.Equal(expected, GyroStability.Classify(sg));

    [Fact]
    public void GreenhillUsesTheVelocityDependentConstant()
    {
        Assert.Equal(150 * 0.264 * 0.264 / 1.36, Run(139, 1.36, 0.264, 8.0, 2723).GreenhillTwistIn, 6);
        Assert.Equal(180 * 0.264 * 0.264 / 1.36, Run(139, 1.36, 0.264, 8.0, 2900).GreenhillTwistIn, 6);
    }

    [Theory]
    [InlineData(0, 1.36, 0.264, 8.0, 2700, 15, 0, "bullet mass")]
    [InlineData(139, 0.2, 0.264, 8.0, 2700, 15, 0, "length")]            // shorter than it is wide: units mixed up
    [InlineData(139, 1.36, 0.264, -8.0, 2700, 15, 0, "twist")]
    [InlineData(139, 1.36, 0.264, 8.0, 2700, 15, 20000, "altitude")]
    [InlineData(139, 1.36, 0.264, 8.0, double.NaN, 15, 0, "velocity")]
    public void BadInputsAreNamed(double m, double l, double d, double tw, double v, double c, double alt, string what)
    {
        Assert.Null(GyroStability.Compute(m, l, d, tw, v, c, alt, out string? err));
        Assert.Equal(what, err);
    }
}
