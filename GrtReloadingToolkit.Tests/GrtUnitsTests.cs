using GrtPluginKit.Grt;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// What a card, a label and the QR block print. The toolkit holds metric because that is what a
/// .grtload stores, so every one of these numbers is a conversion away from what the shooter
/// reads — and a card that disagrees with the load it was printed from is worse than no card.
/// </summary>
public class GrtUnitsTests : IDisposable
{
    private readonly List<string> _dirs = new();

    /// <summary>A throwaway GRT install holding nothing but the one line we read.</summary>
    private GrtConfig Cfg(string valueUnits)
    {
        string dir = Path.Combine(Path.GetTempPath(), "grtunits-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        // Surrounded by other settings, because the real file is ~100 lines and ValueUnits is one.
        File.WriteAllLines(Path.Combine(dir, "GordonsReloadingTool.cfg"), new[]
        {
            "Language=en",
            "ValueUnits=" + valueUnits,
            "WindowState=Maximized",
        });
        var cfg = GrtConfig.Load(dir);
        Assert.NotNull(cfg);
        return cfg!;
    }

    private const string Imperial = "caselen=in;oal=in;gdepth=in;velocity=ft/s;pressure=psi;pt=F;range=yard";
    private const string Metric = "caselen=mm;oal=mm;gdepth=mm;velocity=m/s;pressure=bar;pt=°C;range=m";

    public void Dispose()
    {
        foreach (string d in _dirs) try { Directory.Delete(d, true); } catch (Exception) { }
    }

    [Fact]
    public void ImperialLengthIsInchesToFourPlaces()
    {
        // 71.76008 mm is exactly 2.8252 in — a real COAL, and the four decimals survive the divide.
        Assert.Equal("2.8252 in", GrtUnits.From(Cfg(Imperial)).Length(71.76008));
    }

    [Fact]
    public void MetricLengthIsUnchangedAndStillFourPlaces()
        => Assert.Equal("71.7601 mm", GrtUnits.From(Cfg(Metric)).Length(71.76008));

    [Fact]
    public void ImperialVelocityIsFps()
        => Assert.Equal("2851 ft/s", GrtUnits.From(Cfg(Imperial)).Velocity(869));

    [Fact]
    public void MetricVelocityIsUnchanged()
        => Assert.Equal("869 m/s", GrtUnits.From(Cfg(Metric)).Velocity(869));

    [Fact]
    public void AnSdFollowsItsVelocityIntoFpsAndKeepsOneDecimal()
    {
        // It is printed right after a velocity that already carries the unit, so it has none.
        Assert.Equal("40.4", GrtUnits.From(Cfg(Imperial)).VelocitySd(12.3));
        Assert.Equal("12.3", GrtUnits.From(Cfg(Metric)).VelocitySd(12.3));
    }

    [Fact]
    public void NoInstallMeansMetric()
    {
        // Stand-alone, or a dev box: keep printing what the toolkit has always printed.
        var u = GrtUnits.From(null);
        Assert.False(u.LengthInInch);
        Assert.False(u.VelocityInFps);
        Assert.Equal("71.7601 mm", u.Length(71.76008));
        Assert.Equal("869 m/s", u.Velocity(869));
    }

    [Theory]
    [InlineData("ft/s")]
    [InlineData("fps")]
    [InlineData("ft/sec")]
    public void TheFpsSpellingsGrtMightUseAllCount(string unit)
        => Assert.True(GrtUnits.From(Cfg("oal=in;velocity=" + unit)).VelocityInFps);

    [Fact]
    public void LengthAndVelocityAreReadSeparately()
    {
        // Nothing stops a user mixing them, and GRT lets them: inches with m/s is a valid config.
        var u = GrtUnits.From(Cfg("oal=in;velocity=m/s"));
        Assert.True(u.LengthInInch);
        Assert.False(u.VelocityInFps);
    }

    [Fact]
    public void AConfigWithNoUnitsLineIsNoConfig()
    {
        string dir = Path.Combine(Path.GetTempPath(), "grtunits-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        File.WriteAllLines(Path.Combine(dir, "GordonsReloadingTool.cfg"), new[] { "Language=en" });
        Assert.Null(GrtConfig.Load(dir));
    }

    [Fact]
    public void TheConversionsAreTheDefinedOnes()
    {
        Assert.Equal(25.4, GrtUnits.MmPerInch);
        Assert.Equal(0.3048, GrtUnits.MetresPerFoot);
    }
}
