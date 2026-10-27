using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The reference numbers (slope, its standard error, the p-values, the residual SD, the pause correlation) come from scipy's
/// linregress and pearsonr on the same made-up string, so the t-distribution and the formulas are checked against an independent source.
/// </summary>
public class ShotDriftTests
{
    private static readonly double[] V =
    {
        831.6, 830.0, 831.7, 830.1, 834.0, 832.1, 833.7, 831.1, 835.0, 834.0,
        833.8, 836.6, 834.1, 836.3, 835.8, 837.9, 834.9, 837.6, 838.6, 837.3,
    };
    private static readonly double?[] Clock =
    {
        36000, 36030, 36064, 36092, 36152, 36183, 36228, 36257, 36290, 36380,
        36412, 36447, 36474, 36515, 36551, 36581, 36701, 36732, 36765, 36803,
    };

    [Fact]
    public void SlopeAndItsTestMatchTheReference()
    {
        var r = ShotDrift.Analyze(V)!;
        Assert.Equal(0.3905263, r.SlopePerShot, 6);
        Assert.Equal(0.0487079, r.SlopeSe, 6);
        Assert.True(r.PValue < 1e-6 && r.PValue > 1e-8, $"p = {r.PValue}");     // reference 2.37e-07
        Assert.Equal(7.42, r.TotalChange, 6);
        Assert.Equal(2.6139102, r.SdMps, 6);
        Assert.Equal(1.2560590, r.ResidualSdMps, 6);
        Assert.True(r.DriftSignificant);
        Assert.Equal(0, r.IntervalCount);
        Assert.Null(r.IntervalR);
    }

    [Fact]
    public void PausesAreReadFromTheClock()
    {
        var r = ShotDrift.Analyze(V, Clock)!;
        Assert.Equal(19, r.IntervalCount);
        Assert.Equal(33, r.MedianPauseS!.Value, 6);
        Assert.Equal(27, r.MinPauseS!.Value, 6);
        Assert.Equal(120, r.MaxPauseS!.Value, 6);
        Assert.Equal(0.0588027, r.IntervalR!.Value, 6);
        Assert.Equal(0.811, r.IntervalP!.Value, 3);
        Assert.False(r.IntervalSignificant);
    }

    [Fact]
    public void ADriftWithNoLinkToThePauseIsStillReported()
    {
        // the case the old pause-only method missed: a clear drift, pause r near zero
        var r = ShotDrift.Analyze(V, Clock)!;
        Assert.True(r.DriftSignificant);
        Assert.False(r.IntervalSignificant);
    }

    [Fact]
    public void ANoisyFlatStringShowsNoDrift()
    {
        double[] flat = { 838.0, 841.5, 836.2, 840.1, 837.7, 842.3, 835.9, 839.4, 840.8, 837.1, 841.0, 838.6 };
        var r = ShotDrift.Analyze(flat)!;
        Assert.False(r.DriftSignificant);
        Assert.InRange(r.PValue, 0.05, 1.0);
    }

    [Fact]
    public void TooFewShotsClaimNothing()
    {
        Assert.Null(ShotDrift.Analyze(V.Take(ShotDrift.MinShots - 1).ToArray()));
        Assert.NotNull(ShotDrift.Analyze(V.Take(ShotDrift.MinShots).ToArray()));
    }

    [Fact]
    public void AStringThatDoesNotVaryHasNoTrend()
    {
        var r = ShotDrift.Analyze(Enumerable.Repeat(840.0, 10).ToArray());
        Assert.NotNull(r);
        Assert.Equal(0, r!.SlopePerShot, 9);
        Assert.False(r.DriftSignificant);
    }

    [Theory]
    [InlineData("TIME=10:18:34 AM", 10 * 3600 + 18 * 60 + 34)]
    [InlineData("TIME=1:05:09 PM", 13 * 3600 + 5 * 60 + 9)]
    [InlineData("TIME=12:00:00 AM", 0)]
    [InlineData("TIME=12:30:00 PM", 12 * 3600 + 30 * 60)]
    [InlineData("TIME=14:20:01", 14 * 3600 + 20 * 60 + 1)]
    [InlineData("TIME=09:15:00 am NOTE=first shot", 9 * 3600 + 15 * 60)]
    [InlineData("NOTE=x TIME=3:00:00 pm", 15 * 3600)]
    public void ClockTimesAreParsedFromTheShotNote(string note, double seconds) =>
        Assert.Equal(seconds, ShotDrift.ClockSeconds(note)!.Value, 6);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NOTE=no time here")]
    [InlineData("TIME=13:00:00 PM")]            // 13 on a 12-hour clock
    [InlineData("TIME=10:61:00 AM")]
    [InlineData("TIME=25:00:00")]
    public void NoteWithoutAUsableTimeGivesNull(string? note) => Assert.Null(ShotDrift.ClockSeconds(note));

    [Fact]
    public void AClockPastMidnightIsCarriedOver()
    {
        var times = new double?[10];
        double t = 23 * 3600 + 59 * 60;
        for (int i = 0; i < 10; i++) { times[i] = t % 86400; t += 40; }
        var r = ShotDrift.Analyze(V.Take(10).ToArray(), times)!;
        Assert.Equal(9, r.IntervalCount);
        Assert.Equal(40, r.MedianPauseS!.Value, 6);
        Assert.Equal(40, r.MinPauseS!.Value, 6);
    }

    [Fact]
    public void AShotWithoutATimeBreaksTheChainOfPauses()
    {
        var times = (double?[])Clock.Clone();
        times[5] = null;                         // the pauses into and out of shot 6 are unknown
        var r = ShotDrift.Analyze(V, times)!;
        Assert.Equal(17, r.IntervalCount);
    }
}
