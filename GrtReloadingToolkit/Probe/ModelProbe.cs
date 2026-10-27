using System.Globalization;
using System.Text;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;

namespace GrtReloadingToolkit.Probe;

/// <summary>What the probe reads back from GRT for every point of a sweep.</summary>
internal enum ProbeOutput
{
    /// <summary>Muzzle velocity, m/s (Get_TabResults).</summary>
    MuzzleVelocity,
    /// <summary>Peak chamber pressure, in the unit GRT reports it in (Get_TabResults).</summary>
    MaxPressure,
    /// <summary>Barrel time from the start of the simulation, ms (Get_TabResults "MuzzleTime").</summary>
    BarrelTime,
    /// <summary>Barrel time counted from 10 % of Pmax, ms -- the time GRT's own OBT nodes are matched on.</summary>
    BulletLeadTime10Pmax,
    /// <summary>Time from ignition to peak pressure, ms (read off the P/V/t curve).</summary>
    TimeOfPmax,
    /// <summary>Gas pressure at the muzzle as a percentage of the peak (both off the same curve, so unit-free).</summary>
    MuzzlePressurePct,
    /// <summary>Fraction of the charge burnt when the bullet leaves the muzzle, % (curve, last point).</summary>
    BurntAtMuzzlePct,
    /// <summary>Time at which the charge is all burnt, ms; absent when it is not all burnt in the barrel.</summary>
    BurnoutTime,
    /// <summary>Bullet kinetic energy over the chemical energy of the charge (mc · Qex), %.</summary>
    Efficiency,
}

/// <summary>One simulated point: the probed input held at <see cref="X"/> (in the file's own unit), everything else as in the load.</summary>
internal sealed class ProbePoint
{
    public required double X { get; init; }
    public bool IsBaseline { get; init; }
    public Dictionary<ProbeOutput, double> Y { get; } = new();
    public string PressureUnit { get; set; } = "";
    public double? Get(ProbeOutput o) => Y.TryGetValue(o, out double v) ? v : null;
}

/// <summary>
/// How one output answers the probed input. <see cref="Elasticity"/> is the local answer at the load's own value:
/// a 1 % change of the input moves the output by that many percent (a central difference over the two points either
/// side). <see cref="Exponent"/> is the global answer: the b of output ≈ a · input^b fitted over the whole sweep in
/// log-log, with <see cref="R2"/> saying how well a single power law describes it. An elasticity that differs from the
/// exponent, or a low R², means the response bends inside the range.
/// </summary>
internal sealed record ProbeSensitivity(ProbeOutput Output, double? Baseline, double? Elasticity, double? Exponent, double? R2, double? Min, double? Max);

/// <summary>
/// The pure half of the model probe: which inputs it offers, the values a sweep visits, how one value is written into a
/// load, what one GRT answer becomes, and the sensitivities over a finished sweep. No IPC and no UI, so the tests run it
/// against real .grtload fixtures; <see cref="ProbeRunner"/> is the half that drives GRT.
/// </summary>
internal static class ModelProbe
{
    /// <summary>
    /// Decimal inputs the probe does not offer: the ladder (the probe runs one charge at a time), fields GRT documents as
    /// secondary or outside the interior-ballistic simulation (twist, gun weight, the external-ballistics BCs, the bullet
    /// base type code, the allowed maximum it only compares against), and the COAL, which follows from case length,
    /// bullet length and seating depth and is moved with them (see <see cref="Apply"/>).
    /// </summary>
    public static readonly IReadOnlySet<string> NotOffered = new HashSet<string>(StringComparer.Ordinal)
    {
        "laddercnt", "laddermc", "twistlen", "mgun", "g1bc", "g7bc", "gTailType", "pMaxZul", "oal",
    };

    /// <summary>The inputs of <paramref name="doc"/> a sweep can move, propellant first (the model's own constants
    /// are what the probe is mostly for), then the order GRT's file has them in.</summary>
    public static IReadOnlyList<GrtInput> Offered(GrtLoadDoc doc)
    {
        static int Rank(string section) => section switch { "propellant" => 0, "caliber" => 1, "projectile" => 2, "gun" => 3, _ => 4 };
        return doc.DecimalInputs()
            .Where(i => !NotOffered.Contains(i.Name))
            .Select((input, order) => (input, order))
            .OrderBy(t => Rank(t.input.Section)).ThenBy(t => t.order)
            .Select(t => t.input)
            .ToList();
    }

