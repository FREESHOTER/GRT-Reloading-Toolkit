namespace GrtReloadingToolkit.Log;

/// <summary>
/// Composite 0-10 quality score for a load, from the same numbers the Journal already records
/// (SD, ES, group, round count) plus the spread of SD across repeat sessions of the same load.
///
/// The thresholds below are NOT arbitrary step functions -- each anchor point is a real,
/// citable reference from competitive/military precision-rifle practice, not a guess:
///
///   SD (m/s):   10 fps (3.05 m/s) = the standard handloader goal (Bryan Litz, Applied
///               Ballistics); 15/18 fps (4.6/5.5 m/s) = the Mk 316 Mod 0 sniper-ammo
///               specification ceiling; 20 fps (6.1 m/s) = "poor, mass-produced factory"
///               per Litz; 28 fps (8.5 m/s) = the older M118LR sniper-ammo spec ceiling.
///   ES (m/s):   26 fps (7.9 m/s) = a cited "excellent" 10-shot-string example; 60 fps
///               (18.3 m/s) = the Mk 316 Mod 0 spec's hard ES ceiling.
///   Group (MOA): 0.225 MOA = a winning benchrest-competition group; 0.5 MOA = "precision
///               rifle territory"; 0.75 MOA = excellent for a hunting rifle; 1.0 MOA = the
///               common baseline "acceptable rifle" bar.
///
/// Two things Ballistic Lab v2's own scoring.py had that are deliberately NOT ported here:
///   - CV% (coefficient of variation): no independent citable benchmark for it turned up in
///     research, and it's mathematically close to SD/velocity anyway -- keeping it would have
///     meant inventing a threshold table with no basis, so it's dropped rather than duplicate
///     the SD signal under a fabricated scale.
///   - OBT-node proximity: the Toolkit has no OBT of its own (that's GRT Model OBT, a separate
///     plugin) -- including it here would need a second, unrelated tool's data.
/// Also unlike Ballistic Lab's dual mm-based group metric (extreme spread for &lt;10 shots,
/// mean radius for &gt;=10 shots, since it has per-shot coordinates), the Journal only stores one
/// summary <c>GroupMoa</c> figure -- so there is one MOA-native curve, not two.
///
/// The actual tier tables live in <see cref="LoadScoringConfig"/>, not here -- a user's own
/// discipline may reasonably want a stricter or looser scale than these general-purpose defaults,
/// so the numbers are editable (see the Leaderboard's own "Customize thresholds" dialog) and
/// persisted to disk rather than compiled in. <see cref="Config"/> is the live table every score in
/// this process reads; swap it (or edit it in place and re-<see cref="LoadScoringConfig.Save"/>) to
/// change how every future score comes out.
/// </summary>
public static class LoadScoring
{
    /// <summary>The tier tables every score below reads. Loaded once per process; the Leaderboard's
    /// settings dialog re-<see cref="LoadScoringConfig.Load"/>s it after a save so an edit takes
    /// effect on the next Rank without a restart.</summary>
    public static LoadScoringConfig Config { get; set; } = LoadScoringConfig.Load();

    /// <summary>SD (m/s) -&gt; 0-10 via <see cref="Config"/>'s editable tiers. Defaults anchored to
    /// 10/15/18/20/28 fps (Litz handloader goal, Mk 316, Litz "poor factory", M118LR ceiling).</summary>
    public static double ScoreSd(double sdMps) => LoadScoringConfig.Score(Config.SdTiers, sdMps);

    /// <summary>ES (m/s) -&gt; 0-10 via <see cref="Config"/>'s editable tiers. Defaults anchored to a
    /// cited excellent 10-shot string (~10 fps), a cited excellent example (26 fps), and the
    /// Mk 316 Mod 0 spec's hard ceiling (60 fps).</summary>
    public static double ScoreEs(double esMps) => LoadScoringConfig.Score(Config.EsTiers, esMps);

    /// <summary>Group size (MOA, extreme spread -- the same convention "Group MOA" already uses
    /// everywhere else in the Toolkit) -&gt; 0-10 via <see cref="Config"/>'s editable tiers. Defaults
    /// anchored to winning benchrest (0.225), "precision rifle territory" (0.5), excellent hunting
    /// rifle (0.75), the common baseline (1.0).</summary>
    public static double ScoreGroupMoa(double groupMoa) => LoadScoringConfig.Score(Config.GroupMoaTiers, groupMoa);

