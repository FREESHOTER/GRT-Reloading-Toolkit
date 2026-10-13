using GrtReloadingToolkit.Ui;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>Which phase of the recommended path an open load is in, from what is written in it.</summary>
public sealed class PathStateTests
{
    private static PathPhase Phase(bool measurement, params string[] notes) => PathState.From(notes, measurement).Phase;

    [Fact]
    public void AnEmptyLoadIsAtThePreparation()
        => Assert.Equal(PathPhase.Preparation, Phase(false));

    [Fact]
    public void AMeasurementOrALadderNoteMeansTheChargeLadderIsUnderWay()
    {
        Assert.Equal(PathPhase.ChargeLadder, Phase(true));
        Assert.Equal(PathPhase.ChargeLadder, Phase(false, "OCW Analysis 2026-10-13"));
        Assert.Equal(PathPhase.ChargeLadder, Phase(false, "Chrono Statistics"));
        Assert.Equal(PathPhase.ChargeLadder, Phase(false, "Barrel Calibration"));
    }

    [Fact]
    public void AChosenChargeMovesTheLoadToTheSeatingDepth()
        => Assert.Equal(PathPhase.SeatingDepth, Phase(true, "OCW Analysis", "Chosen Charge"));

    [Fact]
    public void ASeatingAnalysisMovesItToTheCheckAtDistance()
        => Assert.Equal(PathPhase.DistanceCheck, Phase(true, "Chosen Charge", "Seating Depth Analysis"));

    [Fact]
    public void AGroupAnalysisOrADistanceWorkflowMovesItToTheTracking()
    {
        Assert.Equal(PathPhase.Tracking, Phase(true, "Chosen Charge", "Seating Depth Analysis", "Group Analysis"));
        Assert.Equal(PathPhase.Tracking, Phase(true, "Distance Workflow"));
    }

    [Fact]
    public void TheLatestStageWinsWhateverTheOrderOfTheNotes()
        => Assert.Equal(PathPhase.DistanceCheck, Phase(true, "Seating Depth Analysis", "OCW Analysis", "Chosen Charge"));

    [Fact]
    public void TitlesAreMatchedByTheirStartAndWithoutCase()
        => Assert.Equal(PathPhase.SeatingDepth, Phase(false, "  chosen charge 2026-10-13"));
}