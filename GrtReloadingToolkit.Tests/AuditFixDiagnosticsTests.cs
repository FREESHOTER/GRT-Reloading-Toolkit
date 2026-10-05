using GrtReloadingToolkit.Log;
using GrtReloadingToolkit.Ocw;
using GrtReloadingToolkit.Ui;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>The diagnostics half of the real-data audit: verdicts that must not be given when the
/// numbers cannot support them, and labels that must stay unique.</summary>
public sealed class AuditFixDiagnosticsTests
{
    // ── F9: a step number inside a name ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("N550 40.2", 40.2)]
    [InlineData("139LVN550 40,20", 40.2)]
    [InlineData("40.20", 40.2)]
    [InlineData("seat 12", 12)]
    public void ADecimalStepBeatsAPowderNumberInTheName(string name, double expected) =>
        Assert.Equal(expected, LadderLoader.MeasurementStep(LadderMode.Charge, null, name, null)!.Value, 6);

    // ── F8: GRT calls every Measurement "Misurazione" ────────────────────────────────────────────

    [Fact]
    public void DuplicateRowLabelsGetANumberAndNeverCollide()
    {
        var taken = new List<string>();
        foreach (var l in new[] { "40.2 gr (Misurazione)", "40.2 gr (Misurazione)", "40.2 gr (Misurazione)", "40.4 gr (Misurazione)" })
            taken.Add(RowLabels.Unique(taken, l));

        Assert.Equal(4, taken.Distinct().Count());
        Assert.Equal("40.2 gr (Misurazione) #3", taken[2]);
    }

    [Fact]
    public void TheShotFingerprintSeparatesTwoStringsAtTheSameCharge()
    {
        var a = RowLabels.Fingerprint(new[] { 830.0, 831.0, 829.5 });
        var b = RowLabels.Fingerprint(new[] { 830.0, 831.0, 829.6 });
        Assert.NotEqual(a, b);
        Assert.Equal(a, RowLabels.Fingerprint(new[] { 830.0, 831.0, 829.5 }));
    }

    // ── F14: statistics that cannot support a verdict must not give one ──────────────────────────

    [Fact]
    public void ColdBoreWithFewWarmShotsIsInconclusiveNotNotable()
    {
        // The real case: first shot +4.9 m/s over a warm SD of 1.7 on a 4-shot string.
        var v = new List<double> { 810.3, 803.5, 805.8, 806.9 };
        var flags = new List<bool> { true, false, false, false };

        var r = AdvancedDiagnostics.ColdBoreAnalysis(v, flags);

        Assert.True(r.Inconclusive);
        Assert.False(r.ColdBoreIsOutlier);
    }

    [Fact]
    public void ColdBoreWithEnoughWarmShotsStillGivesAVerdict()
    {
        var v = new List<double> { 830, 800, 801, 799, 800, 800 };
        var r = AdvancedDiagnostics.ColdBoreAnalysis(v, new List<bool> { true, false, false, false, false, false });
        Assert.False(r.Inconclusive);
        Assert.True(r.ColdBoreIsOutlier);
    }

    [Fact]
    public void ASlopeSmallerThanTheChronographNoiseGivesNoPressureEstimate()
    {
        // 0.2 gr steps, means good to +/-1.5 m/s: the last slope is 10 +/- 10.6 m/s per grain.
        var charges = new List<double> { 39.2, 39.4, 39.6, 39.8, 40.0 };
        var means = new List<double> { 806.6, 814.2, 816.9, 822.9, 828.7 };
        var se = new List<double> { 1.5, 1.5, 1.5, 1.5, 1.5 };

        var r = PressureTrend.AnalyzePressureTrend(charges, means, se);

        Assert.Equal("unknown", r.PressureLevel);
        Assert.StartsWith("Inconclusive", r.PressureEstimate);
        Assert.False(r.FlatteningTrend);
    }

