using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// Every threshold here traces back to a specific real-world reference (Bryan Litz/Applied
/// Ballistics, or a published military sniper-ammunition spec), not an arbitrary step -- these
/// tests check each anchor point lands on the score it was picked for, so a future edit can't
/// silently drift the curve away from what it's supposed to represent.
/// </summary>
public class LoadScoringTests
{
    // These test the DEFAULT tiers specifically -- the whole point of LoadScoringConfig is that a
    // user can edit them, so every test resets LoadScoring.Config first rather than trusting
    // whatever a previous test (or a real load-scoring.json on the machine running this) left it as.
    public LoadScoringTests() => LoadScoring.Config = LoadScoringConfig.Default;

    // ---- SD: 10/15/18/20/28 fps anchors (Litz handloader goal / Mk 316 / Litz "poor factory" / M118LR) ----

    [Theory]
    [InlineData(3.0, 10.0)]   // <= 10 fps handloader goal
    [InlineData(3.05, 10.0)]  // exactly 10 fps (3.048 m/s), full marks
    [InlineData(4.57, 7.5)]   // 15 fps, Mk 316 Mod 0 ceiling
    [InlineData(6.10, 4.5)]   // 20 fps, Litz "poor, mass-produced factory"
    [InlineData(8.53, 1.5)]   // 28 fps, M118LR spec ceiling
    [InlineData(9.5, 0.5)]    // worse than the oldest cited military spec
    public void SdAnchorsLandOnTheirIntendedScore(double sdMps, double expected) =>
        Assert.Equal(expected, LoadScoring.ScoreSd(sdMps));

    [Fact]
    public void SdScoreNeverIncreasesAsSdWorsens()
    {
        double[] sds = { 1.0, 3.05, 4.57, 5.49, 6.10, 7.32, 8.53, 12.0 };
        for (int i = 1; i < sds.Length; i++)
            Assert.True(LoadScoring.ScoreSd(sds[i]) <= LoadScoring.ScoreSd(sds[i - 1]));
    }

    // ---- ES: 10/26/60 fps anchors (exceptional string / cited excellent example / Mk 316 ceiling) ----

    [Theory]
    [InlineData(3.05, 10.0)]  // ~10 fps, the cited exceptional 10-shot string
    [InlineData(8.0, 7.5)]    // 26 fps, the cited "excellent" ES/SD example
    [InlineData(18.3, 4.5)]   // 60 fps, Mk 316 Mod 0's hard ES ceiling
    [InlineData(30.0, 0.5)]   // well beyond the military ceiling
    public void EsAnchorsLandOnTheirIntendedScore(double esMps, double expected) =>
        Assert.Equal(expected, LoadScoring.ScoreEs(esMps));

    [Fact]
    public void EsScoreNeverIncreasesAsEsWorsens()
    {
        double[] esValues = { 1.0, 3.05, 5.0, 8.0, 12.5, 18.3, 22.0, 27.4, 40.0 };
        for (int i = 1; i < esValues.Length; i++)
            Assert.True(LoadScoring.ScoreEs(esValues[i]) <= LoadScoring.ScoreEs(esValues[i - 1]));
    }

    // ---- Group MOA: 0.225/0.5/0.75/1.0 anchors (benchrest win / precision-rifle / excellent hunting / baseline) ----

    [Theory]
    [InlineData(0.225, 10.0)]  // winning benchrest group
    [InlineData(0.5, 8.0)]     // "precision rifle territory"
    [InlineData(0.75, 6.5)]    // excellent hunting rifle
    [InlineData(1.0, 5.0)]     // the common baseline "1 MOA rifle"
    [InlineData(3.0, 0.5)]     // well below baseline
    public void GroupMoaAnchorsLandOnTheirIntendedScore(double groupMoa, double expected) =>
        Assert.Equal(expected, LoadScoring.ScoreGroupMoa(groupMoa));

    [Fact]
    public void GroupScoreNeverIncreasesAsGroupWorsens()
    {
        double[] groups = { 0.1, 0.225, 0.35, 0.5, 0.75, 1.0, 1.5, 2.0, 4.0 };
        for (int i = 1; i < groups.Length; i++)
            Assert.True(LoadScoring.ScoreGroupMoa(groups[i]) <= LoadScoring.ScoreGroupMoa(groups[i - 1]));
    }

    // ---- Sample size / consistency: statistical confidence, not a performance benchmark ----

    [Theory]
    [InlineData(20, 10.0)]
    [InlineData(10, 8.0)]
    [InlineData(3, 4.0)]
    [InlineData(1, 2.0)]
    public void SampleSizeScoresAsExpected(int rounds, double expected) =>
        Assert.Equal(expected, LoadScoring.ScoreSampleSize(rounds));

