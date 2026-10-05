using System.Globalization;
using GrtPluginKit.Grt;
using GrtPluginKit.Util;

namespace GrtReloadingToolkit.Ocw;

/// <summary>--groups &lt;load.grtload&gt; [mm|cm|in] [m|yd] [--drop-flyers]</summary>
internal static class GroupsCli
{
    public static void Run(string[] args)
    {
        var ci = CultureInfo.InvariantCulture;
        string path = args[1];
        // GRT stores mm and m whatever it displays; naming a unit on the command line is how a
        // file from someone else (a caliper reading in inches) is read.
        var (defRef, defShoot) = GrtShotGroups.GrtDefaults();
        var refU = args.Any(a => a == "mm") ? RefUnit.Mm
            : args.Any(a => a == "cm") ? RefUnit.Cm
            : args.Any(a => a == "in") ? RefUnit.Inch
            : defRef;
        var shootU = args.Any(a => a == "m") ? ShootUnit.Meters
            : args.Any(a => a == "yd") ? ShootUnit.Yards
            : defShoot;
        bool dropFlyers = args.Any(a => a == "--drop-flyers");

        // Printed back in the units GRT shows.
        var gu = GrtUnits.Current;
        var doc = GrtLoadDoc.Load(path);
        var (groups, log) = GrtShotGroups.FromDoc(doc, new GrtShotGroups.Options(refU, shootU, dropFlyers));
        foreach (var l in log) Console.WriteLine("  " + l);
        Console.WriteLine();

        foreach (var g in groups)
        {
            string dist = $"{gu.DistanceValue(g.DistanceM).ToString("0", ci)} {gu.DistanceUnitName}";
            Console.WriteLine($"{g.SourceFile}  charge/step={g.ChargeGrains?.ToString(Str.StepFormat, ci) ?? "?"}  dist={dist}");
            Console.WriteLine($"  n={g.Impacts.Count}  center=({g.CenterXMoa.ToString("0.00", ci)}, {g.CenterYMoa.ToString("0.00", ci)}) MOA");
            Console.WriteLine($"  mean radius={g.MeanRadiusMoa.ToString("0.00", ci)} MOA   ES={g.GroupEsMoa.ToString("0.00", ci)} MOA   vertical={g.VerticalSpreadMoa.ToString("0.00", ci)} MOA");
            // The MOA figures above are unitless; this line is the same group as a size on paper.
            double moaMm = GrtShotGroups.MoaMm(g.DistanceM);
            Console.WriteLine($"  (= {gu.Length(g.MeanRadiusMoa * moaMm)} MR, {gu.Length(g.GroupEsMoa * moaMm)} ES at {dist})");
        }
    }
}
