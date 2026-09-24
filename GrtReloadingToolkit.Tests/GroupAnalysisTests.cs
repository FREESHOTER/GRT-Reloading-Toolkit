using GrtReloadingToolkit.Log;
using GrtReloadingToolkit.Ocw;
using Xunit;
using static GrtReloadingToolkit.Log.GroupAnalysis;

namespace GrtReloadingToolkit.Tests;

public sealed class GroupAnalysisTests
{
    private static TargetGroup Group(double distanceM, params (double X, double Y)[] points)
    {
        var g = new TargetGroup { SourceFile = "test", DistanceM = distanceM };
        foreach (var (x, y) in points) g.Impacts.Add(new Impact(x, y));
        return g;
    }

    // ── Range classification ────────────────────────────────────────────

    [Theory]
    [InlineData(100, DistanceRangeKind.ShortRange)]
    [InlineData(500, DistanceRangeKind.ShortRange)]
    [InlineData(500.1, DistanceRangeKind.LongRange)]
    [InlineData(1000, DistanceRangeKind.LongRange)]
    public void ClassifyRangeUsesTheConfiguredCutoverInclusiveOfTheBoundary(double distanceM, DistanceRangeKind expected)
    {
        Assert.Equal(expected, ClassifyRange(distanceM, new GroupAnalysisConfig()));
    }

    // ── CEP50 ────────────────────────────────────────────────────────────

    [Fact]
    public void Cep50MatchesTheRayleighRelationOnAKnownSymmetricGroup()
    {
        // Four impacts at unit distance on the axes: varX = varY = 2/3, sigma = sqrt(2/3),
        // CEP50 = sigma*sqrt(ln4) -- hand-verified independently.
        var g = Group(100, (1, 0), (-1, 0), (0, 1), (0, -1));
        Assert.Equal(0.961, Cep50Moa(g), 3);
    }

    [Fact]
    public void HorizontalAndVerticalSpreadAreIndependentOfEachOther()
    {
        var g = Group(100, (1, 0), (-1, 0), (0, 1), (0, -1));
        Assert.Equal(2.0, g.HorizontalSpreadMoa, 9);
        Assert.Equal(2.0, g.VerticalSpreadMoa, 9);
    }

    // ── Mahalanobis outliers ─────────────────────────────────────────────

    [Fact]
    public void MahalanobisOutliersFlagsAClearFlierWithoutFlaggingTheTightCluster()
    {
        // 9 tightly jittered shots plus one clear flier -- numerically verified beforehand so the
        // flier's Mahalanobis distance clears 2*ln(2n) while none of the cluster's do.
        var g = Group(100,
            (0.02, 0.01), (-0.02, 0.015), (0.01, -0.02), (-0.015, -0.01), (0.005, 0.02),
            (-0.01, 0.005), (0.015, -0.015), (-0.02, -0.02), (0.0, 0.0), (3.0, 0.5));

        var outliers = MahalanobisOutliers(g, new GroupAnalysisConfig());

        Assert.Equal(10, outliers.Count);
        Assert.True(outliers[9].Flagged);
        Assert.All(outliers.Take(9), o => Assert.False(o.Flagged));
    }

    [Fact]
    public void MahalanobisOutliersReturnsEmptyBelowTheMinimumShotCount()
    {
        var cfg = new GroupAnalysisConfig { MinShotsForOutliers = 5 };
        var g = Group(100, (0, 0), (1, 0), (0, 1), (5, 5)); // n = 4 < 5

        Assert.Empty(MahalanobisOutliers(g, cfg));
    }

    [Fact]
    public void MahalanobisOutliersReturnsEmptyForADegenerateCollinearGroup()
    {
        // All five impacts lie exactly on y = x: the sample covariance is singular (det = 0),
        // so a Mahalanobis read would be a divide-by-zero guess rather than a real answer.
        var g = Group(100, (0, 0), (1, 1), (2, 2), (3, 3), (-1, -1));
        Assert.Empty(MahalanobisOutliers(g, new GroupAnalysisConfig()));
    }

    // ── Score ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.4, DistanceRangeKind.ShortRange, 10.0)]
    [InlineData(0.5, DistanceRangeKind.ShortRange, 10.0)]
    [InlineData(0.6, DistanceRangeKind.ShortRange, 8.0)]
    [InlineData(3.0, DistanceRangeKind.ShortRange, 0.0)]
    [InlineData(0.6, DistanceRangeKind.LongRange, 10.0)] // same MOA scores higher long range: more lenient tiers
    public void ScoreUsesMeanRadiusAgainstTheRangeAppropriateTiers(double meanRadiusMoa, DistanceRangeKind range, double expected)
    {
        // Build a group whose MeanRadiusMoa is exactly the requested value: two impacts symmetric
        // about the origin at that radius put the centroid at (0,0) and each impact's distance
        // from it at meanRadiusMoa.
        var g = Group(range == DistanceRangeKind.LongRange ? 800 : 100, (meanRadiusMoa, 0), (-meanRadiusMoa, 0));
        Assert.Equal(expected, Score(g, range, new GroupAnalysisConfig()));
    }

