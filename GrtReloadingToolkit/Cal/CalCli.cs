using System.Globalization;
using GrtPluginKit.Analysis;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;

namespace GrtReloadingToolkit.Cal;

/// <summary>
/// Console harness:  --cal &lt;ipcport&gt; &lt;base.grtload&gt; &lt;charge:simMv&gt; [&lt;charge:simMv&gt; ...]
/// Loads measured MV from the base file's Measurements and pairs with the given sim MVs.
/// The sim MVs are the ones you type — this exercises the calibration maths, not GRT's solver.
/// For a real per-charge sweep of GRT use the Barrel Calibration window (<c>SweepAll</c>).
/// </summary>
internal static class CalCli
{
    public static async Task RunAsync(string[] args)
    {
        try { AttachConsole(-1); } catch { }
        int port = int.Parse(args[1]);
        string basePath = args[2];

        using var grt = new GrtClient(port);
        grt.Log += l => Console.WriteLine("  [ipc] " + l);
        grt.Connect();
        await Task.Delay(300);

        var top = await grt.GetTabOnTopAsync();
        Console.WriteLine($"tab: {top.caption} [{top.file}]");

        var doc = GrtLoadDoc.Load(File.Exists(top.file) ? top.file : basePath);
        double? ba = doc.PropellantBa;
        Console.WriteLine($"caliber={doc.CaliberName}  powder={doc.PropellantName}  Ba={ba}");

        var meas = new SortedDictionary<double, double>();
        foreach (var m in doc.Measurements())
            foreach (var c in m.Charges)
            {
                var v = c.Shots.Select(s => s.VelocityMps).Where(x => x > 0).ToList();
                if (v.Count > 0 && c.ChargeGrains is { } g) meas[Math.Round(g, 2)] = StringStats.From(v).Mean;
            }

        // The simMv of each charge:simMv pair is used as given. This harness is the calibration
        // *maths* — GRT is read for the load on top and its Ba, not swept: nothing here tells GRT
        // to change the charge, so querying it once per pair returned the same simulated MV every
        // time, for every charge. (It set MOCK_SIM_MV first, which nothing in the repo reads.)
        // The real per-charge sweep is Ui/CalibrationForm.SweepAll, which writes a one-shot load
        // per charge and opens each in GRT.
        var result = new CalResult();
        foreach (string spec in args.Skip(3))
        {
            var parts = spec.Split(':');
            if (parts.Length != 2
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double chg)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double simMv))
            {
                Console.WriteLine($"  skipped '{spec}': expected <charge>:<simMv>, e.g. 39.2:812.5");
                continue;
            }
            double measMv = meas.TryGetValue(Math.Round(chg, 2), out double mv) ? mv : 0;
            result.Points.Add(new CalPoint { ChargeGr = chg, SimMps = simMv, MeasMps = measMv });
            Console.WriteLine($"  {chg} gr: sim {simMv:0.0}, meas {measMv:0.0}"
                + (measMv > 0 ? "" : "   (no measured velocity at this charge in the load)"));
        }

        if (result.Points.Count == 0) { Console.WriteLine("no usable charge:simMv pairs — nothing to report."); return; }

        Console.WriteLine();
        Console.WriteLine(result.BuildReport($"Barrel Calibration {DateTime.Now:yyyy-MM-dd}", ba is > 0 ? ba : null,
            string.IsNullOrWhiteSpace(doc.PropellantName) ? "powder" : doc.PropellantName));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);
}
