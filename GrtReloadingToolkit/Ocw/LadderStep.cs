namespace GrtReloadingToolkit.Ocw;

/// <summary>What kind of ladder is being analysed — changes which signal drives the node.</summary>
public enum LadderMode
{
    /// <summary>Charge-weight ladder: velocity flat-spot (Satterlee) + vertical-POI (OCW/Audette).</summary>
    Charge,
    /// <summary>Seating-depth / jump test: group-size plateau + vertical-POI.</summary>
    Seating,
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
