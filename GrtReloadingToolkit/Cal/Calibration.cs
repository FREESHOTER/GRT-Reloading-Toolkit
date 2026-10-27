using System.Globalization;
using GrtPluginKit.Grt;

namespace GrtReloadingToolkit.Cal;

public sealed class CalPoint
{
    public double ChargeGr { get; set; }
    public double MeasMps { get; set; }
    public double SimMps { get; set; }

    /// <summary>Sim MV at the SAME charge with the propellant's "k" (isentropic exponent) nudged by a
    /// small delta -- the second data series needed to fit k alongside Ba. 0 if that sweep hasn't been
    /// captured for this point.</summary>
    public double SimMpsPertK { get; set; }
    public bool HasPert => SimMpsPertK > 0;

    /// <summary>Sim MV at the SAME charge with the propellant's Ba AND k set to the shape fit's proposed
    /// values: what GRT itself says the proposed correction predicts, as opposed to what the straight-line
    /// fit believes it predicts. 0 until the verification sweep has been run.</summary>
    public double SimMpsVerify { get; set; }
    public bool HasVerify => SimMpsVerify > 0;

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

    /// <summary>The outcome of checking a proposed joint Ba/k correction against GRT's own simulation.</summary>
    public sealed record JointVerification(int N, double MeanOriginalMps, double MeanFitMps, double RmsOriginalMps,
        double RmsFitMps, bool Accepted, string Reason);

    /// <summary>Below this mean offset (m/s) the model already matches the chronograph to within its noise:
    /// there is nothing to correct.</summary>
    public const double NoiseFloorMps = 0.3;
    /// <summary>The verified mean offset must fall to at most this share of the original (or to the noise floor).</summary>
    public const double MaxResidualShare = 0.25;

    /// <summary>
    /// Judges the proposed correction by what GRT actually simulates with it
    /// (<see cref="CalPoint.SimMpsVerify"/>) rather than by the straight-line fit that proposed it. Accepted
    /// only if GRT's own simulation brings the mean offset from the measured velocities down to at most
    /// <see cref="MaxResidualShare"/> of the original (or inside the noise floor) without making the scatter
    /// worse. Null until every captured point has been verified.
    /// </summary>
    public JointVerification? VerifyJoint()
    {
        var all = Valid().ToList();
        var pts = all.Where(p => p.HasVerify).ToList();
        if (all.Count < 1 || pts.Count < all.Count) return null;

        double Mean(IEnumerable<double> d) => d.Average();
        double Rms(IEnumerable<double> d) { var l = d.ToList(); return Math.Sqrt(l.Sum(x => x * x) / l.Count); }
        double meanOrig = Mean(pts.Select(p => p.MeasMps - p.SimMps));
        double meanFit = Mean(pts.Select(p => p.MeasMps - p.SimMpsVerify));
        double rmsOrig = Rms(pts.Select(p => p.MeasMps - p.SimMps));
        double rmsFit = Rms(pts.Select(p => p.MeasMps - p.SimMpsVerify));

        string reason;
        bool ok;
        if (Math.Abs(meanOrig) < NoiseFloorMps)
        { ok = false; reason = Tr.T("The model already matches the measured velocities to within chronograph noise: nothing to correct."); }
        else if (Math.Abs(meanFit) > Math.Max(NoiseFloorMps, MaxResidualShare * Math.Abs(meanOrig)))
        { ok = false; reason = Tr.T("GRT's own simulation with the proposed values still leaves a clear offset from the measured velocities, so it is not offered."); }
        else if (rmsFit > 1.05 * rmsOrig + 0.05)
        { ok = false; reason = Tr.T("GRT's own simulation with the proposed values makes the charge-to-charge scatter worse, so it is not offered."); }
        else { ok = true; reason = Tr.T("GRT's own simulation confirms the correction."); }
        return new JointVerification(pts.Count, meanOrig, meanFit, rmsOrig, rmsFit, ok, reason);
    }

    public sealed record JointFit(double NewBa, double NewK, double Factor, IReadOnlyList<string> Notes)
    {
        public bool Ok => Notes.Count == 0;
        public double DeltaBa(double baOld) => NewBa - baOld;
        public double DeltaK(double kOld) => NewK - kOld;
    }

