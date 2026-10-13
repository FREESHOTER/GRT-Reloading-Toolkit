using GrtReloadingToolkit.Ui;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>The recommended path ("The recommended path" in the manual) as the "next step" bars know it.</summary>
public sealed class ToolPathTests
{
    [Fact]
    public void BothImportsLeadToStatisticsThenTheLadderAnalyzerThenCalibration()
    {
        Assert.Equal("ChronoStats", ToolPathMap.After("Athlon"));
        Assert.Equal("ChronoStats", ToolPathMap.After("ShotMarker"));
        Assert.Equal("Ocw", ToolPathMap.After("ChronoStats"));
        Assert.Equal("Cal", ToolPathMap.After("Ocw"));
        Assert.Equal("Journal", ToolPathMap.After("Cal"));
    }

    [Fact]
    public void ThePathNeverLoopsBack()
    {
        foreach (string start in ToolPathMap.Sources)
        {
            var seen = new HashSet<string> { start };
            for (string? t = ToolPathMap.After(start); t is not null; t = ToolPathMap.After(t))
                Assert.True(seen.Add(t), $"the path from {start} comes back to {t}");
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
        Assert.Null(ToolPathMap.After("Journal"));     // the Journal is also the last step, so it is not wired to a next one yet
        Assert.Null(ToolPathMap.After("Leaderboard")); // a tool for comparing loads, not a stop on the way
        Assert.Null(ToolPathMap.After("Seating"));    // its successor is a visit to the range, not a tool
        Assert.Null(ToolPathMap.After("Launcher"));
    }

    [Fact]
    public void EveryNextStepHasADisplayNameAndNotJustItsEnumName()
    {
        foreach (string src in ToolPathMap.Sources)
        {
            string next = ToolPathMap.After(src)!;
            Assert.NotEqual(next, ToolNames.EnglishName(next));
        }
    }
}
