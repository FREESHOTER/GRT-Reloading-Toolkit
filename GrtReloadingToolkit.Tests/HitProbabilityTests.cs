using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

public class HitProbabilityTests
{
    [Fact]
    public void CentredCircleMatchesTheClosedForm()
    {
        double sigma = 10, radius = 10;
        Assert.Equal(1 - Math.Exp(-0.5), HitProbability.Circle(radius, sigma), 9);
        Assert.Equal(39.3, HitProbability.Circle(radius, sigma) * 100, 1);   // the textbook case R = sigma
    }

    [Fact]
    public void AHugeTargetIsAlwaysHit() => Assert.True(HitProbability.Circle(200, 5) > 0.999);

    [Fact]
    public void QuantilesAreTheRayleighOnes()
    {
        Assert.Equal(10 * Math.Sqrt(2 * Math.Log(2)), HitProbability.Quantile(10, 0.5), 9);
        Assert.True(HitProbability.Quantile(10, 0.5) < HitProbability.Quantile(10, 0.9));
        Assert.True(HitProbability.Quantile(10, 0.9) < HitProbability.Quantile(10, 0.95));
    }

    [Fact]
    public void SpreadGrowsInProportionToTheDistance()
    {
        Assert.Equal(2 * HitProbability.SigmaMm(0.5, 300), HitProbability.SigmaMm(0.5, 600), 9);
        // 1 MOA at 100 m is 29.09 mm
        Assert.Equal(29.089, HitProbability.SigmaMm(1, 100), 2);
    }

    [Fact]
    public void RectangleMatchesTwoIndependentNormals()
    {
        // one sigma each way: 0.6827 x 0.6827
        double p = HitProbability.Rectangle(20, 20, 10);
        Assert.Equal(0.682689 * 0.682689, p, 4);
    }

    [Fact]
    public void ATinyOffsetIsContinuousWithTheCentredFormula()
    {
        double centred = HitProbability.Circle(20, 15);
        Assert.Equal(centred, HitProbability.Circle(20, 15, 1e-3, 1e-3), 4);
    }

    [Theory]
    [InlineData(15, 20, 12, 0)]      // offset along one axis
    [InlineData(15, 20, 8, 9)]
    [InlineData(6, 40, 30, 10)]      // far off-centre: the integral must still be right
    public void OffCentreCircleAgreesWithAnIndependentSimulation(double sigma, double radius, double dx, double dy)
    {
        var rng = new Random(20261026);
        int hits = 0, n = 400_000;
        double Gauss() { double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble(); return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2); }
        for (int i = 0; i < n; i++)
        {
            double x = dx + sigma * Gauss(), y = dy + sigma * Gauss();
            if (x * x + y * y <= radius * radius) hits++;
        }
        // 400 000 shots: the sampling error of a probability is below 0.001, so 0.003 is three times the noise
        Assert.InRange(Math.Abs((double)hits / n - HitProbability.Circle(radius, sigma, dx, dy)), 0, 0.003);
    }

    [Fact]
    public void AnOffsetTargetIsHarderToHitThanACentredOne()
    {
        Assert.True(HitProbability.Circle(20, 15, 25, 0) < HitProbability.Circle(20, 15));
    }

    [Fact]
    public void TheCautiousFactorMatchesTheChiSquareTable()
    {
        // 5 shots: nu = 8, chi2(0.10, 8) = 3.490, so the factor is sqrt(8 / 3.490) = 1.514
        Assert.Equal(1.514, HitProbability.UpperSigmaFactor(5)!.Value, 2);
        // 20 shots: nu = 38, chi2(0.10, 38) = 27.343 -> 1.179
        Assert.Equal(1.179, HitProbability.UpperSigmaFactor(20)!.Value, 2);
        Assert.Null(HitProbability.UpperSigmaFactor(2));
        Assert.True(HitProbability.UpperSigmaFactor(100)! < HitProbability.UpperSigmaFactor(10)!);
    }

    [Fact]
    public void AShortGroupGetsALowerCautiousChance()
    {
        var r = HitProbability.Compute(0.6, 5, 800, HitProbability.Shape.Circle, 300, 0);
        Assert.NotNull(r.CautiousHitPct);
        Assert.True(r.CautiousHitPct < r.HitPct);
        Assert.InRange(r.HitPct, 0, 100);
        // sigma of 0.6 MOA at 800 m = 0.6 * 800 * 0.29089 = 139.6 mm
        Assert.Equal(139.6, r.SigmaMm, 1);
    }

    [Fact]
    public void WithTwoShotsThereIsNoCautiousFigure()
    {
        Assert.Null(HitProbability.Compute(0.5, 2, 300, HitProbability.Shape.Circle, 200, 0).CautiousHitPct);
    }

    [Fact]
    public void BesselScaledFunctionIsRightAtKnownPoints()
    {
        // I0(1) = 1.2660658777, I0(5) = 27.2398718236, I0(10) = 2815.716628466
        Assert.Equal(1.2660658777 * Math.Exp(-1), HitProbability.BesselI0Scaled(1), 6);
        Assert.Equal(27.2398718236 * Math.Exp(-5), HitProbability.BesselI0Scaled(5), 6);
        Assert.Equal(2815.716628466 * Math.Exp(-10), HitProbability.BesselI0Scaled(10), 6);
    }
}
