using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

public sealed class FollowUpReportTests
{
    private static readonly DateTime Day0 = new(2026, 3, 1);
    private static FollowSession S(int day, double? temp, double mean, string? powderLot = null)
        => new(Day0.AddDays(day), temp, new[] { -2.0, 2, -1, 1, 0 }.Select(d => mean + d).ToList(), PowderLot: powderLot);

    private static string Report(IEnumerable<FollowSession> s) => FollowUpReport.Build(LoadFollowUp.Analyze(s), "139 LVN N550", x => x, FollowUpFormat.Metric);

    [Fact]
    public void WithAFewOutingsItSaysWhatItCouldNotEvaluateAndShowsNoVerdict()
    {
        var text = Report(new[] { S(0, 10, 800), S(14, 20, 805) });
        Assert.Contains("139 LVN N550: 2 outings, 2 with at least 3 shots", text);
        Assert.Contains("NOT EVALUATED", text);
        Assert.DoesNotContain("LATEST OUTING", text);
        Assert.DoesNotContain("TEMPERATURE:", text);
    }

    [Fact]
    public void ALatestOutingFarFromTheUsualIsReportedAsOutsideItWithTheNumbers()
    {
        var baseline = new[] { 800.0, 801, 799.5, 800.5, 800, 799 }.Select((m, i) => S(i * 14, null, m)).ToList();
        var text = Report(baseline.Append(S(90, null, 812)));
        Assert.Contains("LATEST OUTING (2026-05-30), against the 6 before it", text);
        Assert.Contains("Mean 812.0 m/s; this load usually gives 800.0 m/s: outside what is usual for it.", text);
        Assert.Contains("Spread: SD 1.6 m/s", text);
    }

    [Fact]
    public void TheTemperatureAndTimeEffectsAreReportedWithTheirIntervals()
    {
        double[] temps = { 5, 12, 20, 28, 10, 24 };
        double[] noise = { 0.2, -0.3, 0.1, -0.1, 0.3, -0.2 };
        var text = Report(temps.Select((t, i) => S(i * 14, t, 800 + 0.5 * (t - 15) + noise[i])));
        Assert.Matches(@"TEMPERATURE: 0\.(4[5-9]|5[0-4]) m/s per C", text);
        Assert.Contains("with time taken into account", text);
        Assert.Contains("The data say this load does change with temperature.", text);
        Assert.Contains("OVER TIME:", text);
        Assert.Contains("No movement over time can be told apart from the noise yet.", text);
    }

    [Fact]
    public void TemperatureDifferencesInTheMessagesFollowTheUnitOfTheFormat()
    {
        var f = FollowUpFormat.Metric with { TemperatureDelta = c => (c * 9 / 5).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " °F" };
        var text = FollowUpReport.Build(LoadFollowUp.Analyze(new[] { S(0, 10, 800), S(14, 12, 801) }), "x", s => s, f);
        Assert.Contains("at least 9 °F apart (there are 2, 3.6 °F apart)", text);        // 5 C and 2 C
        Assert.DoesNotContain(" C apart", text);
    }

    [Fact]
    public void ADetectableButTinyDriftIsCalledSmallNotAMovement()
    {
        // 0.1 m/s every 30 days against strings with an SD of 1.6: measurable with clean data, irrelevant in practice
        var text = Report(Enumerable.Range(0, 6).Select(i => S(i * 30, null, 800 + 0.1 * i + new[] { 0.01, -0.01, 0.02, -0.02, 0.01, 0.0 }[i])));
        Assert.Contains("OVER TIME:", text);
        Assert.Contains("can be detected, but it is small", text);
        Assert.DoesNotContain("is moving over time", text);
    }

    [Fact]
    public void ABigDriftIsCalledAMovement()
    {
        var text = Report(Enumerable.Range(0, 6).Select(i => S(i * 30, null, 800 - 2.0 * i + new[] { 0.1, -0.1, 0.2, -0.2, 0.1, 0.0 }[i])));
        Assert.Contains("The mean velocity is moving over time.", text);
    }

    [Fact]
    public void AChangeOfLotIsNamedWithTheDateWhereItShowsUp()
    {
        var text = Report(new[] { S(0, null, 800, "A1"), S(14, null, 800, "A1"), S(28, null, 800, "B7") });
        Assert.Contains("WHAT CHANGED BETWEEN OUTINGS", text);
        Assert.Contains("2026-03-29: powder lot", text);
    }

    [Fact]
    public void ItAlwaysEndsWithTheCaveatAboutFewOutings()
        => Assert.EndsWith("it firms up as the outings add up.", Report(new[] { S(0, null, 800) }).Trim());

    [Fact]
    public void TheUnitsFollowTheFormatItIsGiven()
    {
        var f = FollowUpFormat.Metric with { Velocity = v => (v / 0.3048).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " ft/s" };
        var baseline = new[] { 800.0, 801, 799.5, 800.5, 800, 799 }.Select((m, i) => S(i * 14, null, m)).ToList();
        var text = FollowUpReport.Build(LoadFollowUp.Analyze(baseline.Append(S(90, null, 812))), "x", s => s, f);
        Assert.Contains("Mean 2664 ft/s", text);
    }
}
