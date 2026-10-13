using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>The guided diagnosis: each rule fires on the data it is meant for, and says what is missing when it cannot be applied.</summary>
public sealed class DiagnosisTests
{
    private static string V(double mps) => mps.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " m/s";

    private static DiagnosisResult Run(IEnumerable<DiagnosisString> strings, double? chosen = null, bool nodeSearched = false, DiagnosisNode? node = null)
        => Diagnosis.Run(new DiagnosisInput(strings.ToList(), chosen, nodeSearched, node, V));

    private static readonly double[] WideTen = { 800, 812, 790, 805, 795, 815, 785, 808, 792, 803 };
    private static readonly double[] TightEight = { 800, 801, 799, 800, 802, 800, 801, 830 };
    private static readonly double[] Tight = { 800, 801, 799, 800, 802, 801, 800, 799, 801, 800 };

    private static DiagnosisString S(string name, double[] v, double? charge = null, double? temp = null, double? group = null)
        => new(name, charge, v, temp, group);

    // ---- R1
    [Fact]
    public void R1_AWideVelocitySpreadWithAGoodGroupPointsAtTheChargeOrTheIgnition()
    {
        var r = Run(new[] { S("40.2 gr", WideTen, 40.2, group: 0.4) });
        var f = Assert.Single(r.Findings, x => x.Rule == "R1");
        Assert.Equal(DiagnosisConfidence.Medium, f.Confidence);      // ten shots
        Assert.Contains("charge or the ignition", f.Template);
        Assert.Equal("40.2 gr", f.Args[0]);
    }

    [Fact]
    public void R1_FewerThanTenShotsIsOnlyLowConfidence()
    {
        var r = Run(new[] { S("x", WideTen.Take(7).ToArray(), group: 0.4) });
        Assert.Equal(DiagnosisConfidence.Low, Assert.Single(r.Findings, x => x.Rule == "R1").Confidence);
    }

    [Fact]
    public void R1_StaysQuietWhenTheGroupIsBadToo()
        => Assert.DoesNotContain(Run(new[] { S("x", WideTen, group: 3.0) }).Findings, x => x.Rule == "R1");

    [Fact]
    public void R1_StaysQuietWhenTheVelocityIsFine()
        => Assert.DoesNotContain(Run(new[] { S("x", Tight, group: 0.4) }).Findings, x => x.Rule == "R1");

    [Fact]
    public void R1_SaysWhyItCouldNotRun()
    {
        Assert.Contains(Run(new[] { S("x", new double[] { 800, 801, 802 }, group: 0.4) }).Gaps, g => g.Rule == "R1" && g.Template.Contains("shots or more"));
        Assert.Contains(Run(new[] { S("x", WideTen) }).Gaps, g => g.Rule == "R1" && g.Template.Contains("no group"));
    }

    // ---- R2
    [Fact]
    public void R2_OneShotBehindTheSpreadIsNamed()
    {
        var f = Assert.Single(Run(new[] { S("s", TightEight) }).Findings, x => x.Rule == "R2");
        Assert.Equal(8, f.Args[1]);                        // shot number
        Assert.Equal(DiagnosisConfidence.Low, f.Confidence);   // eight shots: Chauvenet flags too readily on short strings
    }

    [Fact]
    public void R2_TenShotsOrMoreMakeItMedium()
    {
        var ten = new double[] { 800, 801, 799, 800, 802, 800, 801, 799, 800, 835 };
        Assert.Equal(DiagnosisConfidence.Medium, Assert.Single(Run(new[] { S("s", ten) }).Findings, x => x.Rule == "R2").Confidence);
    }

    [Fact]
    public void R2_AStringWithoutAnOutlierGivesNothing()
        => Assert.DoesNotContain(Run(new[] { S("s", Tight) }).Findings, x => x.Rule == "R2");

    [Fact]
    public void R2_ATooShortStringIsReportedAsAGapNotSkippedInSilence()
        => Assert.Contains(Run(new[] { S("s", new double[] { 800, 801, 830 }) }).Gaps, g => g.Rule == "R2");