    [Fact]
    public void ConsistencyIsNeutralWithOnlyOneSession() =>
        Assert.Equal(7.0, LoadScoring.ScoreConsistency(new List<double> { 3.0 }));

    [Fact]
    public void ConsistencyPenalizesWideSdSpreadAcrossSessions()
    {
        // Two sessions of the same load with SD 2.0 and 2.5 m/s: spread 0.5 <= 0.61 -> full marks.
        Assert.Equal(10.0, LoadScoring.ScoreConsistency(new List<double> { 2.0, 2.5 }));
        // Spread of 5.0 m/s is well beyond the widest tier -> worst score.
        Assert.Equal(2.0, LoadScoring.ScoreConsistency(new List<double> { 2.0, 7.0 }));
    }

    // ---- Composite scores: simple average of whichever sub-scores are available ----

    [Fact]
    public void CompositeScoreAveragesOnlyAvailableSubScores()
    {
        // SD=10.0 (<=10fps), ES null (not measured), Group=8.0 (0.5 MOA), sample size (20 rounds)=10.0.
        // Average of {10.0, 8.0, 10.0} = 9.33 (rounded to 2 places), ES excluded entirely.
        var b = LoadScoring.CompositeQualityScore(sdMps: 3.0, esMps: null, groupMoa: 0.5, rounds: 20);
        Assert.Null(b.Es);
        Assert.Equal(9.33, b.Total);
    }

    [Fact]
    public void CompositeScoreIsNullWithNoData()
    {
        var b = new LoadScoring.Breakdown(null, null, null, null, null);
        Assert.Null(b.Total);
    }

    [Fact]
    public void UnmeasuredLoadIsNotRankedAtAll()
    {
        // 20 rounds logged, nothing chronographed and no target measured: sample size (10.0) and
        // the neutral single-session consistency (7.0) would average to 8.5 -- "Very good" -- and
        // outrank a real load that merely shot badly. Nothing measured means nothing to rank.
        var unmeasured = LoadScoring.LeaderboardScore(null, null, null, rounds: 20, new List<double>());
        Assert.False(unmeasured.Rankable);
        Assert.Null(unmeasured.Total);

        // A genuinely measured but mediocre load still scores, and so ranks above the above.
        var measured = LoadScoring.LeaderboardScore(6.0, 20.0, 1.5, rounds: 20, new List<double>());
        Assert.True(measured.Rankable);
        Assert.NotNull(measured.Total);
    }

    [Fact]
    public void OneMeasuredSignalIsEnoughToRank()
    {
        var groupOnly = LoadScoring.LeaderboardScore(null, null, 0.5, rounds: 0, new List<double>());
        Assert.True(groupOnly.Rankable);
        // Group 8.0 + neutral consistency 7.0, with no round count recorded to fold in.
        Assert.Null(groupOnly.SampleSize);
        Assert.Equal(7.5, groupOnly.Total);
    }

    [Fact]
    public void BlankRoundCountIsNotScoredAsAOneShotString()
    {
        // Rounds is a plain int that starts at 0, so 0 means "not filled in". Scoring it as
        // ScoreSampleSize(0) = 2.0 would quietly cost a load ~1.5 points for a blank field.
        var blank = LoadScoring.CompositeQualityScore(3.0, null, null, rounds: 0);
        Assert.Null(blank.SampleSize);
        Assert.Equal(10.0, blank.Total);

        // A real one-round string is a different thing and still scores 2.0.
        var oneShot = LoadScoring.CompositeQualityScore(3.0, null, null, rounds: 1);
        Assert.Equal(2.0, oneShot.SampleSize);
        Assert.Equal(6.0, oneShot.Total);
    }

    [Fact]
    public void LeaderboardScoreIncludesConsistencyUnlikeSingleEntryScore()
    {
        var single = LoadScoring.CompositeQualityScore(3.0, 8.0, 0.5, 20);
        var leaderboard = LoadScoring.LeaderboardScore(3.0, 8.0, 0.5, 20, new List<double> { 2.0, 2.1 });
        Assert.Null(single.Consistency);
        Assert.Equal(10.0, leaderboard.Consistency);
        // Same inputs otherwise, but the leaderboard total differs because it has one more
        // (perfect, 10.0) sub-score folded into the average.
        Assert.NotEqual(single.Total, leaderboard.Total);
    }

    [Theory]
    [InlineData(9.5, "Excellent")]
    [InlineData(8.0, "Very good")]
    [InlineData(6.5, "Good")]
    [InlineData(5.0, "Fair")]
    [InlineData(2.0, "Needs work")]
    [InlineData(null, "N/A")]
    public void ScoreLabelMatchesTier(double? score, string expected) =>
        Assert.Equal(expected, LoadScoring.ScoreLabel(score));
}