    /// <summary>
    /// GRT-style calibration: scale Ba AND k by one common factor f so the simulated velocity matches the
    /// measured one. This is what GRT's own OBT tool does (checked live on a 40.2 gr N550 load: asked to match
    /// 836.0 m/s instead of the simulated 830.1, it moved k and Ba both by exactly +0.1797 %; asked for 825.0 it
    /// moved both by -0.1552 %), so a load calibrated this way has the same Pmax, BLT and OBT nodes GRT's own
    /// tool would give it. Ba and k cannot be told apart from velocity alone -- both mostly scale it -- but a
    /// single common factor needs no such separation, which is why this is well-conditioned where an
    /// independent Ba + k (or Ba + a0) fit is not.
    ///
    /// One Gauss-Newton step: dMV/df per charge comes from the sweep with Ba and k both nudged by
    /// <paramref name="delta"/> (<see cref="CalPoint.SimMpsPertK"/>), and f - 1 is the least-squares solution over
    /// every charge. A PROPOSAL only: it is offered for writing once GRT's own simulation confirms it
    /// (<see cref="VerifyJoint"/>).
    /// </summary>
    public JointFit? FitJoint(double baOld, double kOld, double delta)
    {
        var pts = Valid().Where(p => p.HasPert).ToList();
        if (pts.Count < 1 || delta <= 0) return null;

        double sgg = 0, sgr = 0;
        foreach (var p in pts)
        {
            double g = (p.SimMpsPertK - p.SimMps) / delta;   // d(MV)/d(relative change of Ba and k together)
            double r = p.MeasMps - p.SimMps;
            sgg += g * g; sgr += g * r;
        }
        var notes = new List<string>();
        if (sgg <= 0)
        {
            notes.Add(Tr.T("the simulated velocity did not respond to the nudge of Ba and k -- capture the sweep again."));
            return new JointFit(baOld, kOld, 1.0, notes);
        }
        double d = sgr / sgg;
        double factor = 1.0 + d;
        double newBa = baOld * factor, newK = kOld * factor;
        if (Math.Abs(d) > MaxJointStep || newK < 1.05 || newK > 1.50)
        {
            notes.Add(Tr.F("the correction asks for Ba and k to move together by {0} -- larger than a believable powder-model adjustment (limit +/-{1}, k between 1.05 and 1.50). Check the measured velocities and the load before trusting any calibration.",
                d.ToString("+0.00%;-0.00%", CultureInfo.InvariantCulture), MaxJointStep.ToString("0%", CultureInfo.InvariantCulture)));
            return new JointFit(baOld, kOld, 1.0, notes);
        }
        return new JointFit(newBa, newK, factor, notes);
    }

    private const double MaxJointStep = 0.05;

