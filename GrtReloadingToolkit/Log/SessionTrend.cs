namespace GrtReloadingToolkit.Log;

/// <summary>
/// Ported from Ballistic Lab v2's <c>core/session_trend.py</c>: the SD/ES/velocity slope across
/// REPEATED sessions of the SAME charge over time — a rising SD/ES session over session is the
/// classic signature of a degrading component (powder, primers, barrel). Unifies two functions the
/// old (pre-Python-rewrite) software had for the same metric with INCONSISTENT thresholds
/// (1.0 fps/session in one, 0.5 warn/2.0 critical in the other) — this keeps only the more
/// informative two-level version, not a third variant.
///
/// Groups the same way <see cref="LeaderboardRanking"/> does (caliber+powder+bullet+charge is one
/// "load"), but orders each group by <see cref="JournalEntry.Id"/> — the database's own
/// auto-increment, assigned in creation order — as an explicit, ordered stand-in for chronological
/// order, rather than trusting whatever order the caller's list happens to arrive in (the old
/// software's <c>predictive_maintenance</c> never sorted by date at all, a real bug).
///
/// **Real bug this design guards against**: the old <c>predictive_maintenance</c> filtered out
/// sessions with a missing SD/ES/velocity, then zipped the shorter resulting list against
/// <c>dates[:len(list)]</c> — the FIRST N chronological slots, not the slots of the sessions that
/// actually contributed. With one gap mid-series the computed slope came out wrong (a real case: 2.4
/// instead of 1.7 fps/session, enough to flip "warn" into "critical"). Every (rank, value) pair here
/// is built inline, per entry, never by zipping two separately-filtered lists.
/// </summary>
public static class SessionTrend
{
    public sealed record ChargeTrend(string Caliber, long? PowderId, long? BulletId, double ChargeGr,
        IReadOnlyList<long> EntryIds, int NSessions,
        double? SdTrendSlopeMps, double? EsTrendSlopeMps, double? VelocityTrendSlopeMps,
        string SdSeverity, string EsSeverity, bool VelocityDeclining,
        double? VelocitySpreadMps, bool VelocityMismatch);

    // Thresholds are the old software's fps calibrations (0.5/2.0 SD, 2.0 ES, -3.0 decline),
    // re-expressed in this toolkit's native m/s by the fixed x0.3048 factor every other
    // fps-to-m/s threshold conversion in this project uses.
    private const double SdWarnMps = 0.5 * 0.3048;
    private const double SdCriticalMps = 2.0 * 0.3048;
    private const double EsWarnMps = 2.0 * 0.3048;
    private const double VelocityDeclineThresholdMps = -3.0 * 0.3048;
    private const double VelocityMismatchRelThreshold = 0.005;

    private static string SdSeverityOf(double? slope) =>
        slope is null || slope <= SdWarnMps ? "ok" : slope <= SdCriticalMps ? "warn" : "critical";

    private static string EsSeverityOf(double? slope) => slope is null || slope <= EsWarnMps ? "ok" : "warn";

    /// <summary>Slope from (rank, value) pairs, dropping missing values POINT BY POINT so a gap
    /// mid-series never desyncs rank from value (see the class doc's bug writeup).</summary>
    private static double? PairedSlope(IReadOnlyList<(int Rank, double? Value)> valuesByRank)
    {
        var pairs = valuesByRank.Where(p => p.Value.HasValue).ToList();
        if (pairs.Count < 2) return null;
        return AdvancedDiagnostics.LinearSlope(pairs.Select(p => (double)p.Rank).ToList(), pairs.Select(p => p.Value!.Value).ToList());
    }

    /// <summary>SD/ES/velocity trend for every caliber+powder+bullet+charge tested in more than one
    /// session — a charge tested once has no trend and is excluded.</summary>
    public static List<ChargeTrend> AnalyzeChargeTrends(IEnumerable<JournalEntry> journal)
    {
        var groups = journal.GroupBy(e => (Caliber: e.Caliber.ToUpperInvariant(), e.PowderId, e.BulletId, Charge: Math.Round(e.ChargeGr, 2)));

        var results = new List<ChargeTrend>();
        foreach (var g in groups)
        {
            var ordered = g.OrderBy(e => e.Id).ToList();
            if (ordered.Count < 2) continue;

            double? sdSlope = PairedSlope(ordered.Select((e, i) => (i, e.SdMs)).ToList());
            double? esSlope = PairedSlope(ordered.Select((e, i) => (i, e.EsMs)).ToList());
            double? mvSlope = PairedSlope(ordered.Select((e, i) => (i, e.VelocityAvgMs)).ToList());

            var mvs = ordered.Where(e => e.VelocityAvgMs.HasValue).Select(e => e.VelocityAvgMs!.Value).ToList();
            double? spread = mvs.Count >= 2 ? Math.Round(mvs.Max() - mvs.Min(), 1) : null;
            bool mismatch = false;
            if (spread is { } sp)
            {
                double meanMv = mvs.Average();
                if (meanMv > 0 && sp / meanMv > VelocityMismatchRelThreshold) mismatch = true;
            }

            results.Add(new ChargeTrend(ordered[0].Caliber, g.Key.PowderId, g.Key.BulletId, g.Key.Charge,
                ordered.Select(e => e.Id).ToList(), ordered.Count,
                sdSlope is { } sd ? Math.Round(sd, 5) : null,
                esSlope is { } es ? Math.Round(es, 5) : null,
                mvSlope is { } mv ? Math.Round(mv, 5) : null,
                SdSeverityOf(sdSlope), EsSeverityOf(esSlope),
                VelocityDeclining: mvSlope is { } s && s < VelocityDeclineThresholdMps,
                spread, mismatch));
        }
        return results;
    }
}
