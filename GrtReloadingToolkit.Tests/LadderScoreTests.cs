using GrtReloadingToolkit.Log;
using GrtReloadingToolkit.Ocw;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>The score the ladder analyzer shows per charge is the Journal leaderboard's own score on the ladder's numbers.</summary>
public sealed class LadderScoreTests
{
    private static LadderStep Step(int velN, double sd, double es, int impacts, double groupMoa) => new()
    {
        X = 40.0, VelN = velN, MeanMps = 830, SdMps = sd, EsMps = es, Impacts = impacts, GroupEsMoa = groupMoa,
    };

    [Fact]
    public void ItIsTheLeaderboardsCompositeOnTheStepsOwnFigures()
    {
        var s = Step(5, 3.0, 8.0, 5, 0.6);
        var expected = LoadScoring.CompositeQualityScore(3.0, 8.0, 0.6, 5);
        Assert.Equal(expected.Total, LadderScore.Of(s).Total);
        Assert.True(LadderScore.Of(s).Total > 0);
    }

    [Fact]
    public void ATightStepOutscoresALooseOne()
    {
        var tight = LadderScore.Of(Step(5, 2.0, 5.0, 5, 0.4)).Total!.Value;
        var loose = LadderScore.Of(Step(5, 9.0, 25.0, 5, 2.5)).Total!.Value;
        Assert.True(tight > loose, $"{tight} should beat {loose}");
    }

    [Fact]
    public void AStepWithNothingMeasuredHasNoScoreAndCannotBeChosen()
        => Assert.Null(LadderScore.Of(Step(0, 0, 0, 0, 0)).Total);

    [Fact]
    public void VelocitiesAloneAreEnoughToScoreAStep()
        => Assert.NotNull(LadderScore.Of(Step(5, 3.0, 8.0, 0, 0)).Total);
}
