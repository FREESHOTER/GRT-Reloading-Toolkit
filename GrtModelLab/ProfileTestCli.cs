using GrtModelLab.BarrelProfile;
using GrtModelLab.Ui;

namespace GrtModelLab;

/// <summary>Console harness: --profiletest [out.png] — a synthetic tapered profile, no fixtures needed.</summary>
internal static class ProfileTestCli
{
    public static void Run(string? outPng)
    {
        try { AttachConsole(-1); } catch { }

        var rec = new BarrelProfileRecord { Name = "Synthetic taper", BoreDiameterMm = 6.71, LengthMm = 660 };
        // a believable taper: heavy at the breech, necking down toward the muzzle
        (double pos, double od)[] profile =
        {
            (0, 30), (100, 28), (250, 24), (400, 20), (550, 17), (660, 15.5),
        };
        foreach (var (pos, od) in profile) rec.Points.Add(new BarrelProfilePoint { PositionMm = pos, OuterDiameterMm = od });

        var chart = new BarrelProfileChart();
        chart.Show(rec, inches: false);
        byte[]? png = chart.RenderPng();
        Console.WriteLine(png != null ? $"chart PNG: {png.Length} bytes" : "chart PNG: (none)");
        if (png == null || string.IsNullOrWhiteSpace(outPng)) return;
        File.WriteAllBytes(outPng, png);
        Console.WriteLine("wrote " + Path.GetFullPath(outPng));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);
}
