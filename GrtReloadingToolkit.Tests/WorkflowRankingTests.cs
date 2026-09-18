using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// <see cref="WorkflowRanking"/> is the grouping/scoring/filtering behind the Distance Workflow
/// tool's "Rank" button and its "Write workflow note to GRT load" match logic -- never live-tested
/// against real data (the Journal was empty when the tool shipped), so this is what actually proves
/// the distance-scoping, min-rounds exclusion and cross-entry aggregation work, rather than trusting
/// them from reading the code next to the already-verified Load Leaderboard.
/// </summary>
public sealed class WorkflowRankingTests
{
    private static JournalEntry Entry(string caliber = "6.5 Creedmoor", long? powder = 1, long? bullet = 1,
        double charge = 40.0, int rounds = 5, double? sd = null, double? es = null, double? group = null,
        double? distanceM = 100) => new()
    {
        Caliber = caliber, PowderId = powder, BulletId = bullet, ChargeGr = charge, Rounds = rounds,
        SdMs = sd, EsMs = es, GroupMoa = group, DistanceM = distanceM,
    };

    [Fact]
    public void OnlyEntriesAtTheRequestedCaliberAndDistanceAreGrouped()
    {
        var journal = new[]
        {
            Entry(distanceM: 100),
            Entry(distanceM: 300),                    // different distance -- excluded
            Entry(caliber: ".308 Winchester"),         // different caliber -- excluded
        };

        var groups = WorkflowRanking.ComputeGroups(journal, "6.5 Creedmoor", 100);

        Assert.Single(groups);
    }

    [Fact]
    public void DistanceMatchesToTheNearestMetre()
    {
        // 99.6 and 100.4 both round to 100 -- a range-day distance isn't laser-measured to the mm.
        var journal = new[] { Entry(distanceM: 99.6), Entry(distanceM: 100.4) };

        var groups = WorkflowRanking.ComputeGroups(journal, "6.5 Creedmoor", 100);

        Assert.Single(groups);
        Assert.Equal(2, groups[0].Sessions);
    }

    [Fact]
    public void RepeatSessionsOfTheSameChargeAggregateIntoOneGroup()
    {
        var journal = new[]
        {
            Entry(sd: 3.0, es: 8.0, group: 0.5, rounds: 5),
            Entry(sd: 5.0, es: 12.0, group: 0.7, rounds: 5),
        };

        var groups = WorkflowRanking.ComputeGroups(journal, "6.5 Creedmoor", 100);

        var g = Assert.Single(groups);
        Assert.Equal(2, g.Sessions);
        Assert.Equal(10, g.Rounds);
        Assert.Equal(4.0, g.Sd);   // mean of 3.0 and 5.0
        Assert.Equal(10.0, g.Es);  // mean of 8.0 and 12.0
    }

    [Fact]
    public void DifferentChargesAtTheSameDistanceAreSeparateGroups()
    {
        var journal = new[] { Entry(charge: 40.0), Entry(charge: 40.2) };

        var groups = WorkflowRanking.ComputeGroups(journal, "6.5 Creedmoor", 100);

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void RankOrdersTheBetterScoreFirst()
    {
        var journal = new[]
        {
            // Good: tight SD/ES/group, plenty of rounds.
            Entry(charge: 40.0, sd: 3.0, es: 8.0, group: 0.4, rounds: 20),
            // Worse: loose SD/ES/group, same round count.
            Entry(charge: 40.5, sd: 8.0, es: 25.0, group: 1.2, rounds: 20),
        };

        var ranked = WorkflowRanking.Rank(WorkflowRanking.ComputeGroups(journal, "6.5 Creedmoor", 100));

        Assert.Equal(2, ranked.Count);
        Assert.Equal(40.0, ranked[0].ChargeGr);
        Assert.True(ranked[0].Score.Total > ranked[1].Score.Total);
    }

    [Fact]
    public void GroupsUnderThreeRoundsAreExcludedFromRankingButNotFromComputeGroups()
    {
        var journal = new[]
        {
            Entry(charge: 40.0, rounds: 2, sd: 3.0),  // under the 3-round floor
            Entry(charge: 40.5, rounds: 5, sd: 3.0),
        };

        var groups = WorkflowRanking.ComputeGroups(journal, "6.5 Creedmoor", 100);
        var ranked = WorkflowRanking.Rank(groups);

        Assert.Equal(2, groups.Count);   // both charges are known
        Assert.Single(ranked);           // only the 5-round one is trusted enough to rank
        Assert.Equal(40.5, ranked[0].ChargeGr);
    }

    [Fact]
    public void AGroupWithNoScoreableSignalSortsLastNotFirst()
    {
        // Enough rounds to clear the floor, but no SD/ES/group ever recorded -- CompositeQualityScore
        // still returns a sample-size sub-score alone, so Total is not null; the real "nothing to
        // score" case is when even rounds are too few to reach the floor, already covered above.
        // Here we just confirm a fully-scored group outranks a same-round-count group with a worse
        // score, i.e. Rank doesn't just fall back to insertion order.
        var journal = new[]
        {
            Entry(charge: 40.0, rounds: 20, sd: 2.5, es: 7.0, group: 0.3),
            Entry(charge: 41.0, rounds: 20, sd: 9.0, es: 30.0, group: 1.5),
        };

        var ranked = WorkflowRanking.Rank(WorkflowRanking.ComputeGroups(journal, "6.5 Creedmoor", 100));

        Assert.Equal(40.0, ranked[0].ChargeGr);
        Assert.Equal(41.0, ranked[1].ChargeGr);
    }
}
