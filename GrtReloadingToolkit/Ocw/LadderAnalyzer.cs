using System.Globalization;

namespace GrtReloadingToolkit.Ocw;

public static class LadderAnalyzer
{
    public static LadderResult Analyze(IEnumerable<LadderStep> steps, LadderMode mode, string xUnit, AnalysisWeights? w = null)
    {
        w ??= AnalysisWeights.For(mode);
        var r = new LadderResult { Mode = mode, XUnit = xUnit, Weights = w };
        r.Rows.AddRange(steps.OrderBy(x => x.X));
        var xs = r.Rows;
        if (xs.Count < 3) { r.Warnings.Add("need at least 3 steps for node analysis"); return r; }

        if (mode == LadderMode.Charge && xs.Any(x => !x.HasVel)) r.Warnings.Add("some steps have no chrono velocity");
        if (xs.Any(x => !x.HasTarget)) r.Warnings.Add("some steps have no target group");
        var dists = xs.Where(x => x.HasTarget).Select(x => Math.Round(x.DistanceM)).Distinct().ToList();
        if (dists.Count > 1) r.Warnings.Add($"target groups shot at different distances ({string.Join(" / ", dists)} m) — POI compared in MOA, but conditions differ");

        int win = Math.Clamp(w.Window, 3, 5);
        var windows = new List<Win>();
        for (int i = 0; i + win - 1 < xs.Count; i++)
        {
            var seg = xs.Skip(i).Take(win).ToList();
            windows.Add(new Win(
                i, i + win - 1,
                VelRange: Range(seg.Where(s => s.HasVel).Select(s => s.MeanMps)),
                PoiRange: Range(seg.Where(s => s.HasTarget).Select(s => s.PoiYMoa)),
                GrpMean: Mean(seg.Where(s => s.HasTarget).Select(s => s.MeanRadiusMoa)),
                GrpRange: Range(seg.Where(s => s.HasTarget).Select(s => s.MeanRadiusMoa)),
                SdMean: Mean(seg.Where(s => s.HasVel).Select(s => s.SdMps))));
        }

        // ---- primary node -----------------------------------------------------
        if (mode == LadderMode.Charge)
        {
            var velW = windows.Where(x => Count(xs, x, win, s => s.HasVel) >= win).ToList();
            if (velW.Count > 0)
            {
                var best = velW.OrderBy(x => x.VelRange).First();
                r.PrimaryNode = Node(xs, best,
                    string.Format(CultureInfo.InvariantCulture, "MV varies only {0:0.0} m/s across {1} steps", best.VelRange, win));
            }
        }
        else
        {
            var grpW = windows.Where(x => Count(xs, x, win, s => s.HasTarget) >= win).ToList();
            if (grpW.Count > 0)
            {
                // tightest groups that are also stable across the window (forgiving node)
                var best = grpW.OrderBy(x => x.GrpMean + 0.5 * x.GrpRange).First();
                r.PrimaryNode = Node(xs, best,
                    string.Format(CultureInfo.InvariantCulture, "mean group {0:0.00} MOA, varies only {1:0.00} MOA across {2} steps", best.GrpMean, best.GrpRange, win));
            }
        }

        // ---- vertical-POI node ----------------------------------------------
        var poiW = windows.Where(x => Count(xs, x, win, s => s.HasTarget) >= win).ToList();
        if (poiW.Count > 0)
        {
            var best = poiW.OrderBy(x => x.PoiRange).ThenBy(x => x.GrpMean).First();
            r.PoiNode = Node(xs, best,
                string.Format(CultureInfo.InvariantCulture, "vertical POI varies only {0:0.00} MOA across {1} steps", best.PoiRange, win));
        }

        // ---- weighted best -------------------------------------------------
        double nVel = Norm(windows.Select(x => x.VelRange));
        double nPoi = Norm(windows.Select(x => x.PoiRange));
        double nGrpM = Norm(windows.Select(x => x.GrpMean));
        double nGrpR = Norm(windows.Select(x => x.GrpRange));
        double nSd = Norm(windows.Select(x => x.SdMean));
        Win? bestC = null; double bestS = double.MaxValue;
        foreach (var x in windows)
        {
            double groupTerm = mode == LadderMode.Seating
                ? Safe(x.GrpMean, nGrpM) + Safe(x.GrpRange, nGrpR)
                : Safe(x.GrpMean, nGrpM);
            double score =
                w.Velocity * Safe(x.VelRange, nVel) +
                w.Poi * Safe(x.PoiRange, nPoi) +
                w.Group * groupTerm +
                w.Sd * Safe(x.SdMean, nSd);
            if (score < bestS) { bestS = score; bestC = x; }
        }
        if (bestC is { } bc)
            r.BestNode = Node(xs, bc, bestS, "weighted "
                + (mode == LadderMode.Seating ? "group plateau + POI stability" : "MV flatness + POI stability + group + SD"));

        return r;
    }

