using System.Globalization;
using GrtPluginKit.Grt;

namespace GrtReloadingToolkit.Cal;

public sealed class CalPoint
{
    public double ChargeGr { get; set; }
    public double MeasMps { get; set; }
    public double SimMps { get; set; }

    /// <summary>Sim MV at the SAME charge with the propellant's "a0" (prog/deg shape coefficient)
    /// nudged by a small delta -- the second data series needed to fit a shape correction
    /// alongside Ba's scale correction. 0 if that sweep hasn't been captured for this point.</summary>
    public double SimMpsPertA0 { get; set; }
    public bool HasPert => SimMpsPertA0 > 0;

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
    public int NPert => Valid().Count(p => p.HasPert);

    public sealed record ShapeFit(double NewBa, double NewA0, double DeltaBa, double DeltaA0, IReadOnlyList<string> Notes)
    {
        public bool Ok => Notes.Count == 0;
    }

    /// <summary>
    /// Fits BOTH Ba (scale) and a0 (the propellant's "prog/deg" burn-shape coefficient) so the
    /// model matches measured MV across every captured charge at once -- the two-parameter
    /// extension of <see cref="BaMultiplier"/>'s single-parameter fit, for when
    /// <see cref="Consistent"/> is false (the offset varies with charge, meaning the burn SHAPE is
    /// off, not just the scale). Chose a0 over the propellant's "k" (ratio of specific heats) as
    /// the second knob: k is a thermochemical property of the combustion gases, not a curve-fitting
    /// parameter -- bending it to match MV would leave Pmax and burn-time predictions (which is
    /// exactly what OBT/BLT depend on) wrong in ways this MV-only fit can't see. a0 is what
    /// GRT's own doku (formalism.txt, "form functions") describes as the coefficient meant to be
    /// adjusted to match a measured burn curve.
    ///
    /// Linearized (Gauss-Newton, one step): dMV/dBa is estimated analytically from the same
    /// MV~sqrt(Ba) relationship <see cref="BaMultiplier"/> already assumes (no extra simulation
    /// needed for that column); dMV/da0 needs one extra simulated MV per charge, at a0 nudged by
    /// <paramref name="deltaA0"/> (<see cref="CalPoint.SimMpsPertA0"/>). Solved by 2x2 least
    /// squares (normal equations) across every point that has both series captured.
    /// </summary>
    public ShapeFit? FitBaAndA0(double baOld, double a0Old, double deltaA0)
    {
        var pts = Valid().Where(p => p.HasPert).ToList();
        if (pts.Count < 2) return null;                    // 2 unknowns need at least 2 equations

        var notes = new List<string>();
        if (pts.Count == 2)
            notes.Add("only 2 points with shape-fit data -- an exact fit with no redundancy to check consistency. Capture a 3rd charge if you can.");

        double sbb = 0, sba = 0, saa = 0, sbr = 0, sar = 0;
        foreach (var p in pts)
        {
            double dBa = p.SimMps / (2.0 * baOld);              // d(MV)/d(Ba), from MV ~ sqrt(Ba)
            double dA0 = (p.SimMpsPertA0 - p.SimMps) / deltaA0; // d(MV)/d(a0), numeric
            double r = p.MeasMps - p.SimMps;                    // residual MV still unexplained
            sbb += dBa * dBa; sba += dBa * dA0; saa += dA0 * dA0;
            sbr += dBa * r; sar += dA0 * r;
        }

        double det = sbb * saa - sba * sba;
        if (Math.Abs(det) < 1e-9)
        {
            notes.Add("Ba and a0 affect these charges too similarly to separate reliably -- spread the measured charges further apart (or add more) and try again.");
            return new ShapeFit(baOld, a0Old, 0, 0, notes);
        }

        double deltaBa = (sbr * saa - sar * sba) / det;
        double deltaA0Sol = (sbb * sar - sba * sbr) / det;
        double newBa = baOld + deltaBa;
        double newA0 = a0Old + deltaA0Sol;
        if (newBa <= 0)
        {
            notes.Add($"fit produced a non-physical Ba <= 0 ({newBa:0.####}) -- discard; the data is likely too noisy or the charges too close together.");
            return new ShapeFit(baOld, a0Old, 0, 0, notes);
        }
        return new ShapeFit(newBa, newA0, deltaBa, deltaA0Sol, notes);
    }