    // ---- R6
    private static double[] Around(double mean) => new[] { mean - 2, mean + 2, mean - 1, mean + 1, mean };

    [Fact]
    public void R6_TheSameChargeAtTwoTemperaturesWithAClearDifferenceIsFlagged()
    {
        var r = Run(new[] { S("cold", Around(800), 40.0, 5), S("hot", Around(806), 40.0, 25) });
        var f = Assert.Single(r.Findings, x => x.Rule == "R6");
        Assert.Equal(DiagnosisConfidence.Medium, f.Confidence);
        Assert.Equal("0.30 m/s per degree C", f.Args[4]);   // 6 m/s over 20 C
        Assert.Equal("5.0 C", f.Args[1]);
        Assert.Equal("25.0 C", f.Args[2]);
    }

    [Fact]
    public void R6_QuotesTemperatureAndSlopeInTheUnitsTheShooterSees()
    {
        // what the window passes for a GRT set to ft/s and degrees F
        var input = new DiagnosisInput(
            new[] { S("cold", Around(800), 40.0, 5), S("hot", Around(806), 40.0, 25) }.ToList(), null, false, null, V, null,
            c => (c * 9 / 5 + 32).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " °F",
            v => (v / 0.3048 * 5 / 9).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " ft/s per °F");
        var f = Assert.Single(Diagnosis.Run(input).Findings, x => x.Rule == "R6");
        Assert.Equal("41.0 °F", f.Args[1]);
        Assert.Equal("77.0 °F", f.Args[2]);
        Assert.Equal("0.55 ft/s per °F", f.Args[4]);
    }

    [Fact]
    public void R6_ADifferenceWithinTheNoiseIsNotAFinding()
        => Assert.DoesNotContain(Run(new[] { S("cold", Around(800), 40.0, 5), S("hot", Around(800.5), 40.0, 25) }).Findings, x => x.Rule == "R6");

    [Fact]
    public void R6_NeedsTwoTemperaturesFarEnoughApart()
    {
        var r = Run(new[] { S("a", Around(800), 40.0, 15), S("b", Around(806), 40.0, 17) });
        Assert.DoesNotContain(r.Findings, x => x.Rule == "R6");
        Assert.Contains(r.Gaps, g => g.Rule == "R6");
    }

    // ---- R7
    [Fact]
    public void R7_AChargeOutsideTheNodeIsFlagged_MoreSurelyTheFartherItIs()
    {
        var near = Assert.Single(Run(Array.Empty<DiagnosisString>(), 39.8, true, new DiagnosisNode(40.2, 40.6)).Findings, x => x.Rule == "R7");
        Assert.Equal(DiagnosisConfidence.Low, near.Confidence);
        var far = Assert.Single(Run(Array.Empty<DiagnosisString>(), 39.2, true, new DiagnosisNode(40.2, 40.6)).Findings, x => x.Rule == "R7");
        Assert.Equal(DiagnosisConfidence.Medium, far.Confidence);
    }

    [Fact]
    public void R7_AChargeInsideTheNodeGivesNothing()
        => Assert.DoesNotContain(Run(Array.Empty<DiagnosisString>(), 40.4, true, new DiagnosisNode(40.2, 40.6)).Findings, x => x.Rule == "R7");

    [Fact]
    public void R7_SaysWhatIsMissing()
    {
        Assert.Contains(Run(Array.Empty<DiagnosisString>()).Gaps, g => g.Rule == "R7" && g.Template.Contains("no charge has been chosen"));
        Assert.Contains(Run(Array.Empty<DiagnosisString>(), 40.0, false, null).Gaps, g => g.Rule == "R7" && g.Template.Contains("no node"));
    }

    // ---- the whole
    [Fact]
    public void FindingsComeStrongestFirst()
    {
        var r = Run(new[] { S("40.2 gr", WideTen, 40.2, group: 0.4) }, 39.8, true, new DiagnosisNode(40.2, 40.6));
        Assert.Equal(new[] { "R1", "R7" }, r.Findings.Select(f => f.Rule).ToArray());   // Medium before Low
    }

