namespace GrtReloadingToolkit.Log;

/// <summary>
/// Four intra-session/comparative diagnostics ported from Ballistic Lab v2's
/// <c>core/advanced_diagnostics.py</c> (the only module in that project's whole "group 7" with
/// genuinely original logic — everything else there is a porting or a merge of existing scoring).
/// The Python formulas were hand-verified there against <c>numpy.polyfit</c>/<c>numpy.corrcoef</c>
/// before being trusted; the C# below is a straight port of already-checked math, not a re-derivation.
///
/// A real bug was found and fixed in the OLD (pre-Python-rewrite) software's
/// <c>neck_tension_scatter</c>: it built the tension&#8596;ES and tension&#8596;SD pairs with two
/// INDEPENDENT filters over the same loop, so a point with tension but no ES (or no SD) desynced the
/// lists and silently paired one point's tension with a DIFFERENT point's dispersion value. Every
/// point-pair here is built inline, per point, never by zipping two separately-filtered lists.
/// </summary>
public static class AdvancedDiagnostics
{
    public sealed class AdvancedDiagnosticsError : ArgumentException
    {
        public AdvancedDiagnosticsError(string message) : base(message) { }
    }

    /// <summary>Ordinary-least-squares slope, <c>cov(x,y)/var(x)</c> — shared by every trend
    /// calculation in this file and in <see cref="PressureTrend"/>/session-trend analysis, so the
    /// same formula is never re-derived a second time.</summary>
    public static double LinearSlope(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        int n = xs.Count;
        double mx = xs.Average(), my = ys.Average();
        double cov = 0, varX = 0;
        for (int i = 0; i < n; i++) { cov += (xs[i] - mx) * (ys[i] - my); varX += (xs[i] - mx) * (xs[i] - mx); }
        return varX != 0 ? cov / varX : 0.0;
    }

    // ── Fouling Tracker ──────────────────────────────────────────────────

    public sealed record FoulingPoint(int ShotNumber, double RollingSdMps, double RollingMeanMps);

    public sealed record FoulingResult(int Window, int NShots, IReadOnlyList<FoulingPoint> Points,
        double SdTrendSlope, double VelocityTrendSlope, bool FoulingSuspected, bool VelocityDeclining);

    /// <summary>Rolling-window SD/velocity trend across one string — a rising SD as the string
    /// progresses is the classic signature of barrel-fouling buildup. Thresholds are the old
    /// software's heuristic calibrations (0.5/-0.8 fps), converted to this toolkit's native m/s by
    /// the same fixed factor (&#215;0.3048) every other threshold in this project uses when moving
    /// unit systems — not re-derived, just re-expressed.</summary>
    public static FoulingResult FoulingTrend(IReadOnlyList<double> velocities, int window = 5,
        double foulingSlopeThreshold = 0.1524, double velocityDropThreshold = -0.2438)
    {
        if (window < 2) throw new AdvancedDiagnosticsError("window must be >= 2.");
        if (velocities.Count < window + 1)
            throw new AdvancedDiagnosticsError($"Need at least {window + 1} shots (window={window}).");

        var points = new List<FoulingPoint>();
        var rollingSd = new List<double>();
        for (int i = 0; i <= velocities.Count - window; i++)
        {
            var chunk = velocities.Skip(i).Take(window).ToList();
            double mean = chunk.Average();
            double sd = Math.Sqrt(chunk.Sum(v => (v - mean) * (v - mean)) / (chunk.Count - 1));
            rollingSd.Add(sd);
            points.Add(new FoulingPoint(i + window, Math.Round(sd, 3), Math.Round(mean, 2)));
        }

        double sdSlope = LinearSlope(Enumerable.Range(0, rollingSd.Count).Select(i => (double)i).ToList(), rollingSd);
        double mvSlope = LinearSlope(Enumerable.Range(0, velocities.Count).Select(i => (double)i).ToList(), velocities.ToList());

        return new FoulingResult(window, velocities.Count, points,
            Math.Round(sdSlope, 5), Math.Round(mvSlope, 5),
            FoulingSuspected: sdSlope > foulingSlopeThreshold && velocities.Count > 10,
            VelocityDeclining: mvSlope < velocityDropThreshold);
    }

    // ── Cold Bore ────────────────────────────────────────────────────────

    public sealed record ColdBoreResult(int NCold, int NWarm, double MeanColdMps, double MeanWarmMps,
        double DeltaMps, double SdWarmMps, double ZScore, bool ColdBoreIsOutlier, bool ColdBoreFaster);

    /// <summary>Compares the cold-bore shot(s) against the rest of the string. Z-score =
    /// |mean_cold-mean_warm| / SD_warm is an informal "how unusual is this" heuristic, NOT a rigorous
    /// significance test — with more than one cold-bore shot the standard error of their mean should
    /// be used instead of the raw warm SD. Ported honestly as the heuristic it is, not upgraded into
    /// something it isn't.</summary>
    public static ColdBoreResult ColdBoreAnalysis(IReadOnlyList<double> velocities, IReadOnlyList<bool> isColdBore, double zThreshold = 1.5)
    {
        if (velocities.Count != isColdBore.Count)
            throw new AdvancedDiagnosticsError("velocities and isColdBore must have the same length.");
        if (velocities.Count < 3) throw new AdvancedDiagnosticsError("Need at least 3 shots.");

        var cold = velocities.Where((_, i) => isColdBore[i]).ToList();
        var warm = velocities.Where((_, i) => !isColdBore[i]).ToList();
        if (cold.Count < 1 || warm.Count < 2)
            throw new AdvancedDiagnosticsError("Need at least 1 cold-bore shot and 2 warm shots.");

        double meanCold = cold.Average(), meanWarm = warm.Average();
        double sdWarm = Math.Sqrt(warm.Sum(v => (v - meanWarm) * (v - meanWarm)) / (warm.Count - 1));
        double delta = meanCold - meanWarm;
        double z = sdWarm > 0 ? Math.Abs(delta) / sdWarm : 0.0;

        return new ColdBoreResult(cold.Count, warm.Count, Math.Round(meanCold, 1), Math.Round(meanWarm, 1),
            Math.Round(delta, 1), Math.Round(sdWarm, 2), Math.Round(z, 2), z > zThreshold, delta > 0);
    }

