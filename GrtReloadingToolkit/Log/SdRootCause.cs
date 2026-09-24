using GrtReloadingToolkit.ChronoStats;
using GrtReloadingToolkit.Ocw;

namespace GrtReloadingToolkit.Log;

/// <summary>
/// For one shot string: which shot(s), if any, are dragging an otherwise-good SD down, and (when the
/// data exists) why. Built to answer that question directly instead of requiring the user to guess
/// which of <see cref="AdvancedDiagnostics"/>'s six separate, manually-configured tabs to open first.
///
/// GRT's own plugin API carries only velocity per shot (<c>GrtPluginKit.Grt.GrtShot</c> has no
/// temperature, construction, or lot field) — so per-shot temperature here is always the caller's own
/// manual entry (a probe read at the moment of each shot), never something GRT itself supplies. Every
/// other cause reuses an existing, already-verified building block rather than re-deriving it:
/// <see cref="AdvancedDiagnostics.ColdBoreAnalysis"/>, <see cref="AdvancedDiagnostics.FoulingTrend"/>,
/// <see cref="AdvancedDiagnostics.Pearson"/>, and <see cref="ChronoStatsCalc.ChauvenetOutliers"/>.
/// </summary>
public static class SdRootCause
{
    public sealed class SdRootCauseError : ArgumentException
    {
        public SdRootCauseError(string message) : base(message) { }
    }

    public enum SdPatternKind { SingleOutlier, PartialOutlier, Systematic }

    /// <summary>One shot's effect on the string's SD if it alone were removed (largest reduction
    /// first). <see cref="ChauvenetFlagged"/> — not an invented percentage cutoff — is what
    /// <see cref="ClassifySdPattern"/> uses to decide how many shots are actually driving the SD.</summary>
    public sealed record LeaveOneOutRow(int ShotIndex, double VelocityMps, double SdWithoutMps,
        double ReductionMps, double ReductionPct, bool ChauvenetFlagged);

    /// <summary>Whether a Measurement's shots and a Shot-group tab's hits could be paired by firing
    /// order. <see cref="Reason"/> must be shown to the caller whenever <see cref="Aligned"/> is
    /// true, not only when it's false — a matching count does not prove the order actually lines up,
    /// since GRT does not link the two tabs itself.</summary>
    public sealed record AlignmentResult(bool Aligned, string Reason, IReadOnlyList<(int ShotIndex, double RadiusMoa)> Pairs);

    public sealed record SdRootCauseReport(
        double SdMps,
        SdPatternKind Pattern,
        IReadOnlyList<LeaveOneOutRow> Ranking,
        AdvancedDiagnostics.ColdBoreResult? ColdBore,
        AdvancedDiagnostics.FoulingResult? ProgressiveDrift,
        double? TempVelocityR,
        double? TempPoiR,
        double Skewness);

    /// <summary>For each shot, the string's sample SD with that one shot excluded — sample SD
    /// (&#247;n&#8722;1), the same convention <see cref="ChronoStatsCalc.SampleSd"/> uses and what
    /// "SD" means everywhere else in the toolkit.</summary>
    public static IReadOnlyList<LeaveOneOutRow> LeaveOneOutRanking(IReadOnlyList<double> velocities)
    {
        if (velocities.Count < 3) throw new SdRootCauseError("Need at least 3 shots.");
        double sdAll = SampleSd(velocities);
        var flagged = ChronoStatsCalc.ChauvenetOutliers(velocities).Where(o => o.Flagged).Select(o => o.Index).ToHashSet();

        var rows = new List<LeaveOneOutRow>();
        for (int i = 0; i < velocities.Count; i++)
        {
            var without = velocities.Where((_, j) => j != i).ToList();
            double sdWithout = SampleSd(without);
            double reduction = sdAll - sdWithout;
            double pct = sdAll > 0 ? reduction / sdAll * 100 : 0;
            rows.Add(new LeaveOneOutRow(i, velocities[i], sdWithout, reduction, pct, flagged.Contains(i)));
        }
        return rows.OrderByDescending(r => r.ReductionMps).ToList();
    }

