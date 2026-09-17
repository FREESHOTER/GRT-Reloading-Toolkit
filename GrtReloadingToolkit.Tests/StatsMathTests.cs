using GrtPluginKit.Analysis;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>Pins the hand-rolled special functions (no external stats package) against textbook
/// values, so a typo in the continued fraction or the Lanczos coefficients doesn't silently
/// mis-shape every confidence interval and p-value downstream.</summary>
public class StatsMathTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0.8427007929)]
    [InlineData(-1, -0.8427007929)]
    [InlineData(2, 0.9953222650)]
    public void Erf_MatchesReferenceValues(double x, double expected) =>
        Assert.Equal(expected, StatsMath.Erf(x), 6);

    [Theory]
    [InlineData(5, 3.178053830)]     // ln(4!) = ln(24)
    [InlineData(1, 0)]               // ln(0!) = ln(1)
    [InlineData(0.5, 0.572364943)]   // ln(sqrt(pi))
    public void LogGamma_MatchesReferenceValues(double x, double expected) =>
        Assert.Equal(expected, StatsMath.LogGamma(x), 6);

    [Theory]
    // two-tailed 95% critical values from a standard t-table
    [InlineData(1, 12.706)]
    [InlineData(5, 2.571)]
    [InlineData(10, 2.228)]
    [InlineData(30, 2.042)]
    public void StudentT_TwoTailedCritical_MatchesTTable(double dof, double expected) =>
        Assert.Equal(expected, StudentT.TwoTailedCritical(dof, 0.05), 2);

    [Fact]
    public void StudentT_LargeDof_ApproachesNormalZ()
    {
        // at large dof the t distribution converges to the standard normal (95% two-tailed -> 1.96)
        Assert.Equal(1.96, StudentT.TwoTailedCritical(100_000, 0.05), 2);
    }

    [Fact]
    public void StudentT_Cdf_IsAntisymmetricAroundZero()
    {
        Assert.Equal(0.5, StudentT.Cdf(0, 9), 9);
        Assert.Equal(1 - StudentT.Cdf(2.0, 9), StudentT.Cdf(-2.0, 9), 9);
    }

    [Fact]
    public void FDist_Cdf_AtKnownUpperFivePercentPoint()
    {
        // the upper-5% critical value of F(5,5) is ~5.05 -> P(F <= 5.05) ~= 0.95
        Assert.Equal(0.95, FDist.Cdf(5.05, 5, 5), 2);
    }

    [Fact]
    public void FDist_TwoTailedPValue_IsOneWhenRatiosMatch() =>
        Assert.Equal(1.0, FDist.TwoTailedPValue(1.0, 8, 8), 9);
}
