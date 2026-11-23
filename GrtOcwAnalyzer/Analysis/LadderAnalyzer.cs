using System.Globalization;
using GrtOcwAnalyzer.Targets;
using GrtPluginKit.Analysis;

namespace GrtOcwAnalyzer.Analysis;

/// <summary>One charge step of a ladder: chrono velocity stats + target group stats.</summary>
public sealed class ChargeRow
{
    public required double ChargeGr { get; init; }

    // velocity (from Athlon)
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
    public required double CenterChargeGr { get; init; }
    public required double LowGr { get; init; }
    public required double HighGr { get; init; }
    public double Score { get; init; }
    public string Basis { get; init; } = "";
}

public sealed class LadderResult
{
    public List<ChargeRow> Rows { get; } = new();
    public NodeWindow? VelocityNode { get; set; }
    public NodeWindow? OcwNode { get; set; }
    public NodeWindow? BestNode { get; set; }
    public List<string> Warnings { get; } = new();
    public AnalysisWeights Weights { get; init; } = new();
}

public sealed class AnalysisWeights
{
    public double Velocity { get; init; } = 1.0;   // MV flatness
    public double Poi { get; init; } = 1.4;         // vertical POI stability (OCW)
    public double Group { get; init; } = 1.0;       // group size
    public double Sd { get; init; } = 0.8;          // MV consistency
    public int Window { get; init; } = 3;           // consecutive charges per node window
}

public static class LadderAnalyzer
{
    public static LadderResult Analyze(IEnumerable<ChargeRow> rows, AnalysisWeights? w = null)
    {
        w ??= new AnalysisWeights();
        var r = new LadderResult { Weights = w };
        r.Rows.AddRange(rows.OrderBy(x => x.ChargeGr));
        var xs = r.Rows;
        if (xs.Count < 3) { r.Warnings.Add("need at least 3 charges for node analysis"); return r; }

        // sanity / data-coverage warnings
        if (xs.Any(x => !x.HasVel)) r.Warnings.Add("some charges have no chrono velocity");
        if (xs.Any(x => !x.HasTarget)) r.Warnings.Add("some charges have no target group — OCW node uses velocity only for those");
        var dists = xs.Where(x => x.HasTarget).Select(x => Math.Round(x.DistanceM)).Distinct().ToList();
        if (dists.Count > 1) r.Warnings.Add($"target groups shot at different distances ({string.Join(" / ", dists)} m) — POI compared in MOA, but conditions differ");

        int win = Math.Clamp(w.Window, 3, 5);
        var windows = new List<(int lo, int hi, double velRange, double poiRange, double grp, double sd)>();
        for (int i = 0; i + win - 1 < xs.Count; i++)
        {
            var seg = xs.Skip(i).Take(win).ToList();
            double velRange = Range(seg.Where(s => s.HasVel).Select(s => s.MeanMps));
            double poiRange = Range(seg.Where(s => s.HasTarget).Select(s => s.PoiYMoa));
            double grp = Mean(seg.Where(s => s.HasTarget).Select(s => s.MeanRadiusMoa));
            double sd = Mean(seg.Where(s => s.HasVel).Select(s => s.SdMps));
            windows.Add((i, i + win - 1, velRange, poiRange, grp, sd));
        }

        // velocity-only node (Satterlee flat spot)
        var velW = windows.Where(x => xs.Skip(x.lo).Take(win).Count(s => s.HasVel) >= win).ToList();
        if (velW.Count > 0)
        {
            var best = velW.OrderBy(x => x.velRange).First();
            r.VelocityNode = MakeWindow(xs, best.lo, best.hi, best.velRange,
                string.Format(CultureInfo.InvariantCulture, "MV varies only {0:0.0} m/s across {1} charges", best.velRange, win));
        }

        // OCW / Audette node (vertical POI stability)
        var poiW = windows.Where(x => xs.Skip(x.lo).Take(win).Count(s => s.HasTarget) >= win).ToList();
        if (poiW.Count > 0)
        {
            var best = poiW.OrderBy(x => x.poiRange).ThenBy(x => x.grp).First();
            r.OcwNode = MakeWindow(xs, best.lo, best.hi, best.poiRange,
                string.Format(CultureInfo.InvariantCulture, "vertical POI varies only {0:0.00} MOA across {1} charges", best.poiRange, win));
        }

        // combined
        double nVel = Norm(windows.Select(x => x.velRange));
        double nPoi = Norm(windows.Select(x => x.poiRange));
        double nGrp = Norm(windows.Select(x => x.grp));
        double nSd = Norm(windows.Select(x => x.sd));
        (int lo, int hi, double score)? bestC = null;
        foreach (var x in windows)
        {
            double score =
                w.Velocity * Safe(x.velRange, nVel) +
                w.Poi * Safe(x.poiRange, nPoi) +
                w.Group * Safe(x.grp, nGrp) +
                w.Sd * Safe(x.sd, nSd);
            if (bestC is null || score < bestC.Value.score) bestC = (x.lo, x.hi, score);
        }
        if (bestC is { } bc)
            r.BestNode = MakeWindow(xs, bc.lo, bc.hi, bc.score, "weighted MV flatness + POI stability + group + SD");

        return r;
    }

