using System.Text.Json;

namespace GrtReloadingToolkit.Log;

/// <summary>
/// One distance's own score tiers and sample-size-for-confidence thresholds. Matches the way shooters
/// actually pick a test distance (100/200/300/600/800/1000 m are the common ones, not an arbitrary
/// short/long split) — a load tested at 300 m and one tested at 800 m don't deserve the same
/// "confident read" bar, and a single 500 m cutover hid that difference. <see cref="MaxDistanceM"/> is
/// this band's own label distance; <see cref="GroupAnalysis.ClassifyBand"/> picks the first band whose
/// <see cref="MaxDistanceM"/> is at or above the group's actual distance, falling back to the last
/// (longest) band for anything beyond it — so "1000 m and beyond" all read the same band without a
/// separate unbounded entry to maintain.
/// </summary>
public sealed record DistanceBand(double MaxDistanceM, List<ScoreTier> ScoreTiers, int MediumAt, int HighAt, bool WindCaveat);

/// <summary>
/// Editable thresholds for <see cref="GroupAnalysis"/>, persisted as JSON beside the journal
/// database (same convention as <see cref="LoadScoringConfig"/>, whose <see cref="ScoreTier"/> and
/// <see cref="LoadScoringConfig.Score"/> are reused here rather than re-derived). Defaults are
/// starting points, not settled science, stated plainly rather than dressed up as one: the two score
/// curves (<see cref="DistanceBand.ScoreTiers"/>) only have two real anchors (short ≤300 m, long
/// ≥600 m — unchanged from this tool's original short/long split), and the per-band sample-size
/// minimums are a straight-line interpolation between the 100 m end (5/10, this tool's own original
/// short-range default) and the 1000 m end (10/15) — the "10" itself IS a real anchor, the practical
/// minimum a competitive F-Class shooter (<c>@xquizitclaw-creator</c>, GitHub issue #31) said he never
/// trusts a read below, at any distance. The 200/300/600/800 m steps in between are not independently
/// sourced from anyone's practice, only smoothed between those two real endpoints.
/// </summary>
public sealed class GroupAnalysisConfig
{
    /// <summary>Six bands at the distances shooters actually test at, ordered near to far. The last
    /// band's own threshold (1000) is really "1000 m and anything beyond it" — see
    /// <see cref="GroupAnalysis.ClassifyBand"/>.</summary>
    public List<DistanceBand> DistanceBands { get; set; } = DefaultDistanceBands();

    private static List<ScoreTier> DefaultShortRangeScoreTiers() => new()
    {
        new(0.50, 10), new(0.75, 8), new(1.00, 6), new(1.50, 4), new(2.00, 2), new(double.PositiveInfinity, 0),
    };

    private static List<ScoreTier> DefaultLongRangeScoreTiers() => new()
    {
        new(0.75, 10), new(1.00, 8), new(1.50, 6), new(2.00, 4), new(2.50, 2), new(double.PositiveInfinity, 0),
    };

    private static List<DistanceBand> DefaultDistanceBands()
    {
        var shortTiers = DefaultShortRangeScoreTiers();
        var longTiers = DefaultLongRangeScoreTiers();
        return new()
        {
            new(100, new List<ScoreTier>(shortTiers), MediumAt: 5, HighAt: 10, WindCaveat: false),
            new(200, new List<ScoreTier>(shortTiers), MediumAt: 5, HighAt: 10, WindCaveat: false),
            new(300, new List<ScoreTier>(shortTiers), MediumAt: 6, HighAt: 11, WindCaveat: false),
            new(600, new List<ScoreTier>(longTiers), MediumAt: 7, HighAt: 13, WindCaveat: true),
            new(800, new List<ScoreTier>(longTiers), MediumAt: 8, HighAt: 14, WindCaveat: true),
            new(1000, new List<ScoreTier>(longTiers), MediumAt: 10, HighAt: 15, WindCaveat: true),
        };
    }

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

    /// <summary>
    /// Below this vertical spread (MOA), the group is informational-good-news territory: "the load has
    /// done its job, what's left is wind, not the rifle" — <c>@xquizitclaw-creator</c>'s own framing on
    /// issue #31. Default is his competitive F-Class benchmark, 6 inches of vertical at 1000 yards,
    /// converted to MOA (1 MOA = 1.047" at 100 yd, so 6"/10.47"-per-MOA ≈ 0.573) — MOA is already
    /// distance-independent, so this reads the same whether the group was actually shot at 1000 yards,
    /// 1000 m, or 300 m with a proportionally smaller vertical spread in inches.</summary>
    public double WindNotLoadVerticalThresholdMoa { get; set; } = 0.573;

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
