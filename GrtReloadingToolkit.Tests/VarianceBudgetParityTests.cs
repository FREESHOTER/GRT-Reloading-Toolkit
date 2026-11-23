using GrtReloadingToolkit.Cartridges;
using Xunit;

namespace GrtReloadingToolkit.Tests;

// The variance budget of the "Cartridges" tool against the chapter "Analisi della SD" of Analisi DS on the same (invented) shots.
// Expected values come from running Analisi DS's own _sd_charge_chapter, whose tables print the SD with two decimals and the share as a
// whole percent: that is the precision compared here. The helper functions are compared to the full precision of scipy.
public sealed class VarianceBudgetParityTests
{
    private static IReadOnlyList<ShotSample> Shots((int S, double V, double? Cw, double? Pw)[] rows) =>
        rows.Select(r => new ShotSample(r.S, r.V, r.Cw is { } cw
            ? new Dictionary<string, double> { ["case_weight_gr"] = cw, ["powder_weight_gr"] = r.Pw!.Value }
            : new Dictionary<string, double>())).ToList();

    private sealed record P(string Kind, string? Key, double Sd, int Share, bool Proven);

    private static void Check(VarianceBudgetResult b, int n, double sdW, double sdTot, double lo, double hi, double sdRest, P[] parts,
        (double Half, double None)[] advice, (double D, double Mm, double SdMm, double Moa)[] cost, int? need)
    {
        Assert.Equal(n, b.N);
        Assert.Equal(sdW, b.SdWithin, 9); Assert.Equal(sdTot, b.SdTotal, 9);
        Assert.Equal(lo, b.SdWithinInterval.Lo, 9); Assert.Equal(hi, b.SdWithinInterval.Hi, 9);
        Assert.Equal(sdRest, b.SdRest, 9);
        Assert.Equal(parts.Length, b.Parts.Count);
        for (int i = 0; i < parts.Length; i++)
        {
            var e = parts[i]; var a = b.Parts[i];
            Assert.Equal(e.Kind, a.Kind);
            Assert.Equal(e.Key, a.Key);
            Assert.Equal(e.Sd, Math.Round(a.SdMps, 2, MidpointRounding.AwayFromZero), 9);
            Assert.Equal(e.Share, (int)Math.Round(a.Share * 100, 0, MidpointRounding.ToEven));            // Python rounds half to even
            if (e.Kind is "between" or "trend" or "measure") Assert.Equal(e.Proven, a.Proven);
        }
        Assert.Equal(advice.Length, b.Advice.Count);
        for (int i = 0; i < advice.Length; i++)
        {
            Assert.Equal(advice[i].Half, Math.Round(b.Advice[i].SdIfHalved, 2, MidpointRounding.AwayFromZero), 9);
            Assert.Equal(advice[i].None, Math.Round(b.Advice[i].SdIfRemoved, 2, MidpointRounding.AwayFromZero), 9);
        }
        Assert.Equal(cost.Length, b.TargetCost.Count);
        for (int i = 0; i < cost.Length; i++)
        {
            Assert.Equal(cost[i].D, b.TargetCost[i].DistanceM);
            Assert.Equal(cost[i].Mm, Math.Round(b.TargetCost[i].MmPerMs, 2, MidpointRounding.AwayFromZero), 9);
            Assert.Equal(cost[i].SdMm, Math.Round(b.TargetCost[i].SdMm, 1, MidpointRounding.AwayFromZero), 9);
            Assert.Equal(cost[i].Moa, Math.Round(b.TargetCost[i].SdMoa, 2, MidpointRounding.AwayFromZero), 9);
        }
        Assert.Equal(need, b.ShotsNeeded);
    }

    private static readonly (int S, double V, double? Cw, double? Pw)[] MeasuresShots =
    {
        (0, 825.0, 120.01, 40.34),
        (0, 824.5, 119.82, 40.29),
        (0, 827.8, 120.2, 40.3),
        (0, 822.8, 119.35, 40.35),
        (0, 826.7, 120.24, 40.3),
        (0, 826.5, 120.16, 40.32),
        (0, 824.7, 119.95, 40.32),
        (0, 822.7, 119.47, 40.31),
        (0, 822.3, 119.33, 40.28),
        (0, 824.1, 119.58, 40.26),
        (0, 826.6, 120.31, 40.29),
        (0, 826.3, 120.13, 40.32),
        (0, 826.6, 120.19, 40.33),
        (0, 825.0, 119.72, 40.31),
        (1, 830.5, 120.38, 40.26),
        (1, 830.8, 119.71, 40.25),
        (1, 834.5, 120.18, 40.28),
        (1, 833.8, 120.29, 40.32),
        (1, 834.3, 120.33, 40.26),
        (1, 833.4, 120.21, 40.25),
        (1, 831.1, 119.91, 40.32),
        (1, 830.9, 119.99, 40.31),
        (1, 833.6, 120.21, 40.3),
        (1, 831.9, 119.82, 40.33),
        (1, 833.2, 119.94, 40.32),
        (1, 836.0, 120.63, 40.25),
        (1, 831.5, 120.16, 40.3),
        (1, 834.7, 120.44, 40.26),
        (2, 837.3, 120.46, 40.25),
        (2, 837.7, 119.54, 40.31),
        (2, 840.6, 120.71, 40.25),
        (2, 839.7, 120.25, 40.35),
        (2, 836.8, 119.74, 40.31),
        (2, 838.1, 119.93, 40.28),
        (2, 838.3, 120.11, 40.3),
        (2, 837.3, 119.55, 40.29),
        (2, 839.3, 119.93, 40.26),
        (2, 839.0, 120.19, 40.36),
        (2, 838.8, 119.84, 40.26),
        (2, 841.5, 120.9, 40.28),
        (2, 838.7, 120.21, 40.28),
        (2, 838.7, 119.88, 40.31),
    };