    public static string BuildReport(LadderResult r, string headline)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(headline);
        sb.AppendLine(new string('-', headline.Length));
        sb.AppendLine();
        sb.AppendLine("charge  n   MV      SD    ES   | dist  POI-Y   grpMR  vert  (MOA)");
        foreach (var x in r.Rows)
        {
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,5:0.0} {1,3} {2,7:0.0} {3,5:0.0} {4,5:0.0} | {5,4:0} {6,7:0.00} {7,6:0.00} {8,5:0.00}",
                x.ChargeGr, x.HasVel ? x.VelN : 0, x.MeanMps, x.SdMps, x.EsMps,
                x.HasTarget ? x.DistanceM : 0, x.PoiYMoa, x.MeanRadiusMoa, x.VertSpreadMoa));
        }
        sb.AppendLine();
        Line(sb, "Velocity flat-spot (Satterlee)", r.VelocityNode);
        Line(sb, "OCW / Audette node (vertical POI)", r.OcwNode);
        Line(sb, "Recommended node (weighted)", r.BestNode);
        if (r.Warnings.Count > 0)
        {
            sb.AppendLine();
            foreach (var wn in r.Warnings) sb.AppendLine("! " + wn);
        }
        sb.AppendLine();
        sb.AppendLine("Method: MOA coordinates from Ballistic-X, velocities from Athlon Rangecraft.");
        sb.AppendLine("SD is population (n). Confirm any node with a round-robin group before committing.");
        return sb.ToString();

        static void Line(System.Text.StringBuilder s, string label, NodeWindow? n)
            => s.AppendLine(n is null
                ? $"{label}: n/a"
                : string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.0}-{2:0.0} gr  (center {3:0.0})  -- {4}",
                    label, n.LowGr, n.HighGr, n.CenterChargeGr, n.Basis));
    }

    // ---- helpers ---------------------------------------------------------
    private static NodeWindow MakeWindow(List<ChargeRow> xs, int lo, int hi, double score, string basis) => new()
    {
        LowGr = xs[lo].ChargeGr,
        HighGr = xs[hi].ChargeGr,
        CenterChargeGr = xs[(lo + hi) / 2].ChargeGr,
        Score = score,
        Basis = basis,
    };
    private static double Range(IEnumerable<double> v) { var l = v.ToList(); return l.Count == 0 ? 0 : l.Max() - l.Min(); }
    private static double Mean(IEnumerable<double> v) { var l = v.ToList(); return l.Count == 0 ? 0 : l.Average(); }
    private static double Norm(IEnumerable<double> v) { var l = v.ToList(); double m = l.Count == 0 ? 0 : l.Max(); return m > 0 ? m : 1; }
    private static double Safe(double val, double norm) => norm > 0 ? val / norm : 0;
}
