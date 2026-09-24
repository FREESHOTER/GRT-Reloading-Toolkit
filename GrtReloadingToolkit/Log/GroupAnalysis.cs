using GrtReloadingToolkit.Ocw;

namespace GrtReloadingToolkit.Log;

public enum DistanceRangeKind { ShortRange, LongRange }

public enum ConfidenceLevel { Low, Medium, High }

public enum ProblemKind { StatisticalOutlier, SmallSample, SpreadRatioSkewed, OffCenter, CenterMismatch, LongRangeWindCaveat, VelocityCorrelation }

/// <summary>Mahalanobis distance of one impact from the group's own centroid, using the group's own sample covariance.</summary>
public sealed record MahalanobisOutlier(int Index, double DistanceSquared, bool Flagged);

public sealed record GroupProblem(ProblemKind Kind, string Message);

public sealed record GroupAnalysisReport(
    DistanceRangeKind Range,
    double Score,
    ConfidenceLevel Confidence,
    double MeanRadiusMoa,
    double GroupEsMoa,
    double Cep50Moa,
    double HorizontalSpreadMoa,
    double VerticalSpreadMoa,
    IReadOnlyList<MahalanobisOutlier> Outliers,
    IReadOnlyList<GroupProblem> Problems,
    double? VelocityPoiR = null);

/// <summary>
/// The 2D sibling of <see cref="SdRootCause"/>: given one shot group (from OnTarget, GRT's own
/// shot-group tabs, or manual entry — all three already normalized to the same <see cref="TargetGroup"/>
/// shape), answers the same kind of question SD Root-Cause asks of a velocity string — is this actually
/// good, how much should you trust that read given the sample size, and what specific, evidenced
/// problems (if any) explain a shot or a pattern dragging it down.
/// </summary>
public static class GroupAnalysis
{
    /// <summary>The live config every score/confidence/problem read in this process uses. Swap it
    /// (or edit it in place and re-<see cref="GroupAnalysisConfig.Save"/>) to change thresholds --
    /// same convention as <c>LoadScoring.Config</c>.</summary>
    public static GroupAnalysisConfig Config { get; set; } = GroupAnalysisConfig.Load();

    public static DistanceRangeKind ClassifyRange(double distanceM, GroupAnalysisConfig cfg)
        => distanceM > cfg.ShortRangeMaxM ? DistanceRangeKind.LongRange : DistanceRangeKind.ShortRange;

    private static (double VarX, double VarY, double CovXY) Covariance(TargetGroup group)
    {
        int n = group.Impacts.Count;
        double cx = group.CenterXMoa, cy = group.CenterYMoa;
        double sxx = 0, syy = 0, sxy = 0;
        foreach (var im in group.Impacts)
        {
            double dx = im.XMoa - cx, dy = im.YMoa - cy;
            sxx += dx * dx; syy += dy * dy; sxy += dx * dy;
        }
        int denom = Math.Max(n - 1, 1);
        return (sxx / denom, syy / denom, sxy / denom);
    }

    /// <summary>
    /// Sigma estimated from the sample's own pooled X/Y variance (average of the two, i.e. assuming a
    /// roughly circular spread), then CEP50 = sigma·√(ln4) — the companion of the Rayleigh-distribution
    /// relation <c>TargetGroup.MeanRadiusMoa</c> is already an estimate of (Mean Radius = sigma·√(π/2)).
    /// </summary>
    public static double Cep50Moa(TargetGroup group)
    {
        if (group.Impacts.Count < 2) return 0;
        var (varX, varY, _) = Covariance(group);
        double sigma = Math.Sqrt((varX + varY) / 2.0);
        return sigma * Math.Sqrt(Math.Log(4));
    }

