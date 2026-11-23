namespace GrtReloadingToolkit.Cartridges;

/// <summary>One loaded cartridge with its measures (keys as in <see cref="ShotSample.Measures"/>).</summary>
public sealed record CartridgeInput(
    int Number, string Mark, IReadOnlyDictionary<string, double> Measures, bool OutOfSpec = false, string Target = "");

/// <summary>A cartridge with the velocity the model expects of it, relative to the median cartridge of the lot.</summary>
public sealed class CartridgeEstimate
{
    public required CartridgeInput Cartridge { get; init; }
    /// <summary>m/s above (+) or below (−) the lot's median cartridge.</summary>
    public double Estimate { get; set; }
    /// <summary>Standard error of that estimate, from the standard errors of the sensitivities.</summary>
    public double EstimateSe { get; set; }
    /// <summary>The model's measures this cartridge does not carry: without them it has no estimate.</summary>
    public List<string> Missing { get; } = new();
    /// <summary>Where the plan put it: the target ("1", "2"…), "P" for a proving shot, "R" for reserve.</summary>
    public string Assigned { get; set; } = "R";
    public int Order { get; set; }
    public int SeriesNumber { get; set; }
    public int Number => Cartridge.Number;
}

public enum PlanMode { Race, Training }

public sealed class PlanGroup
{
    public required string Name { get; init; }
    public required IReadOnlyList<CartridgeEstimate> Cartridges { get; init; }
    /// <summary>The SD the group should show: the spread of its estimates plus what the model leaves unexplained.</summary>
    public double PredictedSd { get; init; }
    /// <summary>Only the part that comes from the cartridges.</summary>
    public double CartridgeSd { get; init; }
    public double From { get; init; }
    public double To { get; init; }
}

public sealed class CartridgePlan
{
    public PlanMode Mode { get; init; }
    public string? Error { get; init; }
    public IReadOnlyList<PlanGroup> Groups { get; init; } = Array.Empty<PlanGroup>();
    /// <summary>Cartridges left out of the plan (out of spec in a race, or without the measures of the model).</summary>
    public IReadOnlyList<CartridgeEstimate> Excluded { get; init; } = Array.Empty<CartridgeEstimate>();
    /// <summary>The SD the same cartridges would show if they were shot in random order.</summary>
    public double SdAtRandom { get; init; }
    public double CartridgeSdAtRandom { get; init; }
    public double Median { get; init; }
}

/// <summary>
/// From the model to a shooting plan: the velocity each cartridge should have, and which cartridges go to which target (race) or in which
/// order (training). The estimate is always relative to the median cartridge of the lot, so the plan needs no knowledge of the absolute
/// velocity. A port of "Analisi DS", checked against it on the same data.
/// </summary>
public static class CartridgePlanner
{
    public static (List<CartridgeEstimate> Estimates, Dictionary<string, double?> Medians) Predict(
        IEnumerable<CartridgeInput> cartridges, VelocityModelResult model)
    {
        var terms = model.Significant;
        var list = cartridges.Select(c => new CartridgeEstimate { Cartridge = c }).ToList();
        var medians = new Dictionary<string, double?>();
        foreach (var t in terms)
        {
            var vals = list.Where(x => x.Cartridge.Measures.ContainsKey(t.Key)).Select(x => x.Cartridge.Measures[t.Key]).ToList();
            medians[t.Key] = vals.Count > 0 ? Median(vals) : null;
        }
        foreach (var x in list)
        {
            double s = 0, se2 = 0;
            foreach (var t in terms)
            {
                if (!x.Cartridge.Measures.TryGetValue(t.Key, out double v) || medians[t.Key] is not { } med) { x.Missing.Add(t.Key); continue; }
                double dx = v - med;
                s += t.Slope * dx;
                se2 += (t.Se * dx) * (t.Se * dx);
            }
            x.Estimate = s;
            x.EstimateSe = Math.Sqrt(se2);
        }
        return (list, medians);
    }

