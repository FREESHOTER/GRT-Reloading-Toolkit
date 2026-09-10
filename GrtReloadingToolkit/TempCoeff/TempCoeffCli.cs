using System.Globalization;
using GrtReloadingToolkit.Athlon;
using GrtPluginKit.Analysis;

namespace GrtReloadingToolkit.TempCoeff;

/// <summary>Console harness:  --tcoeff &lt;Ba&gt; &lt;file.xlsx:tempC&gt; &lt;file.xlsx:tempC&gt; ...</summary>
internal static class TempCoeffCli
{
    public static void Run(string[] args)
    {
        try { AttachConsole(-1); } catch { }
        double ba = double.Parse(args[1], CultureInfo.InvariantCulture);

        var pts = new List<TempPoint>();
        foreach (string spec in args.Skip(2))
        {
            int c = spec.LastIndexOf(':');
            string file = spec[..c];
            double temp = double.Parse(spec[(c + 1)..], CultureInfo.InvariantCulture);
            var a = AthlonParser.Parse(file);
            var st = StringStats.From(a.Velocities.ToList());
            pts.Add(new TempPoint { File = file, ChargeGr = a.ChargeGrains ?? 0, TempC = temp, N = st.N, MeanMps = st.Mean, SdMps = st.Sd });
            Console.WriteLine($"  {Path.GetFileName(file)} @ {temp}°C : {st.N} shots, {st.Mean:0.0} m/s");
        }

        var r = TempCoeffResult.Fit(pts, ba, "N550");
        Console.WriteLine();
        Console.WriteLine(r.BuildReport($"Powder Temp Coefficients {DateTime.Now:yyyy-MM-dd}"));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);
}
