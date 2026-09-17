using GrtPluginKit.Grt;
using GrtPluginKit.Util;

namespace GrtReloadingToolkit.Ocw;

/// <summary>What kind of ladder is being analysed — changes which signal drives the node.</summary>
public enum LadderMode
{
    /// <summary>Charge-weight ladder: velocity flat-spot (Satterlee) + vertical-POI (OCW/Audette).</summary>
    Charge,
    /// <summary>Seating-depth / jump test: group-size plateau + vertical-POI.</summary>
    Seating,
}

/// <summary>Everything that differs between the two ladders but is not worth a subclass.</summary>
public static class LadderModes
{
    /// <summary>
    /// The unit to print beside <see cref="LadderStep.X"/>. A label, not a conversion. Unlike a
    /// COAL read off the load, a ladder X is the shooter's own number: <see cref="LadderLoader"/>
    /// parses it out of their file names and charge notes ("jump 0.020", "salto 1.5"), so nothing
    /// records which unit they wrote it in, and dividing it by 25.4 would corrupt an inch ladder.
    /// GRT's own length unit is the best evidence of the convention they were writing in, so we
    /// say that and leave the number exactly as they typed it. A charge ladder is the other case:
    /// the loader resolves those steps to grains, so the number is known and <see cref="XValue"/>
    /// converts it to whatever GRT weighs powder in.
    /// </summary>
    public static string XUnitName(this LadderMode mode) =>
        mode == LadderMode.Seating ? GrtUnits.Current.LengthUnitName : GrtUnits.Current.ChargeUnitName;

    /// <summary>
    /// A <see cref="LadderStep.X"/> as it should be shown. A seating step is the shooter's own
    /// number in an unrecorded unit — see <see cref="XUnitName"/> — so it passes through
    /// untouched; a charge step is stored grains, so it follows GRT's <c>charge</c> unit.
    /// </summary>
    public static double XValue(this LadderMode mode, double x) =>
        mode == LadderMode.Seating ? x : GrtUnits.Current.ChargeValue(x);

    /// <summary>
    /// The numeric format that goes with <see cref="XValue"/>. Lengths keep the four places GRT
    /// shows them in; a charge follows <see cref="GrtUnits.ChargeFormat"/>, which is wide enough
    /// that two neighbouring gram steps do not round onto the same number.
    /// </summary>
    public static string XFormat(this LadderMode mode) =>
        mode == LadderMode.Seating ? Str.LengthFormat : GrtUnits.Current.ChargeFormat;
}

/// <summary>
/// One step of a ladder — a charge weight (gr) or a seating depth / jump (mm or in).
/// Carries the chrono velocity stats and the target group stats for that step.
/// </summary>
public sealed class LadderStep
{
    /// <summary>The independent variable: grains (Charge mode) or mm/inch (Seating mode).</summary>
    public required double X { get; init; }

    // velocity (from Athlon Rangecraft)
    public int VelN { get; set; }
    public double MeanMps { get; set; }
    public double SdMps { get; set; }
    public double EsMps { get; set; }

    // target group (from Ballistic-X csv), all MOA
    public int Impacts { get; set; }
    public double DistanceM { get; set; }
    public double PoiXMoa { get; set; }
    public double PoiYMoa { get; set; }
    public double GroupEsMoa { get; set; }
    public double MeanRadiusMoa { get; set; }
    public double VertSpreadMoa { get; set; }

    public bool HasVel => VelN > 0;
    public bool HasTarget => Impacts > 0;
}

public sealed class NodeWindow
{
    public required double Center { get; init; }
    public required double Low { get; init; }
    public required double High { get; init; }
    public double Score { get; init; }
    public string Basis { get; init; } = "";
}

public sealed class AnalysisWeights
{
    public double Velocity { get; set; } = 1.0;   // MV flatness  (Charge mode)
    public double Poi { get; set; } = 1.4;         // vertical POI stability
    public double Group { get; set; } = 1.0;       // group size / plateau
    public double Sd { get; set; } = 0.8;          // MV consistency
    public int Window { get; set; } = 3;           // consecutive steps per node window

    public static AnalysisWeights For(LadderMode m) => m == LadderMode.Seating
        ? new AnalysisWeights { Group = 1.6, Poi = 1.2, Velocity = 0.3, Sd = 0.4 }
        : new AnalysisWeights { Velocity = 1.0, Poi = 1.4, Group = 1.0, Sd = 0.8 };
}

public sealed class LadderResult
{
    public LadderMode Mode { get; init; }
    /// <summary>"gr" / "mm" / "in" — unit of <see cref="LadderStep.X"/>.</summary>
    public string XUnit { get; init; } = "gr";
    public List<LadderStep> Rows { get; } = new();
    public NodeWindow? PrimaryNode { get; set; }   // velocity flat-spot (Charge) or group plateau (Seating)
    public NodeWindow? PoiNode { get; set; }
    public NodeWindow? BestNode { get; set; }
    public List<string> Warnings { get; } = new();
    public AnalysisWeights Weights { get; init; } = new();
}
