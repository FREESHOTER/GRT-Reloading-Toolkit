using System.Globalization;

namespace GrtReloadingToolkit.TempCoeff;

public sealed class TempPoint
{
    public string File { get; set; } = "";
    public double ChargeGr { get; set; }
    public double TempC { get; set; }
    public int N { get; set; }
    public double MeanMps { get; set; }
    public double SdMps { get; set; }
    public bool Use { get; set; } = true;
}

public sealed class TempCoeffResult
{
    public const double NormalC = 21.0;

    public double? MvNormal { get; init; }
    public double? Tcc { get; init; }          // dBa/dT below 21°C  (GRT units)
    public double? Tch { get; init; }          // dBa/dT above 21°C
    public double? ColdSlopeMps { get; init; } // measured m/s per °C, cold side
    public double? HotSlopeMps { get; init; }  // measured m/s per °C, hot side
    public double BaNormal { get; init; }
    public string PowderName { get; init; } = "powder";
    public List<string> Notes { get; } = new();
    public List<TempPoint> Points { get; } = new();

    /// <summary>
    /// tcc/tch per GRT's formalism: coefficient = ΔBa/ΔT referenced to the normal (21°C) point,
    /// with Ba(T) ≈ Ba_normal·(MV(T)/MV_normal)² (first-order, MV ∝ √Ba).
    /// </summary>
    public static TempCoeffResult Fit(IEnumerable<TempPoint> pts, double baNormal, string powder)
    {
        var p = pts.Where(x => x.Use && x.MeanMps > 0).OrderBy(x => x.TempC).ToList();
        var notes = new List<string>();

        if (p.Count < 2)
        {
            var bad = new TempCoeffResult { BaNormal = baNormal, PowderName = powder };
            bad.Notes.Add("need at least 2 usable strings at different temperatures");
            bad.Points.AddRange(p);
            return bad;
        }

        // MV at the normal temperature: use a point within ±2°C of 21, else linear-fit to 21°C.
        double mvNormal;
        var near = p.OrderBy(x => Math.Abs(x.TempC - NormalC)).First();
        if (Math.Abs(near.TempC - NormalC) <= 2.0)
        {
            mvNormal = near.MeanMps;
            if (near.TempC != NormalC) notes.Add($"normal point is {near.TempC:0.#}°C, not 21°C — used its MV as the 21°C reference");
        }
        else
        {
            var (a, b) = LinFit(p.Select(x => x.TempC).ToArray(), p.Select(x => x.MeanMps).ToArray());
            mvNormal = a + b * NormalC;
            notes.Add($"no string near 21°C — extrapolated MV(21°C) ≈ {mvNormal:0.0} m/s from a linear fit");
        }

        double Ba(double mv) => baNormal * (mv / mvNormal) * (mv / mvNormal);

        var cold = p.Where(x => x.TempC < NormalC - 0.5).ToList();
        var hot = p.Where(x => x.TempC > NormalC + 0.5).ToList();

        double? tcc = null, coldMps = null;
        if (cold.Count > 0 && baNormal > 0)
        {
            tcc = cold.Average(x => (baNormal - Ba(x.MeanMps)) / (NormalC - x.TempC));
            coldMps = cold.Average(x => (mvNormal - x.MeanMps) / (NormalC - x.TempC));
        }
        else if (cold.Count == 0) notes.Add("no cold string (< 21°C) — tcc not fitted");

        double? tch = null, hotMps = null;
        if (hot.Count > 0 && baNormal > 0)
        {
            tch = hot.Average(x => (Ba(x.MeanMps) - baNormal) / (x.TempC - NormalC));
            hotMps = hot.Average(x => (x.MeanMps - mvNormal) / (x.TempC - NormalC));
        }
        else if (hot.Count == 0) notes.Add("no hot string (> 21°C) — tch not fitted");

        if (baNormal <= 0) notes.Add("the load has no propellant Ba — can only report the raw m/s per °C");

        var r = new TempCoeffResult
        {
            MvNormal = mvNormal, Tcc = tcc, Tch = tch,
            ColdSlopeMps = coldMps, HotSlopeMps = hotMps,
            BaNormal = baNormal, PowderName = powder,
        };
        r.Notes.AddRange(notes);
        r.Points.AddRange(p);
        return r;
    }

    private static (double a, double b) LinFit(double[] x, double[] y)
    {
        int n = x.Length;
        double sx = x.Sum(), sy = y.Sum(), sxy = 0, sxx = 0;
        for (int i = 0; i < n; i++) { sxy += x[i] * y[i]; sxx += x[i] * x[i]; }
        double denom = n * sxx - sx * sx;
        double b = Math.Abs(denom) < 1e-9 ? 0 : (n * sxy - sx * sy) / denom;
        return ((sy - b * sx) / n, b);
    }

    public string BuildReport(string headline)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(headline);
        sb.AppendLine(new string('-', headline.Length));
        sb.AppendLine();
        sb.AppendLine("temp C   n     MV      SD    charge");
        foreach (var x in Points)
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,6:0.0} {1,3} {2,8:0.0} {3,6:0.0}   {4:0.0##}",
                x.TempC, x.N, x.MeanMps, x.SdMps, x.ChargeGr));
        sb.AppendLine();
        if (MvNormal is { } mvn) sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "MV at 21°C: {0:0.0} m/s   (powder Ba = {1:0.######})", mvn, BaNormal));
        if (ColdSlopeMps is { } c) sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "cold side: {0:+0.00;-0.00} m/s per °C", c));
        if (HotSlopeMps is { } h) sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "hot side : {0:+0.00;-0.00} m/s per °C", h));
        sb.AppendLine();
        if (Tcc is { } tcc) sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "tcc = {0:0.########}   (GRT cold coefficient, ΔBa/ΔT below 21°C)", tcc));
        if (Tch is { } tch) sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "tch = {0:0.########}   (GRT hot coefficient, ΔBa/ΔT above 21°C)", tch));
        if (Tcc is null && Tch is null) sb.AppendLine("no coefficients fitted — see notes.");
        if (Notes.Count > 0) { sb.AppendLine(); foreach (var n in Notes) sb.AppendLine("! " + n); }
        sb.AppendLine();
        sb.AppendLine("Method: Ba(T) ≈ Ba_normal·(MV(T)/MV_21)² per GRT formalism (formalism.txt). First-order.");
        sb.AppendLine("Best with 3 strings: cold (~-10°C), normal (21°C), hot (~+40°C), same charge & barrel.");
        return sb.ToString();
    }
}
