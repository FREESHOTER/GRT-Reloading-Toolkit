using System.Globalization;

namespace GrtReloadingToolkit.Cal;

public sealed class CalPoint
{
    public double ChargeGr { get; set; }
    public double MeasMps { get; set; }
    public double SimMps { get; set; }

    public bool Valid => MeasMps > 0 && SimMps > 0;
    public double DeltaMps => MeasMps - SimMps;
    public double DeltaPct => SimMps > 0 ? 100.0 * (MeasMps - SimMps) / SimMps : 0;
    public double Ratio => SimMps > 0 ? MeasMps / SimMps : 1;
}

public sealed class CalResult
{
    public List<CalPoint> Points { get; } = new();
    public int N => Points.Count(p => p.Valid);
    public double MeanDeltaMps => Valid().Select(p => p.DeltaMps).DefaultIfEmpty(0).Average();
    public double MeanDeltaPct => Valid().Select(p => p.DeltaPct).DefaultIfEmpty(0).Average();
    public double SdDeltaPct
    {
        get
        {
            var d = Valid().Select(p => p.DeltaPct).ToList();
            if (d.Count < 2) return 0;
            double m = d.Average();
            return Math.Sqrt(d.Sum(x => (x - m) * (x - m)) / d.Count);
        }
    }
    /// <summary>First-order Ba scale so the model matches measured MV: Ba·(meas/sim)².</summary>
    public double BaMultiplier => Math.Pow(Valid().Select(p => p.Ratio).DefaultIfEmpty(1).Average(), 2);
    public bool Consistent => N >= 2 && SdDeltaPct <= 1.0;

    private IEnumerable<CalPoint> Valid() => Points.Where(p => p.Valid);

    public string BuildReport(string headline, double? baOld, string powderName)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(headline);
        sb.AppendLine(new string('-', headline.Length));
        sb.AppendLine();
        sb.AppendLine("charge   meas MV   sim MV    d m/s    d %");
        foreach (var p in Points.OrderBy(p => p.ChargeGr))
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,6:0.0} {1,9:0.0} {2,9:0.0} {3,8:+0.0;-0.0;0.0} {4,7:+0.00;-0.00;0.00}",
                p.ChargeGr, p.MeasMps, p.SimMps, p.DeltaMps, p.DeltaPct));
        sb.AppendLine();
        if (N == 0) { sb.AppendLine("no valid points captured."); return sb.ToString(); }

        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "mean offset: {0:+0.0;-0.0;0.0} m/s  ({1:+0.00;-0.00;0.00} %)   over {2} point(s), spread {3:0.00} %",
            MeanDeltaMps, MeanDeltaPct, N, SdDeltaPct));
        sb.AppendLine(N >= 2
            ? (Consistent ? "-> consistent across charges: a clean barrel-vs-model offset."
                          : "-> offset varies with charge: the powder model shape is off, not just a scale.")
            : "-> capture 2+ charges to tell a scale error from a shape error.");
        sb.AppendLine();
        if (baOld is { } ba)
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "suggested Ba for {0}: {1:0.######}  (was {2:0.######}, x{3:0.####})  -- first-order, MV scales as sqrt(Ba)",
                powderName, ba * BaMultiplier, ba, BaMultiplier));
        else
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "no Ba in the load; apply the offset mentally: GRT runs {0:0.0} m/s {1} for this barrel.",
                Math.Abs(MeanDeltaMps), MeanDeltaMps >= 0 ? "slow" : "fast"));
        sb.AppendLine();
        sb.AppendLine("Method: measured MV = Athlon Rangecraft; sim MV = GRT Get_TabResults for the charge set in GRT.");
        sb.AppendLine("Re-verify after applying, and keep this per barrel + powder lot.");
        return sb.ToString();
    }
}
