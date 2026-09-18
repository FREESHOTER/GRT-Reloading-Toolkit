namespace GrtReloadingToolkit.Log;

/// <summary>
/// The grouping/scoring/ordering behind <see cref="Ui.LoadLeaderboardForm"/>'s "Rank" button and
/// its "Write leaderboard note to GRT load" placement, pulled out into its own testable Log/ class
/// -- same split as <see cref="WorkflowRanking"/> vs the form that calls it. Ranks a load across
/// ALL distances it was ever tested at (unlike <see cref="WorkflowRanking"/>, which scopes to one),
/// scored with <see cref="LoadScoring.LeaderboardScore"/> so the cross-session consistency term
/// applies: here a load's history is the point.
///
/// Takes the journal and the component-name lookup as plain arguments rather than a <see cref="Db"/>
/// so the ranking can be tested without a database, and takes the "no component" label as a string
/// so the UI keeps ownership of its own wording and translation.
/// </summary>
public static class LeaderboardRanking
{
    public sealed record Row(string Caliber, long? PowderId, string Powder, long? BulletId, string Bullet,
        double ChargeGr, int Sessions, int Rounds, double? Sd, double? Es, double? Group, LoadScoring.Breakdown Score);

    /// <summary>
    /// Every caliber+powder+bullet+charge group in <paramref name="journal"/>, scored and ordered
    /// best first, with unscoreable rows (logged, but no SD/ES/group yet) kept on the end rather
    /// than dropped -- the grid lists them greyed, and the caller counts only the scored ones.
    /// <paramref name="caliberFilter"/> is null for "every load at all"; the form resolves its own
    /// "-- any --" dropdown sentinel to null before calling, so no UI wording reaches here.
    /// </summary>
    public static List<Row> Rank(IEnumerable<JournalEntry> journal,
        IReadOnlyDictionary<long, string> componentNames, string? caliberFilter, string noneLabel)
    {
        // The caliber is MATCHED case-insensitively, so it has to GROUP that way too. Keyed on the
        // raw string, ".308 Win" and ".308 win" -- one load, typed twice over two range days --
        // became two half-populated rows, each averaging only its own sessions and both counted in
        // the "of {N} loads" the write-back note reports. Upper-cased in the key, displayed from
        // the first entry that used it, the same way Charge is rounded in the key.
        var groups = journal
            .Where(e => caliberFilter is null || string.Equals(e.Caliber, caliberFilter, StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => (Caliber: e.Caliber.ToUpperInvariant(), e.PowderId, e.BulletId, Charge: Math.Round(e.ChargeGr, 2)));

        var rows = new List<Row>();
        foreach (var g in groups)
        {
            var entries = g.ToList();
            double? avgSd = Avg(entries.Select(e => e.SdMs));
            double? avgEs = Avg(entries.Select(e => e.EsMs));
            double? avgGroup = Avg(entries.Select(e => e.GroupMoa));
            int totalRounds = entries.Sum(e => e.Rounds);
            var sdAcrossSessions = entries.Where(e => e.SdMs.HasValue).Select(e => e.SdMs!.Value).ToList();
            var score = LoadScoring.LeaderboardScore(avgSd, avgEs, avgGroup, totalRounds, sdAcrossSessions);

            rows.Add(new Row(entries[0].Caliber, g.Key.PowderId,
                g.Key.PowderId is { } pid && componentNames.TryGetValue(pid, out var pn) ? pn : noneLabel,
                g.Key.BulletId,
                g.Key.BulletId is { } bid && componentNames.TryGetValue(bid, out var bn) ? bn : noneLabel,
                g.Key.Charge, entries.Count, totalRounds, avgSd, avgEs, avgGroup, score));
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
