using GrtReloadingToolkit.Cal;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// A stand-in for GRT, linear around the numbers measured live on a 41.0 gr N550 load (6.5 Creedmoor): velocity falls
/// 57 m/s per unit of Sebert and rises 3285 m/s per unit of common Ba/k factor; at matched velocity a higher Sebert
/// shortens the lead time by 0.105 ms per unit and raises Pmax by 1730 per unit; 0.2 gr more powder shortens the lead
/// time by 0.0093 ms.
/// </summary>
public sealed class SebertSensitivityTests
{
    private const double S0 = 0.5, C0 = 41.0;
    private int _calls;

    private Task<SebertSim?> Model(double sebert, double f, double charge)
    {
        _calls++;
        double mv = 845.43 - 57.0 * (sebert - S0) + 3285.0 * (f - 1) + 19.0 * (charge - C0);
        double blt = 1.3296 - 0.0465 * (charge - C0) - 0.105 * (sebert - S0) - 0.0186 * (f - 1) * 100 * 0.35 + 0.0 * mv;
        double pmax = 3673 + 450.0 * (sebert - S0) + 565 * (charge - C0) + 1600 * (f - 1) + 1730 * (sebert - S0) * 0;
        return Task.FromResult<SebertSim?>(new SebertSim(mv, pmax, blt, "bar"));
    }

    [Fact]
    public async Task TheNodeMovesWithSebertEvenWhenVelocityIsMatched()
    {
        var r = await SebertSensitivity.RunAsync(Model, S0, C0);
        Assert.NotNull(r);
        Assert.NotNull(r!.Higher);
        Assert.NotNull(r.Lower);
        // both sides are re-matched to the baseline velocity
        Assert.True(r.Higher!.MatchedWithin < 0.1);
        Assert.True(r.Lower!.MatchedWithin < 0.1);
        // a higher Sebert shortens the lead time, so the node's charge moves down; a lower one moves it up
        Assert.True(r.NodeShiftGr(r.Higher) < 0);
        Assert.True(r.NodeShiftGr(r.Lower) > 0);
        Assert.True(r.WorstShiftGr > 0.1);
    }

    [Fact]
    public async Task ShiftsAreTheLeadTimeChangeOverTheChargeSlope()
    {
        var r = (await SebertSensitivity.RunAsync(Model, S0, C0))!;
        Assert.Equal(-0.0465, r.BltPerGrMs, 4);
        double dBlt = r.Higher!.Sim.BltMs - r.Baseline.BltMs;
        Assert.Equal(dBlt / 0.0465, r.NodeShiftGr(r.Higher), 6);
    }

    [Fact]
    public async Task ASebertAtTheEdgeOfGrtsRangeHasOnlyOneSide()
    {
        var r = (await SebertSensitivity.RunAsync(Model, 0.95, C0))!;
        Assert.Null(r.Higher);
        Assert.NotNull(r.Lower);
        var low = (await SebertSensitivity.RunAsync(Model, 0.15, C0))!;
        Assert.Null(low.Lower);
        Assert.NotNull(low.Higher);
    }

    [Fact]
    public async Task ASimulatorThatCannotAnswerGivesNothingRatherThanAGuess()
    {
        Assert.Null(await SebertSensitivity.RunAsync((_, _, _) => Task.FromResult<SebertSim?>(null), S0, C0));
    }

    [Fact]
    public async Task LeadTimeThatDoesNotFallWithChargeIsNotTrusted()
    {
        // GRT showing a stale tab returns the same numbers for every request: a slope of zero must not be divided by.
        var same = new SebertSim(845, 3673, 1.33, "bar");
        Assert.Null(await SebertSensitivity.RunAsync((_, _, _) => Task.FromResult<SebertSim?>(same), S0, C0));
    }

    [Fact]
    public async Task TheReportStatesWhatTheChronographCannotSee()
    {
        var r = (await SebertSensitivity.RunAsync(Model, S0, C0))!;
        string text = r.BuildReport();
        Assert.Contains("Sebert factor sensitivity", text);
        Assert.Contains("cannot tell the Sebert factor from Ba and k", text);
        Assert.Contains("Sebert 0.60", text);
        Assert.Contains("Sebert 0.40", text);
        Assert.Contains("node charge about", text);
    }
}