    public string BuildReport(string headline, double? baOld, string powderName)
    {
        // Written into GRT as a note, so it reads in GRT's units, charges included. The window's
        // own charge column is editable and is left in grains, which is what its header says.
        var gu = GrtUnits.Current;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(headline);
        sb.AppendLine(new string('-', headline.Length));
        sb.AppendLine();
        sb.AppendLine(Tr.F("charge in {0}, velocities in {1}", gu.ChargeUnitName, gu.VelocityUnitName));
        sb.AppendLine(Tr.T("charge   meas MV   sim MV     d MV    d %"));
        foreach (var p in Points.OrderBy(p => p.ChargeGr))
            // A point whose sim MV has not been captured yet has no offset: printing meas - 0 gave a
            // meaningless "+806.6" row.
            if (p.Valid)
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,6} {1,9:0.0} {2,9:0.0} {3,8:+0.0;-0.0;0.0} {4,7:+0.00;-0.00;0.00}",
                    gu.ChargeValue(p.ChargeGr).ToString(gu.ChargeFormat, CultureInfo.InvariantCulture),
                    gu.VelocityValue(p.MeasMps), gu.VelocityValue(p.SimMps),
                    gu.VelocityValue(p.DeltaMps), p.DeltaPct));
            else
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,6} {1,9:0.0} {2,9} {3,8} {4,7}",
                    gu.ChargeValue(p.ChargeGr).ToString(gu.ChargeFormat, CultureInfo.InvariantCulture),
                    gu.VelocityValue(p.MeasMps), "-", "-", "-"));
        sb.AppendLine();
        if (N == 0) { sb.AppendLine(Tr.T("no valid points captured.")); return sb.ToString(); }

        sb.AppendLine(Tr.F("mean offset: {0} {1}  ({2} %)   over {3} point(s), spread {4} %",
            gu.VelocityValue(MeanDeltaMps).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture), gu.VelocityUnitName,
            MeanDeltaPct.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture), N, SdDeltaPct.ToString("0.00", CultureInfo.InvariantCulture)));
        sb.AppendLine(N >= 2
            ? (Consistent ? Tr.T("-> consistent across charges: a clean barrel-vs-model offset.")
                          : Tr.T("-> offset varies with charge: the powder model shape is off, not just a scale.")
                            + (N >= 3 ? Tr.T(" Try 'Capture shape-fit sweep (k)' below.") : Tr.T(" Capture a 3rd+ charge, then try the k shape fit.")))
            : Tr.T("-> capture 2+ charges to tell a scale error from a shape error."));
        sb.AppendLine();
        if (baOld is { } ba)
            sb.AppendLine(Tr.F("suggested Ba for {0}: {1}  (was {2}, x{3})  -- first-order, MV scales as sqrt(Ba)",
                powderName, (ba * BaMultiplier).ToString("0.######", CultureInfo.InvariantCulture), ba.ToString("0.######", CultureInfo.InvariantCulture),
                BaMultiplier.ToString("0.####", CultureInfo.InvariantCulture)));
        else
            sb.AppendLine(Tr.F("no Ba in the load; apply the offset mentally: GRT runs {0} {1} {2} for this barrel.",
                Math.Abs(gu.VelocityValue(MeanDeltaMps)).ToString("0.0", CultureInfo.InvariantCulture), gu.VelocityUnitName,
                MeanDeltaMps >= 0 ? Tr.T("slow") : Tr.T("fast")));
        sb.AppendLine();
        sb.AppendLine(Tr.T("Method: measured MV = Athlon Rangecraft; sim MV = GRT Get_TabResults for the charge set in GRT."));
        sb.AppendLine(Tr.T("Re-verify after applying, and keep this per barrel + powder lot."));
        return sb.ToString();
    }

    public string BuildJointReport(JointFit fit, double baOld, double kOld, string powderName, JointVerification? verification = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine(Tr.T("GRT-style correction (Ba and k together)"));
        sb.AppendLine(new string('-', 40));
        if (!fit.Ok)
        {
            foreach (var n in fit.Notes) sb.AppendLine("! " + n);
            return sb.ToString();
        }
        sb.AppendLine(Tr.F("common factor for {0}: x{1}  ({2})", powderName, fit.Factor.ToString("0.#####", CultureInfo.InvariantCulture),
            (fit.Factor - 1).ToString("+0.000%;-0.000%", CultureInfo.InvariantCulture)));
        sb.AppendLine(Tr.F("suggested Ba: {0}  (was {1})", fit.NewBa.ToString("0.######", CultureInfo.InvariantCulture), baOld.ToString("0.######", CultureInfo.InvariantCulture)));
        sb.AppendLine(Tr.F("suggested k:  {0}  (was {1})", fit.NewK.ToString("0.#######", CultureInfo.InvariantCulture), kOld.ToString("0.#######", CultureInfo.InvariantCulture)));
        sb.AppendLine(Tr.F("Fit over {0} charge(s) with both baseline and Ba/k-nudged sim MV captured.", NPert));
        if (verification is { } v)
            sb.AppendLine(Tr.F("Verified in GRT at the proposed Ba and k: mean offset {0} m/s (was {1}), RMS {2} m/s (was {3}) -> {4}. {5}",
                v.MeanFitMps.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture), v.MeanOriginalMps.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture),
                v.RmsFitMps.ToString("0.00", CultureInfo.InvariantCulture), v.RmsOriginalMps.ToString("0.00", CultureInfo.InvariantCulture),
                v.Accepted ? Tr.T("ACCEPTED") : Tr.T("REFUSED"), v.Reason));
        else
            sb.AppendLine(Tr.T("! NOT verified yet: run 'Verify correction in GRT'. The Ba + k file is written only after GRT's own simulation confirms it."));
        sb.AppendLine(Tr.T("This is the adjustment GRT's own OBT tool makes (Ba and k scaled by the same factor), so Pmax, BLT and the"));
        sb.AppendLine(Tr.T("OBT nodes of the corrected load agree with what GRT's OBT tool would give."));
        return sb.ToString();
    }
}
