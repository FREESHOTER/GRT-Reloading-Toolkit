using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// <see cref="LeaderboardRanking"/> is the grouping/scoring behind the Load Leaderboard's "Rank"
/// button and the placement its "Write leaderboard note to GRT load" reports. It lived inside the
/// WinForms form until the caliber case-split below was found, which made it untestable from this
/// net8.0 project -- the bug survived precisely because nothing here could see it.
/// </summary>
public sealed class LeaderboardRankingTests
{
    private const string None = "— none —";

    private static readonly Dictionary<long, string> Names =
        new() { [1] = "Vihtavuori N140", [2] = "Berger 140gr Hybrid" };

    private static JournalEntry Entry(string caliber = "6.5 Creedmoor", long? powder = 1, long? bullet = 2,
        double charge = 40.0, int rounds = 5, double? sd = 8, double? es = null, double? group = null) => new()
    {
        Caliber = caliber, PowderId = powder, BulletId = bullet, ChargeGr = charge, Rounds = rounds,
        SdMs = sd, EsMs = es, GroupMoa = group,
    };

    /// <summary>
    /// The regression. One load, its caliber typed with different capitalisation on two range days,
    /// is ONE load -- it was splitting into two half-populated rows, each averaging only its own
    /// sessions, and both counted in the "of {N} loads" denominator the GRT write-back note reports.
    /// </summary>
    [Fact]
    public void CalibersDifferingOnlyInCaseAreTheSameLoad()
    {
        var journal = new[]
        {
            Entry(caliber: ".308 Win", rounds: 5, sd: 6),
            Entry(caliber: ".308 win", rounds: 7, sd: 10),
        };

        var ranked = LeaderboardRanking.Rank(journal, Names, caliberFilter: null, None);

        var row = Assert.Single(ranked);
        Assert.Equal(2, row.Sessions);
        Assert.Equal(12, row.Rounds);
        Assert.Equal(8, row.Sd!.Value, 6);   // averaged across BOTH sessions, not one of them
    }

    /// <summary>Grouping upper-cases the key, but the row has to display the caliber the way the
    /// user actually typed it -- an all-caps ".308 WIN" in the grid would be a new defect.</summary>
    [Fact]
    public void TheDisplayedCaliberKeepsTheSpellingItWasLoggedWith()
    {
        var ranked = LeaderboardRanking.Rank(new[] { Entry(caliber: ".308 Win") }, Names, null, None);

        Assert.Equal(".308 Win", Assert.Single(ranked).Caliber);
    }

    /// <summary>The filter and the grouping have to agree; a filter that matched case-sensitively
    /// while the key did not would just move the split rather than fix it.</summary>
    [Fact]
    public void TheCaliberFilterMatchesCaseInsensitively()
    {
        var journal = new[] { Entry(caliber: ".308 Win"), Entry(caliber: "6.5 Creedmoor") };

        var ranked = LeaderboardRanking.Rank(journal, Names, caliberFilter: ".308 WIN", None);

        Assert.Equal(".308 Win", Assert.Single(ranked).Caliber);
    }

    /// <summary>A null filter is "every load at all" -- the form resolves its own "-- any --"
    /// dropdown sentinel to null, so this class never sees UI wording.</summary>
    [Fact]
    public void ANullFilterRanksEveryCaliber()
    {
        var journal = new[] { Entry(caliber: ".308 Win"), Entry(caliber: "6.5 Creedmoor") };

        Assert.Equal(2, LeaderboardRanking.Rank(journal, Names, caliberFilter: null, None).Count);
    }

    /// <summary>Different charges of the same components stay different loads -- the case fix must
    /// not have widened the key into merging things that genuinely differ.</summary>
    [Fact]
    public void DifferentChargesRemainSeparateLoads()
    {
        var journal = new[] { Entry(charge: 40.0), Entry(charge: 41.5) };

        Assert.Equal(2, LeaderboardRanking.Rank(journal, Names, null, None).Count);
    }

    /// <summary>An entry naming no powder/bullet shows the caller's "no component" label. This read
    /// "-- any --" -- the FILTER's sentinel, meaning "don't filter" -- which in a component cell
    /// says the opposite of what the row actually holds.</summary>
    [Fact]
    public void AnEntryWithNoComponentsShowsTheSuppliedNoneLabel()
    {
        var ranked = LeaderboardRanking.Rank(new[] { Entry(powder: null, bullet: null) }, Names, null, None);

        var row = Assert.Single(ranked);
        Assert.Equal(None, row.Powder);
        Assert.Equal(None, row.Bullet);
    }

    /// <summary>Named components resolve through the supplied lookup.</summary>
    [Fact]
    public void NamedComponentsResolveThroughTheLookup()
    {
        var row = Assert.Single(LeaderboardRanking.Rank(new[] { Entry() }, Names, null, None));

        Assert.Equal("Vihtavuori N140", row.Powder);
        Assert.Equal("Berger 140gr Hybrid", row.Bullet);
    }

    /// <summary>Scoreable loads rank best-first; a load logged with only a round count is listed
    /// after them rather than dropped, so the grid can grey it instead of hiding it.</summary>
    [Fact]
    public void UnscoreableLoadsAreKeptOnTheEnd()
    {
        var journal = new[]
        {
            Entry(charge: 40.0, sd: 20),           // scoreable, worse
            Entry(charge: 41.0, sd: null),          // nothing but a round count
            Entry(charge: 42.0, sd: 4),            // scoreable, better
        };

        var ranked = LeaderboardRanking.Rank(journal, Names, null, None);

        Assert.Equal(new[] { 42.0, 40.0, 41.0 }, ranked.Select(r => r.ChargeGr));
        Assert.Null(ranked[2].Score.Total);
    }
}
