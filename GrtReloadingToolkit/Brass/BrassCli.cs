using System.Globalization;

namespace GrtReloadingToolkit.Brass;

/// <summary>
/// Debug CLI:
///   --brass vol gr  <g1> <g2> ...        water weights in grains H2O
///   --brass vol g   <g1> <g2> ...        water weights in grams
///   --brass neck <bulletDiaMm> <wallMm> <interfMm> [loadedOdMm]
///   --brass seat <cbto> <caselen> <bbto> [mm|in]     (default mm)
/// </summary>
internal static class BrassCli
{
    public static void Run(string[] args)
    {
        var ci = CultureInfo.InvariantCulture;
        string sub = args.Length >= 2 ? args[1] : "";

        if (sub == "vol")
        {
            bool grams = args.Length >= 3 && args[2].StartsWith("g", StringComparison.OrdinalIgnoreCase) && args[2].Length == 1;
            var raw = args.Skip(3).Select(a => double.Parse(a, NumberStyles.Float, ci)).ToList();
            var grains = grams ? raw.Select(BrassCalc.GramsToGrains).ToList() : raw;
            var r = BrassCalc.CaseVolume(grains);
            Console.WriteLine($"n={r.N}");
            Console.WriteLine($"mean : {r.MeanGr.ToString("0.00", ci)} grain H2O   ({r.MeanCm3.ToString("0.000", ci)} cm3)");
            Console.WriteLine($"SD   : {r.SdGr.ToString("0.00", ci)} gr   ({r.SdPct.ToString("0.0", ci)} %)");
            Console.WriteLine($"range: {r.MinGr.ToString("0.00", ci)} - {r.MaxGr.ToString("0.00", ci)} gr");
            return;
        }

        if (sub == "neck" && args.Length >= 5)
        {
            double dia = double.Parse(args[2], NumberStyles.Float, ci);
            double wall = double.Parse(args[3], NumberStyles.Float, ci);
            double interf = double.Parse(args[4], NumberStyles.Float, ci);
            double? loaded = args.Length >= 6 ? double.Parse(args[5], NumberStyles.Float, ci) : null;
            var r = BrassCalc.Neck(dia, wall, interf, loaded);
            Console.WriteLine($"loaded neck OD : {r.LoadedNeckOdMm.ToString("0.000", ci)} mm");
            Console.WriteLine($"BUSHING OD     : {r.BushingOdMm.ToString("0.000", ci)} mm  ({(r.BushingOdMm / 25.4).ToString("0.0000", ci)} in)");
            Console.WriteLine($"MANDREL OD     : {r.MandrelOdMm.ToString("0.000", ci)} mm  ({(r.MandrelOdMm / 25.4).ToString("0.0000", ci)} in)");
            if (r.Notes.Length > 0) Console.WriteLine("note: " + r.Notes);
            return;
        }

        if (sub == "seat" && args.Length >= 5)
        {
            bool inch = args.Length >= 6 && args[5].StartsWith("in", StringComparison.OrdinalIgnoreCase);
            double f = inch ? BrassCalc.MmPerInch : 1.0;
            double cbto = double.Parse(args[2], NumberStyles.Float, ci) * f;
            double cl = double.Parse(args[3], NumberStyles.Float, ci) * f;
            double bbto = double.Parse(args[4], NumberStyles.Float, ci) * f;
            var r = BrassCalc.Seating(cbto, cl, bbto);
            Console.WriteLine($"DIFF          : {r.DiffMm.ToString("0.###", ci)} mm ({r.DiffIn.ToString("0.0000", ci)} in)");
            Console.WriteLine($"seating depth : {r.SeatingDepthMm.ToString("0.###", ci)} mm ({r.SeatingDepthIn.ToString("0.0000", ci)} in)  -> GRT gdepth");
            foreach (var n in r.Notes) Console.WriteLine("! " + n);
            return;
        }

        Console.WriteLine("usage: --brass vol gr|g <w..>  |  --brass neck <dia> <wall> <interf> [loadedOd]  |  --brass seat <cbto> <caselen> <bbto> [mm|in]");
    }
}
