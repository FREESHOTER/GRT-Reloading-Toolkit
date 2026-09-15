using GrtPluginKit.Grt;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The unit map GRT keeps in GordonsReloadingTool.cfg. Both fixtures below are the real
/// <c>ValueUnits</c> line copied from a working install — one configured imperial, one metric —
/// because the whole point of reading this file is that the two disagree and the toolkit has to
/// follow whichever it is given.
/// </summary>
public class GrtConfigTests
{
    private const string Imperial =
        "mc=grain;caselen=in;oal=in;gdepth=in;pressure=psi;energy=ft-lb;velocity=ft/s;impulse=lbsec;" +
        "xe=in;casevol=grain H2O;Vb=in3;Dbul=in;mp=grain;glen=in;gdepthc=in;pt=F;time=ms;force=lbf;" +
        "charge=grain;range=yard;refdistance=in;angle=;twistlen=in;gdepthmax=in;M=in;gjump=in";

    private const string Metric =
        "mc=grain;caselen=mm;oal=mm;gdepth=mm;pressure=bar;energy=J;velocity=m/s;impulse=Ns;" +
        "xe=mm;Aeff=mm²;casevol=grain H2O;Vb=cm³;Dbul=in;mp=grain;glen=mm;gdepthc=mm;pt=°C;time=ms;" +
        "force=kN;charge=grain;range=m;refdistance=mm;angle=°;twistlen=mm;gdepthmax=mm;M=mm;gjump=mm";

    [Theory]
    [InlineData("gdepth", "in")]
    [InlineData("caselen", "in")]
    [InlineData("oal", "in")]
    [InlineData("velocity", "ft/s")]
    [InlineData("pressure", "psi")]
    [InlineData("pt", "F")]
    [InlineData("range", "yard")]
    public void ReadsImperialFields(string field, string unit)
        => Assert.Equal(unit, GrtConfig.ParseUnits(Imperial)[field]);

    [Theory]
    [InlineData("gdepth", "mm")]
    [InlineData("velocity", "m/s")]
    [InlineData("pressure", "bar")]
    [InlineData("range", "m")]
    public void ReadsMetricFields(string field, string unit)
        => Assert.Equal(unit, GrtConfig.ParseUnits(Metric)[field]);

    [Fact]
    public void KeepsUnitsWithSpaces()
        => Assert.Equal("grain H2O", GrtConfig.ParseUnits(Imperial)["casevol"]);

    [Fact]
    public void KeepsNonAsciiUnits()
    {
        var m = GrtConfig.ParseUnits(Metric);
        Assert.Equal("mm²", m["Aeff"]);
        Assert.Equal("°C", m["pt"]);
        Assert.Equal("cm³", m["Vb"]);
    }

    [Fact]
    public void AnEmptyUnitIsNotAValue()
    {
        // angle= carries no unit in the imperial fixture; degrees are implied. A caller asking
        // what unit to show must get "no opinion", not an empty string it might print.
        Assert.Equal(string.Empty, GrtConfig.ParseUnits(Imperial)["angle"]);
    }

    [Fact]
    public void IgnoresMalformedPairs()
    {
        var m = GrtConfig.ParseUnits("gdepth=in;;garbage;=nokey;oal=in");
        Assert.Equal(2, m.Count);
        Assert.Equal("in", m["gdepth"]);
        Assert.Equal("in", m["oal"]);
    }

    [Fact]
    public void TheTwoInstallsDisagreeOnLengthButAgreeOnCharge()
    {
        // The reason this class exists: same field, same GRT, different answer per user.
        var imp = GrtConfig.ParseUnits(Imperial);
        var met = GrtConfig.ParseUnits(Metric);
        Assert.NotEqual(imp["gdepth"], met["gdepth"]);
        Assert.Equal(imp["charge"], met["charge"]);
        // Bullet diameter is inch in both: GRT ships it that way and neither user changed it.
        Assert.Equal("in", imp["Dbul"]);
        Assert.Equal("in", met["Dbul"]);
    }
}