    // ── Confidence ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(4, DistanceRangeKind.ShortRange, ConfidenceLevel.Low)]
    [InlineData(5, DistanceRangeKind.ShortRange, ConfidenceLevel.Medium)]
    [InlineData(10, DistanceRangeKind.ShortRange, ConfidenceLevel.High)]
    [InlineData(7, DistanceRangeKind.LongRange, ConfidenceLevel.Low)] // would be Medium short range, not long
    [InlineData(8, DistanceRangeKind.LongRange, ConfidenceLevel.Medium)]
    [InlineData(15, DistanceRangeKind.LongRange, ConfidenceLevel.High)]
    public void ConfidenceIsStricterAtLongRangeThanShortRange(int shotCount, DistanceRangeKind range, ConfidenceLevel expected)
    {
        Assert.Equal(expected, Confidence(shotCount, range, new GroupAnalysisConfig()));
    }

    [Fact]
    public void ConfidenceDoesNotChangeWhenScoreWouldChange()
    {
        // A tight group and a loose group with the same shot count score very differently, but they
        // must report the same Confidence -- the two axes are independent by design, never collapsed
        // into one number (a tight group from few shots is not the same claim as one from many).
        var cfg = new GroupAnalysisConfig();
        var tight = Group(100, (0.1, 0), (-0.1, 0), (0, 0.1), (0, -0.1), (0.05, 0.05));
        var loose = Group(100, (3, 0), (-3, 0), (0, 3), (0, -3), (1.5, 1.5));

        Assert.NotEqual(Score(tight, DistanceRangeKind.ShortRange, cfg), Score(loose, DistanceRangeKind.ShortRange, cfg));
        Assert.Equal(Confidence(tight.Impacts.Count, DistanceRangeKind.ShortRange, cfg), Confidence(loose.Impacts.Count, DistanceRangeKind.ShortRange, cfg));
    }

    // ── Problem diagnosis ────────────────────────────────────────────────

    [Fact]
    public void DiagnoseProblemsFlagsASmallSample()
    {
        var g = Group(100, (0, 0), (0.1, 0), (0, 0.1)); // n = 3, below ShortRangeMediumAt = 5
        var problems = DiagnoseProblems(g, DistanceRangeKind.ShortRange, new GroupAnalysisConfig());
        Assert.Contains(problems, p => p.Kind == ProblemKind.SmallSample);
    }

    [Fact]
    public void DiagnoseProblemsFlagsASkewedSpreadRatio()
    {
        // Vertical spread (4) is well over 1.5x the horizontal spread (1) -- default threshold.
        var g = Group(100, (0, -2), (0, 2), (0.5, 0), (-0.5, 0), (0.2, 1), (-0.2, -1));
        var problems = DiagnoseProblems(g, DistanceRangeKind.ShortRange, new GroupAnalysisConfig());
        var p = Assert.Single(problems, p => p.Kind == ProblemKind.SpreadRatioSkewed);
        Assert.Contains("vertical", p.Message);
    }

    [Fact]
    public void DiagnoseProblemsFlagsOffCenterFromPointOfAim()
    {
        var g = Group(100, (2, 2), (2.1, 1.9), (1.9, 2.1), (2.05, 1.95), (1.95, 2.05));
        g.AimXMoa = 0; g.AimYMoa = 0; // point of aim is the origin, group centres near (2,2)
        var problems = DiagnoseProblems(g, DistanceRangeKind.ShortRange, new GroupAnalysisConfig());
        Assert.Contains(problems, p => p.Kind == ProblemKind.OffCenter);
    }

    [Fact]
    public void DiagnoseProblemsAlwaysAddsTheWindCaveatAtLongRangeOnly()
    {
        var g = Group(800, (0, 0), (0.1, 0), (0, 0.1), (0.1, 0.1), (-0.1, -0.1));
        var longRange = DiagnoseProblems(g, DistanceRangeKind.LongRange, new GroupAnalysisConfig());
        var shortRange = DiagnoseProblems(g, DistanceRangeKind.ShortRange, new GroupAnalysisConfig());
        Assert.Contains(longRange, p => p.Kind == ProblemKind.LongRangeWindCaveat);
        Assert.DoesNotContain(shortRange, p => p.Kind == ProblemKind.LongRangeWindCaveat);
    }

