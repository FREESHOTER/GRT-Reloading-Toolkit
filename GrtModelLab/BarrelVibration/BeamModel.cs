using GrtModelLab.BarrelProfile;

namespace GrtModelLab.BarrelVibration;

/// <summary>
/// V1 — closed-form Euler-Bernoulli natural frequencies for a barrel treated as a UNIFORM hollow
/// cylinder (constant outer diameter, no taper): clamped at the chamber end, free at the muzzle,
/// same "cantilever" boundary condition a real barrel has. No finite elements yet — this is the
/// analytical baseline the future variable-profile FEM (V2) must reproduce when fed a uniform
/// profile, before its taper-handling is trusted on a real one.
/// </summary>
public static class BeamModel
{
    /// <summary>
    /// First five roots of cos(betaL)*cosh(betaL) = -1 for a clamped-free uniform beam — the
    /// eigenvalue equation's dimensionless mode constants (Blevins, "Formulas for Natural Frequency
    /// and Mode Shape", Table 8-1). Independent of material/geometry: any uniform cantilever's mode
    /// frequencies scale with these same five numbers.
    /// </summary>
    private static readonly double[] BetaL = { 1.875104, 4.694091, 7.854757, 10.995541, 14.137168 };

    public static int MaxModes => BetaL.Length;

    /// <summary>
    /// Natural frequencies in Hz for the first <paramref name="modeCount"/> bending modes of a
    /// uniform hollow-cylinder cantilever.
    /// </summary>
    /// <param name="elasticModulusGPa">Steel's Young's modulus, GPa (e.g. ~205 for chrome-moly).</param>
    /// <param name="densityKgM3">Steel density, kg/m3 (e.g. ~7850).</param>
    /// <param name="outerDiameterMm">Barrel OD, mm — constant along the length in this V1 model.</param>
    /// <param name="boreDiameterMm">Bore/groove diameter, mm (the "hole" removed from the section).</param>
    /// <param name="lengthMm">Free (cantilevered) barrel length, mm.</param>
    public static double[] CantileverFrequenciesHz(double elasticModulusGPa, double densityKgM3,
        double outerDiameterMm, double boreDiameterMm, double lengthMm, int modeCount = 3)
    {
        if (modeCount < 1 || modeCount > BetaL.Length)
            throw new ArgumentOutOfRangeException(nameof(modeCount), $"supports 1..{BetaL.Length} modes");
        if (outerDiameterMm <= boreDiameterMm) throw new ArgumentException("outer diameter must exceed bore diameter");
        if (lengthMm <= 0) throw new ArgumentException("length must be positive");

        double e = elasticModulusGPa * 1e9;    // GPa -> Pa
        double rho = densityKgM3;               // already SI (kg/m3)
        double length = lengthMm / 1000.0;      // mm -> m
        var (momentOfInertia, area) = BeamSection.Annulus(outerDiameterMm, boreDiameterMm);

        double coeff = Math.Sqrt(e * momentOfInertia / (rho * area)); // m^2/s
        var freqs = new double[modeCount];
        for (int i = 0; i < modeCount; i++)
        {
            double bl = BetaL[i];
            freqs[i] = bl * bl / (2.0 * Math.PI * length * length) * coeff; // Hz
        }
        return freqs;
    }

    /// <summary>
    /// V1 treats the barrel as uniform, but a saved profile is tapered — reduces it to one
    /// "equivalent" OD via a length-weighted (trapezoidal) average along the profile, the closest
    /// single number to "what constant diameter has the same average cross-section as this taper".
    /// Needs at least 2 points (same requirement the profile chart already enforces).
    /// </summary>
    public static double EquivalentUniformDiameterMm(BarrelProfileRecord rec)
    {
        var pts = rec.Points.OrderBy(p => p.PositionMm).ToList();
        if (pts.Count < 2) throw new ArgumentException("profile needs at least 2 points");

        double weightedSum = 0, totalLength = 0;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            double segLen = pts[i + 1].PositionMm - pts[i].PositionMm;
            if (segLen <= 0) continue;
            double segAvgOd = (pts[i].OuterDiameterMm + pts[i + 1].OuterDiameterMm) / 2.0;
            weightedSum += segLen * segAvgOd;
            totalLength += segLen;
        }
        if (totalLength <= 0) throw new ArgumentException("profile points must span a positive length");
        return weightedSum / totalLength;
    }

    /// <summary>Runs the V1 model directly off a saved barrel profile — its own material, bore, length,
    /// and the taper reduced to <see cref="EquivalentUniformDiameterMm"/>.</summary>
    public static double[] CantileverFrequenciesHz(BarrelProfileRecord rec, int modeCount = 3) =>
        CantileverFrequenciesHz(rec.ElasticModulusGPa, rec.DensityKgM3,
            EquivalentUniformDiameterMm(rec), rec.BoreDiameterMm, rec.LengthMm, modeCount);
}
