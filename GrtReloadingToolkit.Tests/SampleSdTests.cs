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

    /// <summary>
    /// The whole block GRT prints under a charge (avg, sd, es, min, max), read off a real GRT's measurement tab for three
    /// strings of the same ladder and compared at the one decimal it shows. Four of the means fall exactly on a rounding tie
    /// (816.85, 822.85, 842.35, 842.65) and GRT printed 816.8, 822.9, 842.3 and 842.7: only the plain sequential sum reproduces
    /// all four, the vector sum behind Enumerable.Average gave 816.9 for the first.
    /// </summary>
    [Theory]
    [InlineData(new[] { 810.3, 803.5, 805.8, 806.9 }, "806.6", "2.8", "6.8", "803.5", "810.3")]
    [InlineData(new[] { 816.4, 808.3, 816.5, 815.7 }, "814.2", "4.0", "8.2", "808.3", "816.5")]
    [InlineData(new[] { 819.5, 811.8, 817.3, 818.8 }, "816.8", "3.5", "7.7", "811.8", "819.5")]
    [InlineData(new[] { 828.4, 817.0, 824.9, 821.1 }, "822.9", "4.9", "11.4", "817.0", "828.4")]
    [InlineData(new[] { 833.0, 828.8, 829.2, 823.8 }, "828.7", "3.8", "9.2", "823.8", "833.0")]
    [InlineData(new[] { 849.9, 842.7, 843.0, 833.8 }, "842.3", "6.6", "16.1", "833.8", "849.9")]
    [InlineData(new[] { 852.1, 839.2, 839.6, 839.7 }, "842.7", "6.3", "12.9", "839.2", "852.1")]
    public void EveryFigureOfGrtsBlockMatchesAtItsOneDecimal(double[] shots, string avg, string sd, string es, string min, string max)
    {
        var st = StringStats.From(shots);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        Assert.Equal(avg, st.Mean.ToString("F1", inv));
        Assert.Equal(sd, st.Sd.ToString("0.0", inv));
        Assert.Equal(es, st.Es.ToString("0.0", inv));
        Assert.Equal(min, st.Min.ToString("0.0", inv));
        Assert.Equal(max, st.Max.ToString("0.0", inv));
    }
    /// <summary>
    /// The printing end of the same tie cases: a mean that is 816.84999999999991 in the double has to print as GRT does,
    /// 816.8, not 816.9. The custom format "0.#" cut the value to 15 significant digits first and rounded that.
    /// </summary>
    [Theory]
    [InlineData(816.84999999999991, "816.8")]
    [InlineData(822.85, "822.9")]
    [InlineData(842.34999999999991, "842.3")]
    [InlineData(842.65000000000009, "842.7")]
    [InlineData(3.0, "3")]
    [InlineData(2.5, "2.5")]
    [InlineData(0.04, "0")]
    public void StrNumRoundsTheExactValueAndTrimsZeros(double value, string expected)
        => Assert.Equal(expected, GrtPluginKit.Util.Str.Num(value, 1));
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
