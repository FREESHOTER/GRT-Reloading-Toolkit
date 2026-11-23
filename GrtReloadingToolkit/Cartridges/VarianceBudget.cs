using GrtPluginKit.Analysis;

namespace GrtReloadingToolkit.Cartridges;

/// <summary>One item of the budget: how much of the variance of the velocity it carries.</summary>
public sealed record BudgetPart(string Kind, string? Key, double SumOfSquares, double Share, double SdMps, double? P)
{
    /// <summary>Under 5 % that it is chance.</summary>
    public bool Proven => P is < 0.05;
}

/// <summary>What one proven measure of the cartridges costs, and what correcting it would give.</summary>
public sealed record MeasureAdvice(string Key, double SdMps, double Share, double Slope, double Min, double Max, double SdIfHalved, double SdIfRemoved);

public sealed record SeriesRow(int SeriesId, int N, double Mean, double Sd, (double Lo, double Hi)? Interval, double Es);

public sealed record TargetCostRow(double DistanceM, double MmPerMs, double SdMm, double SdMoa);

public sealed record TrendInfo(double SlopePerLogShot, double P, double FirstShotOffsetMps);

public sealed class VarianceBudgetResult
{
    public int N { get; init; }
    public int SeriesCount { get; init; }
    public double Mean { get; init; }
    /// <summary>All shots together, around the overall mean.</summary>
    public double SdTotal { get; init; }
    /// <summary>Inside the series, with each series' own mean taken out: the SD that matters for the cartridges.</summary>
    public double SdWithin { get; init; }
    public (double Lo, double Hi) SdWithinInterval { get; init; }
    public required IReadOnlyList<SeriesRow> Series { get; init; }
    /// <summary>Biggest first. The shares add up to the whole variance (the SD² of all the shots), so the SDs of the items do not add up.</summary>
    public required IReadOnlyList<BudgetPart> Parts { get; init; }
    /// <summary>What would be left of the total SD if everything that can be corrected were: the cartridges and the barrel.</summary>
    public double SdRest { get; init; }
    public TrendInfo? Trend { get; init; }
    public VelocityModelResult? Model { get; init; }
    public required IReadOnlyList<MeasureAdvice> Advice { get; init; }
    /// <summary>The SD the shooter would like to show next, and the shots per variant needed to prove the improvement.</summary>
    public double TargetSd { get; init; }
    public int? ShotsNeeded { get; init; }
    public required IReadOnlyList<TargetCostRow> TargetCost { get; init; }
}

/// <summary>
/// Where the SD of a charge comes from: the change of velocity between one series and the next, the first shots of a series, each
/// measure of the cartridges that is proven to matter, the chronograph, and what is left (cartridges and barrel). Plus what the SD costs
/// on the target and how many shots it takes to prove an improvement. A port of the chapter "Analisi della SD" of Analisi DS.
/// </summary>
public static class VarianceBudget
{
    public const double SdGood = 2.0, SdAcceptable = 4.0;
    /// <summary>1 minute of angle at 100 m, in mm.</summary>
    public const double MoaMmAt100 = 29.0888;
    public const int MinShots = 5;

    /// <summary>
    /// Millimetres more of drop for every m/s less, at <paramref name="distanceM"/> (no air: the real figure is higher, more so at 300 m).
    /// </summary>
    public static double MmPerMps(double distanceM, double velocityMps)
    {
        double t = distanceM / velocityMps;
        return 2 * 0.5 * 9.81 * t * t * 1000 / velocityMps;
    }

