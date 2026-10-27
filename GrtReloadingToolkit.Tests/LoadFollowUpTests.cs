using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

public sealed class LoadFollowUpTests
{
    private static readonly DateTime Day0 = new(2026, 3, 1);
    private static readonly double[] Spread = { -2, 2, -1, 1, 0 };            // five shots, sample SD about 1.58

    private static FollowSession S(int day, double? temp, double mean, double[]? shape = null, string? powderLot = null, string? primerLot = null, double? seating = null)
        => new(Day0.AddDays(day), temp, (shape ?? Spread).Select(d => mean + d).ToList(), PowderLot: powderLot, PrimerLot: primerLot, SeatingMm: seating);

    // ---- least squares

    [Fact]
    public void OlsRecoversAnExactLine()
    {
        var fit = LoadFollowUp.Ols(new[] { new[] { 1.0, 0 }, new[] { 1.0, 1 }, new[] { 1.0, 2 }, new[] { 1.0, 3 } }, new[] { 2.0, 5, 8, 11 });
        Assert.NotNull(fit);
        Assert.Equal(2, fit!.Value.Beta[0], 9);
        Assert.Equal(3, fit.Value.Beta[1], 9);
        Assert.Equal(2, fit.Value.Dof);
    }

    [Fact]
    public void OlsRefusesCollinearPredictors()
        => Assert.Null(LoadFollowUp.Ols(new[] { new[] { 1.0, 1, 2 }, new[] { 1.0, 2, 4 }, new[] { 1.0, 3, 6 }, new[] { 1.0, 4, 8 } }, new[] { 1.0, 2, 3, 4 }));

    // ---- statistics per session and the gaps

    [Fact]
    public void SessionStatsUseTheSampleSdAndOrderByDate()
    {
        var r = LoadFollowUp.Analyze(new[] { S(30, 15, 810), S(0, 10, 800) });
        Assert.Equal(new[] { 0, 30 }, r.Sessions.Select(s => (int)(s.Session.Date - Day0).TotalDays));
        Assert.Equal(800, r.Sessions[0].Stats.Mean, 9);
        Assert.Equal(Math.Sqrt(2.5), r.Sessions[0].Stats.Sd, 9);       // (4+4+1+1+0)/4
        Assert.Equal(4, r.Sessions[0].Stats.Es, 9);
    }

    [Fact]
    public void WithTooFewSessionsNothingIsConcludedAndEachGapIsNamed()
    {
        var r = LoadFollowUp.Analyze(new[] { S(0, 10, 800), S(14, 20, 805), S(28, 15, 802) });
        Assert.Null(r.Temperature);
        Assert.Null(r.Drift);
        Assert.Null(r.Latest);
        Assert.Equal(3, r.NotEvaluated.Count);
    }

    [Fact]
    public void ASessionBelowThreeShotsIsListedButNotUsed()
    {
        var thin = new FollowSession(Day0.AddDays(5), 12, new[] { 800.0, 801 });
        var r = LoadFollowUp.Analyze(new[] { S(0, 10, 800), thin, S(10, 20, 805) });
        Assert.Equal(3, r.Sessions.Count);
        Assert.Null(r.Latest);                                          // 2 usable sessions only
    }

    // ---- temperature and time

    [Fact]
    public void TheTemperatureSlopeOfALoadIsRecoveredAndSeparatedFromTime()
    {
        // velocity = 800 + 0.5 m/s per C around 15 C, no drift; temperatures not following the dates
        double[] temps = { 5, 12, 20, 28, 10, 24 };
        var sessions = temps.Select((t, i) => S(i * 14, t, 800 + 0.5 * (t - 15) + new[] { 0.2, -0.3, 0.1, -0.1, 0.3, -0.2 }[i]));
        var r = LoadFollowUp.Analyze(sessions);
        Assert.NotNull(r.Temperature);
        Assert.True(r.Temperature!.TimeAccountedFor);
        Assert.Equal(0.5, r.Temperature.MpsPerC.Estimate, 1);
        Assert.True(r.Temperature.MpsPerC.Significant);
        Assert.NotNull(r.Drift);
        Assert.True(r.Drift!.TemperatureAccountedFor);
        Assert.False(r.Drift.MpsPer30Days.Significant);                 // there is none
    }

    [Fact]
    public void ADriftOverTimeIsFoundWhenTemperatureIsNotWrittenDown()
    {
        // 1 m/s lost every 30 days, small noise
        var noise = new[] { 0.2, -0.1, 0.1, -0.2, 0.15, -0.05 };
        var r = LoadFollowUp.Analyze(Enumerable.Range(0, 6).Select(i => S(i * 30, null, 800 - i + noise[i])));
        Assert.Null(r.Temperature);
        Assert.NotNull(r.Drift);
        Assert.False(r.Drift!.TemperatureAccountedFor);
        Assert.Equal(-1.0, r.Drift.MpsPer30Days.Estimate, 1);
        Assert.True(r.Drift.MpsPer30Days.Significant);
    }

    [Fact]
    public void NoisyFlatDataGivesAnEffectThatIsNotSignificant()
    {
        double[] means = { 800, 806, 797, 804, 799 };
        double[] temps = { 5, 25, 12, 18, 30 };
        var r = LoadFollowUp.Analyze(means.Select((m, i) => S(i * 10, temps[i], m)));
        Assert.NotNull(r.Temperature);
        Assert.False(r.Temperature!.MpsPerC.Significant);
    }

