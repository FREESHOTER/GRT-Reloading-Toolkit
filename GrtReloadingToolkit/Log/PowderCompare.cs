namespace GrtReloadingToolkit.Log;

/// <summary>
/// Groups journal entries by caliber+powder ACROSS every bullet and charge tried with it, for the
/// question "of the powders I've tried in this caliber, how do they stack up against each other" --
/// one level coarser than <see cref="LeaderboardRanking"/>, which keeps bullet+charge as part of the
/// group so it can tell one specific recipe from another. Same journal, same underlying score,
/// different question: "which recipe is best" vs. "which powder tends to work out for me here".
///
/// <see cref="Row.AvgBa"/>/<see cref="Row.AvgA0"/> average whatever calibrated values the journal
/// carries for that powder (see <see cref="JournalEntry.Ba"/>) -- not GRT's own factory value, which
/// this tool has no way to read for a powder that hasn't been calibrated at least once (see
/// reference_grt_plugin_ipc: the IPC has no "list every propellant" call, and the factory database is
/// a single opaque binary GRT ships, not a file this plugin can read on its own).
/// </summary>
public static class PowderCompare
{
    public sealed record Row(string Caliber, long? PowderId, string Powder,
        int Recipes, int Sessions, int Rounds, double? AvgBa, double? AvgA0,
        double? Sd, double? Es, double? Group, LoadScoring.Breakdown Score);

    /// <summary>
    /// Every caliber+powder in <paramref name="journal"/>, scored and ordered best first, with
    /// unscoreable rows (logged, but no SD/ES/group yet) kept on the end rather than dropped --
    /// same convention as <see cref="LeaderboardRanking.Rank"/>. <paramref name="caliberFilter"/> is
    /// null for "every load at all"; the form resolves its own "-- any --" dropdown sentinel to null
    /// before calling, so no UI wording reaches here.
    /// </summary>
    public static List<Row> Compare(IEnumerable<JournalEntry> journal,
        IReadOnlyDictionary<long, string> componentNames, string? caliberFilter, string noneLabel)
    {
        // Caliber matched case-insensitively, grouped the same way -- see LeaderboardRanking's own
        // note on why (".308 Win" / ".308 win" logged as two calibers becomes two half-populated rows).
        var groups = journal
            .Where(e => caliberFilter is null || string.Equals(e.Caliber, caliberFilter, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => (Caliber: e.Caliber.ToUpperInvariant(), e.PowderId));

        var rows = new List<Row>();
        foreach (var g in groups)
        {
            var entries = g.ToList();
            double? avgSd = Avg(entries.Select(e => e.SdMs));
            double? avgEs = Avg(entries.Select(e => e.EsMs));
            double? avgGroup = Avg(entries.Select(e => e.GroupMoa));
            double? avgBa = Avg(entries.Select(e => e.Ba));
            double? avgA0 = Avg(entries.Select(e => e.A0));
            int totalRounds = entries.Sum(e => e.Rounds);
            int recipes = entries.Select(e => Math.Round(e.ChargeGr, 2)).Distinct().Count();
            var sdAcrossSessions = entries.Where(e => e.SdMs.HasValue).Select(e => e.SdMs!.Value).ToList();
            var score = LoadScoring.LeaderboardScore(avgSd, avgEs, avgGroup, totalRounds, sdAcrossSessions);

            rows.Add(new Row(entries[0].Caliber, g.Key.PowderId,
                g.Key.PowderId is { } pid && componentNames.TryGetValue(pid, out var pn) ? pn : noneLabel,
                recipes, entries.Count, totalRounds, avgBa, avgA0, avgSd, avgEs, avgGroup, score));
        }

        return rows.Where(r => r.Score.Total.HasValue).OrderByDescending(r => r.Score.Total)
            .Concat(rows.Where(r => !r.Score.Total.HasValue))
            .ToList();

        static double? Avg(IEnumerable<double?> values)
        {
            var v = values.Where(x => x.HasValue).Select(x => x!.Value).ToList();
            return v.Count == 0 ? null : v.Average();
        }
    }
}
