using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The empirical MV(charge, temperature) fit. Every "fits" case uses data built from an exact known
/// formula, so the recovered coefficients have a ground truth to check against, not just "some
/// number came back". Every "does not fit" case is a specific way real logged data comes out
/// underdetermined -- each must be told apart, not collapsed into one generic "not enough data".
/// </summary>
public class VelocityModelTests
{
    private const double A = 700.0, B = 10.0, C = -2.0; // MV = 700 + 10*charge - 2*temperature
    private static double Mv(double chg, double temp) => A + B * chg + C * temp;

    private static readonly (double, double, double)[] VariedPoints =
    {
        (38, 10, Mv(38, 10)),
        (38, 20, Mv(38, 20)),
        (40, 10, Mv(40, 10)),
        (40, 30, Mv(40, 30)),
        (42, 20, Mv(42, 20)),
        (42, 0, Mv(42, 0)),
    };

    [Fact]
    public void ExactLinearDataRecoversTheTrueCoefficients()
    {
        var r = VelocityModel.Fit(VariedPoints);

        Assert.True(r.Fitted);
        Assert.Equal(A, r.Intercept, 6);
        Assert.Equal(B, r.ChargeCoef, 6);
        Assert.Equal(C, r.TempCoef, 6);
        Assert.Equal(1.0, r.R2, 6);
        Assert.Equal(VariedPoints.Length, r.N);
    }

    [Fact]
    public void FewerThanFivePointsIsToolFew()
    {
        var r = VelocityModel.Fit(VariedPoints[..4]);

        Assert.False(r.Fitted);
        Assert.Equal(VelocityModel.UnfitReason.TooFewPoints, r.Reason);
    }

    [Fact]
    public void ExactlyFivePointsIsEnough()
    {
        var r = VelocityModel.Fit(VariedPoints[..5]);

        Assert.True(r.Fitted);
    }

    [Fact]
    public void EveryStringAtTheSameChargeHasNoChargeVarianceToFitFrom()
    {
        var points = new (double, double, double)[]
        {
            (40, 5, Mv(40, 5)), (40, 10, Mv(40, 10)), (40, 15, Mv(40, 15)),
            (40, 20, Mv(40, 20)), (40, 25, Mv(40, 25)),
        };

        var r = VelocityModel.Fit(points);

        Assert.False(r.Fitted);
        Assert.Equal(VelocityModel.UnfitReason.NoChargeVariance, r.Reason);
    }

    [Fact]
    public void EveryStringAtTheSameTemperatureHasNoTemperatureVarianceToFitFrom()
    {
        var points = new (double, double, double)[]
        {
            (38, 20, Mv(38, 20)), (39, 20, Mv(39, 20)), (40, 20, Mv(40, 20)),
            (41, 20, Mv(41, 20)), (42, 20, Mv(42, 20)),
        };

        var r = VelocityModel.Fit(points);

        Assert.False(r.Fitted);
        Assert.Equal(VelocityModel.UnfitReason.NoTemperatureVariance, r.Reason);
    }

    [Fact]
    public void ChargeAndTemperatureMovingTogetherOnEveryStringIsCollinear()
    {
        // Both predictors vary here (neither variance check trips), but temperature always equals
        // charge -- no combination of a charge coefficient and a temperature coefficient can be told
        // apart from any other that gives the same sum, so the fit is genuinely underdetermined.
        var points = new (double, double, double)[]
        {
            (38, 38, Mv(38, 38)), (39, 39, Mv(39, 39)), (40, 40, Mv(40, 40)),
            (41, 41, Mv(41, 41)), (42, 42, Mv(42, 42)),
        };

        var r = VelocityModel.Fit(points);

        Assert.False(r.Fitted);
        Assert.Equal(VelocityModel.UnfitReason.Collinear, r.Reason);
    }

    [Fact]
    public void NoisyDataStillFitsWithAnHonestlyLowerR2()
    {
        var noisy = new (double, double, double)[]
        {
            (38, 10, Mv(38, 10) + 0.4),
            (38, 20, Mv(38, 20) - 0.3),
            (40, 10, Mv(40, 10) + 0.2),
            (40, 30, Mv(40, 30) - 0.5),
            (42, 20, Mv(42, 20) + 0.1),
            (42, 0, Mv(42, 0) - 0.2),
        };

        var r = VelocityModel.Fit(noisy);

        Assert.True(r.Fitted);
        Assert.InRange(r.ChargeCoef, B - 1, B + 1);
        Assert.InRange(r.TempCoef, C - 1, C + 1);
        Assert.InRange(r.R2, 0.9, 1.0); // real signal, small noise -- should stay high but not exactly 1
    }

    [Fact]
    public void PredictAndChargeForTargetRoundTrip()
    {
        var r = VelocityModel.Fit(VariedPoints);

        double predicted = VelocityModel.Predict(r, 40, 15);
        Assert.Equal(Mv(40, 15), predicted, 6);

        double neededCharge = VelocityModel.ChargeForTarget(r, predicted, 15);
        Assert.Equal(40, neededCharge, 6);
    }

    [Fact]
    public void ChargeForTargetIsNaNWhenTheChargeCoefficientIsZero()
    {
        var zeroChargeCoef = new VelocityModel.Result(6, VelocityModel.UnfitReason.None, 700, 0, -2, 0.5);

        Assert.True(double.IsNaN(VelocityModel.ChargeForTarget(zeroChargeCoef, 800, 20)));
    }
}