    /// <summary>
    /// <paramref name="points"/> evenly spaced values from <paramref name="from"/> to <paramref name="to"/>, plus the
    /// load's own <paramref name="baseline"/> when the spacing does not land on it, sorted. Every sweep carries the
    /// baseline, because the elasticity is taken there and a result that does not reproduce GRT's own tab is one to distrust.
    /// </summary>
    public static IReadOnlyList<double> Values(double from, double to, int points, double baseline)
    {
        if (points < 2) throw new ArgumentOutOfRangeException(nameof(points), "a sweep needs at least two points");
        if (from == to) throw new ArgumentException(Tr.T("the sweep's two ends are the same value"));
        if (from > to) (from, to) = (to, from);

        var values = new List<double>(points + 1);
        for (int i = 0; i < points; i++) values.Add(from + (to - from) * i / (points - 1));
        double tol = (to - from) / (points - 1) * 1e-6;
        int near = values.FindIndex(v => Math.Abs(v - baseline) <= tol);
        if (near >= 0) values[near] = baseline;
        else values.Add(baseline);
        values.Sort();
        return values;
    }

    /// <summary>The default sweep: ±10 % around the load's value, or 0..1 in the file's unit when the value is 0 and a
    /// percentage of it would be no range at all (bullet friction, a zero jump).</summary>
    public static (double From, double To) DefaultRange(double baseline) =>
        baseline == 0 ? (0, 1) : (Math.Min(baseline * 0.9, baseline * 1.1), Math.Max(baseline * 0.9, baseline * 1.1));

