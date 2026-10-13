using GrtReloadingToolkit.Ocw;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The ladder report is written into the user's load as a note. Its closing line said the SD was the population SD (÷n)
/// after the velocity statistics had moved to the sample SD (÷n−1), found on the first live run of the ladder analyzer
/// against a real GRT: a note in the load that states the wrong formula next to figures computed with the other.
/// </summary>
public sealed class LadderReportSdWordingTests
{
    [Fact]
    public void TheReportNamesTheSampleSdAndNeverThePopulationOne()
    {
        var rows = new List<LadderStep>();
        double[] xs = { 39.2, 39.6, 40.0, 40.4 };
        for (int i = 0; i < xs.Length; i++)
            rows.Add(new LadderStep { X = xs[i], VelN = 5, MeanMps = 800 + i * 4, SdMps = 3, EsMps = 8, Impacts = 5, DistanceM = 100, PoiYMoa = 0.1 * i, MeanRadiusMoa = 0.6, VertSpreadMoa = 1.0 });

        string report = LadderAnalyzer.BuildReport(LadderAnalyzer.Analyze(rows, LadderMode.Charge, "gr"), "OCW test");

        Assert.Contains("sample SD (n-1)", report);
        Assert.DoesNotContain("population", report, StringComparison.OrdinalIgnoreCase);
    }
}