    [Fact]
    public void Measures_BudgetsTheVarianceLikeAnalisiDs()
    {
        var b = VarianceBudget.Analyze(Shots(MeasuresShots), 0.0)!;
        Check(b, 42, 1.5973191827022524, 5.843874663281149, 1.3503245677920612, 1.967869979846806, 0.937121317827832,
            new P[] { new("between", null, 5.63, 93, true), new("measure", "case_weight_gr", 1.19, 4, true), new("rest", null, 0.94, 3, false), new("trend", null, 0.35, 0, false) },
            new (double, double)[] { (1.19, 1.02) },
            new (double, double, double, double)[] { (100.0, 0.17, 0.3, 0.01), (200.0, 0.68, 1.1, 0.02), (300.0, 1.53, 2.4, 0.03) }, 51);
    }

    private static readonly (int S, double V, double? Cw, double? Pw)[] NoMeasuresShots =
    {
        (0, 826.5, null, null),
        (0, 830.2, null, null),
        (0, 831.6, null, null),
        (0, 827.3, null, null),
        (0, 830.4, null, null),
        (0, 827.4, null, null),
        (0, 828.4, null, null),
        (0, 828.7, null, null),
        (0, 826.4, null, null),
        (0, 829.6, null, null),
        (0, 829.9, null, null),
        (0, 829.1, null, null),
        (1, 833.5, null, null),
        (1, 837.6, null, null),
        (1, 833.6, null, null),
        (1, 831.6, null, null),
        (1, 838.1, null, null),
        (1, 833.6, null, null),
        (1, 839.2, null, null),
        (1, 837.4, null, null),
        (1, 834.7, null, null),
        (1, 835.1, null, null),
        (1, 837.3, null, null),
        (1, 836.5, null, null),
    };

    [Fact]
    public void NoMeasures_BudgetsTheVarianceLikeAnalisiDs()
    {
        var b = VarianceBudget.Analyze(Shots(NoMeasuresShots), 0.5)!;
        Check(b, 24, 2.0143331101850785, 4.03375027788631, 1.6221323965367942, 2.689800018831481, 1.8535576954778288,
            new P[] { new("between", null, 3.52, 76, true), new("rest", null, 1.85, 21, false), new("chrono", null, 0.5, 2, false), new("trend", null, 0.44, 1, false) },
            new (double, double)[] {  },
            new (double, double, double, double)[] { (100.0, 0.17, 0.3, 0.01), (200.0, 0.68, 1.4, 0.02), (300.0, 1.53, 3.1, 0.04) }, 51);
    }

    private static readonly (int S, double V, double? Cw, double? Pw)[] OneSeriesShots =
    {
        (0, 835.3, null, null),
        (0, 833.3, null, null),
        (0, 831.3, null, null),
        (0, 832.9, null, null),
        (0, 830.7, null, null),
        (0, 833.3, null, null),
        (0, 834.1, null, null),
        (0, 832.4, null, null),
        (0, 840.0, null, null),
        (0, 837.1, null, null),
        (0, 836.7, null, null),
        (0, 836.1, null, null),
        (0, 831.0, null, null),
        (0, 836.2, null, null),
        (0, 834.1, null, null),
        (0, 836.3, null, null),
    };

    [Fact]
    public void OneSeries_BudgetsTheVarianceLikeAnalisiDs()
    {
        var b = VarianceBudget.Analyze(Shots(OneSeriesShots), 0.0)!;
        Check(b, 16, 2.5585151944047624, 2.5585151944047624, 1.9819842323491232, 3.6773679658216847, 2.441273850621301,
            new P[] { new("rest", null, 2.44, 91, false), new("trend", null, 0.77, 9, false) },
            new (double, double)[] {  },
            new (double, double, double, double)[] { (100.0, 0.17, 0.4, 0.01), (200.0, 0.68, 1.7, 0.03), (300.0, 1.52, 3.9, 0.04) }, 51);
    }

    [Fact]
    public void TheIntervalOfAnSdIsTheOneScipyGives()
    {
        foreach (var (sd, n, lo, hi) in new (double, int, double, double)[] { (2.8, 24, 2.2642301397548374, 3.711449848693526), (4.16, 40, 3.5167362057919234, 5.125049022646517), (1.75, 3, 1.0110823975470353, 7.726943774728147), (6.8, 10, 4.959559554526939, 11.187343505880706) })
        {
            var i = StatsExtra.SdInterval(sd, n)!.Value;
            Assert.Equal(lo, i.Lo, 9); Assert.Equal(hi, i.Hi, 9);
        }
        Assert.Null(StatsExtra.SdInterval(2.0, 1));
    }

    [Fact]
    public void TheShotsNeededToProveALowerSdAreTheOnesAnalisiDsGives()
    {
        foreach (var (small, big, expected) in new (double, double, int?)[] { (4.0, 6.8, 24), (2.0, 4.1, 14), (3.0, 3.6, 188), (1.0, 5.0, 5) })
            Assert.Equal(expected, StatsExtra.ShotsForSdDifference(small, big));
    }

    [Fact]
    public void TheDropPerMetrePerSecondIsTheOneOfAnalisiDs()
    {
        foreach (var (d, v, mm) in new (double, double, double)[] { (100.0, 830.0, 0.17156738435816135), (200.0, 838.5, 0.6656099383746528), (300.0, 810.0, 1.6613321140070112) })
            Assert.Equal(mm, VarianceBudget.MmPerMps(d, v), 9);
    }
}
