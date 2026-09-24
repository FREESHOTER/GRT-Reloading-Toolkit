namespace GrtReloadingToolkit.Log;

/// <summary>
/// Editable tuning for <see cref="PressureSign.Diagnose"/> -- same "citable defaults, editable" shape
/// as <see cref="GroupAnalysisConfig"/>/<c>LoadScoringConfig</c>, with one honest difference stated
/// plainly rather than glossed over: there is no literature-cited absolute expansion-rate threshold
/// to default to here the way CEP50/Mahalanobis have one. Case-head expansion per grain depends on
/// brass lot, chamber and caliber, so this tool only ever compares a ladder's OWN later steps against
/// its OWN earlier steps (a ratio), never against a fixed mm/grain number claimed to be universal.
/// </summary>
public sealed class PressureSignConfig
{
    /// <summary>Need at least this many distinct charges before a trend claim is even attempted --
    /// judging a step's acceleration needs a believable "normal rate" behind it first.</summary>
    public int MinPoints { get; set; } = 4;

    /// <summary>How many earlier steps must exist before the step being judged, so "normal rate"
    /// means an actual average, not one lucky/unlucky prior reading.</summary>
    public int MinPriorSteps { get; set; } = 2;

    /// <summary>A step flags when its own rate exceeds this multiple of the ladder's own average
    /// prior rate. 2.0 (the default) means "expansion rate roughly doubled" -- a common informal
    /// reloading-forum rule of thumb, not a cited engineering standard; tune it against your own
    /// data once you've seen a couple of real ladders through this tool.</summary>
    public double AccelerationRatio { get; set; } = 2.0;

    /// <summary>A numerical floor for the prior-average rate before the ratio test above is applied,
    /// not a claimed physical threshold -- without it, a prior average that happens to be flat or
    /// slightly negative (measurement noise between two very similar charges) would make ANY small
    /// positive rate look like a huge multiple and flag constantly.</summary>
    public double MinBaselineSlopeMmPerGr { get; set; } = 0.002;
}

/// <summary>
/// Diagnoses accelerating fired-case head expansion across a charge ladder for one firearm+powder
/// group -- see <see cref="CaseHeadMeasurement"/> for the underlying log. Pure function, no Db
/// dependency, like every other Log/ calculation.
/// </summary>
public static class PressureSign
{
    public sealed record ChargePoint(double ChargeGr, double AvgHeadDiameterMm, int ReadingCount);

    public sealed record AccelerationFlag(
        double FromChargeGr, double ToChargeGr,
        double StepSlopeMmPerGr, double BaselineSlopeMmPerGr, double Ratio);

    /// <summary>Collapses possibly-several readings at the same charge (multiple fired cases from one
    /// round-robin point) into one averaged point per distinct charge, sorted ascending -- an
    /// unsorted or repeated-charge input list is expected, not a caller error, since that is exactly
    /// how a case-head log fills up over a real range session.</summary>
    public static IReadOnlyList<ChargePoint> BuildSeries(IEnumerable<CaseHeadMeasurement> measurements) =>
        measurements
            .GroupBy(m => m.ChargeGr)
            .Select(g => new ChargePoint(g.Key, g.Average(m => m.HeadDiameterMm), g.Count()))
            .OrderBy(p => p.ChargeGr)
            .ToList();

    /// <summary>Flags every charge-to-charge step whose expansion rate accelerates well beyond the
    /// ladder's own average rate up to that point. Returns empty (not a guess) below
    /// <see cref="PressureSignConfig.MinPoints"/> -- the same honesty convention as
    /// <c>GroupAnalysis.MahalanobisOutliers</c>'s own minimum-sample guard.</summary>
    public static IReadOnlyList<AccelerationFlag> Diagnose(IReadOnlyList<ChargePoint> series, PressureSignConfig cfg)
    {
        var flags = new List<AccelerationFlag>();
        if (series.Count < cfg.MinPoints) return flags;

        var slopes = new List<double>();
        for (int i = 1; i < series.Count; i++)
        {
            var a = series[i - 1];
            var b = series[i];
            double dCharge = b.ChargeGr - a.ChargeGr;
            if (dCharge <= 0) continue; // duplicate/out-of-order charge after grouping -- can't happen via BuildSeries, but a hand-built series shouldn't divide by zero if one ever is
            double slope = (b.AvgHeadDiameterMm - a.AvgHeadDiameterMm) / dCharge;

            if (slopes.Count >= cfg.MinPriorSteps)
            {
                double baseline = Math.Max(slopes.Average(), cfg.MinBaselineSlopeMmPerGr);
                double ratio = slope / baseline;
                if (ratio > cfg.AccelerationRatio)
                    flags.Add(new AccelerationFlag(a.ChargeGr, b.ChargeGr, slope, baseline, ratio));
            }
            slopes.Add(slope);
        }
        return flags;
    }
}