    /// <summary>
    /// Mahalanobis distance of each impact from the group's own centroid, using the group's own 2x2
    /// sample covariance — not an assumed circular spread. This is the 2D analogue of
    /// <c>ChronoStatsCalc.ChauvenetOutliers</c>, since Chauvenet itself is a 1D criterion. A shot is
    /// flagged the same way Chauvenet flags one: when the expected count of a deviation this extreme,
    /// over the whole sample, is below 0.5. For 2 degrees of freedom the chi-square survival function
    /// has the closed form exp(-d²/2), so "expected &lt; 0.5" reduces to d² &gt; 2·ln(2n).
    /// Returns empty below <see cref="GroupAnalysisConfig.MinShotsForOutliers"/> or when the covariance
    /// is degenerate (collinear impacts, or all impacts share an axis) — a guess would be worse than none.
    /// </summary>
    public static IReadOnlyList<MahalanobisOutlier> MahalanobisOutliers(TargetGroup group, GroupAnalysisConfig cfg)
    {
        int n = group.Impacts.Count;
        if (n < cfg.MinShotsForOutliers) return Array.Empty<MahalanobisOutlier>();

        var (varX, varY, covXY) = Covariance(group);
        if (varX <= 0 || varY <= 0) return Array.Empty<MahalanobisOutlier>();
        double det = varX * varY - covXY * covXY;
        if (det <= 1e-9) return Array.Empty<MahalanobisOutlier>();

        double invA = varY / det, invB = -covXY / det, invD = varX / det;
        double cx = group.CenterXMoa, cy = group.CenterYMoa;
        var result = new List<MahalanobisOutlier>(n);
        for (int i = 0; i < n; i++)
        {
            double dx = group.Impacts[i].XMoa - cx, dy = group.Impacts[i].YMoa - cy;
            double d2 = dx * dx * invA + 2 * dx * dy * invB + dy * dy * invD;
            bool flagged = d2 > 2.0 * Math.Log(2.0 * n);
            result.Add(new MahalanobisOutlier(i, d2, flagged));
        }
        return result;
    }

    /// <summary>
    /// 0-10 from <c>MeanRadiusMoa</c> against the config's tiers for this range — mean radius rather
    /// than extreme spread, since ES is far noisier on the small samples a rifle group usually is.
    /// </summary>
    public static double Score(TargetGroup group, DistanceRangeKind range, GroupAnalysisConfig cfg)
    {
        var tiers = range == DistanceRangeKind.LongRange ? cfg.LongRangeScoreTiers : cfg.ShortRangeScoreTiers;
        return LoadScoringConfig.Score(tiers, group.MeanRadiusMoa);
    }

    /// <summary>From sample size alone — deliberately independent of <see cref="Score"/>, so a tight
    /// group from 3 shots and a tight group from 15 shots are never collapsed into one number.</summary>
    public static ConfidenceLevel Confidence(int shotCount, DistanceRangeKind range, GroupAnalysisConfig cfg)
    {
        int highAt = range == DistanceRangeKind.LongRange ? cfg.LongRangeHighAt : cfg.ShortRangeHighAt;
        int mediumAt = range == DistanceRangeKind.LongRange ? cfg.LongRangeMediumAt : cfg.ShortRangeMediumAt;
        if (shotCount >= highAt) return ConfidenceLevel.High;
        if (shotCount >= mediumAt) return ConfidenceLevel.Medium;
        return ConfidenceLevel.Low;
    }

