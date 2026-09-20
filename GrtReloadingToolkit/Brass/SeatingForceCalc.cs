namespace GrtReloadingToolkit.Brass;

/// <summary>
/// Estimates expected bullet-seating (press-fit) force from neck-tension prep parameters, for a QC
/// press's own "max pressure" safety threshold. Ported from the QC press project's own estimator
/// (Pressa_Automatica_v8/python/pressa/seating_force_estimator.py).
///
/// Deliberately caliber-agnostic here, unlike the Python original: that version picked a baseline
/// force/interference/depth from a lookup table of 3 hardcoded calibers (the press author's own
/// convenience shortcut, not something inherent to the math) -- every baseline value is a plain
/// numeric input here instead, so this works for any caliber. The per-factor tables
/// (annealing/neck-sizing/lube/coating/boat-tail) stay as fixed lookups: those are multipliers on
/// prep quality, not caliber-specific baselines.
///
/// Lives here, alongside the Neck tab's bushing/mandrel sizing (which already computes this
/// estimator's own bullet-diameter and neck-ID-after-sizing inputs) and the Seating-depth tab
/// (which supplies the actual seating depth) -- a natural brass-prep calculator, not tied to any
/// GRT input. NOTE: an earlier version of this also converted the estimated force into a GRT
/// "Initial Pressure" (gpressure) estimate via P = force / bore area -- removed 2026-09-16 after a
/// sanity check against GRT's own doc (typical rifle jacketed Initial Pressure ~250 bar) showed the
/// conversion underestimating by ~4-5x: GRT's shot-start pressure is dominated by bearing-surface
/// engraving into the rifling, a phenomenon this estimator (built for a bench press's static neck
/// tension, not a fired round's rifling engagement) doesn't model at all. The
/// <see cref="ForceKgToEquivalentPressure"/> conversion kept below is NOT that -- see its own doc.
///
/// The philosophy is empirical, not first-principles physics: a caliber-typical baseline force,
/// scaled linearly by interference and seating depth relative to their own typical values, times
/// multiplicative correction factors.
/// </summary>
public static class SeatingForceCalc
{
    private const double KgToNewton = 9.80665;

    public readonly record struct Factor(double Mean, double Std);

    /// <summary>Effect of case-neck heat treatment: freshly annealed = softer = less force.</summary>
    public static readonly IReadOnlyList<(string Key, string Label, Factor F)> AnnealingOptions = new[]
    {
        ("fresh_annealed", "Freshly annealed (torch/AMP, 1-2 firings)", new Factor(0.85, 0.75)),
        ("annealed_5plus", "Annealed, 5+ firings since", new Factor(1.00, 1.00)),
        ("never_annealed_new", "Never annealed, new brass", new Factor(1.15, 1.20)),
        ("never_annealed_used", "Never annealed, 5+ firings (hardened neck)", new Factor(1.35, 1.60)),
    };

    /// <summary>Neck sizing method: affects run-to-run uniformity (std) more than the mean.</summary>
    public static readonly IReadOnlyList<(string Key, string Label, Factor F)> NeckSizingOptions = new[]
    {
        ("full_length", "Full-length die (non-bushing)", new Factor(1.05, 1.15)),
        ("neck_only", "Neck-sizing die (non-bushing)", new Factor(1.00, 1.10)),
        ("bushing", "Bushing die (Redding/Forster)", new Factor(1.00, 0.90)),
        ("bushing_mandrel", "Bushing + final mandrel (best)", new Factor(0.95, 0.75)),
    };

    /// <summary>Lubricant inside the neck: reduces friction and mean force.</summary>
    public static readonly IReadOnlyList<(string Key, string Label, Factor F)> LubeOptions = new[]
    {
        ("dry", "Dry (no lubrication)", new Factor(1.25, 1.30)),
        ("graphite", "Graphite (Imperial/Redding dry)", new Factor(0.95, 0.95)),
        ("hbn", "hBN (hexagonal boron nitride)", new Factor(0.90, 0.90)),
        ("imperial_dry", "Imperial Dry Neck Lube", new Factor(0.85, 0.85)),
        ("lapua_wax", "Lapua Neck Wax (or similar)", new Factor(0.80, 0.85)),
        ("moly", "Moly (molybdenum disulfide)", new Factor(0.85, 0.90)),
    };

