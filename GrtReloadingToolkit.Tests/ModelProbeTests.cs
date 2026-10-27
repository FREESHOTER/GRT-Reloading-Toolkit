using System.Globalization;
using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;
using GrtReloadingToolkit.Probe;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The pure half of the model probe: the inputs it offers, the values a sweep visits, the one change it writes into a
/// load, what a GRT answer becomes, and the elasticity / power-law exponent it reports. The load below has the shape of
/// a real GRT 2021.2030 file (6.5 Creedmoor, N550), trimmed to the inputs the probe touches.
/// </summary>
public sealed class ModelProbeTests : IDisposable
{
    private const string Load = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes" ?>
        <GordonsReloadingTool version='2021.2030-NIGHTLY'>
          <InnerBallistikInput>
            <title>Probe%20fixture</title>
            <caliber>
              <input name="CaliberName" value="6.5%20Creedmoor" unit="" type="string" descr="caliber name" />
              <input name="oal" value="71.55" unit="mm" type="decimal" descr="cartridge overall length" />
              <input name="caselen" value="48.5" unit="mm" type="decimal" descr="case length" />
              <input name="casevol" value="3.3763" unit="cm3" type="decimal" descr="case volume" />
              <input name="pMaxZul" value="4350" unit="bar" type="decimal" descr="maximum allowed pressure" />
              <input name="method" value="Piezo%20CIP" unit="" type="string" descr="cartridge test method" />
            </caliber>
            <gun>
              <input name="xe" value="660.4" unit="mm" type="decimal" descr="barrel length" />
              <input name="twistlen" value="203" unit="mm" type="decimal" descr="twist length" />
            </gun>
            <projectile>
              <input name="mp" value="9.007" unit="g" type="decimal" descr="projectile weight" />
              <input name="gdepth" value="11.5" unit="mm" type="decimal" descr="bullet seating depth" />
              <input name="glen" value="34.55" unit="mm" type="decimal" descr="projectile overall length" />
              <input name="gfriction" value="0" unit="bar" type="decimal" descr="special bullet resistance inside the barrel" />
            </projectile>
            <propellant>
              <input name="pname" value="N550" unit="" type="string" descr="propellant product name" />
              <input name="Ba" value="0.4854647" unit="" type="decimal" descr="vivacity coefficient" />
              <input name="Qex" value="3950" unit="kJ/kg" type="decimal" descr="specific explosive heat" />
              <input name="mc" value="2.566" unit="g" type="decimal" descr="propellant mass" />
              <input name="laddercnt" value="5" unit="" type="decimal" descr="ladder steps" />
              <input name="laddermc" value="0.00648" unit="g" type="decimal" descr="ladder step propellant mass" />
            </propellant>
            <appendix>
            </appendix>
          </InnerBallistikInput>
        </GordonsReloadingTool>
        """;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "probe_tests_" + Guid.NewGuid().ToString("N")[..8]);

    public ModelProbeTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private GrtLoadDoc Doc()
    {
        string path = Path.Combine(_dir, "load.grtload");
        File.WriteAllText(path, Load);
        return GrtLoadDoc.Load(path);
    }

    private static GrtInput Input(GrtLoadDoc doc, string name) => doc.DecimalInputs().Single(i => i.Name == name);

    [Fact]
    public void DecimalInputsReadValueUnitAndDescriptionAndSkipText()
    {
        var doc = Doc();
        var ba = Input(doc, "Ba");
        Assert.Equal("propellant", ba.Section);
        Assert.Equal(0.4854647, ba.Value);
        Assert.Equal("vivacity coefficient", ba.Description);
        Assert.Equal("cm3", Input(doc, "casevol").Unit);
        Assert.DoesNotContain(doc.DecimalInputs(), i => i.Name is "CaliberName" or "method" or "pname");
    }

    [Fact]
    public void TheLadderTheCoalAndTheFieldsOutsideTheSimulationAreNotOffered()
    {
        var names = ModelProbe.Offered(Doc()).Select(i => i.Name).ToList();
        foreach (string n in new[] { "laddercnt", "laddermc", "oal", "pMaxZul", "twistlen" })
            Assert.DoesNotContain(n, names);
        Assert.Contains("gfriction", names);
    }

    [Fact]
    public void ThePropellantComesFirstThenTheFileOrder()
    {
        var offered = ModelProbe.Offered(Doc()).Select(i => i.Section + "." + i.Name).ToList();
        Assert.Equal(new[] { "propellant.Ba", "propellant.Qex", "propellant.mc", "caliber.caselen", "caliber.casevol",
                             "projectile.mp", "projectile.gdepth", "projectile.glen", "projectile.gfriction", "gun.xe" }, offered);
    }

    [Fact]
    public void ASweepThatLandsOnTheLoadValueUsesItExactly()
    {
        var v = ModelProbe.Values(0.9, 1.1, 5, 1.0);
        Assert.Equal(5, v.Count);
        Assert.Contains(1.0, v);
        Assert.Equal(new[] { 0.9, 0.95, 1.0, 1.05, 1.1 }, v.Select(x => Math.Round(x, 10)));
    }

    [Fact]
    public void ASweepThatMissesTheLoadValueGetsItAddedInOrder()
    {
        var v = ModelProbe.Values(10, 20, 3, 12);
        Assert.Equal(new[] { 10.0, 12.0, 15.0, 20.0 }, v);
    }

    [Fact]
    public void ReversedEndsAreSweptLowToHigh()
        => Assert.Equal(new[] { 1.0, 2.0, 3.0 }, ModelProbe.Values(3, 1, 3, 2));

    [Fact]
    public void ADegenerateSweepIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ModelProbe.Values(1, 2, 1, 1.5));
        Assert.Throws<ArgumentException>(() => ModelProbe.Values(2, 2, 5, 2));
    }

    [Fact]
    public void TheDefaultRangeIsTenPercentEitherSideOrZeroToOneForAZero()
    {
        var (f, t) = ModelProbe.DefaultRange(0.5);
        Assert.Equal(0.45, f, 12);
        Assert.Equal(0.55, t, 12);
        Assert.Equal((0.0, 1.0), ModelProbe.DefaultRange(0));
        var (nf, nt) = ModelProbe.DefaultRange(-2);
        Assert.True(nf < nt);
    }

    [Fact]
    public void ApplyWritesTheValueAndTurnsTheLadderOff()
    {
        var doc = Doc();
        ModelProbe.Apply(doc, Input(doc, "Ba"), 0.5);
        Assert.Equal(0.5, doc.InputNumber("propellant", "Ba"));
        Assert.Equal(0, doc.LadderCount);
        Assert.Equal(71.55, doc.CoalMm);   // Ba has nothing to do with the COAL
        Assert.Equal("", doc.InputUnit("propellant", "Ba"));
    }

    [Fact]
    public void MovingTheSeatingDepthMovesTheCoalWithIt()
    {
        var doc = Doc();
        ModelProbe.Apply(doc, Input(doc, "gdepth"), 12.5);
        Assert.Equal(12.5, doc.SeatingDepthMm);
        Assert.Equal(48.5 + 34.55 - 12.5, doc.CoalMm!.Value, 9);
        Assert.Equal("mm", doc.InputUnit("projectile", "gdepth"));
    }

    [Fact]
    public void ApplyRefusesAnInputTheLoadDoesNotHave()
    {
        var doc = Doc();
        Assert.Throws<InvalidOperationException>(() => ModelProbe.Apply(doc, new GrtInput("gun", "nope", 1, "", ""), 2));
    }

    private static GrtCurvePoint Pt(double z, double p, double t) => new(0, 0, z, p, 0, 0, t);

    [Fact]
    public void APointReadsTheScalarsAndWhatTheCurveSays()
    {
        var doc = Doc();
        var r = new GrtResults { MuzzleVelocityMps = 850, MaxPressure = 3800, MaxPressureUnit = "bar", BarrelTimeMs = 1.2, BulletLeadTime10PmaxMs = 1.1 };
        var curve = new[] { Pt(0, 250, 0), Pt(0.4, 4000, 0.3), Pt(1.0, 1500, 0.8), Pt(1.0, 800, 1.2) };

        var p = ModelProbe.PointFrom(0.4854647, true, r, curve, doc);

        Assert.True(p.IsBaseline);
        Assert.Equal("bar", p.PressureUnit);
        Assert.Equal(850, p.Get(ProbeOutput.MuzzleVelocity));
        Assert.Equal(3800, p.Get(ProbeOutput.MaxPressure));
        Assert.Equal(0.3, p.Get(ProbeOutput.TimeOfPmax));
        Assert.Equal(20.0, p.Get(ProbeOutput.MuzzlePressurePct)!.Value, 9);   // 800 of a 4000 peak
        Assert.Equal(100.0, p.Get(ProbeOutput.BurntAtMuzzlePct));
        Assert.Equal(0.8, p.Get(ProbeOutput.BurnoutTime));
        // 0.5 · 9.007 g · 850² over 2.566 g · 3950 kJ/kg
        double expected = 0.5 * 0.009007 * 850 * 850 / (0.002566 * 3_950_000) * 100;
        Assert.Equal(expected, p.Get(ProbeOutput.Efficiency)!.Value, 6);
    }

    [Fact]
    public void WithoutTheCurveTheCurveOutputsAreAbsentAndNoBurnoutMeansNoBurnoutTime()
    {
        var doc = Doc();
        var r = new GrtResults { MuzzleVelocityMps = 850, MaxPressure = 3800 };
        var bare = ModelProbe.PointFrom(1, false, r, Array.Empty<GrtCurvePoint>(), doc);
        Assert.Null(bare.Get(ProbeOutput.TimeOfPmax));
        Assert.Null(bare.Get(ProbeOutput.BurntAtMuzzlePct));

        var unburnt = ModelProbe.PointFrom(1, false, r, new[] { Pt(0, 300, 0), Pt(0.9, 900, 1.0) }, doc);
        Assert.Equal(90.0, unburnt.Get(ProbeOutput.BurntAtMuzzlePct)!.Value, 9);
        Assert.Null(unburnt.Get(ProbeOutput.BurnoutTime));
    }

    private static List<ProbePoint> Sweep(Func<double, double> f, double baseline, params double[] xs) =>
        xs.Select(x =>
        {
            var p = new ProbePoint { X = x, IsBaseline = x == baseline };
            p.Y[ProbeOutput.MuzzleVelocity] = f(x);
            return p;
        }).ToList();

    [Fact]
    public void APowerLawGivesItsExponentAsBothElasticityAndFit()
    {
        var pts = Sweep(x => 800 * Math.Pow(x, 0.25), 1.0, 0.9, 0.95, 1.0, 1.05, 1.1);
        var s = Assert.Single(ModelProbe.Analyze(pts));
        Assert.Equal(ProbeOutput.MuzzleVelocity, s.Output);
        Assert.Equal(800, s.Baseline!.Value, 9);
        Assert.Equal(0.25, s.Exponent!.Value, 9);
        Assert.Equal(1.0, s.R2!.Value, 9);
        Assert.Equal(0.25, s.Elasticity!.Value, 3);   // a central difference, so close, not exact
    }

    [Fact]
    public void TheElasticityIsLocalAndTheExponentGlobal()
    {
        // y = x + 10 around x = 10: locally a 1 % change of x is a 0.5 % change of y.
        var s = ModelProbe.Analyze(Sweep(x => x + 10, 10, 5, 10, 15)).Single();
        Assert.Equal(0.5, s.Elasticity!.Value, 9);
        Assert.Equal(15, s.Min);
        Assert.Equal(25, s.Max);
    }

    [Fact]
    public void AtTheEdgeOfTheSweepTheElasticityIsOneSided()
    {
        var s = ModelProbe.Analyze(Sweep(x => 2 * x, 1, 1, 2, 3)).Single();
        Assert.Equal(1.0, s.Elasticity!.Value, 9);
    }

    [Fact]
    public void NoBaselineOrAZeroInputMeansNoElasticity()
    {
        Assert.Null(ModelProbe.Analyze(Sweep(x => x + 1, 99, 1, 2, 3)).Single().Elasticity);
        Assert.Null(ModelProbe.Analyze(Sweep(x => x + 1, 0, 0, 1, 2)).Single().Elasticity);
    }

    [Fact]
    public void APowerLawNeedsThreePositivePoints()
    {
        Assert.Equal((null, null), ModelProbe.PowerFit(new[] { (1.0, 1.0), (2.0, 2.0) }));
        Assert.Equal((null, null), ModelProbe.PowerFit(new[] { (0.0, 1.0), (1.0, 2.0), (2.0, 3.0) }));
        Assert.Equal((null, null), ModelProbe.PowerFit(new[] { (1.0, -1.0), (2.0, 2.0), (3.0, 3.0) }));
    }

    [Fact]
    public void AnOutputWithFewerThanTwoValuesIsLeftOut()
    {
        var pts = Sweep(x => x, 1, 1, 2, 3);
        pts[0].Y[ProbeOutput.BurnoutTime] = 0.9;
        Assert.DoesNotContain(ModelProbe.Analyze(pts), s => s.Output == ProbeOutput.BurnoutTime);
    }

    [Fact]
    public void GrtsForwardSlashesNameTheSameFile()
    {
        Assert.True(ProbeRunner.SamePath("C:/Temp/grt_probe_1a2b3c4d/probe_1a2b3c4d_Ba_00.grtload", @"C:\Temp\grt_probe_1a2b3c4d\probe_1a2b3c4d_Ba_00.grtload"));
        // a short 8.3 spelling of the temp folder: the run-unique file name still settles it
        Assert.True(ProbeRunner.SamePath(@"C:\Users\USER~1\AppData\Local\Temp\grt_probe_1a2b3c4d\probe_1a2b3c4d_Ba_00.grtload",
                                         @"C:\Users\User\AppData\Local\Temp\grt_probe_1a2b3c4d\probe_1a2b3c4d_Ba_00.grtload"));
        Assert.False(ProbeRunner.SamePath("C:/Temp/probe_1a2b3c4d_Ba_00.grtload", "C:/Temp/probe_1a2b3c4d_Ba_01.grtload"));
        Assert.False(ProbeRunner.SamePath("", "C:/Temp/probe.grtload"));
        Assert.False(ProbeRunner.SamePath(null, null));
    }

    [Fact]
    public void TheCsvIsInvariantWithAHeaderAndOneRowPerPoint()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("it-IT");
            var input = new GrtInput("propellant", "Ba", 0.5, "", "vivacity coefficient");
            var pts = Sweep(x => 800.5 * x, 0.5, 0.6, 0.5);
            string[] lines = ModelProbe.Csv(input, pts).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

            Assert.Equal(3, lines.Length);
            Assert.StartsWith("propellant.Ba,baseline,MuzzleVelocity,MaxPressure,", lines[0]);
            Assert.StartsWith("0.5,1,400.25,", lines[1]);   // sorted by input, decimal points, no commas inside numbers
            Assert.StartsWith("0.6,0,480.", lines[2]);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
