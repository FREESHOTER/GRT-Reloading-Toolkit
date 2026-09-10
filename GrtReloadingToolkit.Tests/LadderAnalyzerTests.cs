using GrtReloadingToolkit.Ocw;
using Xunit;

namespace GrtReloadingToolkit.Tests;

public class LadderAnalyzerTests
{
    /// <summary>One ladder step. A velocity of 0 means "not chronographed", a mean radius of 0
    /// means "no target shot" — the two kinds of hole a real ladder comes back with.</summary>
    private static LadderStep Step(double x, double mv = 0, double sd = 0, double poiY = 0, double meanRadius = 0) => new()
    {
        X = x,
        VelN = mv > 0 ? 5 : 0,
        MeanMps = mv,
        SdMps = sd,
        Impacts = meanRadius > 0 ? 5 : 0,
        DistanceM = 100,
        PoiYMoa = poiY,
        MeanRadiusMoa = meanRadius,
    };

    private static (double Low, double High) Bounds(NodeWindow? n)
    {
        Assert.NotNull(n);
        return (n!.Low, n.High);
    }

    private const string BestEffort = "best-effort";

    // A charge ladder where the chrono was only running for the first three steps.
    // The last window has no velocity data at all, so its velocity range and SD are both
    // 0 — the lowest, i.e. best, score on those terms if coverage is not checked.
    private static readonly LadderStep[] PartlyChronographed =
    {
        Step(40.0, mv: 800, sd: 6, poiY: 0.20, meanRadius: 0.60),
        Step(40.3, mv: 810, sd: 6, poiY: 0.25, meanRadius: 0.62),
        Step(40.6, mv: 820, sd: 6, poiY: 0.22, meanRadius: 0.61),
        Step(40.9, poiY: 0.60, meanRadius: 0.95),
        Step(41.2, poiY: 0.10, meanRadius: 0.90),
        Step(41.5, poiY: 0.55, meanRadius: 0.98),
    };

    [Fact]
    public void RecommendedNodeIgnoresWindowsWithNoChrono()
    {
        var r = LadderAnalyzer.Analyze(PartlyChronographed, LadderMode.Charge, "gr");

        // 40.9-41.5 is the unmeasured window and used to win outright.
        Assert.Equal((40.0, 40.6), Bounds(r.BestNode));
    }

    [Fact]
    public void RecommendedNodeIsNotHedgedWhenACompleteWindowExists()
    {
        var r = LadderAnalyzer.Analyze(PartlyChronographed, LadderMode.Charge, "gr");

        Assert.DoesNotContain(r.Warnings, w => w.Contains(BestEffort));
        // the pre-existing "some steps have no chrono velocity" warning still fires
        Assert.Contains(r.Warnings, w => w.Contains("no chrono velocity"));
    }

    [Fact]
    public void SeatingTestWithoutAChronoStillGetsANode()
    {
        // Velocity is missing from every step, so it cannot discriminate between windows
        // and must not be required — otherwise a seating test shot without a chrono, which
        // is the normal case, would get no recommendation at all.
        var steps = new[]
        {
            Step(2.00, poiY: 0.80, meanRadius: 1.10),
            Step(2.05, poiY: 0.20, meanRadius: 0.55),
            Step(2.10, poiY: 0.24, meanRadius: 0.58),
            Step(2.15, poiY: 0.22, meanRadius: 0.57),
            Step(2.20, poiY: 0.90, meanRadius: 1.20),
        };

        var r = LadderAnalyzer.Analyze(steps, LadderMode.Seating, "mm");

        Assert.Equal((2.05, 2.15), Bounds(r.BestNode));
        Assert.DoesNotContain(r.Warnings, w => w.Contains(BestEffort));
    }

    [Fact]
    public void PartlyMeasuredLadderWithNoCompleteWindowSaysSo()
    {
        // Every window is missing something, so there is nothing clean to recommend.
        // A node is still offered — it is the best available — but it is labelled.
        var steps = new[]
        {
            Step(40.0, mv: 800, sd: 6, poiY: 0.20, meanRadius: 0.60),
            Step(40.3, poiY: 0.25, meanRadius: 0.62),
            Step(40.6, mv: 820, sd: 6, poiY: 0.22, meanRadius: 0.61),
            Step(40.9, poiY: 0.60, meanRadius: 0.95),
            Step(41.2, mv: 830, sd: 6, poiY: 0.10, meanRadius: 0.90),
        };

        var r = LadderAnalyzer.Analyze(steps, LadderMode.Charge, "gr");

        Assert.NotNull(r.BestNode);
        Assert.Contains(r.Warnings, w => w.Contains(BestEffort));
    }

    // A ladder with a genuine node: 40.6-41.2 is flat (820/821/822), has the tightest
    // groups and the least vertical spread. Every step is measured, so coverage plays
    // no part — this pins the ordinary path against regression.
    private static readonly LadderStep[] FullyMeasured =
    {
        Step(40.0, mv: 800, sd: 9, poiY: 0.80, meanRadius: 0.90),
        Step(40.3, mv: 812, sd: 8, poiY: 0.70, meanRadius: 0.88),
        Step(40.6, mv: 820, sd: 5, poiY: 0.22, meanRadius: 0.60),
        Step(40.9, mv: 821, sd: 5, poiY: 0.25, meanRadius: 0.58),
        Step(41.2, mv: 822, sd: 5, poiY: 0.24, meanRadius: 0.61),
        Step(41.5, mv: 840, sd: 9, poiY: 0.95, meanRadius: 0.95),
    };

    [Fact]
    public void FullyMeasuredLadderFindsTheFlatWindow()
    {
        var r = LadderAnalyzer.Analyze(FullyMeasured, LadderMode.Charge, "gr");

        Assert.Equal((40.6, 41.2), Bounds(r.BestNode));
        Assert.Equal((40.6, 41.2), Bounds(r.PrimaryNode));   // velocity flat-spot
        Assert.Equal((40.6, 41.2), Bounds(r.PoiNode));       // vertical-POI
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void StepsAreSortedByXBeforeAnalysis()
    {
        var shuffled = new[] { FullyMeasured[3], FullyMeasured[0], FullyMeasured[5], FullyMeasured[2], FullyMeasured[4], FullyMeasured[1] };

        var r = LadderAnalyzer.Analyze(shuffled, LadderMode.Charge, "gr");

        Assert.Equal(FullyMeasured.Select(s => s.X), r.Rows.Select(s => s.X));
        Assert.Equal((40.6, 41.2), Bounds(r.BestNode));
    }

    [Fact]
    public void TooFewStepsProducesNoNode()
    {
        var r = LadderAnalyzer.Analyze(FullyMeasured.Take(2), LadderMode.Charge, "gr");

        Assert.Null(r.BestNode);
        Assert.Null(r.PrimaryNode);
        Assert.Null(r.PoiNode);
        Assert.Contains(r.Warnings, w => w.Contains("at least 3 steps"));
    }
}