    [Fact]
    public void TemperatureNeedsAFiveDegreeSpread()
    {
        var r = LoadFollowUp.Analyze(Enumerable.Range(0, 6).Select(i => S(i * 14, 15 + (i % 2) * 2, 800 + i)));
        Assert.Null(r.Temperature);
    }

    // ---- the latest session

    [Fact]
    public void ALatestMeanFarFromTheUsualIsFlaggedAndANormalOneIsNot()
    {
        var baseline = new[] { 800.0, 801, 799.5, 800.5, 800, 799 }.Select((m, i) => S(i * 14, null, m)).ToList();
        var normal = LoadFollowUp.Analyze(baseline.Append(S(90, null, 800.8)));
        var off = LoadFollowUp.Analyze(baseline.Append(S(90, null, 812)));
        Assert.Equal(FollowSignal.Normal, normal.Latest!.Mean);
        Assert.Equal(FollowSignal.Out, off.Latest!.Mean);
        Assert.False(off.Latest.TemperatureAdjusted);
    }

    [Fact]
    public void ABaselineWhoseMeansAreIdenticalStillFlagsALargeDeviationButNotAnOrdinaryOne()
    {
        // means exactly alike would give the individuals chart a sigma of zero and no limits at all; the floor is the noise of one string's mean
        var baseline = Enumerable.Range(0, 5).Select(i => S(i * 14, null, 800.0)).ToList();
        var ordinary = LoadFollowUp.Analyze(baseline.Append(S(90, null, 800.4)));
        var large = LoadFollowUp.Analyze(baseline.Append(S(90, null, 806.0)));
        Assert.Equal(FollowSignal.Normal, ordinary.Latest!.Mean);
        Assert.Equal(FollowSignal.Out, large.Latest!.Mean);
    }

    [Fact]
    public void TheControlLimitsOfTheChartAndOfTheVerdictAreOneDefinition()
    {
        var r = LoadFollowUp.Analyze(Enumerable.Range(0, 6).Select(i => S(i * 14, null, 800 + (i % 2))));
        var (centre, sigma) = LoadFollowUp.MeanControl(r.Sessions);
        Assert.Equal(800.5, centre, 9);
        Assert.True(sigma > 0);
    }

    [Fact]
    public void WhenTheBaselineSaysTemperatureMattersTheLatestIsJudgedAtItsOwnTemperature()
    {
        double[] temps = { 5, 30, 10, 25, 15, 20 };
        double[] noise = { 0.3, -0.2, 0.1, -0.3, 0.2, -0.1 };
        var baseline = temps.Select((t, i) => S(i * 14, t, 800 + 0.6 * (t - 5) + noise[i])).ToList();
        var asExpected = LoadFollowUp.Analyze(baseline.Append(S(100, 35, 818)));         // 800 + 0.6 * 30
        var tooLow = LoadFollowUp.Analyze(baseline.Append(S(100, 35, 806)));
        Assert.True(asExpected.Latest!.TemperatureAdjusted);
        Assert.Equal(818, asExpected.Latest.ExpectedMps!.Value, 0);
        Assert.Equal(FollowSignal.Normal, asExpected.Latest.Mean);
        Assert.Equal(FollowSignal.Out, tooLow.Latest!.Mean);
    }

    [Fact]
    public void ALargerSpreadThanTheLoadUsuallyShowsIsFlaggedASmallerOneIsNot()
    {
        var baseline = Enumerable.Range(0, 5).Select(i => S(i * 14, null, 800 + (i % 2))).ToList();      // SD about 1.58 each
        var wide = LoadFollowUp.Analyze(baseline.Append(S(90, null, 800, new double[] { -9, 8, -7, 10, -2 })));
        var tight = LoadFollowUp.Analyze(baseline.Append(S(90, null, 800, new double[] { -0.5, 0.5, 0, 0.2, -0.2 })));
        Assert.Equal(FollowSignal.Out, wide.Latest!.Spread);
        Assert.Equal(FollowSignal.Normal, tight.Latest!.Spread);
    }

    // ---- what changed

    [Fact]
    public void ALotOrSeatingChangeIsNamedAtTheSessionWhereItShowsUp()
    {
        var r = LoadFollowUp.Analyze(new[]
        {
            S(0, 10, 800, powderLot: "A1", primerLot: "P1", seating: 5.00),
            S(14, 10, 800, powderLot: "A1", primerLot: "P1", seating: 5.02),     // 0.02: not a change
            S(28, 10, 800, powderLot: "B7", primerLot: "P1", seating: 5.02),
            S(42, 10, 800, powderLot: "B7", primerLot: "P2", seating: 4.90),
            S(56, 10, 800, powderLot: null, primerLot: null, seating: null),      // nothing written: nothing to compare
        });
        Assert.Equal(2, r.Changes.Count);
        Assert.Equal(2, r.Changes[0].Index);
        Assert.Equal(new[] { "powder lot" }, r.Changes[0].Changes);
        Assert.Equal(3, r.Changes[1].Index);
        Assert.Equal(new[] { "primer lot", "seating depth" }, r.Changes[1].Changes);
    }
}
