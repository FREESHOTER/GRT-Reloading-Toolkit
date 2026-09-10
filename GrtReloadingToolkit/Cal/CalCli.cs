using System.Globalization;
using GrtPluginKit.Analysis;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;

namespace GrtReloadingToolkit.Cal;

/// <summary>
/// Console harness:  --cal &lt;ipcport&gt; &lt;base.grtload&gt; &lt;charge:simMv&gt; [&lt;charge:simMv&gt; ...]
/// Loads measured MV from the base file's Measurements and pairs with the given sim MVs.
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

        var result = new CalResult();
        foreach (string spec in args.Skip(3))
        {
            var parts = spec.Split(':');
            double chg = double.Parse(parts[0], CultureInfo.InvariantCulture);
            Environment.SetEnvironmentVariable("MOCK_SIM_MV", parts[1]);
            var r = await grt.GetTabResultsAsync(top.handle);
            double measMv = meas.TryGetValue(Math.Round(chg, 2), out double mv) ? mv : 0;
            result.Points.Add(new CalPoint { ChargeGr = chg, SimMps = r.MuzzleVelocityMps ?? 0, MeasMps = measMv });
            Console.WriteLine($"  captured {chg} gr: sim {r.MuzzleVelocityMps:0.0}, meas {measMv:0.0}");
        }

        Console.WriteLine();
        Console.WriteLine(result.BuildReport($"Barrel Calibration {DateTime.Now:yyyy-MM-dd}", ba is > 0 ? ba : null,
            string.IsNullOrWhiteSpace(doc.PropellantName) ? "powder" : doc.PropellantName));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);
}