    [Fact]
    public void DiagnoseProblemsCrossChecksAppCenterOnlyWhenAsked()
    {
        var g = Group(100, (0, 0), (0.1, 0), (0, 0.1), (0.1, 0.1), (-0.1, -0.1));
        g.AppCenterXMoa = 5; g.AppCenterYMoa = 5; // wildly different from the impact-mean centroid

        var withoutCrossCheck = DiagnoseProblems(g, DistanceRangeKind.ShortRange, new GroupAnalysisConfig(), crossCheckAppCenter: false);
        var withCrossCheck = DiagnoseProblems(g, DistanceRangeKind.ShortRange, new GroupAnalysisConfig(), crossCheckAppCenter: true);

        Assert.DoesNotContain(withoutCrossCheck, p => p.Kind == ProblemKind.CenterMismatch);
        Assert.Contains(withCrossCheck, p => p.Kind == ProblemKind.CenterMismatch);
    }

    // ── Manual entry (third data source) ────────────────────────────────

    [Fact]
    public void BuildManualGroupConvertsMmToMoaViaTheSameConstantAsMoaToMm()
    {
        double distanceM = 100;
        double moa = 1.3;
        double mm = TargetGroup.MoaToMm(moa, distanceM); // round-trip through the existing, already-tested conversion

        var g = BuildManualGroup(distanceM, new List<(double, double)> { (mm, -mm) }, aimXMm: 0, aimYMm: 0);

        Assert.Equal(moa, g.Impacts[0].XMoa, 6);
        Assert.Equal(-moa, g.Impacts[0].YMoa, 6);
        Assert.Equal(0, g.AimXMoa, 9);
        Assert.Equal(0, g.AimYMoa, 9);
    }

    // ── Velocity-to-dispersion correlation (per-shot, firing-order aligned) ─

    [Fact]
    public void VelocityPoiCorrelationIsPerfectWhenRadiusIsExactlyProportionalToVelocity()
    {
        // Impacts' radius-from-centroid is exactly (velocity-800)/50 in firing order, so alignment
        // by shot index (SdRootCause.AlignShotsWithGroup) must recover a perfect r=1.
        var g = Group(100, (0, 0), (0.1, 0), (0.2, 0), (0.3, 0), (0.4, 0)); // centroid (0.2,0)
        var velocities = new List<double> { 810, 805, 800, 805, 810 };

        double? r = VelocityPoiCorrelation(velocities, g);

        Assert.NotNull(r);
        Assert.Equal(1.0, r!.Value, 6);
    }

    [Fact]
    public void VelocityPoiCorrelationIsNullWhenShotCountDoesNotMatchImpactCount()
    {
        var g = Group(100, (0, 0), (0.1, 0), (0.2, 0)); // 3 impacts
        var velocities = new List<double> { 800, 801, 802, 803 }; // 4 shots -- can't align
        Assert.Null(VelocityPoiCorrelation(velocities, g));
    }

    [Fact]
    public void VelocityPoiCorrelationIsNullBelowThreeAlignedPairs()
    {
        var g = Group(100, (0, 0), (0.1, 0));
        var velocities = new List<double> { 800, 801 };
        Assert.Null(VelocityPoiCorrelation(velocities, g));
    }

    [Fact]
    public void DiagnoseOmitsVelocityCorrelationWhenNoVelocitiesGiven()
    {
        var g = Group(100, (0, 0), (0.1, 0), (0.2, 0), (0.3, 0), (0.4, 0));
        var report = Diagnose(g, new GroupAnalysisConfig());
        Assert.Null(report.VelocityPoiR);
    }

    [Fact]
    public void DiagnoseIncludesVelocityCorrelationWhenVelocitiesAlign()
    {
        var g = Group(100, (0, 0), (0.1, 0), (0.2, 0), (0.3, 0), (0.4, 0));
        var velocities = new List<double> { 810, 805, 800, 805, 810 };
        var report = Diagnose(g, new GroupAnalysisConfig(), velocities: velocities);
        Assert.NotNull(report.VelocityPoiR);
        Assert.Equal(1.0, report.VelocityPoiR!.Value, 6);
    }

    [Fact]
    public void DiagnoseFlagsAProblemWhenVelocityCorrelationExceedsTheThreshold()
    {
        // r=1.0 here comfortably exceeds the default 0.3 threshold.
        var g = Group(100, (0, 0), (0.1, 0), (0.2, 0), (0.3, 0), (0.4, 0));
        var velocities = new List<double> { 810, 805, 800, 805, 810 };
        var report = Diagnose(g, new GroupAnalysisConfig(), velocities: velocities);
        var p = Assert.Single(report.Problems, p => p.Kind == ProblemKind.VelocityCorrelation);
        Assert.Contains("further from", p.Message);
    }

