using System.Globalization;
using GrtModelLab.BarrelProfile;
using GrtModelLab.BarrelVibration;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;

namespace GrtModelLab;

/// <summary>
/// Console harness: --forcetest &lt;ipcport&gt; — runs <see cref="ForcedResponse.MuzzleResponse"/>
/// against a real, live GRT curve and prints diagnostics + sanity checks. Needs a live GRT with a
/// computed tab (same requirement as --curvetest, which this builds on).
/// </summary>
internal static class ForceTestCli
{
    public static void Run(int port)
    {
        try { AttachConsole(-1); } catch { }
        var ci = CultureInfo.InvariantCulture;

        using var grt = new GrtClient(port);
        try { grt.Connect(); }
        catch (Exception ex) { Console.WriteLine("Could not connect to GRT on port " + port + ": " + ex.Message); return; }

        RunAsync(grt, ci).GetAwaiter().GetResult();
    }

    private static async Task RunAsync(GrtClient grt, CultureInfo ci)
    {
        var top = await grt.GetTabOnTopAsync();
        if (top.handle == 0) { Console.WriteLine("No tab open in GRT."); return; }
        if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file)) { Console.WriteLine("Active tab has no file on disk."); return; }

        var doc = GrtLoadDoc.Load(GrtLoadDoc.PristineBasePath(top.file) is { } basePath && File.Exists(basePath) ? basePath : top.file);
        double? bulletMassGr = doc.BulletMassGr;
        if (bulletMassGr is null) { Console.WriteLine("Could not read bullet mass from the load."); return; }
        Console.WriteLine(string.Format(ci, "Bullet mass: {0:0.00} gr", bulletMassGr));

        using var db = new BarrelProfileDb(BarrelProfileDb.DefaultPath);
        var profiles = db.ListProfiles();
        if (profiles.Count == 0) { Console.WriteLine("No saved barrel profiles."); return; }
        var rec = db.Load(profiles[0].Id)!;
        Console.WriteLine($"Barrel profile: '{rec.Name}' (bore {rec.BoreDiameterMm}mm, length {rec.LengthMm}mm)");

        const int elements = 120, modeCount = 5;
        var modes = BeamFem.Modes(rec, elements, modeCount);
        var sag = BeamFem.StaticSagMm(rec, elements);
        Console.WriteLine(string.Format(ci, "Modes (Hz): {0}", string.Join(", ", modes.Select(m => m.FrequencyHz.ToString("0.0", ci)))));
        Console.WriteLine(string.Format(ci, "Static sag at muzzle: {0:0.00000} mm", sag[^1]));

        Console.WriteLine();
        Console.WriteLine("Reading GRT's real P(t)/v(t) curve...");
        var (results, curve) = await grt.GetTabResultsWithCurveAsync(top.handle);
        if (curve.Length < 3) { Console.WriteLine("Curve too short or empty."); return; }
        Console.WriteLine($"  {curve.Length} point(s), barrel time {results.BarrelTimeMs:0.0000} ms");

        var timeMs = curve.Select(p => p.TimeMs).ToArray();
        var posMm = curve.Select(p => p.PositionMm).ToArray();
        var velMps = curve.Select(p => p.VelocityMps).ToArray();

        int dupT = 0, dupTFirst = -1;
        for (int i = 0; i + 1 < timeMs.Length; i++)
            if (timeMs[i + 1] <= timeMs[i]) { dupT++; if (dupTFirst < 0) dupTFirst = i; }
        Console.WriteLine(string.Format(ci, "  duplicate/non-increasing time steps: {0} (first at index {1})", dupT, dupTFirst));
        if (dupTFirst >= 0)
            Console.WriteLine(string.Format(ci, "    t[{0}]={1:0.000000}ms t[{2}]={3:0.000000}ms", dupTFirst, timeMs[dupTFirst], dupTFirst + 1, timeMs[dupTFirst + 1]));

        Console.WriteLine();
        Console.WriteLine("  last 10 raw curve samples (dt to previous, ms):");
        for (int i = Math.Max(0, timeMs.Length - 10); i < timeMs.Length; i++)
            Console.WriteLine(string.Format(ci, "    [{0}] t={1:0.000000} v={2:0.000} dt_prev={3:0.000000}",
                i, timeMs[i], velMps[i], i > 0 ? timeMs[i] - timeMs[i - 1] : 0));

        var forceDbg = new List<(int i, double x, double v, double a, double slope, double f)>();
        ForcedResponse.DebugForceSink = (i, x, v, a, slope, f) => { if (i >= timeMs.Length - 10) forceDbg.Add((i, x, v, a, slope, f)); };

        Console.WriteLine();
        Console.WriteLine("== Forced response (bullet-follows-sag mechanism) ==");
        var series = ForcedResponse.MuzzleResponse(modes, sag, rec.LengthMm, bulletMassGr.Value, timeMs, posMm, velMps);
        ForcedResponse.DebugForceSink = null;

        Console.WriteLine("  last 10 computed force samples: i, x(mm), v(m/s), a_est(m/s2), slope, F(N)");
        foreach (var d in forceDbg)
            Console.WriteLine(string.Format(ci, "    [{0}] x={1:0.000} v={2:0.000} a={3:0.0000} slope={4:0.00000000} F={5:0.000000}", d.i, d.x, d.v, d.a, d.slope, d.f));

        bool anyNaN = series.VelocityMmPerMs.Any(double.IsNaN) || series.DisplacementMm.Any(double.IsNaN)
            || series.VelocityMmPerMs.Any(double.IsInfinity) || series.DisplacementMm.Any(double.IsInfinity);
        Check("no NaN/Infinity in the output", !anyNaN, 0);
        if (anyNaN) return;

        double maxDispMm = series.DisplacementMm.Select(Math.Abs).Max();
        double maxVelMmMs = series.VelocityMmPerMs.Select(Math.Abs).Max();
        int maxDispIdx = Array.FindIndex(series.DisplacementMm, d => Math.Abs(d) == maxDispMm);
        int maxVelIdx = Array.FindIndex(series.VelocityMmPerMs, v => Math.Abs(v) == maxVelMmMs);
        Console.WriteLine(string.Format(ci, "  peak muzzle displacement (this mechanism): {0:0.000000} mm at t={1:0.0000}ms (point {2}/{3})", maxDispMm, timeMs[maxDispIdx], maxDispIdx, timeMs.Length));
        Console.WriteLine(string.Format(ci, "  peak muzzle transverse velocity: {0:0.000000} mm/ms = {1:0.000000} m/s at t={2:0.0000}ms (point {3}/{4})", maxVelMmMs, maxVelMmMs, timeMs[maxVelIdx], maxVelIdx, timeMs.Length));
        Console.WriteLine("  first 10 points: t(ms) x(mm) v(m/s) a(m/s2,est) disp(mm) vel(mm/ms)");
        for (int i = 0; i < Math.Min(10, timeMs.Length); i++)
            Console.WriteLine(string.Format(ci, "    [{0}] t={1:0.000000} x={2:0.000} v={3:0.000} disp={4:0.00000000} vel={5:0.00000000}",
                i, timeMs[i], posMm[i], velMps[i], series.DisplacementMm[i], series.VelocityMmPerMs[i]));
        Console.WriteLine(string.Format(ci, "  muzzle displacement/velocity at shot end: {0:0.000000} mm, {1:0.000000} mm/ms", series.DisplacementMm[^1], series.VelocityMmPerMs[^1]));

        var mode1Series = ForcedResponse.MuzzleResponse(new[] { modes[0] }, sag, rec.LengthMm, bulletMassGr.Value, timeMs, posMm, velMps);
        Console.WriteLine(string.Format(ci, "  mode 1 ({0:0.0} Hz, period {1:0.0}ms) alone, over time:", modes[0].FrequencyHz, 1000.0 / modes[0].FrequencyHz));
        foreach (int idx in new[] { 0, 200, 400, 600, 800, 1000, 1200, 1400, 1600, 1800, 1999 })
            Console.WriteLine(string.Format(ci, "    [{0,4}] t={1:0.0000}ms disp={2,14:0.00000000}mm vel={3,14:0.00000000}mm/ms", idx, timeMs[idx], mode1Series.DisplacementMm[idx], mode1Series.VelocityMmPerMs[idx]));

        Console.WriteLine("  per-mode contribution to final displacement/velocity:");
        foreach (var (freqHz, dispMm, velMmMs) in ForcedResponse.PerModeFinalContribution(modes, sag, rec.LengthMm, bulletMassGr.Value, timeMs, posMm, velMps))
            Console.WriteLine(string.Format(ci, "    mode {0,7:0.0} Hz: disp={1,12:0.00000000} mm  vel={2,12:0.00000000} mm/ms", freqHz, dispMm, velMmMs));

        double[] crossings = ForcedResponse.VelocityZeroCrossingsMs(series);
        Console.WriteLine($"  {crossings.Length} zero-crossing(s) of transverse velocity within the shot:");
        foreach (double t in crossings.Take(20))
            Console.WriteLine(string.Format(ci, "    t={0:0.0000} ms", t));

        // Energy sanity: the barrel's own modal kinetic energy should be a tiny fraction of the
        // bullet's kinetic energy at the muzzle - this mechanism is a small perturbation, not a
        // comparable-energy phenomenon. A result anywhere near "comparable" would mean something in
        // the units/formula is wrong by many orders of magnitude, not that barrel whip is unexpectedly
        // energetic.
        double bulletMassKg = bulletMassGr.Value * 0.06479891 / 1000.0;
        double muzzleVelMps = velMps[^1];
        double bulletKeJ = 0.5 * bulletMassKg * muzzleVelMps * muzzleVelMps;
        double barrelPeakVelMps = maxVelMmMs; // numerically identical, mm/ms == m/s
        // crude proxy using total barrel mass at peak transverse velocity (upper bound, ignores modal
        // mass distribution) - only meant to confirm "many orders of magnitude smaller", not a precise figure
        Console.WriteLine(string.Format(ci, "  (context) bullet KE at muzzle: {0:0.0} J; barrel peak transverse speed: {1:0.000000} m/s", bulletKeJ, barrelPeakVelMps));
        Check("muzzle transverse displacement stays well under 1mm (a barrel doesn't visibly whip that far from one shot)",
            maxDispMm < 1.0, maxDispMm);
        Check("all zero-crossings fall within [0, barrel time]",
            crossings.All(t => t >= 0 && t <= results.BarrelTimeMs + 1e-6), crossings.Length);
    }

    private static void Check(string label, bool ok, double value)
    {
        var ci = CultureInfo.InvariantCulture;
        Console.WriteLine(string.Format(ci, "  {0}: {1}  (value={2:0.000000})", label, ok ? "OK" : "FAIL", value));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);
}