    public string BuildReport(string headline, double? baOld, string powderName)
    {
        // Written into GRT as a note, so it reads in GRT's units, charges included. The window's
        // own charge column is editable and is left in grains, which is what its header says.
        var gu = GrtUnits.Current;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(headline);
        sb.AppendLine(new string('-', headline.Length));
        sb.AppendLine();
        sb.AppendLine($"charge in {gu.ChargeUnitName}, velocities in {gu.VelocityUnitName}");
        sb.AppendLine("charge   meas MV   sim MV     d MV    d %");
        foreach (var p in Points.OrderBy(p => p.ChargeGr))
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,6} {1,9:0.0} {2,9:0.0} {3,8:+0.0;-0.0;0.0} {4,7:+0.00;-0.00;0.00}",
                gu.ChargeValue(p.ChargeGr).ToString(gu.ChargeFormat, CultureInfo.InvariantCulture),
                gu.VelocityValue(p.MeasMps), gu.VelocityValue(p.SimMps),
                gu.VelocityValue(p.DeltaMps), p.DeltaPct));
        sb.AppendLine();
        if (N == 0) { sb.AppendLine("no valid points captured."); return sb.ToString(); }

        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "mean offset: {0:+0.0;-0.0;0.0} {1}  ({2:+0.00;-0.00;0.00} %)   over {3} point(s), spread {4:0.00} %",
            gu.VelocityValue(MeanDeltaMps), gu.VelocityUnitName, MeanDeltaPct, N, SdDeltaPct));
        sb.AppendLine(N >= 2
            ? (Consistent ? "-> consistent across charges: a clean barrel-vs-model offset."
                          : "-> offset varies with charge: the powder model shape is off, not just a scale."
                            + (N >= 3 ? " Try 'Capture shape-fit sweep (a0)' below." : " Capture a 3rd+ charge, then try the a0 shape fit."))
            : "-> capture 2+ charges to tell a scale error from a shape error.");
        sb.AppendLine();
        if (baOld is { } ba)
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "suggested Ba for {0}: {1:0.######}  (was {2:0.######}, x{3:0.####})  -- first-order, MV scales as sqrt(Ba)",
                powderName, ba * BaMultiplier, ba, BaMultiplier));
        else
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "no Ba in the load; apply the offset mentally: GRT runs {0:0.0} {1} {2} for this barrel.",
                Math.Abs(gu.VelocityValue(MeanDeltaMps)), gu.VelocityUnitName, MeanDeltaMps >= 0 ? "slow" : "fast"));
        sb.AppendLine();
        sb.AppendLine("Method: measured MV = Athlon Rangecraft; sim MV = GRT Get_TabResults for the charge set in GRT.");
        sb.AppendLine("Re-verify after applying, and keep this per barrel + powder lot.");
        return sb.ToString();
    }

    public string BuildShapeReport(ShapeFit fit, double baOld, double a0Old, string powderName)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Shape fit (Ba + a0)");
        sb.AppendLine(new string('-', 19));
        if (!fit.Ok)
        {
            foreach (var n in fit.Notes) sb.AppendLine("! " + n);
            return sb.ToString();
        }
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "suggested Ba for {0}: {1:0.######}  (was {2:0.######}, delta {3:+0.######;-0.######})",
            powderName, fit.NewBa, baOld, fit.DeltaBa));
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "suggested a0 for {0}: {1:0.####}  (was {2:0.####}, delta {3:+0.####;-0.####})",
            powderName, fit.NewA0, a0Old, fit.DeltaA0));
        foreach (var n in fit.Notes) sb.AppendLine("! " + n);
        sb.AppendLine("Fit over " + NPert + " charge(s) with both baseline and a0-perturbed sim MV captured.");
        sb.AppendLine("a0 is the propellant's prog/deg burn-shape coefficient (GRT doku: \"form functions\") --");
        sb.AppendLine("not the same as \"k\" (ratio of specific heats), which is a thermochemical property, not a fit knob.");
        return sb.ToString();
    }
}