    /// <param name="samples">Every shot, with the series it belongs to (a chronograph string). Series of fewer than 2 shots are left out.</param>
    /// <param name="chronographSd">The chronograph's own precision (SD, m/s); 0 leaves it out of the budget.</param>
    /// <param name="sessionDistanceM">The distance of the shooting, added to 100, 200 and 300 m in the target-cost table.</param>
    public static VarianceBudgetResult? Analyze(IReadOnlyList<ShotSample> samples, double chronographSd = 0, double sessionDistanceM = 100)
    {
        var data = samples.Where(s => double.IsFinite(s.VelocityMps)).GroupBy(s => s.SeriesId)
            .Where(g => g.Count() >= 2).SelectMany(g => g).ToList();
        var bySeries = data.GroupBy(s => s.SeriesId).Select(g => (Id: g.Key, V: g.Select(x => x.VelocityMps).ToArray())).ToList();
        if (bySeries.Count == 0) return null;
        var all = bySeries.SelectMany(s => s.V).ToArray();
        int n = all.Length;
        if (n < MinShots) return null;

        double mean = all.Average();
        double ssTot = all.Sum(v => (v - mean) * (v - mean));
        double sdTot = Math.Sqrt(ssTot / (n - 1));
        double ssW = bySeries.Sum(s => { double m = s.V.Average(); return s.V.Sum(v => (v - m) * (v - m)); });
        int dfW = bySeries.Sum(s => s.V.Length - 1);
        double sdW = dfW > 0 ? Math.Sqrt(ssW / dfW) : sdTot;
        int k = bySeries.Count > 1 ? dfW : n - 1;
        double loW = sdW * Math.Sqrt(k / StatsExtra.ChiSquareQuantile(0.95, k));
        double hiW = sdW * Math.Sqrt(k / StatsExtra.ChiSquareQuantile(0.05, k));

        var rows = bySeries.Select(s =>
        {
            double m = s.V.Average();
            double sd = Math.Sqrt(s.V.Sum(v => (v - m) * (v - m)) / (s.V.Length - 1));
            return new SeriesRow(s.Id, s.V.Length, m, sd, StatsExtra.SdInterval(sd, s.V.Length), s.V.Max() - s.V.Min());
        }).ToList();

        var parts = new List<(string Kind, string? Key, double Ss, double? P)>();

        // the change of velocity from one series to the next
        if (bySeries.Count > 1)
        {
            double ssB = Math.Max(0, ssTot - ssW);
            int dfB = bySeries.Count - 1;
            double fStat = ssW > 0 ? (ssB / dfB) / (ssW / dfW) : double.PositiveInfinity;
            double p = double.IsInfinity(fStat) ? 0 : Math.Clamp(1 - FDist.Cdf(fStat, dfB, dfW), 0, 1);
            parts.Add(("between", null, ssB, p));
        }

        // how the velocity moves inside a series (first shots, barrel warming): against the log of the shot's place in its series
        TrendInfo? trend = null;
        var dv = bySeries.SelectMany(s => { double m = s.V.Average(); return s.V.Select(v => v - m); }).ToArray();
        var pos = bySeries.SelectMany(s => Enumerable.Range(1, s.V.Length).Select(i => Math.Log(i))).ToArray();
        if (n >= 8 && PopStd(dv) > 0 && PopStd(pos) > 0)
        {
            var reg = VelocityModel.SimpleRegression(pos, dv);
            double ssT = reg.R * reg.R * ssW;
            var firsts = bySeries.Where(s => s.V.Length >= 3).Select(s => s.V[0] - s.V.Skip(1).Average()).ToList();
            double first = firsts.Count > 0 ? firsts.Average() : 0;
            trend = new TrendInfo(reg.Slope, reg.P, first);
            parts.Add(("trend", null, ssT, reg.P));
        }

        // the measures of the cartridges that are proven, fitted together
        var model = VelocityModel.Learn(data);
        var proven = model?.Significant ?? Array.Empty<ModelTerm>();
        foreach (var t in proven) parts.Add(("measure", t.Key, t.SsFraction * ssW, t.PAdj));

        if (chronographSd > 0)
        {
            double ssCh = Math.Min(chronographSd * chronographSd * (n - 1), Math.Max(0, ssTot - parts.Sum(p => p.Ss)));
            parts.Add(("chrono", null, ssCh, null));
        }
        double ssRest = Math.Max(0, ssTot - parts.Sum(p => p.Ss));
        parts.Add(("rest", null, ssRest, null));

        var list = parts.OrderByDescending(p => p.Ss)
            .Select(p => { double share = ssTot > 0 ? p.Ss / ssTot : 0; return new BudgetPart(p.Kind, p.Key, p.Ss, share, sdTot * Math.Sqrt(share), p.P); })
            .ToList();
        double sdRest = ssTot > 0 ? sdTot * Math.Sqrt(ssRest / ssTot) : 0;

        var advice = new List<MeasureAdvice>();
        foreach (var p in list.Where(p => p.Kind == "measure"))
        {
            var t = proven.First(x => x.Key == p.Key);
            advice.Add(new MeasureAdvice(t.Key, p.SdMps, p.Share, t.Slope, t.Min, t.Max,
                Math.Sqrt(Math.Max(0, sdW * sdW - 0.75 * p.SumOfSquares / dfW)), Math.Sqrt(Math.Max(0, sdW * sdW - p.SumOfSquares / dfW))));
        }

        double target = sdW > SdAcceptable * 1.1 ? SdAcceptable : sdW * 0.7;
        var dists = new SortedSet<double> { 100, 200, 300, sessionDistanceM };
        var cost = dists.Select(d =>
        {
            double mm = MmPerMps(d, mean);
            return new TargetCostRow(d, mm, sdW * mm, sdW * mm / (d / 100 * MoaMmAt100));
        }).ToList();

        return new VarianceBudgetResult
        {
            N = n, SeriesCount = bySeries.Count, Mean = mean, SdTotal = sdTot, SdWithin = sdW, SdWithinInterval = (loW, hiW),
            Series = rows, Parts = list, SdRest = sdRest, Trend = trend, Model = model, Advice = advice,
            TargetSd = target, ShotsNeeded = StatsExtra.ShotsForSdDifference(target, sdW), TargetCost = cost,
        };
    }

    private static double PopStd(double[] v)
    {
        double m = v.Average();
        return Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / v.Length);
    }
}

/// <summary>The width a piece measure may vary over for it to put no more than a given SD into the velocity.</summary>
public sealed record ToleranceRow(string Key, double Slope, double SdOfMeasure, double SdTodayMps, double BandForOne, double BandForHalf)
{
    public double BandFor(double sdMps) => ToleranceBands.Band(Slope, sdMps);
}

public static class ToleranceBands
{
    /// <summary>
    /// Pieces spread evenly inside a band <c>w</c> wide have an SD of <c>w/√12</c>, which the measure turns into <c>|slope|·w/√12</c> of velocity:
    /// so the band that puts at most <paramref name="sdMps"/> into the velocity is <c>√12·sdMps/|slope|</c>.
    /// </summary>
    public static double Band(double slope, double sdMps) => Math.Sqrt(12) * sdMps / Math.Abs(slope);

    /// <summary>One row for each measure the model has proven; empty when none is.</summary>
    public static List<ToleranceRow> From(VelocityModelResult? model)
        => (model?.Significant ?? Array.Empty<ModelTerm>())
            .Where(t => Math.Abs(t.Slope) > 0)
            .Select(t => new ToleranceRow(t.Key, t.Slope, t.SdX, Math.Abs(t.Slope) * t.SdX, Band(t.Slope, 1.0), Band(t.Slope, 0.5)))
            .ToList();
}
