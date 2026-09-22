using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

public sealed class SessionTrendTests
{
    private static JournalEntry Entry(long id, string caliber = "6.5 Creedmoor", long? powder = 1, long? bullet = 2,
        double charge = 40.0, double? sd = null, double? es = null, double? mv = null) => new()
    {
        Id = id, Caliber = caliber, PowderId = powder, BulletId = bullet, ChargeGr = charge,
        SdMs = sd, EsMs = es, VelocityAvgMs = mv,
    };

    [Fact]
    public void ASingleSessionForAChargeHasNoTrendAndIsExcluded()
    {
        var journal = new[] { Entry(1, sd: 5) };

        Assert.Empty(SessionTrend.AnalyzeChargeTrends(journal));
    }

    [Fact]
    public void RisingSdAcrossSessionsIsFlaggedCritical()
    {
        // +3 fps/session in original terms -> well past the 2.0fps critical threshold once converted.
        var journal = new[]
        {
            Entry(1, sd: 2.0), Entry(2, sd: 4.0), Entry(3, sd: 6.0), Entry(4, sd: 8.0),
        };

        var trends = SessionTrend.AnalyzeChargeTrends(journal);

        var t = Assert.Single(trends);
        Assert.Equal("critical", t.SdSeverity);
        Assert.True(t.SdTrendSlopeMps > 0);
    }

    [Fact]
    public void FlatSdAcrossSessionsIsOk()
    {
        var journal = new[] { Entry(1, sd: 3.0), Entry(2, sd: 3.1), Entry(3, sd: 2.9) };

        var t = Assert.Single(SessionTrend.AnalyzeChargeTrends(journal));

        Assert.Equal("ok", t.SdSeverity);
    }

    /// <summary>The regression from the old software: a session with a missing SD in the MIDDLE of
    /// the series must not shift which rank pairs with which value. Entry 2 has no SD; the slope
    /// must be computed from entries 1, 3 and 4 at their OWN chronological ranks (0, 2, 3), not
    /// entries 1, 3, 4 re-numbered as if entry 2 never existed (0, 1, 2) -- those give different
    /// slopes for this data, so the test would pass under the bug and fail here only if the fix
    /// actually matters.</summary>
    [Fact]
    public void AGapMidSeriesDoesNotDesyncRankFromValue()
    {
        var journal = new[]
        {
            Entry(1, sd: 2.0),  // rank 0
            Entry(2, sd: null), // rank 1 -- missing, must be skipped, not silently reindexed away
            Entry(3, sd: 5.0),  // rank 2
            Entry(4, sd: 8.0),  // rank 3
        };

        var t = Assert.Single(SessionTrend.AnalyzeChargeTrends(journal));

        // Correct pairs: (0,2.0) (2,5.0) (3,8.0) -> slope = cov/var computed on THOSE ranks.
        double expected = AdvancedDiagnostics.LinearSlope(
            new List<double> { 0, 2, 3 }, new List<double> { 2.0, 5.0, 8.0 });
        Assert.Equal(expected, t.SdTrendSlopeMps!.Value, 4);
    }

    [Fact]
    public void EntriesAreOrderedByIdRegardlessOfInputOrder()
    {
        // Fed out of chronological order -- Id (creation order) must still win, not list position.
        var journal = new[] { Entry(3, sd: 8.0), Entry(1, sd: 2.0), Entry(2, sd: 5.0) };

        var t = Assert.Single(SessionTrend.AnalyzeChargeTrends(journal));

        Assert.Equal(new long[] { 1, 2, 3 }, t.EntryIds);
    }

    [Fact]
    public void ADecliningVelocityIsFlagged()
    {
        var journal = new[] { Entry(1, mv: 830), Entry(2, mv: 825), Entry(3, mv: 818), Entry(4, mv: 808) };

        var t = Assert.Single(SessionTrend.AnalyzeChargeTrends(journal));

        Assert.True(t.VelocityDeclining);
    }

    [Fact]
    public void AWideVelocitySpreadRelativeToTheMeanIsFlaggedAsAMismatch()
    {
        // Same nominal charge, but one session's chrono reads far off from the others -- a
        // labeling/measurement mismatch worth a human's attention, not a real component trend.
        var journal = new[] { Entry(1, mv: 800), Entry(2, mv: 801), Entry(3, mv: 850) };

        var t = Assert.Single(SessionTrend.AnalyzeChargeTrends(journal));

        Assert.True(t.VelocityMismatch);
    }

    [Fact]
    public void DifferentPowdersAtTheSameChargeStaySeparateGroups()
    {
        var journal = new[] { Entry(1, powder: 1, sd: 3), Entry(2, powder: 1, sd: 3.5), Entry(3, powder: 2, sd: 4), Entry(4, powder: 2, sd: 4.2) };

        Assert.Equal(2, SessionTrend.AnalyzeChargeTrends(journal).Count);
    }
}
