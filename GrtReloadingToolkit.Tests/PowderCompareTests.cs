using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// <see cref="PowderCompare"/> is one grouping level coarser than <see cref="LeaderboardRanking"/>:
/// it merges every bullet/charge tried with a powder into one row, answering "which powder tends to
/// work out for me in this caliber" rather than "which exact recipe is best". Same journal data,
/// same scoring, different question -- the tests below focus on what the coarser grouping changes
/// (recipes collapse, Ba/A0 average in) rather than repeating LeaderboardRankingTests' own coverage
/// of caliber-case folding, which PowderCompare inherits unchanged from the same grouping idiom.
/// </summary>
public sealed class PowderCompareTests
{
    private const string None = "— none —";

    private static readonly Dictionary<long, string> Names =
        new() { [1] = "Vihtavuori N140", [2] = "Hodgdon H4350" };

    private static JournalEntry Entry(string caliber = "6.5 Creedmoor", long? powder = 1, long? bullet = 2,
        double charge = 40.0, int rounds = 5, double? sd = 8, double? es = null, double? group = null,
        double? ba = null, double? a0 = null) => new()
    {
        Caliber = caliber, PowderId = powder, BulletId = bullet, ChargeGr = charge, Rounds = rounds,
        SdMs = sd, EsMs = es, GroupMoa = group, Ba = ba, A0 = a0,
    };

    /// <summary>The whole point of the coarser grouping: two different charges (and even two
    /// different bullets) of the same powder are ONE row here, unlike in LeaderboardRanking where
    /// they'd be two recipes.</summary>
    [Fact]
    public void DifferentChargesOfTheSamePowderCollapseIntoOneRow()
    {
        var journal = new[]
        {
            Entry(powder: 1, charge: 40.0, rounds: 5, sd: 6),
            Entry(powder: 1, charge: 41.5, rounds: 5, sd: 10),
        };

        var row = Assert.Single(PowderCompare.Compare(journal, Names, null, None));

        Assert.Equal(2, row.Recipes);
        Assert.Equal(2, row.Sessions);
        Assert.Equal(10, row.Rounds);
        Assert.Equal(8, row.Sd!.Value, 6); // averaged across both charges
    }

    /// <summary>The same recipe shot again on another range day is still one recipe, not two --
    /// Recipes counts distinct bullet+charge combinations, Sessions counts journal rows.</summary>
    [Fact]
    public void TheSameChargeLoggedTwiceIsOneRecipeButTwoSessions()
    {
        var journal = new[] { Entry(charge: 40.0), Entry(charge: 40.0) };

        var row = Assert.Single(PowderCompare.Compare(journal, Names, null, None));

        Assert.Equal(1, row.Recipes);
        Assert.Equal(2, row.Sessions);
    }

    /// <summary>The same charge under two different bullets is two recipes, not one. A recipe is
    /// caliber+powder+bullet+charge -- LeaderboardRanking's group key, and the definition all four
    /// manuals give the reader. Counting charge weights alone hid the bullet, so a powder worked up
    /// across two projectiles reported half the recipes it had actually been tried in.</summary>
    [Fact]
    public void TheSameChargeUnderTwoBulletsIsTwoRecipes()
    {
        var journal = new[]
        {
            Entry(powder: 1, bullet: 10, charge: 40.0),
            Entry(powder: 1, bullet: 20, charge: 40.0),
        };

        var row = Assert.Single(PowderCompare.Compare(journal, Names, null, None));

        Assert.Equal(2, row.Recipes);
        Assert.Equal(2, row.Sessions);
    }

    /// <summary>Bullet and charge together make the recipe, so varying both counts every combination
    /// -- two bullets at two charges is four recipes, not two.</summary>
    [Fact]
    public void BulletAndChargeCountAsAPair()
    {
        var journal = new[]
        {
            Entry(bullet: 10, charge: 40.0), Entry(bullet: 10, charge: 41.5),
            Entry(bullet: 20, charge: 40.0), Entry(bullet: 20, charge: 41.5),
        };

        var row = Assert.Single(PowderCompare.Compare(journal, Names, null, None));

        Assert.Equal(4, row.Recipes);
    }

    /// <summary>A bullet the journal never recorded is a bullet in its own right, not a match for
    /// every other unrecorded one merged together -- but it IS one recipe with itself, so an
    /// unbulleted journal still counts by charge exactly as it did before.</summary>
    [Fact]
    public void EntriesWithNoBulletStillCountByCharge()
    {
        var journal = new[]
        {
            Entry(bullet: null, charge: 40.0), Entry(bullet: null, charge: 40.0),
            Entry(bullet: null, charge: 41.5),
        };

        var row = Assert.Single(PowderCompare.Compare(journal, Names, null, None));

        Assert.Equal(2, row.Recipes);
        Assert.Equal(3, row.Sessions);
    }