    // ── Primer Sensitivity ───────────────────────────────────────────────

    public sealed record PrimerLotResult(string PrimerLot, int NShots, double MeanMps, double SdMps, double EsMps);

    public sealed record PrimerSensitivityResult(IReadOnlyList<PrimerLotResult> Lots, string BestPrimerLot,
        double SdSpreadMps, double EsSpreadMps, bool PrimerSensitivityDetected);

    /// <summary>Compares SD/ES between primer-lot strings at the same charge — lots sorted best
    /// (lowest SD) first.</summary>
    public static PrimerSensitivityResult PrimerSensitivity(IReadOnlyDictionary<string, IReadOnlyList<double>> groups,
        double sdSpreadThreshold = 3.0, double esSpreadThreshold = 10.0)
    {
        if (groups.Count < 2) throw new AdvancedDiagnosticsError("Need at least 2 primer lots.");

        var results = new List<PrimerLotResult>();
        foreach (var (lot, velocities) in groups)
        {
            if (velocities.Count < 3) continue;
            double mean = velocities.Average();
            double sd = Math.Sqrt(velocities.Sum(v => (v - mean) * (v - mean)) / (velocities.Count - 1));
            double es = velocities.Max() - velocities.Min();
            results.Add(new PrimerLotResult(lot, velocities.Count, Math.Round(mean, 1), Math.Round(sd, 2), Math.Round(es, 1)));
        }
        if (results.Count < 2) throw new AdvancedDiagnosticsError("Not enough data after filtering (min 3 valid shots per lot).");

        results = results.OrderBy(r => r.SdMps).ToList();
        var best = results[0]; var worst = results[^1];
        double sdSpread = worst.SdMps - best.SdMps;
        double esSpread = worst.EsMps - best.EsMps;

        return new PrimerSensitivityResult(results, best.PrimerLot, Math.Round(sdSpread, 2), Math.Round(esSpread, 1),
            sdSpread > sdSpreadThreshold || esSpread > esSpreadThreshold);
    }

    // ── Neck Tension Correlation ─────────────────────────────────────────

    /// <summary>One tested neck-tension point. <see cref="EsMps"/>/<see cref="SdMps"/> are the
    /// VELOCITY's dispersion (m/s), deliberately not the target-group ES (mm) — an explicit past
    /// project decision (see the class doc): the two dispersion metrics being compared must be
    /// homogeneous, both of velocity, not one of group size and one of velocity.</summary>
    public sealed record NeckTensionPoint(double TensionMm, double? EsMps = null, double? SdMps = null);

    public sealed record NeckTensionResult(int NPoints, double? CorrelationEs, double? CorrelationSd,
        double? OptimalTensionEs, double? OptimalTensionSd);

    /// <summary>Pearson correlation coefficient. <c>internal</c> rather than private so
    /// <see cref="SdRootCause"/> can reuse the same formula instead of re-deriving it a second time
    /// in the same folder — <see cref="LinearSlope"/> above is already shared the same way, with
    /// <c>SessionTrend</c>.</summary>
    internal static double? Pearson(IReadOnlyList<(double X, double Y)> pairs)
    {
        if (pairs.Count < 3) return null;
        double mx = pairs.Average(p => p.X), my = pairs.Average(p => p.Y);
        double num = pairs.Sum(p => (p.X - mx) * (p.Y - my));
        double den = Math.Sqrt(pairs.Sum(p => (p.X - mx) * (p.X - mx)) * pairs.Sum(p => (p.Y - my) * (p.Y - my)));
        return den != 0 ? num / den : null;
    }

    /// <summary>Pearson correlation between neck tension and velocity ES/SD, to estimate the optimal
    /// tension. Pairs are built POINT BY POINT (never by zipping two separately-filtered lists) — see
    /// the class doc for the real bug this guards against.</summary>
    public static NeckTensionResult NeckTensionCorrelation(IReadOnlyList<NeckTensionPoint> dataPoints)
    {
        if (dataPoints.Count < 3) throw new AdvancedDiagnosticsError("Need at least 3 tension points tested.");

        var esPairs = dataPoints.Where(p => p.EsMps.HasValue).Select(p => (p.TensionMm, p.EsMps!.Value)).ToList();
        var sdPairs = dataPoints.Where(p => p.SdMps.HasValue).Select(p => (p.TensionMm, p.SdMps!.Value)).ToList();

        double? corrEs = Pearson(esPairs);
        double? corrSd = Pearson(sdPairs);
        double? optEs = esPairs.Count > 0 ? esPairs.MinBy(p => p.Item2).TensionMm : null;
        double? optSd = sdPairs.Count > 0 ? sdPairs.MinBy(p => p.Item2).TensionMm : null;

        return new NeckTensionResult(dataPoints.Count,
            corrEs.HasValue ? Math.Round(corrEs.Value, 4) : null,
            corrSd.HasValue ? Math.Round(corrSd.Value, 4) : null,
            optEs, optSd);
    }
}