    public static string BuildReport(LadderResult r, string headline)
    {
        bool seat = r.Mode == LadderMode.Seating;
        string xh = seat ? $"seat({r.XUnit})" : "charge";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(headline);
        sb.AppendLine(new string('-', headline.Length));
        sb.AppendLine();
        sb.AppendLine($"{xh,7}  n   MV      SD    ES   | dist  POI-Y   grpMR  vert  (MOA)");
        foreach (var x in r.Rows)
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,7:0.0##} {1,3} {2,7:0.0} {3,5:0.0} {4,5:0.0} | {5,4:0} {6,7:0.00} {7,6:0.00} {8,5:0.00}",
                x.X, x.HasVel ? x.VelN : 0, x.MeanMps, x.SdMps, x.EsMps,
                x.HasTarget ? x.DistanceM : 0, x.PoiYMoa, x.MeanRadiusMoa, x.VertSpreadMoa));
        sb.AppendLine();
        Line(sb, seat ? "Group-size plateau" : "Velocity flat-spot (Satterlee)", r.PrimaryNode, r.XUnit);
        Line(sb, "Vertical-POI node", r.PoiNode, r.XUnit);
        Line(sb, "Recommended node (weighted)", r.BestNode, r.XUnit);
        if (r.Warnings.Count > 0)
        {
            sb.AppendLine();
            foreach (var wn in r.Warnings) sb.AppendLine("! " + wn);
        }
        sb.AppendLine();
        sb.AppendLine("Method: MOA target coordinates from Ballistic-X" + (seat ? "." : ", velocities from Athlon Rangecraft."));
        sb.AppendLine("SD is population (n). Confirm any node with a fresh confirmation group before committing.");
        return sb.ToString();

        static void Line(System.Text.StringBuilder s, string label, NodeWindow? n, string unit)
            => s.AppendLine(n is null
                ? $"{label}: n/a"
                : string.Format(CultureInfo.InvariantCulture, "{0}: {1:0.0##}-{2:0.0##} {3}  (center {4:0.0##})  -- {5}",
                    label, n.Low, n.High, unit, n.Center, n.Basis));
    }

    // ---- helpers ---------------------------------------------------------
    private readonly record struct Win(int Lo, int Hi, double VelRange, double PoiRange, double GrpMean, double GrpRange, double SdMean);

    private static NodeWindow Node(List<LadderStep> xs, Win x, string basis) => Node(xs, x, 0, basis);
    private static NodeWindow Node(List<LadderStep> xs, Win x, double score, string basis) => new()
    {
        Low = xs[x.Lo].X,
        High = xs[x.Hi].X,
        Center = xs[(x.Lo + x.Hi) / 2].X,
        Score = score,
        Basis = basis,
    };
    private static int Count(List<LadderStep> xs, Win x, int win, Func<LadderStep, bool> p) => xs.Skip(x.Lo).Take(win).Count(p);
    private static double Range(IEnumerable<double> v) { var l = v.ToList(); return l.Count == 0 ? 0 : l.Max() - l.Min(); }
    private static double Mean(IEnumerable<double> v) { var l = v.ToList(); return l.Count == 0 ? 0 : l.Average(); }
    private static double Norm(IEnumerable<double> v) { var l = v.ToList(); double m = l.Count == 0 ? 0 : l.Max(); return m > 0 ? m : 1; }
    private static double Safe(double val, double norm) => norm > 0 ? val / norm : 0;
}
