using GrtPluginKit.Analysis;
using GrtReloadingToolkit.ChronoStats;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// GRT's measurement tab shows the SAMPLE standard deviation (÷(n-1)) in its "sd" row. Read from a real GRT
/// with these two 4-shot strings from a charge ladder: it showed sd 2.8 and 4.0, where the population SD
/// (÷n), which the Athlon device reports and the import note carries, is 2.5 and 3.4. The toolkit's own
/// figures have to be the ones sitting next to GRT's.
/// </summary>
public sealed class SampleSdTests
{
    private static readonly double[] A = { 810.3, 803.5, 805.8, 806.9 };
    private static readonly double[] B = { 816.4, 808.3, 816.5, 815.7 };

    [Fact]
    public void StringStatsGivesTheSdGrtShows()
    {
        Assert.Equal(2.8, Math.Round(StringStats.From(A).Sd, 1));
        Assert.Equal(4.0, Math.Round(StringStats.From(B).Sd, 1));
    }

    [Fact]
    public void AnSdOfOneShotIsZeroNotADivisionByZero()
    {
        var s = StringStats.From(new[] { 800.0 });
        Assert.Equal(0, s.Sd);
        Assert.Equal(1, s.N);
    }

    [Fact]
    public void ChronographStatisticsHeadlineIsTheSameFigure()
    {
        var c = ChronoStatsCalc.Analyze(A);
        Assert.Equal(StringStats.From(A).Sd, c.Sd, 9);
        Assert.Equal(c.SampleSd, c.Sd, 12);
    }
}
