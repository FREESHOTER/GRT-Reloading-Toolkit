using GrtReloadingToolkit.Ui;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>Which steps of the path the open load already shows traces of, for the launcher's "done" mark.</summary>
public sealed class PathProgressTests
{
    private static bool Done(string tool, string[] notes, params string[] measurements) => PathProgress.IsDone(tool, notes, measurements);

    [Theory]
    [InlineData("Brass", "Case Volume")]
    [InlineData("Brass", "Seating Depth (geometry)")]
    [InlineData("Label", "Load Sheet")]
    [InlineData("ChronoStats", "Chrono Statistics")]
    [InlineData("Ocw", "OCW Analysis 2026-10-13")]
    [InlineData("Cal", "Barrel Calibration")]
    [InlineData("Seating", "Seating Depth Analysis")]
    [InlineData("GroupAnalysis", "Group Analysis")]
    [InlineData("Workflow", "Distance Workflow")]
    [InlineData("SdRootCause", "SD Root-Cause")]
    [InlineData("Diagnostics", "Advanced Diagnostics")]
    public void AToolIsDoneWhenItsNoteIsInTheLoad(string tool, string note)
    {
        Assert.True(Done(tool, new[] { note }));
        Assert.False(Done(tool, new[] { "Something else" }));
    }

    [Fact]
    public void TheImportsAreDoneByTheirOwnMeasurementsOnly()
    {
        Assert.True(Done("Athlon", Array.Empty<string>(), "Athlon Ladder 2026-10-02"));
        Assert.False(Done("Athlon", Array.Empty<string>(), "ShotMarker — velocities"));
        Assert.True(Done("ShotMarker", Array.Empty<string>(), "ShotMarker — velocities"));
        Assert.False(Done("ShotMarker", Array.Empty<string>(), "Athlon Ladder 2026-10-02"));
    }

    [Fact]
    public void ARepeatedTitleWithGrtsSuffixStillCounts()
        => Assert.True(Done("Cal", new[] { "barrel calibration-1" }));

    [Theory]
    [InlineData("TargetPrint")]
    [InlineData("Journal")]
    [InlineData("Leaderboard")]
    [InlineData("Launcher")]
    public void AStepThatLeavesNoTraceInTheLoadHasNoMark(string tool)
        => Assert.False(Done(tool, new[] { "Case Volume", "OCW Analysis", "Group Analysis" }, "Athlon Ladder"));
}