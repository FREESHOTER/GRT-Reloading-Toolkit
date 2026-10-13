using GrtReloadingToolkit.Ui;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>The optional tools the "next step" line offers, each resting on a fact in the load or the journal.</summary>
public sealed class ToolSuggestionsTests
{
    private static string[] Tools(string current, PathSignals s) => ToolSuggestions.For(current, s).Select(x => x.Tool).ToArray();

    [Fact]
    public void FindBaIsOfferedAfterACalibrationWhenEarlierOnesAreLogged()
    {
        Assert.Equal(new[] { "FindBa" }, Tools("Cal", new PathSignals(true, null, 0, 2)));
        Assert.Empty(Tools("Cal", new PathSignals(false, null, 0, 2)));   // no calibration in the load yet
        Assert.Empty(Tools("Cal", new PathSignals(true, null, 0, 0)));    // nothing earlier to compare with
    }

    [Fact]
    public void TemperatureCoefficientsAreOfferedOnlyWhenTheStringsWereShotAtDifferentTemperatures()
    {
        Assert.Equal(new[] { "Temp" }, Tools("ChronoStats", new PathSignals(false, 12, 0, 0)));
        Assert.Equal(new[] { "Temp" }, Tools("GroupAnalysis", new PathSignals(false, 5, 0, 0)));
        Assert.Empty(Tools("ChronoStats", new PathSignals(false, 4.9, 0, 0)));
        Assert.Empty(Tools("ChronoStats", new PathSignals(false, null, 0, 0)));
    }

    [Fact]
    public void PowderCompareIsOfferedWhenTheJournalHoldsMoreThanOnePowderForTheCaliber()
    {
        Assert.Equal(new[] { "PowderCompare" }, Tools("Workflow", new PathSignals(false, null, 2, 0)));
        Assert.Empty(Tools("Workflow", new PathSignals(false, null, 1, 0)));
    }

    [Fact]
    public void SeveralFactsGiveSeveralSuggestionsAndAToolWithNoRuleGivesNone()
    {
        Assert.Equal(new[] { "FindBa", "PowderCompare" }, Tools("Cal", new PathSignals(true, null, 3, 4)));
        Assert.Empty(Tools("Athlon", new PathSignals(true, 20, 3, 4)));
    }

    [Theory]
    [InlineData("TIME=10:14:02 AM AVG-E=806.6 T=15.0°C SRC=Athlon", 15.0)]
    [InlineData("AVG-E=806.6 T=59°F", 15.0)]
    [InlineData("T=-3.5C", -3.5)]
    [InlineData("TIME=10:14 SRC=x", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ATemperatureTokenIsReadInDegreesC(string? note, double? expected)
    {
        double? t = ToolSuggestions.TemperatureC(note);
        if (expected is null) Assert.Null(t); else Assert.Equal(expected.Value, t!.Value, 6);
    }

    [Fact]
    public void TheSpreadNeedsTwoStringsWithATemperature()
    {
        Assert.Equal(20.0, ToolSuggestions.TemperatureSpreadC(new string?[] { "T=5°C", "T=25°C", "no temperature" })!.Value, 6);
        Assert.Null(ToolSuggestions.TemperatureSpreadC(new string?[] { "T=5°C", "no temperature" }));
        Assert.Null(ToolSuggestions.TemperatureSpreadC(Array.Empty<string?>()));
    }
}