    /// <summary>
    /// Every problem carries the evidence number behind it — never a bare claim. <paramref name="outliers"/>
    /// lets <see cref="Diagnose"/> pass its own already-computed list instead of recomputing it.
    /// <paramref name="crossCheckAppCenter"/> should only be true when <see cref="TargetGroup.AppCenterXMoa"/>/
    /// <c>YMoa</c> are known to have been actually reported by a source app (e.g. an OnTarget CSV) rather
    /// than left at their unset default — a manually-entered or GRT-native group has nothing to cross-check against.
    /// </summary>
    public static IReadOnlyList<GroupProblem> DiagnoseProblems(
        TargetGroup group, DistanceRangeKind range, GroupAnalysisConfig cfg,
        IReadOnlyList<MahalanobisOutlier>? outliers = null, bool crossCheckAppCenter = false,
        double? velocityPoiR = null)
    {
        var problems = new List<GroupProblem>();
        outliers ??= MahalanobisOutliers(group, cfg);

        foreach (var o in outliers)
            if (o.Flagged)
                problems.Add(new GroupProblem(ProblemKind.StatisticalOutlier,
                    FormattableString.Invariant($"Shot #{o.Index + 1} is a statistical outlier (Mahalanobis distance² {o.DistanceSquared:F2}).")));

        int mediumAt = range == DistanceRangeKind.LongRange ? cfg.LongRangeMediumAt : cfg.ShortRangeMediumAt;
        if (group.Impacts.Count < mediumAt)
            problems.Add(new GroupProblem(ProblemKind.SmallSample,
                $"Sample size (n={group.Impacts.Count}) is below the {mediumAt} shots this tool wants for a confident read at this distance."));

        double h = group.HorizontalSpreadMoa, v = group.VerticalSpreadMoa;
        if (Math.Min(h, v) > 0)
        {
            double ratio = Math.Max(h, v) / Math.Min(h, v);
            if (ratio > cfg.SpreadRatioThreshold)
            {
                string axis = v > h ? "vertical" : "horizontal";
                problems.Add(new GroupProblem(ProblemKind.SpreadRatioSkewed,
                    FormattableString.Invariant($"Spread is {axis}-dominant ({h:F2} MOA horizontal vs {v:F2} MOA vertical, ratio {ratio:F1}×) — worth investigating, not a diagnosed cause on this data alone.")));
            }
        }

        double offCenter = Math.Sqrt(Math.Pow(group.CenterXMoa - group.AimXMoa, 2) + Math.Pow(group.CenterYMoa - group.AimYMoa, 2));
        if (offCenter > cfg.OffCenterThresholdMoa)
            problems.Add(new GroupProblem(ProblemKind.OffCenter,
                FormattableString.Invariant($"Group centre is {offCenter:F2} MOA off point-of-aim — possible zero drift, or a called flier pulling the centroid.")));

        if (crossCheckAppCenter)
        {
            double mismatch = Math.Sqrt(Math.Pow(group.CenterXMoa - group.AppCenterXMoa, 2) + Math.Pow(group.CenterYMoa - group.AppCenterYMoa, 2));
            if (mismatch > cfg.AppCenterMismatchThresholdMoa)
                problems.Add(new GroupProblem(ProblemKind.CenterMismatch,
                    FormattableString.Invariant($"This tool's impact-mean centre differs from the source app's reported centre by {mismatch:F2} MOA — check the calibration points.")));
        }

        if (velocityPoiR is { } vr && Math.Abs(vr) > cfg.VelocityCorrelationThreshold)
        {
            string direction = vr > 0 ? "further from" : "closer to";
            problems.Add(new GroupProblem(ProblemKind.VelocityCorrelation,
                FormattableString.Invariant($"Velocity correlates with distance from the group centre (r={vr:F2}) — faster shots tend to land {direction} the centre; worth investigating, not a diagnosed cause on this data alone.")));
        }

        if (range == DistanceRangeKind.LongRange)
            problems.Add(new GroupProblem(ProblemKind.LongRangeWindCaveat,
                "This raw dispersion includes uncorrected wind effects at long range — read the score cautiously until a future version adds wind correction."));

        return problems;
    }

