using System.Text.Json;

namespace GrtReloadingToolkit.Log;

/// <summary>One step of a score curve: "at or below <see cref="Threshold"/>, score
/// <see cref="Score"/>". The last tier in a curve should carry <see cref="double.PositiveInfinity"/>
/// as its threshold -- it is the floor score for anything worse than every other tier, not a real
/// boundary a user would edit.</summary>
public sealed record ScoreTier(double Threshold, double Score);

/// <summary>
/// The editable half of <see cref="LoadScoring"/>: the SD/ES/Group-MOA tier tables, persisted as
/// JSON beside the journal database (same convention as <see cref="Ui.UiState"/>).
///
/// The defaults are the same real-world anchors <see cref="LoadScoring"/>'s own docs cite (Bryan
/// Litz/Applied Ballistics, the Mk 316 Mod 0 and M118LR sniper-ammunition specs, published benchrest
/// and hunting-rifle group-size benchmarks) -- but a user's own discipline may reasonably call for a
/// stricter or looser scale than a general-purpose default, so every number here is meant to be
/// opened and changed, not just read. Sample size and cross-session consistency are deliberately
/// NOT here: those are statistical-confidence curves, not ballistic-performance benchmarks, and
/// have no external standard to tune against.
/// </summary>
public sealed class LoadScoringConfig
{
    public List<ScoreTier> SdTiers { get; set; } = DefaultSdTiers();
    public List<ScoreTier> EsTiers { get; set; } = DefaultEsTiers();
    public List<ScoreTier> GroupMoaTiers { get; set; } = DefaultGroupMoaTiers();

    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GRTPlugins", "load-scoring.json");

    // AllowNamedFloatingPointLiterals is required: the "worst tier" sentinel in every default curve
    // is double.PositiveInfinity, which plain JSON has no token for. Without this, Serialize throws
    // (caught and swallowed by Save()'s try/catch below, so the symptom is a save that silently
    // does nothing) and a saved file with a literal "Infinity" fails to parse back on Load().
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static LoadScoringConfig Default => new();

    /// <summary>SD (m/s) anchors: 10/15/18/20/28 fps (Litz handloader goal, Mk 316 Mod 0, Litz
    /// "poor, mass-produced factory", M118LR spec ceiling).</summary>
    private static List<ScoreTier> DefaultSdTiers() => new()
    {
        new(3.05, 10.0), new(3.81, 9.0), new(4.57, 7.5), new(5.49, 6.0),
        new(6.10, 4.5), new(7.32, 3.0), new(8.53, 1.5), new(double.PositiveInfinity, 0.5),
    };

    /// <summary>ES (m/s) anchors: a cited exceptional 10-shot string (~10 fps), a cited "excellent"
    /// example (26 fps), the Mk 316 Mod 0 spec's hard ceiling (60 fps).</summary>
    private static List<ScoreTier> DefaultEsTiers() => new()
    {
        new(3.05, 10.0), new(5.0, 9.0), new(8.0, 7.5), new(12.5, 6.0),
        new(18.3, 4.5), new(22.0, 3.0), new(27.4, 1.5), new(double.PositiveInfinity, 0.5),
    };

    /// <summary>Group size (MOA, extreme spread) anchors: winning benchrest (0.225), "precision
    /// rifle territory" (0.5), excellent hunting rifle (0.75), the common baseline (1.0).</summary>
    private static List<ScoreTier> DefaultGroupMoaTiers() => new()
    {
        new(0.225, 10.0), new(0.35, 9.0), new(0.5, 8.0), new(0.75, 6.5),
        new(1.0, 5.0), new(1.5, 3.5), new(2.0, 2.0), new(double.PositiveInfinity, 0.5),
    };

    /// <summary>First tier whose threshold the value doesn't exceed. A curve edited down to zero
    /// tiers (or one missing PositiveInfinity as its worst case) falls back to 0.5 rather than
    /// throwing -- an editable table must survive a user leaving it in a state its author didn't
    /// anticipate.</summary>
    public static double Score(IReadOnlyList<ScoreTier> tiers, double value)
    {
        foreach (var t in tiers)
            if (value <= t.Threshold) return t.Score;
        return tiers.Count > 0 ? tiers[^1].Score : 0.5;
    }

    /// <summary>A missing, truncated or hand-edited file must never stop the Leaderboard opening --
    /// the cost of losing a customized scale is falling back to the default one, not a crash.</summary>
    public static LoadScoringConfig Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<LoadScoringConfig>(File.ReadAllText(Path), JsonOptions) ?? Default;
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
