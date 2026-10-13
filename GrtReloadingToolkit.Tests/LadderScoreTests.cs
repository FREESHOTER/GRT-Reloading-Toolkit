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
    public void ItIsTheLeaderboardsScoreOfASingleSessionOnTheStepsOwnFigures()
    {
        var s = Step(5, 3.0, 8.0, 5, 0.6);
        var expected = LoadScoring.LeaderboardScore(3.0, 8.0, 0.6, 5, Array.Empty<double>());
        Assert.Equal(expected.Total, LadderScore.Of(s).Total);
        Assert.True(LadderScore.Of(s).Total > 0);
    }

    /// <summary>
    /// The check that found the difference: the same SD, ES, group and rounds, once as a ladder step and once as a journal entry through
    /// the leaderboard itself, must give the same total. They did not while the step was scored over four sub-scores and the leaderboard
    /// over five (it adds the neutral consistency of a single session).
    /// </summary>
    [Theory]
    [InlineData(5, 3.0, 8.0, 0.6)]
    [InlineData(10, 6.5, 20.0, 1.4)]
    [InlineData(4, 1.9, 4.6, 0.3)]
    public void TheSameFiguresScoreTheSameInTheLeaderboardAndInTheLadder(int rounds, double sd, double es, double groupMoa)
    {
        var entry = new JournalEntry { Id = 1, LoadName = "x", Caliber = ".223 Rem", PowderId = 1, BulletId = 2, ChargeGr = 40.0, Rounds = rounds, SdMs = sd, EsMs = es, GroupMoa = groupMoa };
        var row = Assert.Single(LeaderboardRanking.Rank(new[] { entry }, new Dictionary<long, string> { [1] = "P", [2] = "B" }, null, "-"));
        var step = Step(rounds, sd, es, rounds, groupMoa);
        Assert.Equal(row.Score.Total, LadderScore.Of(step).Total);
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
