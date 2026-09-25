using GrtReloadingToolkit.Ocw;

namespace GrtReloadingToolkit.Log;

public enum ConfidenceLevel { Low, Medium, High }

public enum ProblemKind { StatisticalOutlier, SmallSample, SpreadRatioSkewed, OffCenter, CenterMismatch, LongRangeWindCaveat, VelocityCorrelation }

public enum NodeRelation { Inside, Outside }

/// <summary>Mahalanobis distance of one impact from the group's own centroid, using the group's own sample covariance.</summary>
public sealed record MahalanobisOutlier(int Index, double DistanceSquared, bool Flagged);

public sealed record GroupProblem(ProblemKind Kind, string Message);

/// <summary>Whether this group's own charge falls inside or outside a node the Ladder/OCW Analyzer
/// already found for the same load — see <see cref="GroupAnalysis.CheckAgainstNode"/> for why this
/// is the "reliable" half of connecting Group Analysis's own dispersion read to a candidate physical
/// explanation, and why the other half (a barrel-vibration model) deliberately is not wired in here.</summary>
public sealed record NodeCrossCheck(double ChargeGrains, double NodeLowGrains, double NodeHighGrains, NodeRelation Relation);

public sealed record GroupAnalysisReport(
    DistanceBand Band,
    double Score,
    ConfidenceLevel Confidence,
    double MeanRadiusMoa,
    double GroupEsMoa,
    double Cep50Moa,
    double HorizontalSpreadMoa,
    double VerticalSpreadMoa,
    IReadOnlyList<MahalanobisOutlier> Outliers,
    IReadOnlyList<GroupProblem> Problems,
    double? VelocityPoiR = null,
    NodeCrossCheck? NodeCrossCheck = null,
    string? WindNotLoadNote = null);

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

    /// <summary>Picks the first configured band (ordered near to far) whose own distance is at or
    /// above the group's actual distance; a group beyond every configured band's distance reads the
    /// longest one — so "1000 m and beyond" all share one band without a separate unbounded entry to
    /// maintain. Re-sorts defensively rather than trusting <see cref="GroupAnalysisConfig.DistanceBands"/>'
    /// own order, since a hand-edited settings file could list them out of sequence.</summary>
    public static DistanceBand ClassifyBand(double distanceM, GroupAnalysisConfig cfg)
    {
        var ordered = cfg.DistanceBands.OrderBy(b => b.MaxDistanceM).ToList();
        foreach (var band in ordered)
            if (distanceM <= band.MaxDistanceM) return band;
        return ordered[^1];
    }

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

    /// <summary>0-10 from <c>MeanRadiusMoa</c> against this band's own tiers — mean radius rather than
    /// extreme spread, since ES is far noisier on the small samples a rifle group usually is (a choice
    /// Empirical Precision's own group-analysis methodology reaches independently, for the same
    /// reason: mean radius uses every shot, not just the two widest).</summary>
    public static double Score(TargetGroup group, DistanceBand band) => LoadScoringConfig.Score(band.ScoreTiers, group.MeanRadiusMoa);

    /// <summary>From sample size alone — deliberately independent of <see cref="Score"/>, so a tight
    /// group from 3 shots and a tight group from 15 shots are never collapsed into one number.</summary>
    public static ConfidenceLevel Confidence(int shotCount, DistanceBand band)
    {
        if (shotCount >= band.HighAt) return ConfidenceLevel.High;
        if (shotCount >= band.MediumAt) return ConfidenceLevel.Medium;
        return ConfidenceLevel.Low;
    }

    /// <summary>
    /// Below the config's <see cref="GroupAnalysisConfig.WindNotLoadVerticalThresholdMoa"/>, this is
    /// deliberately good news, not a problem to fix: <c>@xquizitclaw-creator</c>'s own framing (GitHub
    /// issue #31) is that once vertical dispersion is at or below a competitive benchmark, the load has
    /// done its job and everything left to explain is wind, not the rifle. Gated on the band's own
    /// <see cref="DistanceBand.MediumAt"/> shot count first, same as every other read here — a lucky
    /// 3-shot group reading tight at this threshold is not evidence of anything yet.</summary>
    public static string? WindNotLoadNote(TargetGroup group, GroupAnalysisConfig cfg, DistanceBand band)
    {
        if (group.Impacts.Count < band.MediumAt) return null;
        if (group.VerticalSpreadMoa <= 0 || group.VerticalSpreadMoa > cfg.WindNotLoadVerticalThresholdMoa) return null;
        return FormattableString.Invariant(
            $"Vertical spread ({group.VerticalSpreadMoa:F2} MOA) is at or below the competitive benchmark for \"the load has done its job\" (~{cfg.WindNotLoadVerticalThresholdMoa:F2} MOA, from 6\" of vertical at 1000 yards) — from here, tightening the group further is about reading wind, not the load.");
    }

    /// <summary>
    /// Every problem carries the evidence number behind it — never a bare claim. <paramref name="outliers"/>
    /// lets <see cref="Diagnose"/> pass its own already-computed list instead of recomputing it.
    /// <paramref name="crossCheckAppCenter"/> should only be true when <see cref="TargetGroup.AppCenterXMoa"/>/
    /// <c>YMoa</c> are known to have been actually reported by a source app (e.g. an OnTarget CSV) rather
    /// than left at their unset default — a manually-entered or GRT-native group has nothing to cross-check against.
    /// </summary>
    public static IReadOnlyList<GroupProblem> DiagnoseProblems(
        TargetGroup group, DistanceBand band, GroupAnalysisConfig cfg,
        IReadOnlyList<MahalanobisOutlier>? outliers = null, bool crossCheckAppCenter = false,
        double? velocityPoiR = null)
    {
        var problems = new List<GroupProblem>();
        outliers ??= MahalanobisOutliers(group, cfg);

        foreach (var o in outliers)
            if (o.Flagged)
                problems.Add(new GroupProblem(ProblemKind.StatisticalOutlier,
                    FormattableString.Invariant($"Shot #{o.Index + 1} is a statistical outlier (Mahalanobis distance² {o.DistanceSquared:F2}).")));

        if (group.Impacts.Count < band.MediumAt)
            problems.Add(new GroupProblem(ProblemKind.SmallSample,
                FormattableString.Invariant($"Sample size (n={group.Impacts.Count}) is below the {band.MediumAt} shots this tool wants for a confident read at this distance.")));

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

        if (band.WindCaveat)
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

    public static GroupAnalysisReport Diagnose(TargetGroup group, GroupAnalysisConfig cfg, bool crossCheckAppCenter = false,
        IReadOnlyList<double>? velocities = null, (double Low, double High)? ocwNodeGrains = null)
    {
        var band = ClassifyBand(group.DistanceM, cfg);
        var outliers = MahalanobisOutliers(group, cfg);
        double? velR = velocities is { Count: >= 3 } vs ? VelocityPoiCorrelation(vs, group) : null;
        var problems = DiagnoseProblems(group, band, cfg, outliers, crossCheckAppCenter, velR);
        return new GroupAnalysisReport(
            band,
            Score(group, band),
            Confidence(group.Impacts.Count, band),
            group.MeanRadiusMoa,
            group.GroupEsMoa,
            Cep50Moa(group),
            group.HorizontalSpreadMoa,
            group.VerticalSpreadMoa,
            outliers,
            problems,
            VelocityPoiR: velR,
            NodeCrossCheck: CheckAgainstNode(group.ChargeGrains, ocwNodeGrains),
            WindNotLoadNote: WindNotLoadNote(group, cfg, band));
    }

    /// <summary>Parses the machine-readable line <see cref="Ocw.LadderAnalyzer.BuildReport"/> appends
    /// to its own "OCW Analysis" note (<c>NODE_GR=low-high</c>, always raw grains) — deliberately not
    /// the human-readable "Recommended node (weighted): ..." line above it, which follows whatever
    /// unit GRT is currently displaying and would need the unit re-parsed and converted too. Returns
    /// null for a note from an older build that predates this line, or any other text that doesn't
    /// match — a missing cross-check costs nothing this tool didn't already have.</summary>
    public static (double Low, double High)? TryParseOcwNodeGrains(string? noteText)
    {
        if (string.IsNullOrEmpty(noteText)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(noteText, @"NODE_GR=([0-9.]+)-([0-9.]+)");
        if (!m.Success) return null;
        if (double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double low) &&
            double.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double high))
            return (low, high);
        return null;
    }

    /// <summary>
    /// The "reliable" half of connecting Group Analysis's own statistical read to a candidate physical
    /// explanation (see the class doc on <see cref="NodeCrossCheck"/>): whether this group's own
    /// charge falls inside a node the Ladder/OCW Analyzer already found FOR THE SAME LOAD. This is
    /// deliberately scoped to only that comparison — the Ladder/OCW node is itself an empirical read
    /// of the user's own measured ladder, not a physics model, so cross-checking against it never
    /// asserts anything beyond "does this match a pattern you already found in your own data". A
    /// second, physics-model-based connection (GRT Model OBT's barrel-vibration curve) was considered
    /// and deliberately NOT wired in here: that model simulates LONGITUDINAL barrel vibration while
    /// GRT's own native OBT concept assumes TRANSVERSE vibration (see this project's own notes on that
    /// discrepancy) — presenting it as a reliable cross-check risked explaining a real group pattern
    /// with a model that may not even be simulating the right physical phenomenon for that barrel.
    /// </summary>
    public static NodeCrossCheck? CheckAgainstNode(double? chargeGrains, (double Low, double High)? node)
    {
        if (chargeGrains is not { } charge || node is not { } n) return null;
        var relation = charge >= n.Low && charge <= n.High ? NodeRelation.Inside : NodeRelation.Outside;
        return new NodeCrossCheck(charge, n.Low, n.High, relation);
    }

    /// <summary>
    /// Pools multiple groups (e.g. one per charge from a ladder folder) into one combined group for
    /// a single overall dispersion read. Each source group is recentred on its OWN centroid before
    /// pooling — otherwise a genuine point-of-impact difference between charges (a real load
    /// difference, not dispersion) would inflate the combined spread and the two would be
    /// indistinguishable. This measures pooled DISPERSION consistency, not point-of-impact
    /// consistency — the same "virtual group" idea OnTarget TDS uses to combine several small groups
    /// into one larger statistical sample, and the same idea Empirical Precision's own compositing
    /// reaches independently ("ten 5-shot groups aligned by their centres give you a 50-shot picture").
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
