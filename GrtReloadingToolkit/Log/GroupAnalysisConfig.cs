using System.Text.Json;

namespace GrtReloadingToolkit.Log;

/// <summary>
/// Editable thresholds for <see cref="GroupAnalysis"/>, persisted as JSON beside the journal
/// database (same convention as <see cref="LoadScoringConfig"/>, whose <see cref="ScoreTier"/> and
/// <see cref="LoadScoringConfig.Score"/> are reused here rather than re-derived). Defaults are
/// starting points, not settled science: the score bands favour <c>TargetGroup.MeanRadiusMoa</c> over
/// extreme spread specifically because ES is far noisier on the small samples a rifle group usually
/// is, but the exact MOA cut points and the long-range sample-size minimums are both open questions
/// pending outside input (see the "Group Analysis" plan notes).
/// </summary>
public sealed class GroupAnalysisConfig
{
    /// <summary>Groups at or below this distance are "short range"; above it, "long range".</summary>
    public double ShortRangeMaxM { get; set; } = 500.0;

    /// <summary>Score tiers by <c>MeanRadiusMoa</c>, short range.</summary>
    public List<ScoreTier> ShortRangeScoreTiers { get; set; } = DefaultShortRangeScoreTiers();

    /// <summary>Same shape, but more lenient — a raw long-range group carries wind noise this version
    /// doesn't correct for, so the same physical rifle reads worse in MOA at distance than at 100 m.</summary>
    public List<ScoreTier> LongRangeScoreTiers { get; set; } = DefaultLongRangeScoreTiers();

    private static List<ScoreTier> DefaultShortRangeScoreTiers() => new()
    {
        new(0.50, 10), new(0.75, 8), new(1.00, 6), new(1.50, 4), new(2.00, 2), new(double.PositiveInfinity, 0),
    };

    private static List<ScoreTier> DefaultLongRangeScoreTiers() => new()
    {
        new(0.75, 10), new(1.00, 8), new(1.50, 6), new(2.00, 4), new(2.50, 2), new(double.PositiveInfinity, 0),
    };

    /// <summary>Shot counts at/above which Confidence becomes Medium / High, short range.</summary>
    public int ShortRangeMediumAt { get; set; } = 5;
    public int ShortRangeHighAt { get; set; } = 10;

    /// <summary>Same, long range — higher, because a small long-range sample is dominated by wind
    /// noise this version can't separate from real dispersion.</summary>
    public int LongRangeMediumAt { get; set; } = 8;
    public int LongRangeHighAt { get; set; } = 15;

    /// <summary>Minimum shots before a Mahalanobis outlier read is attempted at all (below this, the
    /// 2x2 covariance estimate is too unstable to trust — same "don't guess below a floor" convention
    /// as <c>ChronoStatsCalc.ChauvenetOutliers</c>' own n&lt;3 guard).</summary>
    public int MinShotsForOutliers { get; set; } = 5;

    /// <summary>Flag the group centre as off point-of-aim beyond this many MOA.</summary>
    public double OffCenterThresholdMoa { get; set; } = 1.0;

    /// <summary>Flag horizontal/vertical spread as skewed when the larger is more than this multiple of the smaller.</summary>
    public double SpreadRatioThreshold { get; set; } = 1.5;

    /// <summary>Flag a mismatch between this tool's impact-mean centroid and a source app's own
    /// reported centre (e.g. OnTarget's Center X/Y) beyond this many MOA — usually a bad calibration point.</summary>
    public double AppCenterMismatchThresholdMoa { get; set; } = 0.3;

    /// <summary>Flag the per-shot velocity <-> distance-from-centre correlation when |r| exceeds this
    /// — same informal threshold SD Root-Cause already uses for its own temperature<->group-dispersion
    /// correlation note, kept consistent rather than inventing a second convention.</summary>
    public double VelocityCorrelationThreshold { get; set; } = 0.3;

    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GRTPlugins", "group-analysis.json");

    // See LoadScoringConfig's own doc comment on this option: PositiveInfinity in the worst tier of
    // every default curve has no plain-JSON token, so Serialize throws without it.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static GroupAnalysisConfig Default => new();

    /// <summary>A missing, truncated or hand-edited file must never stop the tool opening — the cost
    /// of losing a customized scale is falling back to the default one, not a crash.</summary>
    public static GroupAnalysisConfig Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<GroupAnalysisConfig>(File.ReadAllText(Path), JsonOptions) ?? Default;
        }
        catch (Exception) { /* fall through to defaults */ }
        return Default;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception) { /* not worth interrupting the user over */ }
    }
}