    /// <param name="targets">Race: how many targets (cards) to shoot.</param>
    /// <param name="shotsPerTarget">Race: shots on each target (the "barilotti" of the card).</param>
    /// <param name="proving">Race: proving shots, taken from both ends of the speed range.</param>
    /// <param name="perSeries">Training: cartridges per series.</param>
    /// <param name="residualSd">What the model leaves unexplained (<see cref="VelocityModelResult.SdResidual"/>).</param>
    /// <param name="returns">Race: how many returns (rientri) are shot after the targets; each is a group of its own.</param>
    /// <param name="shotsPerReturn">Race: shots in each return.</param>
    public static CartridgePlan Compose(IReadOnlyList<CartridgeEstimate> all, PlanMode mode, int targets = 3, int shotsPerTarget = 10,
        int proving = 5, int perSeries = 5, double residualSd = 0, int returns = 0, int shotsPerReturn = 10)
    {
        // in a race the out-of-spec cartridges stay out; in training they are shot too: they put the model to the test
        Func<CartridgeEstimate, bool> use = mode == PlanMode.Training
            ? x => x.Missing.Count == 0
            : x => !x.Cartridge.OutOfSpec && x.Missing.Count == 0;
        var ok = all.Where(use).ToList();
        var excluded = all.Where(x => !use(x)).ToList();
        if (ok.Count == 0)
            return new CartridgePlan { Mode = mode, Error = Tr.T("No usable cartridge in the lot (all out of spec or without the measures of the model).") };

        double med = Median(ok.Select(x => x.Estimate).ToList());
        foreach (var x in all) { x.Assigned = "R"; x.Order = 0; x.SeriesNumber = 0; }

        var groups = new List<(string Name, List<CartridgeEstimate> Items)>();
        if (mode == PlanMode.Race)
        {
            int n = targets * shotsPerTarget + returns * shotsPerReturn + proving;
            if (ok.Count < n)
                return new CartridgePlan
                {
                    Mode = mode,
                    Error = returns == 0
                        ? Tr.F("{0} cartridges are needed ({1} targets of {2} plus {3} proving): the lot has {4} usable.", n, targets, shotsPerTarget, proving, ok.Count)
                        : Tr.F("{0} cartridges are needed ({1} targets of {2}, {3} returns of {4} and {5} proving): the lot has {6} usable.", n, targets, shotsPerTarget, returns, shotsPerReturn, proving, ok.Count),
                };
            var sel = ok.OrderBy(x => Math.Abs(x.Estimate - med)).Take(n).OrderBy(x => x.Estimate).ToList();   // the ones nearest the median
            int lo = 0, hi = sel.Count - 1;
            var prov = new List<CartridgeEstimate>();
            for (int i = 0; i < proving; i++)
            {
                if (i % 2 == 0) { prov.Add(sel[lo]); lo++; }
                else { prov.Add(sel[hi]); hi--; }
            }
            var core = sel.Skip(lo).Take(hi - lo + 1).ToList();
            // The groups, targets first and then the returns, each with its own size. The sorted cartridges are cut into blocks of those sizes; among the
            // blocks of one size the most uniform goes to the first group of that size (so to target 1, and the returns get what is left).
            var sizes = Enumerable.Repeat(shotsPerTarget, targets).Concat(Enumerable.Repeat(shotsPerReturn, returns)).ToList();
            var cut = new List<List<CartridgeEstimate>>();
            int at = 0;
            foreach (int size in sizes) { cut.Add(core.Skip(at).Take(size).ToList()); at += size; }
            var blocks = new List<CartridgeEstimate>[sizes.Count];
            foreach (int size in sizes.Distinct())
            {
                var slots = Enumerable.Range(0, sizes.Count).Where(i => sizes[i] == size).ToList();
                var ranked = slots.Select(i => cut[i]).OrderBy(b => b[^1].Estimate - b[0].Estimate).ToList();
                for (int k = 0; k < slots.Count; k++) blocks[slots[k]] = ranked[k];
            }
            for (int gi = 0; gi < blocks.Length; gi++)
            {
                bool isReturn = gi >= targets;
                int number = isReturn ? gi - targets + 1 : gi + 1;
                var b = blocks[gi].OrderBy(x => x.Number).ToList();
                foreach (var x in b) x.Assigned = isReturn ? "r" + number : number.ToString();
                groups.Add(((isReturn ? "return " : "target ") + number, b));
            }
            foreach (var x in prov) x.Assigned = "P";
            groups.Add(("proving shots", prov.OrderBy(x => x.Number).ToList()));
        }
        else
        {
            var s = ok.OrderBy(x => x.Estimate).ToList();
            var order = new List<CartridgeEstimate>();
            int lo = 0, hi = s.Count - 1, k = 0;
            while (lo <= hi)
            {
                var pair = lo != hi ? new List<CartridgeEstimate> { s[lo], s[hi] } : new List<CartridgeEstimate> { s[lo] };
                if (k % 2 != 0) pair.Reverse();                  // alternate slow and fast so that barrel warming is not mistaken for the measures
                order.AddRange(pair);
                lo++; hi--; k++;
            }
            for (int i = 0; i < order.Count; i++)
            {
                order[i].Order = i + 1;
                order[i].SeriesNumber = i / perSeries + 1;
                order[i].Assigned = string.IsNullOrEmpty(order[i].Cartridge.Target) ? "R" : order[i].Cartridge.Target;
            }
            int last = order[^1].SeriesNumber;
            for (int si = 1; si <= last; si++) groups.Add(($"series {si}", order.Where(x => x.SeriesNumber == si).ToList()));
        }

        (double Total, double Part) SdPred(IReadOnlyList<CartridgeEstimate> xs)
        {
            double part = xs.Count > 1 ? SampleSd(xs.Select(x => x.Estimate).ToList()) : 0;
            return (Math.Sqrt(part * part + residualSd * residualSd), part);
        }
        var (totAll, partAll) = SdPred(ok);
        var output = groups.Select(g =>
        {
            var (tot, part) = SdPred(g.Items);
            return new PlanGroup
            {
                Name = g.Name, Cartridges = g.Items, PredictedSd = tot, CartridgeSd = part,
                From = g.Items.Min(x => x.Estimate), To = g.Items.Max(x => x.Estimate),
            };
        }).ToList();
        return new CartridgePlan { Mode = mode, Groups = output, Excluded = excluded, SdAtRandom = totAll, CartridgeSdAtRandom = partAll, Median = med };
    }

    internal static double Median(IReadOnlyList<double> v)
    {
        var s = v.OrderBy(a => a).ToList();
        int n = s.Count;
        return n % 2 == 1 ? s[n / 2] : (s[n / 2 - 1] + s[n / 2]) / 2;
    }

    internal static double SampleSd(IReadOnlyList<double> v)
    {
        double m = v.Average();
        return Math.Sqrt(v.Sum(a => (a - m) * (a - m)) / (v.Count - 1));
    }
}