    /// <summary>Round count -&gt; 0-10, penalizing small samples. Not a performance benchmark
    /// (there's no "military spec" for sample size) -- kept as Ballistic Lab v2 had it, since
    /// it's about statistical confidence, not ballistic quality.
    ///
    /// Callers pass this only for a count they actually have: the journal's Rounds column is a
    /// plain int that starts at 0, so 0 means "not filled in", not "a zero-shot string", and
    /// scoring it 2.0 would cost a load ~1.5 points for a blank field.</summary>
    public static double ScoreSampleSize(int rounds)
    {
        if (rounds >= 20) return 10.0;
        if (rounds >= 15) return 9.0;
        if (rounds >= 10) return 8.0;
        if (rounds >= 7) return 7.0;
        if (rounds >= 5) return 5.5;
        if (rounds >= 3) return 4.0;
        return 2.0;
    }

    /// <summary>Spread of SD (m/s) across repeat sessions of the same load -&gt; 0-10. Same
    /// statistical-confidence reasoning as <see cref="ScoreSampleSize"/> -- a single session
    /// scores neutral (7.0), since there's nothing yet to be inconsistent with.</summary>
    public static double ScoreConsistency(IReadOnlyList<double> sdAcrossSessionsMps)
    {
        if (sdAcrossSessionsMps.Count < 2) return 7.0;
        double spread = sdAcrossSessionsMps.Max() - sdAcrossSessionsMps.Min();
        if (spread <= 0.61) return 10.0;
        if (spread <= 1.22) return 8.5;
        if (spread <= 2.13) return 7.0;
        if (spread <= 3.05) return 5.5;
        if (spread <= 4.57) return 4.0;
        return 2.0;
    }

    /// <summary>Textual label for a composite 0-10 score, in English -- this is a Log/ file, not a
    /// Ui/ one, so it stays language-agnostic like <see cref="BaBackfill.Result"/>'s reasons; wrap
    /// the call site in <c>Lang.T(...)</c>, don't translate here.</summary>
    public static string ScoreLabel(double? score) => score switch
    {
        null => "N/A",
        >= 9.0 => "Excellent",
        >= 7.5 => "Very good",
        >= 6.0 => "Good",
        >= 4.0 => "Fair",
        _ => "Needs work",
    };

    /// <summary>One sub-score per available signal, and the simple average of whichever are
    /// present -- same philosophy as Ballistic Lab v2's own scoring: no weights, so there's
    /// nothing to re-litigate later about which factor should count for more.</summary>
    public sealed record Breakdown(double? Sd, double? Es, double? Group, double? SampleSize, double? Consistency)
    {
        /// <summary>Null when the load has nothing measured to score -- see
        /// <see cref="Rankable"/>. Otherwise the simple average of whichever sub-scores exist.</summary>
        public double? Total
        {
            get
            {
                if (!Rankable) return null;
                var available = new[] { Sd, Es, Group, SampleSize, Consistency }
                    .Where(v => v.HasValue).Select(v => v!.Value).ToList();
                return Math.Round(available.Average(), 2);
            }
        }

        /// <summary>Whether this load was measured at all. Sample size and consistency say how far
        /// to trust the other three, not how the load shot, so a load with neither SD, ES nor a
        /// group is not scored on them alone: 20 rounds logged with no chronograph and no target
        /// would otherwise average (10 + 7) / 2 = 8.5 and rank above a real load that merely shot
        /// badly.</summary>
        public bool Rankable => Sd.HasValue || Es.HasValue || Group.HasValue;
    }

    /// <summary>Composite score for one journal entry on its own (no cross-session consistency).</summary>
    public static Breakdown CompositeQualityScore(double? sdMps, double? esMps, double? groupMoa, int rounds) =>
        new(
            sdMps is { } sd ? ScoreSd(sd) : null,
            esMps is { } es ? ScoreEs(es) : null,
            groupMoa is { } g ? ScoreGroupMoa(g) : null,
            rounds > 0 ? ScoreSampleSize(rounds) : null,
            null);

    /// <summary>Composite score for the Leaderboard (a load tested across possibly several
    /// sessions) -- adds cross-session consistency to the single-entry breakdown.</summary>
    public static Breakdown LeaderboardScore(double? sdMps, double? esMps, double? groupMoa, int rounds,
        IReadOnlyList<double> sdAcrossSessionsMps) =>
        new(
            sdMps is { } sd ? ScoreSd(sd) : null,
            esMps is { } es ? ScoreEs(es) : null,
            groupMoa is { } g ? ScoreGroupMoa(g) : null,
            rounds > 0 ? ScoreSampleSize(rounds) : null,
            ScoreConsistency(sdAcrossSessionsMps));
}