    /// <summary>Bullet's own outer coating/treatment.</summary>
    public static readonly IReadOnlyList<(string Key, string Label, Factor F)> CoatingOptions = new[]
    {
        ("copper_bare", "Bare copper (standard gilding metal)", new Factor(1.00, 1.00)),
        ("copper_plated", "Plated copper (tombac / FMJ)", new Factor(0.92, 0.90)),
        ("moly", "Moly coated", new Factor(0.80, 0.85)),
        ("hbn", "hBN coated", new Factor(0.78, 0.80)),
        ("hybrid", "Hybrid / H-series / PDX (Berger)", new Factor(0.85, 0.85)),
    };

    /// <summary>Reference baselines from real presses/community data, shown only as a hint -- not a
    /// selector. bulletDiaMm/interferenceMm/seatingDepthMm let a caller pre-fill the free-entry
    /// fields for a quick start; the user is expected to override them for their own bore.</summary>
    public static readonly IReadOnlyList<(string Caliber, double BulletDiaMm, double ForceKg, double StdKg, double InterferenceMm, double SeatingDepthMm)> ReferenceBaselines = new[]
    {
        (".223 Remington / 5.56 NATO", 5.6896, 14.0, 2.5, 0.0508, 5.0),
        ("6.5 Creedmoor", 6.7056, 22.0, 3.0, 0.0508, 6.7),
        (".308 Winchester / 7.62x51", 7.8232, 27.0, 3.5, 0.0762, 7.8),
    };

    public const double MaxSafetyMargin = 1.5;

    public sealed record Estimate(
        double MeanKg, double StdKg,
        double P1LowKg, double P1HighKg, double P2LowKg, double P2HighKg,
        double MaxRecommendedKg,
        double InterferenceMm,
        double EquivalentPressureBar, double EquivalentPressurePsi,
        IReadOnlyList<string> Notes);

