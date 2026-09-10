using System.Globalization;
using GrtPluginKit.Grt;

namespace GrtReloadingToolkit.Ocw;

/// <summary>--groups &lt;load.grtload&gt; [mm|cm|in] [m|yd] [--drop-flyers]</summary>
internal static class GroupsCli
{
    public static void Run(string[] args)
    {
        var ci = CultureInfo.InvariantCulture;
        string path = args[1];
        var refU = args.Any(a => a == "cm") ? RefUnit.Cm : args.Any(a => a == "in") ? RefUnit.Inch : RefUnit.Mm;
        var shootU = args.Any(a => a == "yd") ? ShootUnit.Yards : ShootUnit.Meters;
        bool dropFlyers = args.Any(a => a == "--drop-flyers");

        var doc = GrtLoadDoc.Load(path);
        var (groups, log) = GrtShotGroups.FromDoc(doc, new GrtShotGroups.Options(refU, shootU, dropFlyers));
        foreach (var l in log) Console.WriteLine("  " + l);
        Console.WriteLine();

        foreach (var g in groups)
        {
            Console.WriteLine($"{g.SourceFile}  charge/step={g.ChargeGrains?.ToString("0.0##", ci) ?? "?"}  dist={g.DistanceM.ToString("0", ci)} m");
            Console.WriteLine($"  n={g.Impacts.Count}  center=({g.CenterXMoa.ToString("0.00", ci)}, {g.CenterYMoa.ToString("0.00", ci)}) MOA");
            Console.WriteLine($"  mean radius={g.MeanRadiusMoa.ToString("0.00", ci)} MOA   ES={g.GroupEsMoa.ToString("0.00", ci)} MOA   vertical={g.VerticalSpreadMoa.ToString("0.00", ci)} MOA");
            double moaMm = GrtShotGroups.MoaMm(g.DistanceM);
            Console.WriteLine($"  (= {(g.MeanRadiusMoa * moaMm).ToString("0.0", ci)} mm MR, {(g.GroupEsMoa * moaMm).ToString("0.0", ci)} mm ES at {g.DistanceM.ToString("0", ci)} m)");
        }
    }
}
