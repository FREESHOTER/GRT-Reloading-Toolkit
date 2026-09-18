namespace GrtReloadingToolkit.Log;

/// <summary>
/// The grouping/scoring/filtering <see cref="Ui.WorkflowForm"/> runs, pulled out into its own
/// testable Log/ class -- same split as <see cref="LoadScoring"/> vs the UI that calls it. Ranks
/// charges tested at ONE distance (unlike the Leaderboard's all-distances aggregate), scored with
/// <see cref="LoadScoring.CompositeQualityScore"/> (no cross-session consistency term -- a same-day
/// decision shouldn't be discounted for a load's history), with groups under
/// <see cref="MinRoundsForRanking"/> rounds excluded from the ranking entirely rather than scored
/// low, matching Ballistic Lab v2's own <c>min_shots</c> cutoff.
/// </summary>
public static class WorkflowRanking
{
    public const int MinRoundsForRanking = 3;

    public sealed record Row(long? PowderId, long? BulletId, double ChargeGr, int Sessions, int Rounds,
        double? Sd, double? Es, double? Group, LoadScoring.Breakdown Score);

    /// <summary>Every caliber+powder+bullet+charge group tested at <paramref name="distanceM"/>
    /// (nearest metre), scored but NOT yet filtered by <see cref="MinRoundsForRanking"/> -- kept
    /// separate from the filter so a caller can tell "never logged at this distance" apart from
    /// "logged, but not enough rounds yet".</summary>
    public static List<Row> ComputeGroups(IEnumerable<JournalEntry> journal, string caliber, double distanceM)
    {
        var groups = journal
            .Where(e => string.Equals(e.Caliber, caliber, StringComparison.OrdinalIgnoreCase)
                        && e.DistanceM.HasValue && Math.Round(e.DistanceM.Value) == Math.Round(distanceM))
            .GroupBy(e => (e.PowderId, e.BulletId, Charge: Math.Round(e.ChargeGr, 2)));

        var rows = new List<Row>();
        foreach (var g in groups)
        {
            var entries = g.ToList();
            double? avgSd = Avg(entries.Select(e => e.SdMs));
            double? avgEs = Avg(entries.Select(e => e.EsMs));
            double? avgGroup = Avg(entries.Select(e => e.GroupMoa));
            int totalRounds = entries.Sum(e => e.Rounds);
            var score = LoadScoring.CompositeQualityScore(avgSd, avgEs, avgGroup, totalRounds);

            rows.Add(new Row(g.Key.PowderId, g.Key.BulletId, g.Key.Charge, entries.Count, totalRounds,
                avgSd, avgEs, avgGroup, score));
        }
        return rows;

        static double? Avg(IEnumerable<double?> values)
        {
            var v = values.Where(x => x.HasValue).Select(x => x!.Value).ToList();
            return v.Count == 0 ? null : v.Average();
        }
    }

    /// <summary>The ranking a workflow decision actually uses: <see cref="ComputeGroups"/>'s output
    /// with anything under <see cref="MinRoundsForRanking"/> rounds dropped, best score first.</summary>
    public static List<Row> Rank(IEnumerable<Row> groups) =>
        groups.Where(r => r.Rounds >= MinRoundsForRanking)
            .OrderByDescending(r => r.Score.Total.HasValue).ThenByDescending(r => r.Score.Total)
            .ToList();
}
