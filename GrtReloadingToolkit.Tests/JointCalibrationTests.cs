using GrtReloadingToolkit.Cal;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// GRT's own OBT tool calibrates the simulation to a measured velocity by scaling Ba and k by one common
/// factor (seen live on a 40.2 gr N550 load: +0.1797 % on both for +5.9 m/s, -0.1552 % on both for -5.1 m/s,
/// i.e. ~3285 m/s per unit of relative change). The toolkit does the same, with a least-squares factor over
/// every charge, and only offers the result once GRT's own simulation confirms it.
/// </summary>
public sealed class JointCalibrationTests
{
    private const double Sensitivity = 3285;       // m/s per unit relative change of Ba and k together (measured in GRT)
    private static readonly double[] Charges = { 39.2, 39.6, 40.0, 40.4, 40.8 };

    /// <summary>Points where GRT's model is `offset(charge)` m/s below the measured velocity, with the nudge and
    /// verification series the way GRT would produce them for a model that responds linearly to the common factor.</summary>
    private static CalResult Build(Func<double, double> offset, double nudge = 0.002, double? verifyFactor = null)
    {
        var r = new CalResult();
        foreach (double c in Charges)
        {
            double sim = 800 + 40 * (c - 40);
            r.Points.Add(new CalPoint
            {
                ChargeGr = c, SimMps = sim, MeasMps = sim + offset(c),
                SimMpsPertK = sim + Sensitivity * nudge,
                SimMpsVerify = verifyFactor is { } f ? sim + Sensitivity * (f - 1) : 0,
            });
        }
        return r;
    }

    [Fact]
    public void TheFactorIsWhatMovesTheSimulationOntoTheMeasuredVelocity()
    {
        // GRT is 5.9 m/s slow everywhere: the tool must land where GRT's OBT tool landed (+0.1797 %).
        var r = Build(_ => 5.9);
        var fit = r.FitJoint(0.4871454, 1.2256306, 0.002)!;

        Assert.True(fit.Ok, string.Join("; ", fit.Notes));
        Assert.Equal(1.001796, fit.Factor, 5);
        Assert.Equal(0.4871454 * fit.Factor, fit.NewBa, 9);
        Assert.Equal(1.2256306 * fit.Factor, fit.NewK, 9);
        Assert.Equal(0.48802, fit.NewBa, 4);      // what GRT showed
        Assert.Equal(1.22784, fit.NewK, 4);
    }

    [Fact]
    public void ANegativeOffsetLowersBothTogether()
    {
        var r = Build(_ => -5.1);
        var fit = r.FitJoint(0.4871454, 1.2256306, 0.002)!;
        Assert.Equal(0.99845, fit.Factor, 4);     // GRT showed -0.1552 %
        Assert.True(fit.NewBa < 0.4871454 && fit.NewK < 1.2256306);
    }

    [Fact]
    public void ASimulationThatDoesNotRespondToTheNudgeIsReportedNotDividedByZero()
    {
        var r = new CalResult();
        r.Points.Add(new CalPoint { ChargeGr = 40, SimMps = 800, MeasMps = 805, SimMpsPertK = 800 });
        var fit = r.FitJoint(0.5, 1.2, 0.002)!;
        Assert.False(fit.Ok);
        Assert.Equal(1.0, fit.Factor);
    }

    [Fact]
    public void AnAbsurdlyLargeCorrectionIsRefusedRatherThanWrittenIntoThePowder()
    {
        var r = Build(_ => 400.0);   // GRT 400 m/s off: a wrong load or wrong data, not a calibration
        var fit = r.FitJoint(0.5, 1.2, 0.002)!;
        Assert.False(fit.Ok);
        Assert.Contains("believable", fit.Notes[0]);
    }

    [Fact]
    public void AVerifiedCorrectionThatRemovesTheOffsetIsAccepted()
    {
        var r = Build(_ => 5.9, verifyFactor: 1.001796);
        var v = r.VerifyJoint()!;
        Assert.True(v.Accepted, v.Reason);
        Assert.True(Math.Abs(v.MeanFitMps) < 0.05);
    }

    [Fact]
    public void AProposalThatGrtSimulatesStillOffIsRefused()
    {
        var r = Build(_ => 5.9, verifyFactor: 1.0005);       // GRT says only ~1.6 m/s of the 5.9 was removed
        Assert.False(r.VerifyJoint()!.Accepted);
    }

    [Fact]
    public void ANoiseSizedOffsetHasNothingToCorrect()
    {
        var r = Build(_ => 0.1, verifyFactor: 1.00003);
        var v = r.VerifyJoint()!;
        Assert.False(v.Accepted);
        Assert.Contains("nothing to correct", v.Reason);
    }

    [Fact]
    public void ACorrectionThatAddsScatterIsRefused()
    {
        var r = new CalResult();
        foreach (double c in Charges)
        {
            double sim = 800;
            // the original misses by +3 everywhere; the "corrected" simulation lands the mean right but spreads +/-8
            double verify = sim + 3 + (c < 40 ? 8 : -8);
            r.Points.Add(new CalPoint { ChargeGr = c, SimMps = sim, MeasMps = sim + 3, SimMpsPertK = sim + 6, SimMpsVerify = verify });
        }
        Assert.False(r.VerifyJoint()!.Accepted);
    }

    [Fact]
    public void NothingIsDecidedUntilEveryChargeHasBeenVerified()
    {
        var r = Build(_ => 5.9, verifyFactor: 1.001796);
        r.Points[^1].SimMpsVerify = 0;
        Assert.Null(r.VerifyJoint());
    }

    [Fact]
    public void TheReportSaysWhetherTheCorrectionWasVerified()
    {
        var good = Build(_ => 5.9, verifyFactor: 1.001796);
        var fit = good.FitJoint(0.4871454, 1.2256306, 0.002)!;

        Assert.Contains("ACCEPTED", good.BuildJointReport(fit, 0.4871454, 1.2256306, "N550", good.VerifyJoint()));
        Assert.Contains("NOT verified yet", good.BuildJointReport(fit, 0.4871454, 1.2256306, "N550", null));
        var bad = Build(_ => 5.9, verifyFactor: 1.0005);
        Assert.Contains("REFUSED", bad.BuildJointReport(fit, 0.4871454, 1.2256306, "N550", bad.VerifyJoint()));
    }

    [Fact]
    public void ARowWithNoSimulatedValueYetHasNoOffsetInTheReport()
    {
        var res = new CalResult();
        res.Points.Add(new CalPoint { ChargeGr = 40.2, MeasMps = 806.6 });
        Assert.DoesNotContain("+806", res.BuildReport("Barrel Calibration", 0.5, "N550"));
    }
}