    /// <summary>Recipes counts within its own caliber+powder row, never across rows. The two powders
    /// here were worked up differently on purpose -- one across two charges, one at a single charge --
    /// so a count pooled over the whole journal would report three on both rows instead of two and one.
    /// </summary>
    [Fact]
    public void RecipesAreCountedPerRowNotAcrossThem()
    {
        var journal = new[]
        {
            Entry(powder: 1, bullet: 10, charge: 40.0),
            Entry(powder: 1, bullet: 10, charge: 41.5),
            Entry(powder: 2, bullet: 10, charge: 42.0),
        };

        var rows = PowderCompare.Compare(journal, Names, null, None);

        Assert.Equal(2, Assert.Single(rows, r => r.PowderId == 1).Recipes);
        Assert.Equal(1, Assert.Single(rows, r => r.PowderId == 2).Recipes);
    }

    /// <summary>Different powders in the same caliber stay separate rows -- the whole reason to run
    /// this tool instead of just reading the Leaderboard is to compare powders against each other.</summary>
    [Fact]
    public void DifferentPowdersStaySeparateRows()
    {
        var journal = new[] { Entry(powder: 1), Entry(powder: 2) };

        Assert.Equal(2, PowderCompare.Compare(journal, Names, null, None).Count);
    }

    /// <summary>Ba/A0 average across whatever sessions carry a calibrated value -- a session that
    /// was never calibrated (Ba null) neither drags the average down nor blocks it.</summary>
    [Fact]
    public void BaAndA0AverageOnlyOverCalibratedSessions()
    {
        var journal = new[]
        {
            Entry(charge: 40.0, ba: 0.48, a0: 1.35),
            Entry(charge: 41.0, ba: 0.50, a0: 1.40),
            Entry(charge: 42.0, ba: null, a0: null), // never calibrated -- must not count as 0
        };

        var row = Assert.Single(PowderCompare.Compare(journal, Names, null, None));

        Assert.Equal(0.49, row.AvgBa!.Value, 6);
        Assert.Equal(1.375, row.AvgA0!.Value, 6);
    }

    /// <summary>No calibrated session at all leaves the averages null, not zero -- zero would read
    /// as a real (and nonsensical) burning-rate factor.</summary>
    [Fact]
    public void NoCalibratedSessionLeavesTheAveragesNull()
    {
        var row = Assert.Single(PowderCompare.Compare(new[] { Entry(ba: null, a0: null) }, Names, null, None));

        Assert.Null(row.AvgBa);
        Assert.Null(row.AvgA0);
    }

    /// <summary>Caliber filtering and case-insensitive grouping follow the same convention as
    /// LeaderboardRanking (shared idiom, not re-derived) -- one smoke test to confirm this class
    /// actually applies it, not a full repeat of that class's own coverage.</summary>
    [Fact]
    public void TheCaliberFilterMatchesCaseInsensitivelyAndFoldsTheKey()
    {
        var journal = new[] { Entry(caliber: ".308 Win"), Entry(caliber: ".308 win"), Entry(caliber: "6.5 Creedmoor") };

        var ranked = PowderCompare.Compare(journal, Names, caliberFilter: ".308 WIN", None);

        var row = Assert.Single(ranked);
        Assert.Equal(".308 Win", row.Caliber);
        Assert.Equal(2, row.Sessions);
    }

    /// <summary>Unscoreable rows (only a round count, no SD/ES/group) are kept, not dropped, and
    /// sorted after every scoreable row -- same convention as LeaderboardRanking.</summary>
    [Fact]
    public void UnscoreableRowsAreKeptAfterScoreableOnes()
    {
        var journal = new[] { Entry(powder: 1, sd: null, es: null, group: null), Entry(powder: 2, sd: 6) };

        var ranked = PowderCompare.Compare(journal, Names, null, None);

        Assert.Equal(2, ranked.Count);
        Assert.True(ranked[0].Score.Total.HasValue);
        Assert.False(ranked[1].Score.Total.HasValue);
    }

    /// <summary>An entry naming no powder shows the caller's "no component" label, same convention
    /// as LeaderboardRanking.</summary>
    [Fact]
    public void AnEntryWithNoPowderShowsTheSuppliedNoneLabel()
    {
        var row = Assert.Single(PowderCompare.Compare(new[] { Entry(powder: null) }, Names, null, None));

        Assert.Equal(None, row.Powder);
    }
}