    /// <summary>Single-outlier / partial-outlier / systematic, from how many shots Chauvenet's
    /// criterion actually flags — not an invented percentage-of-SD cutoff. Zero flagged shots is
    /// Systematic: the dispersion is spread across the whole string rather than concentrated in a
    /// shot that is statistically distinct from the rest.</summary>
    public static SdPatternKind ClassifySdPattern(IReadOnlyList<LeaveOneOutRow> ranking) =>
        ranking.Count(r => r.ChauvenetFlagged) switch
        {
            0 => SdPatternKind.Systematic,
            1 => SdPatternKind.SingleOutlier,
            _ => SdPatternKind.PartialOutlier,
        };

    /// <summary>Fisher-Pearson third-moment skewness (no small-sample bias correction) — a
    /// narrative-only signal, never given a quantified SD contribution the way the other causes are.
    /// No reloading-specific citation exists for what counts as "notable"; the caller must present
    /// this as an informal, generic-statistics convention, not a finding with the same weight as the
    /// Chauvenet- or Pearson-based causes.</summary>
    public static double Skewness(IReadOnlyList<double> velocities)
    {
        int n = velocities.Count;
        if (n < 3) return 0;
        double mean = velocities.Average();
        double sd = PopulationSd(velocities, mean);
        if (sd <= 0) return 0;
        double m3 = velocities.Sum(v => Math.Pow(v - mean, 3)) / n;
        return m3 / Math.Pow(sd, 3);
    }

    /// <summary>
    /// Pairs a Measurement's <paramref name="shotCount"/> shots with a Shot-group tab's hits by
    /// firing order — GRT does not link the two itself, so a matching count is the only signal this
    /// can go on. The per-shot radius is distance from <see cref="TargetGroup.CenterXMoa"/>/
    /// <see cref="TargetGroup.CenterYMoa"/> (the same centroid the group's own ES/mean-radius already
    /// use), not a second, differently-computed centre.
    /// </summary>
    public static AlignmentResult AlignShotsWithGroup(int shotCount, TargetGroup group)
    {
        if (group.Impacts.Count != shotCount)
            return new AlignmentResult(false,
                $"{shotCount} chronographed shot(s) but {group.Impacts.Count} hit(s) in the shot-group tab -- cannot align.",
                Array.Empty<(int, double)>());

        double cx = group.CenterXMoa, cy = group.CenterYMoa;
        var pairs = group.Impacts
            .Select((imp, i) => (ShotIndex: i, RadiusMoa: Math.Sqrt((imp.XMoa - cx) * (imp.XMoa - cx) + (imp.YMoa - cy) * (imp.YMoa - cy))))
            .ToList();
        return new AlignmentResult(true,
            "Assumes the shot-group tab's hit order matches this Measurement's firing order -- GRT does not link them itself.",
            pairs);
    }

    /// <summary>Pearson r of manually-entered per-shot temperature against that shot's velocity —
    /// reuses <see cref="AdvancedDiagnostics.Pearson"/> rather than re-deriving the same formula a
    /// second time in this folder.</summary>
    public static double? TempVelocityCorrelation(IReadOnlyList<(int ShotIndex, double TempC, double VelocityMps)> pairs) =>
        AdvancedDiagnostics.Pearson(pairs.Select(p => (p.TempC, p.VelocityMps)).ToList());

    /// <summary>Pearson r of manually-entered per-shot temperature against that shot's distance from
    /// the group centre (MOA) — answers "does this barrel stop holding tight groups above some
    /// temperature" using GRT's own native per-shot impact data (<see cref="AlignShotsWithGroup"/>),
    /// not velocity. Not available anywhere else in the toolkit today.</summary>
    public static double? TempPoiCorrelation(IReadOnlyList<(int ShotIndex, double TempC, double RadiusMoa)> pairs) =>
        AdvancedDiagnostics.Pearson(pairs.Select(p => (p.TempC, p.RadiusMoa)).ToList());

