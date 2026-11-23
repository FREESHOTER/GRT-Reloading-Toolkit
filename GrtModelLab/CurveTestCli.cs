using System.Globalization;
using GrtPluginKit.Ipc;

namespace GrtModelLab;

/// <summary>
/// Console harness: --curvetest &lt;ipcport&gt; — the trust-building step for
/// <see cref="GrtClient.GetTabResultsWithCurveAsync"/> before anything (a forced-response modal
/// calculation) gets built on top of it. Needs a LIVE GRT with a computed tab, unlike --femtest/
/// --beamtest which run fully offline - the P/V/t curve only exists inside a running simulation.
/// Sanity checks: the curve's own last point should match the scalar MuzzleVelocity/BarrelTime GRT
/// already reports (same load, read two different ways) - if a chunk-reading bug silently dropped or
/// misparsed data, this is the cheapest way to notice before trusting the curve for real math.
/// </summary>
internal static class CurveTestCli
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
        Console.WriteLine($"Reading curve for tab '{top.caption}' (handle {top.handle})...");

        var (results, curve) = await grt.GetTabResultsWithCurveAsync(top.handle);

        Console.WriteLine();
        Console.WriteLine("== Scalars (already-trusted fields) ==");
        Console.WriteLine(string.Format(ci, "  MuzzleVelocity: {0:0.000} m/s", results.MuzzleVelocityMps));
        Console.WriteLine(string.Format(ci, "  MaxPressure:    {0:0.0} {1}", results.MaxPressure, results.MaxPressureUnit));
        Console.WriteLine(string.Format(ci, "  BarrelTime (MuzzleTime): {0:0.0000} ms", results.BarrelTimeMs));
        Console.WriteLine(string.Format(ci, "  BulletLeadTime10Pmax:    {0:0.0000} ms", results.BulletLeadTime10PmaxMs));

        Console.WriteLine();
        Console.WriteLine($"== Curve: {curve.Length} point(s) ==");
        if (curve.Length == 0) { Console.WriteLine("  (empty - nothing more to check)"); return; }

        var first = curve[0];
        var last = curve[^1];
        Console.WriteLine(string.Format(ci, "  first: t={0:0.0000}ms x={1:0.000}mm xp={2:0.000}mm p={3:0.0}bar v={4:0.000}m/s", first.TimeMs, first.PositionMm, first.ProjectilePositionMm, first.PressureBar, first.VelocityMps));
        Console.WriteLine(string.Format(ci, "  last:  t={0:0.0000}ms x={1:0.000}mm xp={2:0.000}mm p={3:0.0}bar v={4:0.000}m/s z={5:0.000}", last.TimeMs, last.PositionMm, last.ProjectilePositionMm, last.PressureBar, last.VelocityMps, last.BurnedFraction));
        foreach (int idx in new[] { 0, curve.Length / 4, curve.Length / 2, curve.Length * 3 / 4, curve.Length - 1 })
            Console.WriteLine(string.Format(ci, "  [{0,4}] t={1:0.0000}ms x={2:0.000}mm xp={3:0.000}mm v={4:0.000}m/s", idx, curve[idx].TimeMs, curve[idx].PositionMm, curve[idx].ProjectilePositionMm, curve[idx].VelocityMps));

        double maxP = curve.Max(p => p.PressureBar);
        int maxPIdx = Array.FindIndex(curve, p => p.PressureBar == maxP);
        Console.WriteLine(string.Format(ci, "  peak pressure in curve: {0:0.0} bar at t={1:0.0000}ms (point {2}/{3})", maxP, curve[maxPIdx].TimeMs, maxPIdx, curve.Length));

        Console.WriteLine();
        Console.WriteLine("== Sanity checks: does the curve agree with the scalars GRT reports separately? ==");
        Check("curve's last velocity matches MuzzleVelocity (within 0.5 m/s)",
            results.MuzzleVelocityMps is { } mv && Math.Abs(last.VelocityMps - mv) < 0.5, Math.Abs(last.VelocityMps - (results.MuzzleVelocityMps ?? 0)));
        Check("curve's last time matches BarrelTime/MuzzleTime (within 0.01 ms)",
            results.BarrelTimeMs is { } bt && Math.Abs(last.TimeMs - bt) < 0.01, Math.Abs(last.TimeMs - (results.BarrelTimeMs ?? 0)));
        Check("curve's peak pressure matches MaxPressure (within 5 bar)",
            results.MaxPressure is { } mp && Math.Abs(maxP - mp) < 5.0, Math.Abs(maxP - (results.MaxPressure ?? 0)));
        Check("first point starts at t=0", Math.Abs(first.TimeMs) < 1e-9, first.TimeMs);
        Check("time is monotonically non-decreasing across the curve",
            curve.Zip(curve.Skip(1), (a, b) => b.TimeMs >= a.TimeMs).All(ok => ok), 0);
        Check("pressure rises then falls (single peak, not noise)", maxPIdx > 0 && maxPIdx < curve.Length - 1, maxPIdx);
    }

    private static void Check(string label, bool ok, double value)
    {
        var ci = CultureInfo.InvariantCulture;
        Console.WriteLine(string.Format(ci, "  {0}: {1}  (value={2:0.0000})", label, ok ? "OK" : "FAIL", value));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);
}
