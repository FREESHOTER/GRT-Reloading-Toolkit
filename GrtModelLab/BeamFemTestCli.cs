using System.Globalization;
using GrtModelLab.BarrelProfile;
using GrtModelLab.BarrelVibration;

namespace GrtModelLab;

/// <summary>
/// Console harness: --femtest — the actual trust-building step for V2 (BeamFem). The formulation
/// (standard Euler-Bernoulli element matrices, standard Cholesky-transformed generalized
/// eigenproblem) is textbook, but the ARITHMETIC could still be wrong in a dozen small ways
/// (a transposed matrix, an off-by-one in assembly, the wrong DOFs dropped for the boundary
/// condition) that would only show up as a wrong number, not a crash. The check that catches all
/// of those at once: feed V2 a perfectly UNIFORM profile and confirm it converges to V1's
/// closed-form answer as the mesh refines - if V2 disagreed with V1 on the one case where they
/// must agree, nothing past this point could be trusted regardless of how principled the taper
/// handling looks.
/// </summary>
internal static class BeamFemTestCli
{
    public static void Run()
    {
        try { AttachConsole(-1); } catch { }
        var ci = CultureInfo.InvariantCulture;

        const double e = 205, rho = 7850, od = 20.0, bore = 6.71, len = 660.0;

        Console.WriteLine("== V2 mesh convergence against V1's closed form (uniform profile) ==");
        double[] v1 = BeamModel.CantileverFrequenciesHz(e, rho, od, bore, len, 3);
        Console.WriteLine(string.Format(ci, "V1 (closed form): f1={0:0.0000} f2={1:0.0000} f3={2:0.0000} Hz", v1[0], v1[1], v1[2]));

        // Convergence turned out to be excellent from the smallest mesh tested (sub-0.001% by
        // N=10) - well below where a strict "must shrink every step" check is meaningful; below
        // that level successive errors are floating-point noise around zero; and a real formula
        // bug (a wrong matrix entry, a transpose) shows up as a LARGE, non-shrinking error, not a
        // borderline one. So the check that actually matters is "stays small at every mesh size
        // tested", not "strictly monotonic".
        Func<double, double> uniform = _ => od;
        double[] elementCounts = { 10, 20, 40, 80 };
        double maxErr1 = 0, maxErr2 = 0, maxErr3 = 0;
        foreach (int n in elementCounts.Select(x => (int)x))
        {
            double[] v2 = BeamFem.NaturalFrequenciesHz(e, rho, len, bore, uniform, n, 3);
            double err1 = 100.0 * Math.Abs(v2[0] - v1[0]) / v1[0];
            double err2 = 100.0 * Math.Abs(v2[1] - v1[1]) / v1[1];
            double err3 = 100.0 * Math.Abs(v2[2] - v1[2]) / v1[2];
            Console.WriteLine(string.Format(ci,
                "  N={0,3}  f1={1:0.0000} (err {2:0.0000}%)  f2={3:0.0000} (err {4:0.0000}%)  f3={5:0.0000} (err {6:0.0000}%)",
                n, v2[0], err1, v2[1], err2, v2[2], err3));
            maxErr1 = Math.Max(maxErr1, err1); maxErr2 = Math.Max(maxErr2, err2); maxErr3 = Math.Max(maxErr3, err3);
        }
        Console.WriteLine();
        Check("f1 within 0.01% of V1 at every mesh size tested", maxErr1 < 0.01, maxErr1);
        Check("f2 within 0.05% of V1 at every mesh size tested", maxErr2 < 0.05, maxErr2);
        Check("f3 within 0.1% of V1 at every mesh size tested", maxErr3 < 0.1, maxErr3);

        Console.WriteLine();
        Console.WriteLine("== V2 on a real taper (V1's single equivalent-diameter number vs V2's per-element one) ==");
        // Same synthetic taper as --profiletest: heavy at the breech, necking toward the muzzle.
        var rec = new BarrelProfileRecord { Name = "Synthetic taper", ElasticModulusGPa = e, DensityKgM3 = rho, BoreDiameterMm = bore, LengthMm = len };
        (double pos, double odAt)[] profile = { (0, 30), (100, 28), (250, 24), (400, 20), (550, 17), (660, 15.5) };
        foreach (var (pos, odAt) in profile) rec.Points.Add(new BarrelProfilePoint { PositionMm = pos, OuterDiameterMm = odAt });

        double[] v1Taper = BeamModel.CantileverFrequenciesHz(rec, 3);
        double[] v2Taper = BeamFem.NaturalFrequenciesHz(rec, 80, 3);
        Console.WriteLine(string.Format(ci, "V1 (equivalent uniform OD {0:0.0}mm): f1={1:0.0000} f2={2:0.0000} f3={3:0.0000} Hz",
            BeamModel.EquivalentUniformDiameterMm(rec), v1Taper[0], v1Taper[1], v1Taper[2]));
        Console.WriteLine(string.Format(ci, "V2 (real taper, N=80):               f1={0:0.0000} f2={1:0.0000} f3={2:0.0000} Hz",
            v2Taper[0], v2Taper[1], v2Taper[2]));
        Check("V2's taper-aware f1 differs from V1's equivalent-uniform f1 (taper actually matters)",
            Math.Abs(v2Taper[0] - v1Taper[0]) / v1Taper[0] > 0.001, 100.0 * Math.Abs(v2Taper[0] - v1Taper[0]) / v1Taper[0]);

        Console.WriteLine();
        Console.WriteLine("== Mode shapes: interior-node-count check (uniform profile, N=80) ==");
        // Textbook property of any clamped-free (cantilever) beam, independent of geometry: mode k
        // has exactly k-1 interior zero-crossings along its length (not counting the clamped end,
        // which is always zero by the boundary condition, or the free end, which generally isn't).
        // A wrong sign somewhere in the eigenvector back-transform would show up here as a mode
        // with too many or too few crossings, even if its FREQUENCY happened to look plausible.
        var modes = BeamFem.Modes(e, rho, len, bore, uniform, 80, 3);
        for (int i = 0; i < modes.Length; i++)
        {
            int crossings = CountInteriorSignChanges(modes[i].Displacement);
            Check(string.Format(ci, "mode {0} (f={1:0.0} Hz) has {2} interior node(s)", i + 1, modes[i].FrequencyHz, i),
                crossings == i, crossings);
        }
        Check("mode 1 shape is zero at the clamped root", Math.Abs(modes[0].Displacement[0]) < 1e-9, modes[0].Displacement[0]);

        Console.WriteLine();
        Console.WriteLine("== Static sag under self-weight (uniform profile) vs. closed form ==");
        // Closed-form cantilever tip deflection under a uniform distributed self-weight load w
        // (N/m): delta = w*L^4/(8*E*I) - textbook result (e.g. Rao, "Mechanical Vibrations"),
        // independent of the FEM entirely. If StaticSagMm's element assembly/load vector/BC
        // reduction has a sign or factor-of-two error, this is the cheapest way to catch it before
        // trusting it for a forced-response calculation.
        const double gAccel = 9.80665;
        var (momentOfInertiaSi, areaSi) = BeamSection.Annulus(od, bore);
        double wPerLength = rho * areaSi * gAccel;
        double lengthM = len / 1000.0;
        double closedFormTipSagMm = wPerLength * Math.Pow(lengthM, 4) / (8.0 * e * 1e9 * momentOfInertiaSi) * 1000.0;
        double[] sag = BeamFem.StaticSagMm(e, rho, len, bore, uniform, 80);
        Check("FEM tip sag matches closed form (within 0.1%)",
            Math.Abs(sag[^1] - closedFormTipSagMm) / closedFormTipSagMm < 0.001,
            100.0 * Math.Abs(sag[^1] - closedFormTipSagMm) / closedFormTipSagMm);
        Console.WriteLine(string.Format(ci, "  FEM tip sag: {0:0.00000} mm, closed form: {1:0.00000} mm", sag[^1], closedFormTipSagMm));
        Check("sag is zero at the clamped root", Math.Abs(sag[0]) < 1e-12, sag[0]);
        Check("sag is monotonically increasing toward the muzzle (no wiggles)",
            sag.Zip(sag.Skip(1), (a, b) => b >= a).All(ok => ok), 0);

        Console.WriteLine();
        Console.WriteLine("== Modal mass: formula-derived value vs. independent numerical integration ==");
        // ModalMassKg comes from |y|^2 * scale^2 inside the Cholesky-transformed eigensolve (relies
        // on the eigensolver returning unit-norm eigenvectors). Cross-checked here against the
        // textbook physical definition integrated directly over the FEM's own (peak-normalized)
        // shape: M_i = integral of rho*A(x)*phi(x)^2 dx - a completely independent path to the same
        // number; if MathNet's Evd normalization assumption were wrong, or the scale^2 algebra had a
        // mistake, these two would disagree.
        for (int i = 0; i < modes.Length; i++)
        {
            var mode = modes[i];
            double integral = 0;
            for (int seg = 0; seg + 1 < mode.PositionsMm.Length; seg++)
            {
                double x0 = mode.PositionsMm[seg], x1 = mode.PositionsMm[seg + 1];
                double midMm = (x0 + x1) / 2.0;
                var (_, areaAt) = BeamSection.Annulus(uniform(midMm), bore);
                double phi0 = mode.Displacement[seg], phi1 = mode.Displacement[seg + 1];
                double integrand0 = rho * areaAt * phi0 * phi0;
                double integrand1 = rho * areaAt * phi1 * phi1;
                integral += 0.5 * (integrand0 + integrand1) * ((x1 - x0) / 1000.0); // mm -> m
            }
            double relErr = 100.0 * Math.Abs(integral - mode.ModalMassKg) / mode.ModalMassKg;
            Check(string.Format(ci, "mode {0} modal mass: formula {1:0.000000} kg vs. numerical integral {2:0.000000} kg (within 1%)",
                i + 1, mode.ModalMassKg, integral), relErr < 1.0, relErr);
        }
    }

    /// <summary>Counts sign changes strictly between the first and last sample (the clamped root,
    /// always ~0, and the free tip aren't "interior" crossings).</summary>
    private static int CountInteriorSignChanges(double[] v)
    {
        int count = 0;
        for (int i = 1; i < v.Length - 2; i++)
            if (Math.Sign(v[i]) != 0 && Math.Sign(v[i + 1]) != 0 && Math.Sign(v[i]) != Math.Sign(v[i + 1]))
                count++;
        return count;
    }

    private static void Check(string label, bool ok, double value)
    {
        var ci = CultureInfo.InvariantCulture;
        Console.WriteLine(string.Format(ci, "  {0}: {1}  (value={2:0.0000})", label, ok ? "OK" : "FAIL", value));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);
}