    /// <summary>
    /// Writes <paramref name="value"/> into <paramref name="input"/> of <paramref name="doc"/>, keeping the file's unit, and
    /// turns the ladder off so GRT simulates exactly one charge. Moving case length, bullet length or seating depth moves the
    /// COAL with them, the way GRT's own editor does (see <see cref="GrtLoadDoc.CoalFrom"/>): a load opened from disk keeps
    /// whatever COAL it carries, and a stale one would be a second, unintended change.
    /// </summary>
    public static void Apply(GrtLoadDoc doc, GrtInput input, double value)
    {
        if (!doc.SetInput(input.Section, input.Name, value.ToString("0.############", CultureInfo.InvariantCulture)))
            throw new InvalidOperationException(Tr.F("the load has no input {0}/{1}", input.Section, input.Name));
        doc.SetInput("propellant", "laddercnt", "0");
        if (input.Name is "caselen" or "glen" or "gdepth"
            && GrtLoadDoc.CoalFrom(doc.CaseLenMm, doc.BulletLengthMm, doc.SeatingDepthMm) is { } coal)
            doc.SetInput("caliber", "oal", coal.ToString("0.############", CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// One point from one GRT answer. The curve-derived outputs assume the curve's last point is the muzzle (GRT's
    /// own Get_TabResults example ends where its EndVelocity is), and they are left out when the curve was not read.
    /// The efficiency takes bullet mass, charge and Qex from <paramref name="simulated"/>, the load as it was simulated.
    /// </summary>
    public static ProbePoint PointFrom(double x, bool isBaseline, GrtResults r, IReadOnlyList<GrtCurvePoint> curve, GrtLoadDoc simulated)
    {
        var p = new ProbePoint { X = x, IsBaseline = isBaseline, PressureUnit = r.MaxPressureUnit };
        if (r.MuzzleVelocityMps is { } mv) p.Y[ProbeOutput.MuzzleVelocity] = mv;
        if (r.MaxPressure is { } pm) p.Y[ProbeOutput.MaxPressure] = pm;
        if (r.BarrelTimeMs is { } bt) p.Y[ProbeOutput.BarrelTime] = bt;
        if (r.BulletLeadTime10PmaxMs is { } blt) p.Y[ProbeOutput.BulletLeadTime10Pmax] = blt;

        if (curve.Count > 0)
        {
            var peak = curve.MaxBy(c => c.PressureBar)!;
            p.Y[ProbeOutput.TimeOfPmax] = peak.TimeMs;
            if (peak.PressureBar > 0) p.Y[ProbeOutput.MuzzlePressurePct] = curve[^1].PressureBar / peak.PressureBar * 100;
            p.Y[ProbeOutput.BurntAtMuzzlePct] = curve[^1].BurnedFraction * 100;
            if (curve.FirstOrDefault(c => c.BurnedFraction >= 0.9999) is { } burnout) p.Y[ProbeOutput.BurnoutTime] = burnout.TimeMs;
        }

        double? mpG = simulated.BulletMassGr * 0.06479891, mcG = simulated.PropellantChargeGr * 0.06479891;
        double? qex = simulated.InputNumber("propellant", "Qex");
        if (r.MuzzleVelocityMps is { } v && mpG is > 0 && mcG is > 0 && qex is > 0)
            p.Y[ProbeOutput.Efficiency] = 0.5 * (mpG.Value / 1000) * v * v / ((mcG.Value / 1000) * qex.Value * 1000) * 100;
        return p;
    }

    /// <summary>The sensitivity of every output the sweep has at least two values of.</summary>
    public static IReadOnlyList<ProbeSensitivity> Analyze(IReadOnlyList<ProbePoint> points)
    {
        var sorted = points.OrderBy(p => p.X).ToList();
        var list = new List<ProbeSensitivity>();
        foreach (ProbeOutput o in Enum.GetValues<ProbeOutput>())
        {
            var have = sorted.Where(p => p.Get(o) is not null).ToList();
            if (have.Count < 2) continue;
            double? baseY = have.FirstOrDefault(p => p.IsBaseline)?.Get(o);
            var (exp, r2) = PowerFit(have.Select(p => (p.X, p.Get(o)!.Value)).ToList());
            list.Add(new ProbeSensitivity(o, baseY, Elasticity(have, o), exp, r2, have.Min(p => p.Get(o)), have.Max(p => p.Get(o))));
        }
        return list;
    }

    /// <summary>d ln y / d ln x at the baseline, from the points either side of it (one side at the edge of the sweep).
    /// Null without a baseline, at an input or output of zero, or when the neighbours have the same input.</summary>
    internal static double? Elasticity(IReadOnlyList<ProbePoint> sortedWithOutput, ProbeOutput o)
    {
        int i = sortedWithOutput.ToList().FindIndex(p => p.IsBaseline);
        if (i < 0) return null;
        var b = sortedWithOutput[i];
        double y0 = b.Get(o)!.Value;
        if (b.X == 0 || y0 == 0) return null;
        var lo = sortedWithOutput[Math.Max(0, i - 1)];
        var hi = sortedWithOutput[Math.Min(sortedWithOutput.Count - 1, i + 1)];
        if (hi.X == lo.X) return null;
        double slope = (hi.Get(o)!.Value - lo.Get(o)!.Value) / (hi.X - lo.X);
        return slope * b.X / y0;
    }

    /// <summary>Least squares of ln y on ln x: the exponent b and R². Null unless there are three or more points, all
    /// with positive input and output (a power law has no logarithm otherwise) and some spread in the input.</summary>
    internal static (double? Exponent, double? R2) PowerFit(IReadOnlyList<(double X, double Y)> pts)
    {
        if (pts.Count < 3 || pts.Any(p => p.X <= 0 || p.Y <= 0)) return (null, null);
        var lx = pts.Select(p => Math.Log(p.X)).ToArray();
        var ly = pts.Select(p => Math.Log(p.Y)).ToArray();
        double mx = lx.Average(), my = ly.Average(), sxx = 0, sxy = 0, syy = 0;
        for (int i = 0; i < lx.Length; i++)
        {
            sxx += (lx[i] - mx) * (lx[i] - mx);
            sxy += (lx[i] - mx) * (ly[i] - my);
            syy += (ly[i] - my) * (ly[i] - my);
        }
        if (sxx < 1e-18) return (null, null);
        double b = sxy / sxx;
        double r2 = syy < 1e-18 ? 1 : sxy * sxy / (sxx * syy);
        return (b, r2);
    }

    /// <summary>The sweep as CSV, invariant culture, one row per point: the input, then every output (empty where absent).</summary>
    public static string Csv(GrtInput input, IReadOnlyList<ProbePoint> points)
    {
        var ci = CultureInfo.InvariantCulture;
        var outs = Enum.GetValues<ProbeOutput>();
        var sb = new StringBuilder();
        sb.Append(input.Section).Append('.').Append(input.Name);
        if (input.Unit.Length > 0) sb.Append(" [").Append(input.Unit).Append(']');
        sb.Append(",baseline");
        foreach (var o in outs) sb.Append(',').Append(o);
        sb.Append("\r\n");
        foreach (var p in points.OrderBy(p => p.X))
        {
            sb.Append(p.X.ToString("R", ci)).Append(',').Append(p.IsBaseline ? "1" : "0");
            foreach (var o in outs) sb.Append(',').Append(p.Get(o) is { } v ? v.ToString("R", ci) : "");
            sb.Append("\r\n");
        }
        return sb.ToString();
    }
}

/// <summary>
/// Drives GRT through one sweep: for each value, a copy of the load with that one input changed is written to a
/// temporary folder, opened in GRT, waited for until GRT has computed it, and read back. The user's own file is
/// never written; it is reopened at the end. GRT's plugin API has no command to close a tab, so every point
/// leaves one open tab behind, as Barrel calibration's sweep does.
/// </summary>
internal static class ProbeRunner
{
    /// <summary>How long a point waits for Event_TabComputed before reading anyway: the 1.4 s Barrel calibration
    /// sleeps, with room to spare, since without the event this is the whole of the wait.</summary>
    public static readonly TimeSpan ComputeWait = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Runs the sweep into <paramref name="points"/>, one point at a time, so whatever was read survives a Stop or an
    /// error: a stop ends the sweep quietly, an error is thrown after the points before it are already in the list.
    /// </summary>
    public static async Task RunAsync(GrtClient grt, string basePath, GrtInput input, IReadOnlyList<double> values,
        bool readCurve, List<ProbePoint> points, Action<string> log, Func<string, string> tr, CancellationToken ct)
    {
        string run = Guid.NewGuid().ToString("N")[..8];
        string tmpDir = Path.Combine(Path.GetTempPath(), "grt_probe_" + run);
        Directory.CreateDirectory(tmpDir);
        bool? eventSeen = null;
        try
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (ct.IsCancellationRequested) { log(tr("Stopped.")); break; }
                double x = values[i];
                var doc = GrtLoadDoc.Load(basePath);
                ModelProbe.Apply(doc, input, x);
                // The run id makes every name unique, so a tab left open by an earlier sweep is never mistaken for this point.
                string tmp = Path.Combine(tmpDir, string.Format(CultureInfo.InvariantCulture, "probe_{0}_{1}_{2:00}.grtload", run, input.Name, i));
                doc.Save(tmp);

                bool seen = await grt.LoadFileAndWaitForComputeAsync(tmp, ComputeWait);
                if (eventSeen is null)
                {
                    eventSeen = seen;
                    log(seen ? tr("GRT reports each computation (TabComputed): no fixed waits.")
                             : tr("No TabComputed event from GRT: waiting a fixed time per point."));
                }

                var top = await grt.GetTabOnTopAsync();
                if (!SamePath(top.file, tmp))
                {
                    log(string.Format(CultureInfo.InvariantCulture, tr("{0}: GRT shows '{1}' instead of the probe's file, point skipped."), Fmt(x), top.file));
                    continue;
                }
                GrtResults res;
                IReadOnlyList<GrtCurvePoint> curve = Array.Empty<GrtCurvePoint>();
                if (readCurve) { var rc = await grt.GetTabResultsWithCurveAsync(top.handle); res = rc.Results; curve = rc.Curve; }
                else res = await grt.GetTabResultsAsync(top.handle);

                var p = ModelProbe.PointFrom(x, x == input.Value, res, curve, doc);
                points.Add(p);
                log(string.Format(CultureInfo.InvariantCulture, "{0} -> MV {1}, Pmax {2} {3}", Fmt(x),
                    p.Get(ProbeOutput.MuzzleVelocity) is { } mv ? mv.ToString("0.0", CultureInfo.InvariantCulture) : "-",
                    p.Get(ProbeOutput.MaxPressure) is { } pm ? pm.ToString("0", CultureInfo.InvariantCulture) : "-", p.PressureUnit));
            }
        }
        finally
        {
            try { await grt.LoadFileAsync(basePath); } catch { /* the user can switch back by hand */ }
            try { Directory.Delete(tmpDir, true); } catch { /* GRT may still hold a file open */ }
        }
    }

    private static string Fmt(double x) => x.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>GRT reports paths with forward slashes, and may spell the temp folder its own way (a short 8.3 name);
    /// the file name, unique to this run, is what settles it when the full paths differ.</summary>
    internal static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        string A = a.Replace('/', '\\'), B = b.Replace('/', '\\');
        try { if (string.Equals(Path.GetFullPath(A), Path.GetFullPath(B), StringComparison.OrdinalIgnoreCase)) return true; }
        catch { /* not a path this machine can resolve: fall through to the name */ }
        return string.Equals(A[(A.LastIndexOf('\\') + 1)..], B[(B.LastIndexOf('\\') + 1)..], StringComparison.OrdinalIgnoreCase);
    }
}
