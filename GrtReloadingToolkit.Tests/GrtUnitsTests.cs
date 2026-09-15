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
        Assert.Equal(0.9144, GrtUnits.MetresPerYard, 10);
    }

    [Fact]
    public void ImperialTemperatureIsFahrenheit()
    {
        // 21 °C is GRT's normal powder temperature, and 69.8 °F is what that shooter's log says.
        Assert.Equal("69.8 °F", GrtUnits.From(Cfg(Imperial)).Temperature(21));
        Assert.Equal("21.0 °C", GrtUnits.From(Cfg(Metric)).Temperature(21));
    }

    [Theory]
    [InlineData("F")]      // RAIDER's own config writes it bare
    [InlineData("°F")]     // the degree sign appears on the Celsius side, so allow it on both
    public void TheFahrenheitSpellingsGrtMightUseAllCount(string unit)
        => Assert.True(GrtUnits.From(Cfg("oal=in;pt=" + unit)).TemperatureInF);

    [Theory]
    [InlineData("C")]
    [InlineData("°C")]
    [InlineData("K")]
    public void EverythingElseIsTheMetricSide(string unit)
        => Assert.False(GrtUnits.From(Cfg("oal=mm;pt=" + unit)).TemperatureInF);

    [Fact]
    public void ATemperatureSurvivesTheRoundTrip()
    {
        // The grid shows °F and reads °F back; if these two disagree the load drifts a little
        // colder or hotter every time the window is reopened.
        var u = GrtUnits.From(Cfg(Imperial));
        Assert.Equal(21.0, u.TemperatureToCelsius(u.TemperatureValue(21.0)), 10);
        Assert.Equal(-40.0, u.TemperatureValue(-40.0), 10);   // the one temperature that is its own twin
    }

    [Fact]
    public void AVelocitySurvivesTheRoundTrip()
    {
        var u = GrtUnits.From(Cfg(Imperial));
        Assert.Equal(869.0, u.VelocityToMps(u.VelocityValue(869.0)), 10);
        Assert.Equal(869.0, GrtUnits.From(Cfg(Metric)).VelocityToMps(869.0), 10);
    }

    [Fact]
    public void ImperialDistanceIsYards()
    {
        var u = GrtUnits.From(Cfg(Imperial));
        Assert.True(u.DistanceInYards);
        Assert.Equal("yd", u.DistanceUnitName);
        Assert.Equal(100.0, u.DistanceValue(91.44), 10);      // the 100 yd line, stored in metres
        Assert.False(GrtUnits.From(Cfg(Metric)).DistanceInYards);
    }

    [Theory]
    [InlineData("yd")]
    [InlineData("yard")]
    [InlineData("yards")]
    public void TheYardSpellingsGrtMightUseAllCount(string unit)
        => Assert.True(GrtUnits.From(Cfg("oal=in;range=" + unit)).DistanceInYards);

    [Fact]
    public void TheUnitNamesMatchWhatTheValuesAreIn()
    {
        var i = GrtUnits.From(Cfg(Imperial));
        Assert.Equal(("in", "ft/s", "°F", "yd"), (i.LengthUnitName, i.VelocityUnitName, i.TemperatureUnitName, i.DistanceUnitName));
        var m = GrtUnits.From(Cfg(Metric));
        Assert.Equal(("mm", "m/s", "°C", "m"), (m.LengthUnitName, m.VelocityUnitName, m.TemperatureUnitName, m.DistanceUnitName));
    }

    [Fact]
    public void EveryAxisIsReadSeparately()
    {
        // GRT lets a user mix them, and plenty do — inches and yards with a Celsius powder temp.
        var u = GrtUnits.From(Cfg("oal=in;velocity=ft/s;pt=°C;range=m"));
        Assert.True(u.LengthInInch);
        Assert.True(u.VelocityInFps);
        Assert.False(u.TemperatureInF);
        Assert.False(u.DistanceInYards);
    }

    [Fact]
    public void NoInstallMeansMetricOnEveryAxis()
    {
        var u = GrtUnits.From(null);
        Assert.False(u.TemperatureInF);
        Assert.False(u.DistanceInYards);
        Assert.Equal("21.0 °C", u.Temperature(21));
        Assert.Equal(91.44, u.DistanceValue(91.44), 10);
    }
}
