using GrtPluginKit.Grt;
using GrtReloadingToolkit.Ocw;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The two numbers a shot-group tab is measured with — the reference distance and the shooting
/// distance — carry no unit attribute in the .grtload, unlike every &lt;input&gt; around them.
/// Read them in the wrong unit and the group is silently the wrong size rather than visibly so,
/// so the only honest answer is whatever GRT was configured to when the tab was measured.
/// </summary>
public class GrtShotGroupUnitsTests : IDisposable
{
    private readonly List<string> _dirs = new();

    private GrtConfig Cfg(string valueUnits)
    {
        string dir = Path.Combine(Path.GetTempPath(), "grtgroups-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        _dirs.Add(dir);
        File.WriteAllLines(Path.Combine(dir, "GordonsReloadingTool.cfg"), new[]
        {
            "Language=en",
            "ValueUnits=" + valueUnits,
        });
        var cfg = GrtConfig.Load(dir);
        Assert.NotNull(cfg);
        return cfg!;
    }

    public void Dispose()
    {
        foreach (string d in _dirs) try { Directory.Delete(d, true); } catch (Exception) { }
    }

    [Fact]
    public void AnImperialGrtMeansInchesAtYards()
    {
        // RAIDER's own config: a target measured with a caliper and shot at the 100 yd line.
        Assert.Equal((RefUnit.Inch, ShootUnit.Yards),
            GrtShotGroups.GrtDefaults(Cfg("oal=in;refdistance=in;range=yard")));
    }

    [Fact]
    public void AMetricGrtMeansMillimetresAtMetres()
        => Assert.Equal((RefUnit.Mm, ShootUnit.Meters),
            GrtShotGroups.GrtDefaults(Cfg("oal=mm;refdistance=mm;range=m")));

    [Fact]
    public void CentimetresAreTheirOwnCase()
    {
        // GRT offers cm for the reference distance, and 10x is not a rounding error.
        Assert.Equal(RefUnit.Cm, GrtShotGroups.GrtDefaults(Cfg("oal=mm;refdistance=cm;range=m")).Ref);
    }

    [Fact]
    public void TheTwoAxesAreReadSeparately()
    {
        // A caliper reads inches whatever the range flag says, and plenty of configs mix them.
        Assert.Equal((RefUnit.Inch, ShootUnit.Meters),
            GrtShotGroups.GrtDefaults(Cfg("oal=in;refdistance=in;range=m")));
    }

    [Fact]
    public void RefdistanceIsWhatIsRead_NotOal()
    {
        // The toolkit used to take this from oal, which is a different field GRT sets separately.
        Assert.Equal(RefUnit.Mm, GrtShotGroups.GrtDefaults(Cfg("oal=in;refdistance=mm;range=m")).Ref);
    }

    [Fact]
    public void NoInstallKeepsTheOldMetricAssumption()
    {
        // Stand-alone, or a dev box: behave exactly as this did before it asked GRT anything.
        Assert.Equal((RefUnit.Mm, ShootUnit.Meters), GrtShotGroups.GrtDefaults(null));
    }
}