    /// <param name="bulletDiameterMm">Bullet's own outer diameter.</param>
    /// <param name="neckIdMm">Neck inside diameter AFTER sizing (the actual prepped brass).</param>
    /// <param name="baselineForceKg">Expected mean peak force for a "typical" setup in THIS bore --
    /// the one number that used to come from the 3-caliber lookup table; now typed in directly.</param>
    /// <param name="baselineStdKg">Expected piece-to-piece std deviation for that same baseline.</param>
    /// <param name="typicalInterferenceMm">The interference the baseline force above assumes.</param>
    /// <param name="typicalSeatingDepthMm">The seating depth the baseline force above assumes.</param>
    /// <param name="actualSeatingDepthMm">The seating depth actually being used.</param>
    public static Estimate Compute(
        double bulletDiameterMm, double neckIdMm,
        double baselineForceKg, double baselineStdKg,
        double typicalInterferenceMm, double typicalSeatingDepthMm,
        double actualSeatingDepthMm,
        Factor annealing, Factor neckSizing, Factor lube, Factor coating, bool boatTail)
    {
        var notes = new List<string>();
        double interferenceMm = bulletDiameterMm - neckIdMm;

        double interferenceFactor;
        if (interferenceMm <= 0)
        {
            interferenceFactor = 0.05;
            // Invariant, like every number this plugin renders -- GrtUnits formats all of its
            // string helpers that way, and these notes land in the same report beside them. Bare
            // interpolation picks up the OS locale, so on an Italian machine a note read
            // "0,142 mm" next to a GrtUnits value showing "0.142 mm" for the same quantity.
            notes.Add(FormattableString.Invariant(
                          $"WARNING: neck ID ({neckIdMm:0.000} mm) is wider than the bullet ({bulletDiameterMm:0.000} mm). ") +
                      "Neck tension is essentially absent: the bullet can push back into the case. Dangerous -- resize the neck.");
        }
        else
        {
            interferenceFactor = interferenceMm / typicalInterferenceMm;
            if (interferenceMm < 0.0127)
                notes.Add(FormattableString.Invariant($"Very low interference ({interferenceMm:0.000} mm): insufficient neck tension. Risk of the bullet pushing back into the case."));
            else if (interferenceMm > 0.127)
                notes.Add(FormattableString.Invariant($"Very high interference ({interferenceMm:0.000} mm): risk of deforming the bullet or the neck. Consider a larger bushing."));
        }

        double depthFactorRaw = actualSeatingDepthMm / typicalSeatingDepthMm;
        double depthFactor = Math.Clamp(depthFactorRaw, 0.4, 2.5);
        if (Math.Abs(depthFactorRaw - depthFactor) > 1e-9)
            notes.Add(FormattableString.Invariant($"Seating depth ({actualSeatingDepthMm:0.0} mm) is outside the range these baseline values were calibrated for. Clamped for this calculation."));

        Factor bt = boatTail ? new Factor(0.95, 1.00) : new Factor(1.00, 1.00);

        double forceMean = baselineForceKg * interferenceFactor * depthFactor
            * annealing.Mean * neckSizing.Mean * lube.Mean * coating.Mean * bt.Mean;

        // Combined std grows with the sqrt of the combination: "uniform" factors (bushing, a
        // constant lube) pull it down, "variable" ones (an unannealed case) push it up.
        double stdFactorCombined = Math.Sqrt(Math.Max(0.05,
            annealing.Std * neckSizing.Std * lube.Std * coating.Std * bt.Std));
        double forceStd = baselineStdKg * stdFactorCombined * Math.Max(0.5, depthFactor);

        // Soft caps: std can't exceed 40% of the mean (that would mean a process out of control),
        // and can't be below 0.5 kg (the electronics' own baseline noise floor).
        forceStd = Math.Min(forceStd, forceMean * 0.4);
        forceStd = Math.Max(0.5, forceStd);

        double p1Low = Math.Max(0.0, forceMean - forceStd), p1High = forceMean + forceStd;
        double p2Low = Math.Max(0.0, forceMean - 2 * forceStd), p2High = forceMean + 2 * forceStd;
        double maxRecommended = p2High * MaxSafetyMargin;

        if (forceMean < 8.0)
            notes.Add(FormattableString.Invariant($"Estimated mean force is very low ({forceMean:0.0} kg): check the inputs and that the neck has enough interference."));
        if (forceMean > 45.0)
            notes.Add(FormattableString.Invariant($"Estimated mean force is very high ({forceMean:0.0} kg): check lubrication and case annealing before proceeding."));

        var (bar, psi) = ForceKgToEquivalentPressure(forceMean, bulletDiameterMm);
        return new Estimate(forceMean, forceStd, p1Low, p1High, p2Low, p2High, maxRecommended,
            interferenceMm, bar, psi, notes);
    }

    /// <summary>
    /// Expresses a seating force as an equivalent pressure over the bullet's own cross-section
    /// (P = F / area) -- just another unit for the SAME seating force, the way some of the press
    /// community talks about neck-tension force in terms of contact pressure. This is NOT an
    /// estimate of any GRT ballistics input (chamber/shot-start pressure is a different physical
    /// quantity, dominated by bearing-surface engraving into the rifling -- see this file's own
    /// top-of-file note for why an earlier version's attempt at that conversion was removed).
    /// </summary>
    public static (double Bar, double Psi) ForceKgToEquivalentPressure(double forceKg, double bulletDiameterMm)
    {
        double forceN = forceKg * KgToNewton;
        double radiusM = bulletDiameterMm / 2.0 / 1000.0;
        double areaM2 = Math.PI * radiusM * radiusM;
        double pressurePa = forceN / areaM2;
        double bar = pressurePa / 100_000.0;
        double psi = bar * 14.5038;
        return (bar, psi);
    }
}