    [Fact]
    public void DiagnoseProblemsSaysCloserToNotFurtherFromForANegativeCorrelation()
    {
        // Mirror image of the positive-correlation fixture: here the FASTEST shot (810) sits exactly
        // at the centroid (radius 0) and the two slowest (800) sit furthest out -- a perfect r=-1,
        // so the generated sentence must say "closer to", not always default to "further from".
        var g = Group(100, (0, 0), (0.1, 0), (0.2, 0), (0.3, 0), (0.4, 0)); // centroid (0.2,0)
        var velocities = new List<double> { 800, 805, 810, 805, 800 };

        double? r = VelocityPoiCorrelation(velocities, g);
        Assert.Equal(-1.0, r!.Value, 6);

        var problems = DiagnoseProblems(g, DistanceRangeKind.ShortRange, new GroupAnalysisConfig(), velocityPoiR: r);
        var p = Assert.Single(problems, p => p.Kind == ProblemKind.VelocityCorrelation);
        Assert.Contains("closer to", p.Message);
        Assert.DoesNotContain("further from", p.Message);
    }

    [Fact]
    public void DiagnoseDoesNotFlagAWeakVelocityCorrelation()
    {
        // Configure an unreachable threshold (2.0 -- Pearson r can never exceed 1.0 in magnitude) to
        // prove the flag is conditional, without needing to hand-construct a weakly-correlated sample.
        var g = Group(100, (0, 0), (0.1, 0), (0.2, 0), (0.3, 0), (0.4, 0));
        var velocities = new List<double> { 810, 805, 800, 805, 810 };
        var cfg = new GroupAnalysisConfig { VelocityCorrelationThreshold = 2.0 };
        var report = Diagnose(g, cfg, velocities: velocities);
        Assert.DoesNotContain(report.Problems, p => p.Kind == ProblemKind.VelocityCorrelation);
    }

    // ── Combining multiple groups (pooled dispersion, POI-normalized) ───

    [Fact]
    public void CombineGroupsRecentersEachGroupBeforePoolingSoPoiOffsetsDontInflateDispersion()
    {
        // Two groups with very different centres (5,5) and (-3,2) but the IDENTICAL relative jitter
        // pattern -- after recentering, the pooled 8 impacts must reduce to the same {(1,0),(-1,0),
        // (0,1),(0,-1)} shape twice over, so the combined mean radius equals each source group's own
        // (1.0), not something inflated by the centres being far apart.
        var a = Group(100, (6, 5), (4, 5), (5, 6), (5, 4));   // centre (5,5), same unit-radius jitter
        var b = Group(100, (-2, 2), (-4, 2), (-3, 3), (-3, 1)); // centre (-3,2), same jitter

        var combined = CombineGroups(new[] { a, b });

        Assert.Equal(8, combined.Impacts.Count);
        Assert.Equal(0.0, combined.CenterXMoa, 9);
        Assert.Equal(0.0, combined.CenterYMoa, 9);
        Assert.Equal(1.0, combined.MeanRadiusMoa, 9);
    }

    [Fact]
    public void CombineGroupsOfEmptyListReturnsAnEmptyGroup()
    {
        var combined = CombineGroups(Array.Empty<TargetGroup>());
        Assert.Empty(combined.Impacts);
    }

    // ── Orchestrator ─────────────────────────────────────────────────────

    [Fact]
    public void DiagnoseReturnsAConsistentReportAcrossAllFields()
    {
        var cfg = new GroupAnalysisConfig();
        var g = Group(800, (0.02, 0.01), (-0.02, 0.015), (0.01, -0.02), (-0.015, -0.01), (0.005, 0.02),
            (-0.01, 0.005), (0.015, -0.015), (-0.02, -0.02), (0.0, 0.0), (3.0, 0.5));

        var report = Diagnose(g, cfg);

        Assert.Equal(DistanceRangeKind.LongRange, report.Range);
        Assert.Equal(g.MeanRadiusMoa, report.MeanRadiusMoa, 9);
        Assert.Equal(g.GroupEsMoa, report.GroupEsMoa, 9);
        Assert.True(report.Outliers[9].Flagged);
        Assert.Contains(report.Problems, p => p.Kind == ProblemKind.StatisticalOutlier);
        Assert.Contains(report.Problems, p => p.Kind == ProblemKind.LongRangeWindCaveat);
    }
}