    /// <summary>
    /// Runs every cause this string's data supports and bundles them into one report — the caller
    /// (the UI layer) turns this into ranked, localized text; this method returns plain data only.
    /// <paramref name="coldBoreFlags"/>, <paramref name="manualTemps"/> and <paramref name="shotGroup"/>
    /// are all optional: with none of them, only the leave-one-out ranking and pattern are returned.
    /// When <paramref name="manualTemps"/> has real data, it is used for the real temperature
    /// correlations; otherwise <see cref="AdvancedDiagnostics.FoulingTrend"/> (a firing-order trend,
    /// the only proxy GRT's own data can support) stands in as an unmeasured probable cause — the
    /// caller must label it as such, not present it with the same certainty as a real correlation.
    /// </summary>
    public static SdRootCauseReport Diagnose(
        IReadOnlyList<double> velocities,
        IReadOnlyList<bool>? coldBoreFlags = null,
        IReadOnlyList<(int ShotIndex, double TempC)>? manualTemps = null,
        TargetGroup? shotGroup = null)
    {
        var ranking = LeaveOneOutRanking(velocities);
        var pattern = ClassifySdPattern(ranking);
        double sdAll = SampleSd(velocities);

        AdvancedDiagnostics.ColdBoreResult? coldBore = null;
        if (coldBoreFlags != null)
        {
            try { coldBore = AdvancedDiagnostics.ColdBoreAnalysis(velocities, coldBoreFlags); }
            catch (AdvancedDiagnostics.AdvancedDiagnosticsError) { /* not enough cold/warm shots to compare -- omit, not an error for this caller */ }
        }

        double? tempVelR = null, tempPoiR = null;
        AdvancedDiagnostics.FoulingResult? drift = null;

        if (manualTemps is { Count: >= 3 })
        {
            var velPairs = manualTemps
                .Where(t => t.ShotIndex >= 0 && t.ShotIndex < velocities.Count)
                .Select(t => (t.ShotIndex, t.TempC, VelocityMps: velocities[t.ShotIndex]))
                .ToList();
            if (velPairs.Count >= 3) tempVelR = TempVelocityCorrelation(velPairs);

            if (shotGroup != null)
            {
                var align = AlignShotsWithGroup(velocities.Count, shotGroup);
                if (align.Aligned)
                {
                    var radiusByIndex = align.Pairs.ToDictionary(p => p.ShotIndex, p => p.RadiusMoa);
                    var poiPairs = manualTemps
                        .Where(t => radiusByIndex.ContainsKey(t.ShotIndex))
                        .Select(t => (t.ShotIndex, t.TempC, RadiusMoa: radiusByIndex[t.ShotIndex]))
                        .ToList();
                    if (poiPairs.Count >= 3) tempPoiR = TempPoiCorrelation(poiPairs);
                }
            }
        }
        else if (velocities.Count >= 6)
        {
            try { drift = AdvancedDiagnostics.FoulingTrend(velocities); }
            catch (AdvancedDiagnostics.AdvancedDiagnosticsError) { /* window too large for this string -- omit */ }
        }

        return new SdRootCauseReport(sdAll, pattern, ranking, coldBore, drift, tempVelR, tempPoiR, Skewness(velocities));
    }

    private static double SampleSd(IReadOnlyList<double> v)
    {
        if (v.Count < 2) return 0;
        double mean = v.Average();
        return Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / (v.Count - 1));
    }

    private static double PopulationSd(IReadOnlyList<double> v, double mean) =>
        v.Count == 0 ? 0 : Math.Sqrt(v.Sum(x => (x - mean) * (x - mean)) / v.Count);
}
