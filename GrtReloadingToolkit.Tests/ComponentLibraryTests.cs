using System.Text;
using GrtReloadingToolkit.Archive;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrtReloadingToolkit.Tests;

public sealed class ComponentLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "libreria_test_" + Guid.NewGuid().ToString("N"));
    private readonly ArchiveDb _a;

    public ComponentLibraryTests()
    {
        Directory.CreateDirectory(_dir);
        _a = new ArchiveDb(Path.Combine(_dir, "archivio.db"));
    }

    public void Dispose()
    {
        _a.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private object? Effective(long item, string field) => _a.ScalarP("SELECT value FROM lib_effective WHERE item_id=$i AND field=$f", ("$i", item), ("$f", field));
    private object? EffectiveSource(long item, string field) => _a.ScalarP("SELECT source FROM lib_effective WHERE item_id=$i AND field=$f", ("$i", item), ("$f", field));

    [Fact]
    public void TheMostTrustedSourceWinsAndTheNewestAmongEquals()
    {
        long b = ComponentLibrary.Item(_a, ComponentLibrary.Bullet, "Lapua", "Scenar", "6.71 mm", "palla|lapua|scenar|139.0", "test");
        ComponentLibrary.Value(_a, b, "length_mm", 34.6, null, "mm", ComponentLibrary.EpLibrary, "ep", "2026-01-01 00:00:00");
        Assert.Equal(34.6, Effective(b, "length_mm"));
        ComponentLibrary.Value(_a, b, "length_mm", 34.55, null, "mm", ComponentLibrary.GrtLoad, "a.grtload", "2025-01-01 00:00:00");
        Assert.Equal(34.55, Effective(b, "length_mm"));                     // GRT load beats the library, however old
        ComponentLibrary.Value(_a, b, "length_mm", 34.5, null, "mm", ComponentLibrary.GrtLoad, "b.grtload", "2026-02-01 00:00:00");
        Assert.Equal(34.5, Effective(b, "length_mm"));                      // the newest of the same rank
        ComponentLibrary.Value(_a, b, "length_mm", 34.61, null, "mm", ComponentLibrary.Measured, "calibro, 10 palle", "2020-01-01 00:00:00");
        Assert.Equal(34.61, Effective(b, "length_mm"));                     // the shooter's measurement beats everything
        Assert.Equal(ComponentLibrary.Measured, EffectiveSource(b, "length_mm"));
        // the same value from the same source is not written twice; nothing was overwritten
        Assert.False(ComponentLibrary.Value(_a, b, "length_mm", 34.5, null, "mm", ComponentLibrary.GrtLoad, "c.grtload", "2026-03-01 00:00:00"));
        Assert.Equal(4, _a.Count("lib_values", $"item_id={b}"));
    }

    private const string GrtXml = """
<?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
<GordonsReloadingTool version="2021.2030-NIGHTLY">
  <InnerBallistikInput>
    <title>prova</title>
    <caliber>
      <input name="CaliberName" type="string" value="6.5%20Creedmoor" descr="caliber%20name" />
      <input name="casevol" type="decimal" value="3.376" unit="cm3" descr="case%20volume" />
      <input name="caselen" type="decimal" value="48.5" unit="mm" descr="case%20length" />
      <input name="oal" type="decimal" value="71.55" unit="mm" descr="cartridge%20overall%20length" />
      <input name="pMaxZul" type="decimal" value="4350" unit="bar" descr="maximum%20allowed%20pressure" />
      <input name="method" type="string" value="Piezo%20CIP" descr="cartridge%20test%20method" />
    </caliber>
    <caliberfile>
      <var name="standard" value="CIP" /><var name="L1" value="37.84" /><var name="L3" value="48.77" /><var name="L6" value="71.76" />
      <var name="P1" value="11.95" /><var name="P2" value="11.74" /><var name="H2" value="7.49" /><var name="R1" value="12.01" /><var name="alpha" value="60.00" />
    </caliberfile>
    <gun>
      <input name="GunName" type="string" value="Tikka" descr="gun%20name" />
      <input name="xe" type="decimal" value="660.4" unit="mm" descr="barrel%20length" />
      <input name="twistlen" type="decimal" value="203.2" unit="mm" descr="twist%20length" />
    </gun>
    <projectile>
      <input name="mname" type="string" value="Lapua" descr="projectile%20manufacturer" />
      <input name="pname" type="string" value="Scenar%20GB458" descr="projectile%20product%20name" />
      <input name="Dbul" type="decimal" value="6.71" unit="mm" descr="projectile%20diameter" />
      <input name="mp" type="decimal" value="9.007" unit="g" descr="projectile%20weight" />
      <input name="glen" type="decimal" value="34.55" unit="mm" descr="projectile%20overall%20length" />
      <input name="gdepth" type="decimal" value="11.5" unit="mm" descr="bullet%20seating%20depth" />
      <input name="g7bc" type="decimal" value="0.29" descr="G7%20ballistic%20coefficient" />
    </projectile>
    <propellant>
      <input name="mname" type="string" value="Vihtavuori" descr="propellant%20manufacturer" />
      <input name="pname" type="string" value="N550" descr="propellant%20product%20name" />
      <input name="Ba" type="decimal" value="0.4854" descr="vivacity%20coefficient" />
      <input name="pcd" type="decimal" value="950" unit="kg/m3" descr="bulk%20density" />
      <input name="mc" type="decimal" value="2.566" unit="g" descr="propellant%20mass" />
    </propellant>
    <appendix>
    </appendix>
  </InnerBallistikInput>
</GordonsReloadingTool>
""";

    [Fact]
    public void AGrtLoadFilesItsComponentsAndKeepsItsOwnBrassOnTheLoad()
    {
        long sh = _a.Insert("shooters", ("name", "Io"));
        byte[] data = Encoding.UTF8.GetBytes(GrtXml);
        long f = _a.Insert("files", ("name", "a.grtload"), ("kind", "carico GRT"), ("sha256", "x"), ("size", data.Length), ("data", data),
            ("original_path", @"C:\GRT\a.grtload"), ("modified_at", "2026-04-22 10:00:00"));
        long load = _a.Insert("loads", ("shooter_id", sh), ("name", "a"), ("grtload_file_id", f));
        var log = new List<string>();

        var s = ComponentLibrary.FromArchivedGrtLoads(_a, log);

        Assert.Equal((1, 1, 1, 1, 1, 4), (s.Loads, s.Bullets, s.Cartridges, s.Powders, s.Rifles, s.Links));
        long bullet = (long)_a.Scalar("SELECT id FROM lib_items WHERE kind='palla'")!;
        Assert.Equal(139.0, Math.Round((double)Effective(bullet, "weight_gr")!, 1));
        Assert.Equal(34.55, Effective(bullet, "length_mm"));
        Assert.Equal(0.29, Effective(bullet, "bc_g7"));
        Assert.Null(Effective(bullet, "grt.projectile.gdepth"));           // seating depth is the load's
        long cart = (long)_a.Scalar("SELECT id FROM lib_items WHERE kind='cartuccia'")!;
        Assert.Equal(4350.0, Effective(cart, "pmax_cip_bar"));
        Assert.Equal(30.0, Effective(cart, "shoulder_angle_deg"));         // C.I.P. alpha is the full angle
        Assert.Equal(48.77, Effective(cart, "case_length_max_mm"));
        Assert.Null(Effective(cart, "case_capacity_cm3"));                 // the case volume is the load's brass, not the cartridge's
        Assert.Null(Effective(cart, "grt.caliber.casevol"));
        Assert.Equal(3.376, _a.Scalar($"SELECT case_capacity_cm3 FROM loads WHERE id={load}"));
        Assert.Equal(11.5, _a.Scalar($"SELECT seating_depth_mm FROM loads WHERE id={load}"));
        long powder = (long)_a.Scalar("SELECT id FROM lib_items WHERE kind='polvere'")!;
        Assert.Equal(0.4854, Effective(powder, "grt.propellant.Ba"));
        Assert.Null(Effective(powder, "grt.propellant.mc"));               // the charge is the load's
        long rifle = (long)_a.Scalar("SELECT id FROM lib_items WHERE kind='arma'")!;
        Assert.Equal(203.2, Effective(rifle, "twist_mm"));

        // an archived load (GRT's demonstration file) stays out of the library
        _a.Exec($"UPDATE loads SET archived=1 WHERE id={load}");
        Assert.Equal(0, ComponentLibrary.FromArchivedGrtLoads(_a, log).Loads);
    }

    private string EpJson()
    {
        string json = """
{
  "manufacturers": [ { "id": "MAN_LAP", "name": "Lapua" }, { "id": "MAN_VV", "name": "Vihtavuori" } ],
  "diameters": [ { "id": "DIA_264", "bulletDiameterMm": 6.71, "boreDiameterMm": 6.5 } ],
  "primerPockets": [ { "id": "PKT_LRG", "name": "Large" } ],
  "bullets": [
    { "id": "BUL_1", "manufacturerId": "MAN_LAP", "diameterId": "DIA_264", "name": "Scenar",
      "physis": { "weightGrams": 9.0071, "overallLengthMm": 34.56, "tipLengthMm": null, "tipLengthBasis": null },
      "ballistics": { "g1BC": 0.578, "g7BC": 0.29, "basis": "measured" },
      "drag": { "g7BC": 0.29, "source": "measured", "shape": "measured", "f7": [ 1.0, 1.0 ] } }
  ],
  "cartridges": [
    { "id": "CTG_308", "name": "308 Winchester", "diameterId": "DIA_264", "primerPocketId": "PKT_LRG", "baseCapacityH2oGrams": 3.6,
      "maxCaseLengthMm": 51.18, "baseToShoulderMm": 39.62, "bodyDiameterMm": 11.96, "shoulderDiameterMm": 11.53, "neckDiameterMm": 8.72,
      "rimDiameterMm": 12.01, "oalMm": 71.37, "maxSaamiPa": 427474952, "maxCipPa": 415000000 }
  ],
  "powders": [ { "id": "PWD_N550", "manufacturerId": "MAN_VV", "name": "N550", "bulkDensityKgM3": 960 } ],
  "brass": [ { "id": "BRS_1", "manufacturerId": "MAN_LAP", "cartridgeId": "CTG_308", "capacityH2oGrams": 3.65 } ],
  "primers": [ { "id": "PRI_1", "manufacturerId": "MAN_LAP", "name": "Large Rifle", "primerPocketId": "PKT_LRG", "primerClass": "rifle" } ],
  "bulletDragGrid": { "mach": [ 0.5, 3.0 ] }
}
""";
        string p = Path.Combine(_dir, "master-db.json");
        File.WriteAllText(p, json);
        return p;
    }

    [Fact]
    public void TheEpLibraryIsCopiedWithItsUnitsConverted()
    {
        var s = ComponentLibrary.FromEmpiricalPrecision(_a, EpJson(), "prova", new List<string>());
        Assert.Equal((1, 1, 1, 1, 1), (s.Bullets, s.Cartridges, s.Powders, s.Cases, s.Primers));
        long b = (long)_a.Scalar("SELECT id FROM lib_items WHERE identity='palla|ep:BUL_1'")!;
        Assert.Equal(139.0, Math.Round((double)Effective(b, "weight_gr")!, 1));
        Assert.Equal(6.71, Effective(b, "diameter_mm"));
        Assert.Null(Effective(b, "tip_length_mm"));                         // null means no tip, never 0
        long c = (long)_a.Scalar("SELECT id FROM lib_items WHERE kind='cartuccia'")!;
        Assert.Equal(62000.0, Effective(c, "pmax_saami_psi"));
        Assert.Equal(4150.0, Effective(c, "pmax_cip_bar"));
        Assert.Equal("Large", _a.Scalar($"SELECT text FROM lib_effective WHERE item_id={c} AND field='primer_pocket'"));
    }

    [Fact]
    public void ACartridgeIsJoinedByItsCaseNotByItsName()
    {
        // GRT calls it ".308 Win. (…)" and gives the C.I.P. overall length; EP calls it "308 Winchester" with SAAMI's
        long g = ComponentLibrary.Item(_a, ComponentLibrary.Cartridge, "", ".308 Win. (308 Winchester, 7,62 x 51)", "", "cartuccia|.308 win.", "test");
        foreach (var (f, v) in new[] { ("case_length_max_mm", 51.18), ("base_to_shoulder_mm", 39.62), ("body_diameter_mm", 11.96),
                                       ("shoulder_diameter_mm", 11.53), ("neck_diameter_mm", 8.72), ("rim_diameter_mm", 12.01), ("oal_max_mm", 71.12) })
            ComponentLibrary.Value(_a, g, f, v, null, "mm", ComponentLibrary.GrtLoad, "a.grtload", "2026-01-01 00:00:00");
        ComponentLibrary.FromEmpiricalPrecision(_a, EpJson(), "prova", new List<string>());
        var log = new List<string>();

        ComponentLibrary.MatchCartridgesByDrawing(_a, log);

        Assert.Equal(1, _a.Count("lib_items", "kind='cartuccia'"));
        Assert.Equal(62000.0, Effective(g, "pmax_saami_psi"));               // EP's values joined the GRT item
        Assert.Equal(71.12, Effective(g, "oal_max_mm"));                     // the GRT load still ranks above the library
        Assert.Equal("308 Winchester", _a.Scalar($"SELECT text FROM lib_values WHERE item_id={g} AND field='alias'"));
    }

    [Fact]
    public void TwoCasesOfTheSameShapeAreOfferedNotChosen()
    {
        long g = ComponentLibrary.Item(_a, ComponentLibrary.Cartridge, "", ".223 Rem.", "", "cartuccia|.223 rem.", "test");
        long e1 = ComponentLibrary.Item(_a, ComponentLibrary.Cartridge, "", "223 Remington", "", "cartuccia|223 remington", "test");
        long e2 = ComponentLibrary.Item(_a, ComponentLibrary.Cartridge, "", "5.56x45mm NATO", "", "cartuccia|5.56x45mm nato", "test");
        var dims = new[] { ("case_length_max_mm", 44.7), ("base_to_shoulder_mm", 36.52), ("body_diameter_mm", 9.58),
                           ("shoulder_diameter_mm", 9.0), ("neck_diameter_mm", 6.43), ("rim_diameter_mm", 9.6) };
        foreach (var (f, v) in dims)
        {
            ComponentLibrary.Value(_a, g, f, v, null, "mm", ComponentLibrary.GrtLoad, "a", "2026-01-01 00:00:00");
            ComponentLibrary.Value(_a, e1, f, v, null, "mm", ComponentLibrary.EpLibrary, "ep", "2026-01-01 00:00:00");
            ComponentLibrary.Value(_a, e2, f, v + 0.01, null, "mm", ComponentLibrary.EpLibrary, "ep", "2026-01-01 00:00:00");
        }
        ComponentLibrary.MatchCartridgesByDrawing(_a, new List<string>());
        Assert.Equal(3, _a.Count("lib_items", "kind='cartuccia'"));
        Assert.Equal(2, _a.Count("lib_suggestions", $"item_id={g}"));
    }
}