    [Fact]
    public void EveryFindingCarriesItsProofAndWhereToGoBack()
    {
        var r = Run(new[] { S("s", TightEight, 40.0), S("40.2 gr", WideTen, 40.2, group: 0.4) }, 39.2, true, new DiagnosisNode(40.2, 40.6));
        Assert.NotEmpty(r.Findings);
        Assert.All(r.Findings, f => { Assert.False(string.IsNullOrWhiteSpace(f.ProofTemplate)); Assert.InRange(f.ReturnToPhase, 2, 3); });
    }

    // ---- observations (rules not validated yet)
    private static DiagnosisResult RunWith(IEnumerable<DiagnosisString> strings, IEnumerable<DiagnosisGroup>? groups = null)
        => Diagnosis.Run(new DiagnosisInput(strings.ToList(), null, false, null, V, groups?.ToList()));

    [Fact]
    public void R4_AColdShotApartFromTheWarmOnesIsAnObservationNotAFinding()
    {
        var v = new double[] { 812, 800, 801, 799, 800, 802, 800, 801 };
        var cold = new[] { true, false, false, false, false, false, false, false };
        var r = RunWith(new[] { new DiagnosisString("s", 40.0, v, null, null, cold) });
        Assert.Single(r.Observations, o => o.Rule == "R4");
        Assert.DoesNotContain(r.Findings, f => f.Rule == "R4");
    }

    [Fact]
    public void R4_WithNoMarkedShotItSaysHowToWriteIt()
        => Assert.Contains(RunWith(new[] { S("s", Tight) }).Gaps, g => g.Rule == "R4" && g.Template.Contains("COLD=1"));

    [Fact]
    public void R4_TooFewWarmShotsIsAGap()
    {
        var r = RunWith(new[] { new DiagnosisString("s", null, new double[] { 812, 800, 801, 799 }, null, null, new[] { true, false, false, false }) });
        Assert.Contains(r.Gaps, g => g.Rule == "R4" && g.Template.Contains("fewer than"));
    }

    [Fact]
    public void R3_AFallingStringIsAnObservationAndAShortOneIsAGap()
    {
        var drift = Enumerable.Range(0, 12).Select(i => 820.0 - i * 1.5).ToArray();   // the detector reads a velocity that falls along the string (fouling)
        Assert.Single(RunWith(new[] { S("long", drift) }).Observations, o => o.Rule == "R3");
        Assert.Contains(RunWith(new[] { S("short", Tight.Take(8).ToArray()) }).Gaps, g => g.Rule == "R3");
    }

    [Fact]
    public void R5_AWindWrittenDownMakesTheGroupAnObservationEvenWhenNotSkewed()
    {
        var g = new DiagnosisGroup("600 m tab#40.2 gr", 600, 1.2, 1.1, 5, 3.0, 90);
        var o = Assert.Single(RunWith(Array.Empty<DiagnosisString>(), new[] { g }).Observations, x => x.Rule == "R5");
        Assert.Contains("wind written down", o.Template);
    }

    [Fact]
    public void R5_ASkewedGroupWithoutWindIsReportedAndTheMissingWindAsked()
    {
        var g = new DiagnosisGroup("600 m tab#40.2 gr", 600, 3.0, 1.0, 6, null, null);
        var r = RunWith(Array.Empty<DiagnosisString>(), new[] { g });
        Assert.Single(r.Observations, x => x.Rule == "R5");
        Assert.Contains(r.Gaps, x => x.Rule == "R5" && x.Template.Contains("WIND="));
    }

    [Fact]
    public void R5_ANearGroupOrAShortOneIsNotRead()
    {
        var near = new DiagnosisGroup("100 m", 100, 3.0, 1.0, 6, null, null);
        var few = new DiagnosisGroup("600 m", 600, 3.0, 1.0, 3, null, null);
        var r = RunWith(Array.Empty<DiagnosisString>(), new[] { near, few });
        Assert.Empty(r.Observations);
        Assert.Contains(r.Gaps, x => x.Rule == "R5" && x.Template.Contains("no group"));
    }
}