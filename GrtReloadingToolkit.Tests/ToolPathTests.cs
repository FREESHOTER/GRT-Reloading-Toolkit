using GrtReloadingToolkit.Ui;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>The recommended path ("The recommended path" in the manual) as the "next step" bars know it.</summary>
public sealed class ToolPathTests
{
    private static PathState In(PathPhase p) => new(p);
    private static readonly PathPhase[] AllPhases = Enum.GetValues<PathPhase>();

    [Fact]
    public void WithoutAStateTheChargeLadderPathIsWhatTheBarsPointTo()
    {
        Assert.Equal("ChronoStats", ToolPathMap.After("Athlon"));
        Assert.Equal("ChronoStats", ToolPathMap.After("ShotMarker"));
        Assert.Equal("Ocw", ToolPathMap.After("ChronoStats"));
        Assert.Equal("Cal", ToolPathMap.After("Ocw"));
        Assert.Equal("Journal", ToolPathMap.After("Cal"));
    }

    [Fact]
    public void TheImportLeadsWhereTheLoadIs()
    {
        Assert.Equal("ChronoStats", ToolPathMap.After("Athlon", In(PathPhase.ChargeLadder)));
        Assert.Equal("Seating", ToolPathMap.After("Athlon", In(PathPhase.SeatingDepth)));
        Assert.Equal("GroupAnalysis", ToolPathMap.After("ShotMarker", In(PathPhase.DistanceCheck)));
        Assert.Equal("Seating", ToolPathMap.After("ChronoStats", In(PathPhase.SeatingDepth)));
        Assert.Equal("GroupAnalysis", ToolPathMap.After("ChronoStats", In(PathPhase.DistanceCheck)));
    }

    [Fact]
    public void ThePreparationLeadsToTheCardThenTheTargetThenTheRange()
    {
        var p = In(PathPhase.Preparation);
        Assert.Equal("Label", ToolPathMap.After("Brass", p));
        Assert.Equal("TargetPrint", ToolPathMap.After("Label", p));
        Assert.Equal("Athlon", ToolPathMap.After("TargetPrint", p));
        // a card made much later is the final card, not a stop on the way to the target
        Assert.Null(ToolPathMap.After("Brass", In(PathPhase.Tracking)));
        Assert.Null(ToolPathMap.After("Label", In(PathPhase.Tracking)));
    }

    [Fact]
    public void TheJournalLeadsToTheSeatingTargetOnceTheChargeIsChosenAndToTheCardAtTheEnd()
    {
        Assert.Equal("TargetPrint", ToolPathMap.After("Journal", In(PathPhase.SeatingDepth)));
        Assert.Equal("Label", ToolPathMap.After("Journal", In(PathPhase.Tracking)));
        Assert.Null(ToolPathMap.After("Journal", In(PathPhase.ChargeLadder)));   // no charge chosen yet: nothing to log
        Assert.Equal("Workflow", ToolPathMap.After("GroupAnalysis", In(PathPhase.DistanceCheck)));
        Assert.Equal("Journal", ToolPathMap.After("Workflow", In(PathPhase.DistanceCheck)));
    }

    [Fact]
    public void ThePathNeverLoopsBackWithinAPhase()
    {
        foreach (PathPhase phase in AllPhases)
            foreach (string start in ToolPathMap.Sources)
            {
                var seen = new HashSet<string> { start };
                for (string? t = ToolPathMap.After(start, In(phase)); t is not null; t = ToolPathMap.After(t, In(phase)))
                    Assert.True(seen.Add(t), $"in phase {phase} the path from {start} comes back to {t}");
            }
    }

    [Fact]
    public void TheStatisticsStepReminds_TheLadderAnalyzerNeedsTheHitCoordinatesToo()
    {
        Assert.Contains("hit coordinates", ToolPathMap.Hint("ChronoStats"));
        Assert.Contains("Journal", ToolPathMap.Hint("Cal"));          // log the chosen charge there
        Assert.Contains("Choose this charge", ToolPathMap.Hint("Ocw"));
        Assert.Null(ToolPathMap.Hint("Athlon"));
    }

    [Fact]
    public void AToolOutsideTheGuidedStretchHasNoNextStep()
    {
        foreach (PathPhase phase in AllPhases)
        {
            Assert.Null(ToolPathMap.After("Leaderboard", In(phase))); // a tool for comparing loads, not a stop on the way
            Assert.Null(ToolPathMap.After("Seating", In(phase)));     // its successor is a visit to the range, not a tool
            Assert.Null(ToolPathMap.After("Launcher", In(phase)));
        }
    }

    [Fact]
    public void EveryNextStepHasADisplayNameAndNotJustItsEnumName()
    {
        foreach (PathPhase phase in AllPhases)
            foreach (string src in ToolPathMap.Sources)
                if (ToolPathMap.After(src, In(phase)) is { } next)
                    Assert.NotEqual(next, ToolNames.EnglishName(next));
    }

    [Fact]
    public void EverySourceHasASuccessorInSomePhase()
    {
        foreach (string src in ToolPathMap.Sources)
            Assert.Contains(AllPhases, p => ToolPathMap.After(src, In(p)) is not null);
    }
}