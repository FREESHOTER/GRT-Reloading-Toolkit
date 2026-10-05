using GrtReloadingToolkit.Ocw;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The two numbers a shot-group tab is measured with — the reference distance and the shooting
/// distance — carry no unit attribute in the .grtload, unlike every &lt;input&gt; around them.
/// GRT stores them in SI whatever units it displays, so they are read as millimetres and metres.
/// </summary>
public class GrtShotGroupUnitsTests
{
    [Fact]
    public void GrtStoresShotGroupsInSiWhateverItDisplays()
    {
        // Observed in GRT's own demo load: a 3 inch reference line shot at 100 yd is stored as 76.2 and 91.44.
        Assert.Equal((RefUnit.Mm, ShootUnit.Meters), GrtShotGroups.GrtDefaults());
    }

    [Fact]
    public void AnImperialInstallIsNotReadAsInchesAtYards()
    {
        // Reading 76.2 / 91.44 as inches at yards would give a 1935 mm line at 83.6 m.
        var (r, s) = GrtShotGroups.GrtDefaults();
        Assert.Equal(76.2, GrtShotGroups.RefToMm(76.2, r), 6);
        Assert.Equal(91.44, GrtShotGroups.ShootToM(91.44, s), 6);
    }
}