    /// <summary>
    /// Pearson r of per-shot velocity against that shot's radius from the group centre — "do the
    /// faster shots land further out (or higher/lower)?" Reuses <c>AdvancedDiagnostics.Pearson</c> and
    /// <see cref="SdRootCause.AlignShotsWithGroup"/>'s own firing-order pairing rather than re-deriving
    /// either. Like SD Root-Cause's temperature correlation, this is reported plainly as an
    /// informational r — never turned into a scored penalty, since a correlation on a handful of
    /// shots is evidence to look at, not evidence to score against.
    /// </summary>
    public static double? VelocityPoiCorrelation(IReadOnlyList<double> velocities, TargetGroup group)
    {
        var align = SdRootCause.AlignShotsWithGroup(velocities.Count, group);
        if (!align.Aligned || align.Pairs.Count < 3) return null;
        var pairs = align.Pairs.Select(p => (X: velocities[p.ShotIndex], Y: p.RadiusMoa)).ToList();
        return AdvancedDiagnostics.Pearson(pairs);
    }

    public static GroupAnalysisReport Diagnose(TargetGroup group, GroupAnalysisConfig cfg, bool crossCheckAppCenter = false, IReadOnlyList<double>? velocities = null)
    {
        var range = ClassifyRange(group.DistanceM, cfg);
        var outliers = MahalanobisOutliers(group, cfg);
        double? velR = velocities is { Count: >= 3 } vs ? VelocityPoiCorrelation(vs, group) : null;
        var problems = DiagnoseProblems(group, range, cfg, outliers, crossCheckAppCenter, velR);
        return new GroupAnalysisReport(
            range,
            Score(group, range, cfg),
            Confidence(group.Impacts.Count, range, cfg),
            group.MeanRadiusMoa,
            group.GroupEsMoa,
            Cep50Moa(group),
            group.HorizontalSpreadMoa,
            group.VerticalSpreadMoa,
            outliers,
            problems,
            velR);
    }

    /// <summary>
    /// Pools multiple groups (e.g. one per charge from a ladder folder) into one combined group for
    /// a single overall dispersion read. Each source group is recentred on its OWN centroid before
    /// pooling — otherwise a genuine point-of-impact difference between charges (a real load
    /// difference, not dispersion) would inflate the combined spread and the two would be
    /// indistinguishable. This measures pooled DISPERSION consistency, not point-of-impact
    /// consistency — the same "virtual group" idea OnTarget TDS uses to combine several small groups
    /// into one larger statistical sample.
    /// </summary>
    public static TargetGroup CombineGroups(IReadOnlyList<TargetGroup> groups)
    {
        var combined = new TargetGroup { SourceFile = $"combined ({groups.Count} groups)" };
        if (groups.Count == 0) return combined;
        foreach (var g in groups)
        {
            double cx = g.CenterXMoa, cy = g.CenterYMoa;
            foreach (var im in g.Impacts)
                combined.Impacts.Add(new Impact(im.XMoa - cx, im.YMoa - cy));
        }
        combined.DistanceM = groups.Average(g => g.DistanceM);
        return combined;
    }

    /// <summary>
    /// The third group-data source: manual per-shot entry, for anyone using neither OnTarget nor GRT's
    /// own shot-group tabs. Points and the optional point-of-aim are physical mm offsets from the
    /// group's own reference origin (the Ui layer converts from the user's display unit before calling
    /// this, the same "Log/ takes physical units, Ui/ handles unit choice" split every other tool uses);
    /// converted to MOA via <see cref="TargetGroup.MmToMoa"/>, the same conversion every other source
    /// ultimately relies on.
    /// </summary>
    public static TargetGroup BuildManualGroup(
        double distanceM, IReadOnlyList<(double XMm, double YMm)> pointsMm,
        double? aimXMm = null, double? aimYMm = null)
    {
        var g = new TargetGroup { SourceFile = "manual entry", DistanceM = distanceM };
        foreach (var (x, y) in pointsMm)
            g.Impacts.Add(new Impact(TargetGroup.MmToMoa(x, distanceM), TargetGroup.MmToMoa(y, distanceM)));
        if (aimXMm is { } ax) g.AimXMoa = TargetGroup.MmToMoa(ax, distanceM);
        if (aimYMm is { } ay) g.AimYMoa = TargetGroup.MmToMoa(ay, distanceM);
        return g;
    }
}
