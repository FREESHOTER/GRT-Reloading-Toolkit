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
        // GRT's own units are the starting point, because that is what the tab was measured in;
        // naming one on the command line still wins, which is how a file from someone else is read.
        var (defRef, defShoot) = GrtShotGroups.GrtDefaults();
        var refU = args.Any(a => a == "mm") ? RefUnit.Mm
            : args.Any(a => a == "cm") ? RefUnit.Cm
            : args.Any(a => a == "in") ? RefUnit.Inch
            : defRef;
        var shootU = args.Any(a => a == "m") ? ShootUnit.Meters
            : args.Any(a => a == "yd") ? ShootUnit.Yards
            : defShoot;
        bool dropFlyers = args.Any(a => a == "--drop-flyers");

        var doc = GrtLoadDoc.Load(path);
        var (groups, log) = GrtShotGroups.FromDoc(doc, new GrtShotGroups.Options(refU, shootU, dropFlyers));
        foreach (var l in log) Console.WriteLine("  " + l);
        Console.WriteLine();

        foreach (var g in groups)
        {
            Console.WriteLine($"{g.SourceFile}  charge/step={g.ChargeGrains?.ToString(Str.StepFormat, ci) ?? "?"}  dist={g.DistanceM.ToString("0", ci)} m");
            Console.WriteLine($"  n={g.Impacts.Count}  center=({g.CenterXMoa.ToString("0.00", ci)}, {g.CenterYMoa.ToString("0.00", ci)}) MOA");
            Console.WriteLine($"  mean radius={g.MeanRadiusMoa.ToString("0.00", ci)} MOA   ES={g.GroupEsMoa.ToString("0.00", ci)} MOA   vertical={g.VerticalSpreadMoa.ToString("0.00", ci)} MOA");
            double moaMm = GrtShotGroups.MoaMm(g.DistanceM);
            Console.WriteLine($"  (= {(g.MeanRadiusMoa * moaMm).ToString("0.0", ci)} mm MR, {(g.GroupEsMoa * moaMm).ToString("0.0", ci)} mm ES at {g.DistanceM.ToString("0", ci)} m)");
        }
    }
}
