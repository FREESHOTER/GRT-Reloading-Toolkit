using System.Globalization;
using GrtModelLab.BarrelProfile;
using GrtModelLab.BarrelVibration;

namespace GrtModelLab;

/// <summary>
/// Console harness: --beamtest [profileName] — with no argument, sanity-checks
/// BeamModel.CantileverFrequenciesHz against physical invariants that hold for ANY uniform cantilever
/// beam. With a profile name, loads that SAVED barrel profile from the real database
/// (BarrelProfileDb.DefaultPath) and prints its actual frequencies — the live link from "Barrel
/// physical profile" data to a vibration number, so changing a saved profile's steel/taper and
/// re-running this actually changes the output.
/// </summary>
internal static class BeamModelTestCli
{
    public static void Run(string? profileName = null)
    {
        try { AttachConsole(-1); } catch { }
        var ci = CultureInfo.InvariantCulture;

        if (!string.IsNullOrWhiteSpace(profileName))
        {
            RunAgainstSavedProfile(profileName, ci);
            return;
        }

        // A plausible sporter-contour barrel: Chrome-Moly steel, 6.71mm bore, 20mm representative
        // (untapered, "average") OD, 660mm free length (matches the synthetic profile used by
        // --profiletest and the real N550 load's barrel length seen live this session).
        const double e = 205, rho = 7850, od = 20.0, bore = 6.71, len = 660.0;

        double[] f = BeamModel.CantileverFrequenciesHz(e, rho, od, bore, len, 5);
        Console.WriteLine("== Frequencies for a representative uniform barrel ==");
        for (int i = 0; i < f.Length; i++)
            Console.WriteLine(string.Format(ci, "  mode {0}: {1:0.00} Hz", i + 1, f[i]));

        Console.WriteLine();
        Console.WriteLine("== Invariant checks (true for ANY uniform cantilever, not just this geometry) ==");

        // Blevins' own tabulated ratios for a clamped-free uniform beam: f2/f1 = 6.267, f3/f1 = 17.55.
        // If BetaL were mistyped, these ratios (unlike any single frequency) would be visibly wrong.
        double r21 = f[1] / f[0], r31 = f[2] / f[0];
        Check("f2/f1 ~ 6.267", Math.Abs(r21 - 6.267) < 0.01, r21);
        Check("f3/f1 ~ 17.55", Math.Abs(r31 - 17.55) < 0.02, r31);

        // f1 scales as 1/L^2 -> doubling length quarters the fundamental.
        double fDoubleLen = BeamModel.CantileverFrequenciesHz(e, rho, od, bore, len * 2, 1)[0];
        double ratioLen = f[0] / fDoubleLen;
        Check("doubling length -> f1/4", Math.Abs(ratioLen - 4.0) < 0.001, ratioLen);

        // f1 scales as sqrt(E) -> quadrupling E doubles the fundamental.
        double fQuadE = BeamModel.CantileverFrequenciesHz(e * 4, rho, od, bore, len, 1)[0];
        double ratioE = fQuadE / f[0];
        Check("quadrupling E -> f1*2", Math.Abs(ratioE - 2.0) < 0.001, ratioE);

        // f1 scales as 1/sqrt(rho) -> quadrupling density halves the fundamental.
        double fQuadRho = BeamModel.CantileverFrequenciesHz(e, rho * 4, od, bore, len, 1)[0];
        double ratioRho = f[0] / fQuadRho;
        Check("quadrupling density -> f1/2", Math.Abs(ratioRho - 2.0) < 0.001, ratioRho);

        // A solid rod (bore=0) must be a smooth limit of the annulus formula, no special-casing.
        // Counter-intuitive but correct: for the SAME outer diameter, a hollow tube's I/A ratio is
        // (OD^2+bore^2)/16 versus a solid rod's OD^2/16 — strictly larger whenever bore>0, since the
        // material removed from near the neutral axis barely affects I but still reduces A. So the
        // BORED barrel vibrates FASTER than an equivalent solid rod, not slower (this is also why
        // tubes beat solid rods on stiffness-to-weight in structural engineering generally). An
        // earlier version of this check asserted the opposite and failed — that was this test's own
        // physical-intuition bug, not BeamModel's; verified independently via the I/A algebra above.
        double[] solid = BeamModel.CantileverFrequenciesHz(e, rho, od, 0.0, len, 1);
        Console.WriteLine(string.Format(ci, "solid-rod f1 (bore=0): {0:0.00} Hz (sanity: finite, < hollow-tube f1)", solid[0]));
        Check("hollow tube f1 > solid rod f1 (same OD)", f[0] > solid[0], f[0] / solid[0]);
    }

    private static void RunAgainstSavedProfile(string profileName, CultureInfo ci)
    {
        using var db = new BarrelProfileDb(BarrelProfileDb.DefaultPath);
        var match = db.ListProfiles().FirstOrDefault(p => string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));
        if (match == default)
        {
            Console.WriteLine($"no saved profile named '{profileName}'. Known profiles:");
            foreach (var (_, name) in db.ListProfiles()) Console.WriteLine("  " + name);
            return;
        }
        var rec = db.Load(match.Id)!;
        double eqOd = BeamModel.EquivalentUniformDiameterMm(rec);
        Console.WriteLine(string.Format(ci, "Profile '{0}': steel={1}, E={2} GPa, density={3} kg/m3, bore={4} mm, length={5} mm",
            rec.Name, rec.SteelLabel, rec.ElasticModulusGPa, rec.DensityKgM3, rec.BoreDiameterMm, rec.LengthMm));
        Console.WriteLine(string.Format(ci, "Taper reduced to equivalent uniform OD: {0:0.00} mm ({1} points)", eqOd, rec.Points.Count));
        double[] f = BeamModel.CantileverFrequenciesHz(rec, 5);
        for (int i = 0; i < f.Length; i++)
            Console.WriteLine(string.Format(ci, "  mode {0}: {1:0.00} Hz", i + 1, f[i]));
    }

    private static void Check(string label, bool ok, double value)
    {
        var ci = CultureInfo.InvariantCulture;
        Console.WriteLine(string.Format(ci, "  {0}: {1}  (value={2:0.0000})", label, ok ? "OK" : "FAIL", value));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);
}