    [Fact]
    public void WithoutNoiseInformationThePressureTrendBehavesAsBefore()
    {
        var charges = new List<double> { 39, 39.5, 40, 40.5, 41 };
        var r = PressureTrend.AnalyzePressureTrend(charges, new List<double> { 800, 805, 810, 815, 820 });
        Assert.Equal("normal", r.PressureLevel);
    }

    [Fact]
    public void TheNoiseGateFollowsTheChargesWhenTheyArriveOutOfOrder()
    {
        var charges = new List<double> { 40.0, 39.2, 39.6 };
        var means = new List<double> { 830, 800, 815 };
        var se = new List<double> { 0.1, 0.1, 0.1 };
        Assert.NotEqual("unknown", PressureTrend.AnalyzePressureTrend(charges, means, se).PressureLevel);
    }

    // ── Group Analysis: small groups and the problem list ────────────────────────────────────────

    private static TargetGroup RoundishGroup(int n)
    {
        var g = new TargetGroup { SourceFile = "t", DistanceM = 100 };
        double[] xs = { 0.9, -0.2, 0.1, -0.8, 0.5, -0.4 };
        double[] ys = { 0.1, 0.2, -0.1, 0.0, 0.2, -0.2 };
        for (int i = 0; i < n; i++) g.Impacts.Add(new Impact(xs[i], ys[i]));
        return g;
    }

    [Fact]
    public void TheSpreadRatioIsNotJudgedOnAFourShotGroup()
    {
        var cfg = new GroupAnalysisConfig();
        var four = RoundishGroup(4);
        Assert.True(four.HorizontalSpreadMoa / four.VerticalSpreadMoa > cfg.SpreadRatioThreshold);   // the pattern is there...
        var band = GroupAnalysis.ClassifyBand(100, cfg);
        Assert.DoesNotContain(GroupAnalysis.DiagnoseProblems(four, band, cfg), p => p.Kind == ProblemKind.SpreadRatioSkewed);   // ...but 4 shots cannot prove it
        Assert.Contains(GroupAnalysis.DiagnoseProblems(RoundishGroup(6), band, cfg), p => p.Kind == ProblemKind.SpreadRatioSkewed);
    }

    [Fact]
    public void ProblemsCanBeShownInAnotherLanguageWithTheNumbersIntact()
    {
        var cfg = new GroupAnalysisConfig();
        var g = RoundishGroup(4);
        var band = GroupAnalysis.ClassifyBand(100, cfg);
        var small = GroupAnalysis.DiagnoseProblems(g, band, cfg).First(p => p.Kind == ProblemKind.SmallSample);

        string it = small.Localized(t => t.StartsWith("Sample size") ? "Campione piccolo (n={0}, servono {1})" : t);

        Assert.Equal($"Campione piccolo (n=4, servono {band.MediumAt})", it);
        Assert.Contains("Sample size (n=4)", small.Message);   // English message unchanged
    }

    [Fact]
    public void AGroupFromALadderStepSaysItsCentreMovesWithTheCharge()
    {
        var cfg = new GroupAnalysisConfig();
        var g = RoundishGroup(5);
        g.ChargeGrains = 40.2;
        g.AimYMoa = -10;       // far from the aim point
        var band = GroupAnalysis.ClassifyBand(100, cfg);
        var off = GroupAnalysis.DiagnoseProblems(g, band, cfg).First(p => p.Kind == ProblemKind.OffCenter);
        Assert.Contains("on a ladder", off.Message);
    }

    // ── Unlinked-load identity ───────────────────────────────────────────────────────────────────

    [Fact]
    public void ALoadsIdentityIgnoresToolkitSuffixesAndCase()
    {
        Assert.Equal(LoadIdentity.Normalize("Nosler 77"), LoadIdentity.Normalize("nosler 77_toolkit_20261002_170922"));
        Assert.Equal("", LoadIdentity.Of(1, 2, "anything"));
        Assert.Equal("a", LoadIdentity.Of(null, 2, "A_toolkit_20261001_101010"));
    }
}
