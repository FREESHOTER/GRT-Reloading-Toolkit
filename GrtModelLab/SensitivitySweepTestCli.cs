using System.Globalization;
using GrtModelLab.SensitivitySweep;
using GrtPluginKit.Grt;

namespace GrtModelLab;

/// <summary>
/// Console harness: --sweeptest &lt;real.grtload&gt; — exercises SweepEngine against a real load file
/// (no GRT process, no IPC) to check that every swept input is found and nudged correctly, and that
/// SweepReport formats/sorts as expected on synthetic MV numbers. No GRT connection needed.
/// </summary>
internal static class SensitivitySweepTestCli
{
    public static void Run(string? loadPath)
    {
        try { AttachConsole(-1); } catch { }
        var ci = CultureInfo.InvariantCulture;

        if (string.IsNullOrWhiteSpace(loadPath) || !File.Exists(loadPath))
        {
            Console.WriteLine("usage: --sweeptest <path-to-a-real.grtload>");
            return;
        }

        Console.WriteLine("== SweepEngine.ApplyVariant against every parameter ==");
        foreach (var p in SweepParams.All)
        {
            var baseline = GrtLoadDoc.Load(loadPath);
            double? before = p.Name == "mp" ? baseline.BulletMassGr : baseline.InputNumber(p.Section, p.Name);
            foreach (var v in SweepEngine.BuildVariants(p, p.Delta))
            {
                var doc = GrtLoadDoc.Load(loadPath);
                double written = SweepEngine.ApplyVariant(doc, p, v.Value, v.Absolute);
                double? after = doc.InputNumber(p.Section, p.Name);
                bool ok = Math.Abs((after ?? double.NaN) - written) < 1e-6;
                Console.WriteLine(string.Format(ci, "{0,-40} {1,-14} before={2,10:0.######} written={3,10:0.######} readback-ok={4}",
                    p.Label, v.Label, before, written, ok));
            }
        }

        Console.WriteLine();
        Console.WriteLine("== SweepReport formatting on synthetic numbers ==");
        var report = new SweepReport { BaselineMv = 820.0, BaselinePmax = 3200, PmaxUnit = "bar" };
        report.Points.Add(new SweepPoint { Param = SweepParams.All[0], VariantLabel = "+0.02 cm3", MvMps = 818.4, Pmax = 3150, PmaxUnit = "bar" });
        report.Points.Add(new SweepPoint { Param = SweepParams.All[0], VariantLabel = "-0.02 cm3", MvMps = 821.9, Pmax = 3240, PmaxUnit = "bar" });
        report.Points.Add(new SweepPoint { Param = SweepParams.All[2], VariantLabel = "+2 mm", MvMps = 824.1, Pmax = 3190, PmaxUnit = "bar" });
        report.Points.Add(new SweepPoint { Param = SweepParams.All[2], VariantLabel = "-2 mm", MvMps = 815.6, Pmax = 3180, PmaxUnit = "bar" });
        report.Points.Add(new SweepPoint { Param = SweepParams.All[6], VariantLabel = "+20 bar", MvMps = 819.5, Pmax = 3300, PmaxUnit = "bar" });
        Console.WriteLine(report.Build("Test Caliber", "Test Powder"));

        var ordered = report.BySensitivity().ToList();
        bool sorted = ordered.SequenceEqual(ordered.OrderByDescending(p => Math.Abs((p.MvMps ?? 0) - 820.0)));
        Console.WriteLine("sorted by |dMV| descending: " + sorted);

        Console.WriteLine();
        Console.WriteLine("== regression: negative-rounds-to-zero double-sign bug (.NET 8 only, see SweepReport.Signed) ==");
        var zeroCase = new SweepReport { BaselineMv = 845.4, BaselinePmax = 3673, PmaxUnit = "bar" };
        // tiny negative floating-point noise around an otherwise-unchanged result — exactly the shape
        // a real "no effect" GRT variant produces, and what live-testing against a real load caught
        // as a literal "-+0"/"-+0.0" in the written note before this test existed.
        zeroCase.Points.Add(new SweepPoint { Param = SweepParams.All[2], VariantLabel = "+2 mm", MvMps = 845.4 - 1e-10, Pmax = 3673 - 1e-10, PmaxUnit = "bar" });
        string zeroText = zeroCase.Build("Test Caliber", "Test Powder");
        bool clean = !zeroText.Contains("-+");
        Console.WriteLine(clean ? "OK no double-sign artifact" : "FAIL found '-+' in report text:\n" + zeroText);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);
}
