using GrtPluginKit.Analysis;
using GrtPluginKit.Grt;
using GrtPluginKit.Util;

namespace GrtReloadingToolkit.Brass;

public static class BrassCalc
{
    /// <summary>Water density at ~20 °C, g/cm³. GRT's casevol input is cm³.</summary>
    public const double WaterDensity = 0.99821;
    public const double GrainToGram = 0.06479891;

    public sealed record CaseVolumeResult(int N, double MeanGr, double SdGr, double MinGr, double MaxGr)
    {
        /// <summary>Volume in cm³ — what GRT stores internally for the <c>casevol</c> input.</summary>
        public double MeanCm3 => MeanGr * GrainToGram / WaterDensity;
        public double MinCm3 => MinGr * GrainToGram / WaterDensity;
        public double MaxCm3 => MaxGr * GrainToGram / WaterDensity;
        public double SdCm3 => SdGr * GrainToGram / WaterDensity;
        public double SdPct => MeanGr > 0 ? 100 * SdGr / MeanGr : 0;
    }

    public static double GramsToGrains(double g) => g / GrainToGram;

    /// <summary>Per-case water weight in <b>grains</b> (full − empty). Case volume is reported in grain H₂O and cm³.</summary>
    public static CaseVolumeResult CaseVolume(IEnumerable<double> waterGrains)
    {
        var v = waterGrains.Where(g => g > 0).ToList();
        if (v.Count == 0) return new CaseVolumeResult(0, 0, 0, 0, 0);
        var s = StringStats.From(v);
        return new CaseVolumeResult(s.N, s.Mean, s.Sd, s.Min, s.Max);
    }

    public sealed record NeckResult(
        double BushingOdMm, double MandrelOdMm, double NeckIdAfterSizingMm, double InterferenceMm,
        double LoadedNeckOdMm, string Notes);

    /// <summary>
    /// Bushing / mandrel sizing for a target neck interference (grip).
    /// All mm. springbackMm ≈ 0.03 mm typical for brass.
    /// </summary>
    public static NeckResult Neck(double bulletDiaMm, double neckWallMm, double interferenceMm,
                                  double? measuredLoadedNeckOdMm = null, double springbackMm = 0.03)
    {
        double loadedNeckOd = measuredLoadedNeckOdMm ?? (bulletDiaMm + 2 * neckWallMm);
        // Bushing die: bushing OD ≈ loaded neck OD − interference − springback (neck springs back after sizing).
        double bushing = loadedNeckOd - interferenceMm - springbackMm;
        // Mandrel / expander: sets the final ID directly = bullet dia − interference.
        double mandrel = bulletDiaMm - interferenceMm;
        double neckIdAfter = mandrel; // by definition when a mandrel is the last step
        string notes = measuredLoadedNeckOdMm is null
            ? "loaded neck OD estimated as bullet dia + 2x wall - measure a loaded round for accuracy"
            : "";
        return new NeckResult(bushing, mandrel, neckIdAfter, interferenceMm, loadedNeckOd, notes);
    }

    /// <summary>Kept under the brass-prep name its callers already use; defined once in the kit.</summary>
    public const double MmPerInch = GrtUnits.MmPerInch;

    public sealed record SeatingResult(double SeatingDepthMm, double DiffMm, IReadOnlyList<string> Notes)
    {
        public double SeatingDepthIn => SeatingDepthMm / MmPerInch;
        public double DiffIn => DiffMm / MmPerInch;
    }

    /// <summary>
    /// GRT seating depth (<c>gdepth</c> = bullet tail to case mouth) from comparator measurements:
    ///   DIFF = CBTO - case length   (ogive above the case mouth)
    ///   seating depth = BBTO - DIFF
    /// CBTO = cartridge base-to-ogive (loaded round), BBTO = bullet base-to-ogive (bare bullet),
    /// both to the SAME comparator/ogive datum. All inputs mm.
    /// </summary>
    public static SeatingResult Seating(double cbtoMm, double caseLenMm, double bbtoMm)
    {
        if (cbtoMm <= 0 || caseLenMm <= 0 || bbtoMm <= 0)
            throw new ArgumentException("CBTO, case length and BBTO must all be positive.");

        double diff = cbtoMm - caseLenMm;
        double depth = bbtoMm - diff;

        var notes = new List<string>();
        if (diff < 0)
            notes.Add("CBTO is less than case length - the ogive would sit below the case mouth. Re-check CBTO and case length.");
        if (depth <= 0)
            notes.Add("Seating depth came out <= 0 - the bullet base would be at or above the case mouth. Re-check BBTO.");
        else if (depth > caseLenMm)
            notes.Add("Seating depth exceeds the case length - not physically possible. Re-check the inputs.");
        if (depth > 0 && depth < 3)
            notes.Add("Seating depth under ~3 mm is very shallow - little bearing surface in the neck for grip/alignment.");
        if (depth > 12.7)
            notes.Add("Seating depth over 0.5 in (12.7 mm) is unusually deep for most cartridges - worth double-checking.");

        return new SeatingResult(depth, diff, notes);
    }

    /// <summary>A brass-prep length in one unit. Four decimals, per <see cref="Str.LengthFormat"/>.</summary>
    public static string Fmt(double mm, string unit) => unit == "in"
        ? Str.Len(mm / MmPerInch) + " in"
        : Str.Len(mm) + " mm";

    /// <summary>A length in the working unit, with the other unit in parentheses.</summary>
    public static string Both(double mm, bool inchFirst)
    {
        string m = Fmt(mm, "mm"), i = Fmt(mm, "in");
        return inchFirst ? $"{i}  ({m})" : $"{m}  ({i})";
    }
}
