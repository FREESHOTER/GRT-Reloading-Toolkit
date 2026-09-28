using GrtPluginKit.Grt;
using GrtReloadingToolkit.Ocw;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// Answers "does the shot-coordinate import from GRT's own 'Shot group analysis' tabs actually
/// work?" -- unlike <see cref="LoadSnapshot.AllFromGrtload"/> (fixed for the Journal), this path
/// already lists every group rather than picking one, so there was no "always the same" bug here.
/// What these tests pin instead: the image-fraction-to-MOA math itself, and a real, separate bug
/// found while checking that math against the user's own real ladder file -- a shot-group tab has
/// no charge-weight attribute of its own (unlike a Measurement &lt;charge&gt;, which has a real
/// <c>value</c> in kg), so the only source for a step number is the group's own name, and GRT
/// auto-names every new group "Gruppo N" / "Group N" until a person renames it. Before the fix,
/// <see cref="GrtShotGroups"/> read that auto-numbering as if it were a real charge in grains.
/// </summary>
public sealed class GrtShotGroupsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("grt-shotgroups-tests-").FullName;

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    /// <summary>
    /// A minimal real shot-group tab: a 1000x1000 "picture", two reference points 0.5 image-widths
    /// apart representing 50 mm (so 1 image-width = 100 mm), shot at 100 m (1 MOA = 29.0888 mm), and
    /// one group with a point of aim and shots at known offsets -- chosen so the expected MOA can be
    /// hand-computed rather than merely re-asserting whatever the code already outputs.
    /// </summary>
    private string WriteLoad(string groupName, string tabTitle = "Gruppo%20di%20colpi")
    {
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n"
            + "<GordonsReloadingTool>\n  <InnerBallistikInput>\n"
            + "    <caliber><input name=\"CaliberName\" value=\"Test\" /></caliber>\n"
            + "    <appendix>\n"
            + $"      <ShotGroup title=\"{tabTitle}\" refPoint1X=\"0.25\" refPoint1Y=\"0.5\" refPoint2X=\"0.75\" refPoint2Y=\"0.5\" refDistance=\"50\" shootDistance=\"100\">\n"
            + "        <picture name=\"x\" type=\"jpeg\" width=\"1000\" height=\"1000\" data=\"\" />\n"
            + $"        <group name=\"{groupName}\">\n"
            + "          <point x=\"0.50\" y=\"0.50\" flyer=\"false\" PointOfAim=\"true\" />\n"
            + "          <point x=\"0.51\" y=\"0.50\" flyer=\"false\" />\n"
            + "          <point x=\"0.50\" y=\"0.49\" flyer=\"false\" />\n"
            + "        </group>\n"
            + "      </ShotGroup>\n"
            + "    </appendix>\n  </InnerBallistikInput>\n</GordonsReloadingTool>\n";
        string path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".grtload");
        File.WriteAllText(path, xml);
        return path;
    }

    [Fact]
    public void TheReferencePointsAndShootingDistanceProduceTheExpectedMoaScale()
    {
        // 0.5 image-widths = 50 mm, so 1 mm = 0.01 image-width = 10 px on a 1000 px image;
        // mmPerPx = 50 / 500 = 0.1. At 100 m, 1 MOA = 29.0888 mm, so 1 MOA = 290.888 px.
        // The second point is 0.01 image-widths right of the point of aim = 10 px = 0.0344 MOA.
        var doc = GrtLoadDoc.Load(WriteLoad("Gruppo%201"));
        var (groups, _) = GrtShotGroups.FromDoc(doc);

        var g = Assert.Single(groups);
        var right = g.Impacts.Single(i => i.XMoa > 0);
        Assert.Equal(0.0344, right.XMoa, 4);
        Assert.Equal(0, right.YMoa, 6);

        // The third point is 0.01 image-heights ABOVE the point of aim in image coordinates
        // (smaller y), which is "up" on the target -- positive Y in this toolkit's convention.
        var up = g.Impacts.Single(i => i.YMoa > 0);
        Assert.Equal(0.0344, up.YMoa, 4);
        Assert.Equal(0, up.XMoa, 6);
    }

    [Theory]
    [InlineData("Gruppo%201")]   // GRT's own Italian default
    [InlineData("Gruppo%206")]
    [InlineData("Group%201")]    // GRT's own English default
    [InlineData("Group%2012")]
    public void GrtsOwnDefaultAutoNumberedGroupNameIsNeverReadAsARealCharge(string defaultName)
    {
        var doc = GrtLoadDoc.Load(WriteLoad(defaultName));
        var (groups, _) = GrtShotGroups.FromDoc(doc);

        Assert.Null(Assert.Single(groups).ChargeGrains);
    }

    [Fact]
    public void ADecimalGroupNameIsReadAsTheRealChargeEvenWithoutAKeyword()
    {
        // A user who renames a group to the actual charge they shot (the ordinary way to make a
        // shot-group tab meaningful) must still work -- this is what the fix must not break.
        var doc = GrtLoadDoc.Load(WriteLoad("40.3"));
        var (groups, _) = GrtShotGroups.FromDoc(doc);

        Assert.Equal(40.3, Assert.Single(groups).ChargeGrains);
    }

    [Fact]
    public void AKeywordedIntegerIsStillReadAsARealCharge()
    {
        // Unlike a bare integer, a keyword removes the ambiguity with GRT's own auto-naming --
        // nothing GRT generates on its own ever contains "carica".
        var doc = GrtLoadDoc.Load(WriteLoad("Carica%2040"));
        var (groups, _) = GrtShotGroups.FromDoc(doc);

        Assert.Equal(40, Assert.Single(groups).ChargeGrains);
    }

    [Fact]
    public void FallsBackToTheTabTitleWhenTheGroupNameCarriesNothing()
    {
        var doc = GrtLoadDoc.Load(WriteLoad(groupName: "Gruppo%201", tabTitle: "40.6%20gr"));
        var (groups, _) = GrtShotGroups.FromDoc(doc);

        Assert.Equal(40.6, Assert.Single(groups).ChargeGrains);
    }
}
