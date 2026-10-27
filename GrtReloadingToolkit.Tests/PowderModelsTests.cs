using GrtPluginKit.Grt;
using GrtReloadingToolkit.Cal;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The powder models the user exports from GRT's propellant database into GRT's "Polveri" folder, and how a load's
/// own Ba / k / a0 / z1 / z2 compare with them. The export below is the shape of a real one (Vihtavuori N133,
/// model 2021-04-18, exported by the user on 2026-10-16), trimmed of its picture ids.
/// </summary>
public sealed class PowderModelsTests : IDisposable
{
    private const string N133 = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
        <GordonsReloadingTool>
          <propellantfile>
            <var name="mname" value="Vihtavuori" unit="" type="string" descr="propellant manufacturer" />
            <var name="pname" value="N133" unit="" type="string" descr="propellant product name" />
            <var name="lotid" value="2021-04-18" unit="" type="string" descr="lot-id/no." />
            <var name="Ba" value="0.7189" unit="" type="decimal" descr="vivacity coefficient" />
            <var name="Qex" value="3600" unit="kJ/kg" type="decimal" descr="specific explosive heat" />
            <var name="k" value="1.2345" unit="" type="decimal" descr="ratio of the specific heat of the powder gases" />
            <var name="a0" value="1.2011" unit="" type="decimal" descr="Ba(phi) coefficient 0" />
            <var name="z1" value="0.519" unit="" type="decimal" descr="burn-up limit z1" />
            <var name="z2" value="0.8226" unit="" type="decimal" descr="burn-up limit z2" />
            <var name="Qlty" value="0.85" unit="" type="decimal" descr="model development/quality" />
          </propellantfile>
        </GordonsReloadingTool>
        """;

    private static string Block(string maker, string name, string date, string ba) => $"""
          <propellantfile>
            <var name="mname" value="{maker}" />
            <var name="pname" value="{name}" />
            <var name="lotid" value="{date}" />
            <var name="Ba" value="{ba}" />
          </propellantfile>
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "powder_models_" + Guid.NewGuid().ToString("N")[..8]);

    public PowderModelsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string Write(string name, string content)
    {
        string p = Path.Combine(_dir, name);
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public void AnExportIsReadWithItsModelDate()
    {
        var m = Assert.Single(PowderModels.ReadFile(Write("Vihtavuori N133 - 2021-04-18.propellant", N133)));
        Assert.Equal("Vihtavuori", m.Maker);
        Assert.Equal("N133", m.Name);
        Assert.Equal("2021-04-18", m.ModelDate);
        Assert.Equal(0.7189, m.Ba);
        Assert.Equal(1.2345, m.K);
        Assert.Equal(1.2011, m.A0);
        Assert.Equal(0.519, m.Z1);
        Assert.Equal(0.8226, m.Z2);
        Assert.Equal(3600, m.Qex);
        Assert.Equal(0.85, m.Quality);
        Assert.Null(m.A1);
        Assert.Equal("Vihtavuori N133, 2021-04-18", m.Label);
    }

    [Fact]
    public void AFileWithSeveralBlocksGivesOneModelEach()
    {
        string xml = "<GordonsReloadingTool>" + Block("Alliant", "RL%2025", "2023-04-01", "0.5") + Block("Alliant", "RL 33", "", "0.4") + "</GordonsReloadingTool>";
        var models = PowderModels.ReadFile(Write("two.xml", xml));
        Assert.Equal(new[] { "RL 25", "RL 33" }, models.Select(m => m.Name));
    }

    [Fact]
    public void WhatIsNotAPowderExportIsIgnored()
    {
        Assert.Empty(PowderModels.ReadFile(Write("broken.propellant", "not xml at all")));
        Assert.Empty(PowderModels.ReadFile(Write("other.xml", "<SomethingElse><propellantfile/></SomethingElse>")));
        Assert.Empty(PowderModels.ReadFile(Write("noname.xml", "<GordonsReloadingTool>" + Block("X", "", "", "1") + "</GordonsReloadingTool>")));
    }

    [Fact]
    public void TheFolderReadsPropellantAndXmlFilesOnly()
    {
        Write("a.propellant", N133);
        Write("b.xml", "<GordonsReloadingTool>" + Block("Hodgdon", "H4350", "2022-01-01", "0.4") + "</GordonsReloadingTool>");
        Write("c.txt", N133);
        Assert.Equal(new[] { "N133", "H4350" }, PowderModels.ReadFolder(_dir).Select(m => m.Name));
        Assert.Empty(PowderModels.ReadFolder(Path.Combine(_dir, "missing")));
        Assert.Empty(PowderModels.ReadFolder(null));
    }

    private static PowderModel M(string maker, string name, string date = "", double ba = 0.5) =>
        new(maker, name, date, ba, null, null, null, null, null, null, null, "");

    [Fact]
    public void TheNameMustMatchAndTheMakersSpellingMayDiffer()
    {
        var models = new[] { M("Vihtavuori", "N550"), M("Reload Swiss", "RS14") };
        Assert.Equal("Vihtavuori", PowderModels.Match(models, "Vhitavuori", "N550")!.Maker);   // GRT's own misspelling in a load
        Assert.Equal("RS14", PowderModels.Match(models, "ReloadSwiss", "RS 14")!.Name);
        Assert.Null(PowderModels.Match(models, "Vihtavuori", "N560"));
        Assert.Null(PowderModels.Match(models, "Vihtavuori", ""));
    }

    [Fact]
    public void TheMakerSettlesANameTwoMakersShare()
    {
        var models = new[] { M("Alliant", "X1", ba: 0.3), M("Hodgdon", "X1", ba: 0.6) };
        Assert.Equal(0.6, PowderModels.Match(models, "Hodgdon", "X1")!.Ba);
        Assert.Null(PowderModels.Match(models, "Somebody", "X1"));   // two makers, neither the load's: no guess
    }

    [Fact]
    public void TheNewestModelVersionWins()
    {
        var models = new[] { M("Vihtavuori", "N133", "2019-01-20", 0.70), M("Vihtavuori", "N133", "2021-04-18", 0.7189) };
        Assert.Equal("2021-04-18", PowderModels.Match(models, "Vihtavuori", "N133")!.ModelDate);
    }

    [Fact]
    public void TheLoadIsComparedFieldByField()
    {
        string load = Write("load.grtload", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
            <GordonsReloadingTool version='2021.2030-NIGHTLY'>
              <InnerBallistikInput>
                <propellant>
                  <input name="mname" value="Vihtavuori" unit="" type="string" />
                  <input name="pname" value="N133" unit="" type="string" />
                  <input name="Ba" value="0.79079" unit="" type="decimal" />
                  <input name="k" value="1.2345" unit="" type="decimal" />
                  <input name="a0" value="1.2011" unit="" type="decimal" />
                </propellant>
                <appendix></appendix>
              </InnerBallistikInput>
            </GordonsReloadingTool>
            """);
        var doc = GrtLoadDoc.Load(load);
        Assert.Equal("Vihtavuori", doc.PropellantMaker);
        var model = Assert.Single(PowderModels.ReadFile(Write("n133.propellant", N133)));

        var d = PowderModels.Compare(doc, model);
        Assert.Equal(new[] { "Ba", "k", "a0" }, d.Select(x => x.Field));   // the load has no z1 / z2: not compared
        Assert.Equal(10.0, d[0].Pct, 6);
        Assert.Equal(0.0, d[1].Pct, 9);
    }

    [Fact]
    public void APercentageNeedsAModelValue()
    {
        Assert.Equal(-50.0, PowderModels.PctFrom(0.5, 1.0));
        Assert.Null(PowderModels.PctFrom(0.5, null));
        Assert.Null(PowderModels.PctFrom(0.5, 0));
    }
}
