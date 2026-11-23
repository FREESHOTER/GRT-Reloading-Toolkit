using GrtReloadingToolkit.Archive;
using GrtReloadingToolkit.Ocw;
using GrtReloadingToolkit.Shooting;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The reading of a match (one shot for each target): shots made in the test with the cause put in by hand, so that what the reading finds can be
/// checked against what was put in; the numbers of the regression and the analysis of variance against scipy on the same data.
/// </summary>
public sealed class MatchReadingTests
{
    private static readonly double[] Ds = { 300, 400, 500, 600, 700, 800 };

    private static double Gauss(Random r) => Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());

    /// <summary>A match of n shots, distances going round 300…800 m. The function gives, for a shot, its error in MOA (right, up) from what was put in.</summary>
    private static List<MatchShot> Match(int n, int seed, double noise, Func<int, double, double, (double X, double U, double? V, double? W, string? Pos, string? Lot)> make)
    {
        var r = new Random(seed);
        var list = new List<MatchShot>();
        for (int i = 0; i < n; i++)
        {
            double d = Ds[i % Ds.Length], mm = ShooterErrors.MoaMmAt100 * d / 100;
            var m = make(i, d, (d - 550) / 100);
            list.Add(new MatchShot(i + 1, "T" + (i + 1), d, (m.X + noise * Gauss(r)) * mm, (m.U + noise * Gauss(r)) * mm, m.V, 36000 + 70 * i, m.W, m.Pos, m.Lot));
        }
        return list;
    }

    private static MatchFinding? Find(MatchReport r, string key) => r.Findings.FirstOrDefault(f => f.Key == key);

    [Fact]
    public void TooFewShotsOrBadDistancesGiveNothing()
    {
        Assert.Null(MatchReading.Analyze(Match(5, 1, 0.1, (_, _, _) => (0, 0, null, null, null, null))));
        var ok = Match(8, 1, 0.1, (_, _, _) => (0, 0, null, null, null, null));
        var bad = ok.Take(5).Concat(new[] { ok[5] with { DistanceM = 0 }, ok[6] with { XMm = double.NaN } }).ToList();
        Assert.Null(MatchReading.Analyze(bad));                  // only 5 usable shots left
    }

    [Fact]
    public void AZeroOffIsFoundWithItsClicks()
    {
        var r = MatchReading.Analyze(Match(18, 3, 0.05, (_, _, _) => (-0.5, 0.75, null, null, null, null)), new MatchOptions { ScopeClick = "1/4 MOA" })!;
        var f = Find(r, "zero")!;
        Assert.Equal(MatchOrigin.Settings, f.Origin);
        Assert.False(f.Weak);
        var c = Assert.Single(r.Clicks);
        Assert.Equal(("1/4 MOA", 2, -3), (c.Name, c.Right, c.Up));       // 0.5 MOA to the left: two clicks right; 0.75 MOA high: three down
        Assert.Matches(@"0\.(4[89]|5[0-2]) MOA to the left", f.Detail);   // 0.50 was put in
        Assert.Matches(@"0\.(7[3-7]) MOA above", f.Detail);                // 0.75 was put in
        Assert.Null(Find(r, "elevation"));
        Assert.Null(Find(r, "lateral_distance"));
        Assert.All(r.Shots, s => Assert.Equal("settings", s.Cause));      // every shot carries the zero
        string text = MatchText.Report(r);
        Assert.Contains("• All the shots: the settings.", text);          // said once, not eighteen times
        Assert.DoesNotContain("• Shot 1 (", text);
    }

    [Fact]
    public void AZeroInMradUsesTheClickOfTheScope()
    {
        var r = MatchReading.Analyze(Match(18, 3, 0.05, (_, _, _) => (0, 0.69, null, null, null, null)), new MatchOptions { ScopeClick = "0.1 mrad" })!;
        var c = Assert.Single(r.Clicks);
        Assert.Equal(("1/10 mrad", 0, -2), (c.Name, c.Right, c.Up));      // 1/10 mrad = 0.3438 MOA; 0.69 MOA is two clicks (the old name "0.1 mrad" is still read)
    }

    [Fact]
    public void TheAnglesCanBeReadInMilliradiansWithTheirOwnClicks()
    {
        var shots = Match(18, 3, 0.05, (_, _, _) => (-0.5, 0.75, null, null, null, null));          // 0.5 MOA left and 0.75 MOA high, as in the test of the zero
        var r = MatchReading.Analyze(shots, new MatchOptions { ScopeClick = "1/10 mrad", AngleUnit = "mrad" })!;
        Assert.Equal("mrad", r.AngleUnit);
        var f = Find(r, "zero")!;
        Assert.Matches(@"0\.1[45] mrad to the left", f.Detail.Replace("MOA", "mrad"));            // 0.5 MOA = 0.145 mrad
        var c = Assert.Single(r.Clicks);
        Assert.Equal("1/10 mrad", c.Name);
        Assert.Equal(0.1, c.Moa, 9);                                                              // the click, in the unit of the report
        Assert.Equal((1, -2), (c.Right, c.Up));                                                   // 0.145 mrad: one click right; 0.218 mrad: two down
        string text = MatchText.Report(r);
        Assert.DoesNotContain("MOA", text);
        Assert.Contains("mrad", text);
        Assert.Equal(0.5 * 0.1, r.NoticeMoa, 9);                                                  // half a click is the threshold of what is worth saying
    }

    [Fact]
    public void AClickInMilliradiansOnAReportInMoaKeepsItsNameAndItsValue()
    {
        var shots = Match(18, 3, 0.05, (_, _, _) => (0, 0.75, null, null, null, null));
        var r = MatchReading.Analyze(shots, new MatchOptions { ScopeClick = "1/10 mrad" })!;       // the angle unit stays MOA
        var c = Assert.Single(r.Clicks);
        Assert.Equal(("1/10 mrad", 0, -2), (c.Name, c.Right, c.Up));
        Assert.Equal(0.1 * 3.437746771, c.Moa, 6);
        Assert.Contains("1/10 mrad", MatchText.Report(r));
        // the same shots read in the unit of the click give the same number of clicks
        var rm = MatchReading.Analyze(shots, new MatchOptions { ScopeClick = "1/10 mrad", AngleUnit = "mrad" })!;
        Assert.Equal(c.Up, rm.Clicks[0].Up);
    }

    [Fact]
    public void AnElevationThatDoesNotFollowTheDistanceIsNotTakenForAZero()
    {
        var r = MatchReading.Analyze(Match(18, 5, 0.05, (_, _, dh) => (0, 0.25 * dh, null, null, null, null)))!;
        var f = Find(r, "elevation")!;
        Assert.Equal(MatchOrigin.Settings, f.Origin);
        Assert.False(f.Weak);
        Assert.Contains("higher", f.Detail);
        Assert.Contains("MOA more for every 100 m", f.Detail);
        Assert.Null(Find(r, "zero"));
        Assert.Equal("settings", r.Shots.First(s => s.DistanceM == 800).Cause);
        Assert.Equal("normal", r.Shots.First(s => s.DistanceM == 500).Cause);     // near the middle distance the settings are right
    }

    [Fact]
    public void ASidewaysErrorThatGrowsWithTheDistanceIsFoundApartFromTheWind()
    {
        var r = MatchReading.Analyze(Match(18, 6, 0.05, (_, _, dh) => (-0.2 * dh, 0, null, null, null, null)))!;
        var f = Find(r, "lateral_distance")!;
        Assert.Contains("to the left", f.Detail.Replace("more the shots go to the left", "to the left"));
        Assert.Equal(MatchOrigin.Settings, f.Origin);
    }

    [Fact]
    public void TheVelocityOfTheCartridgesIsFoundAndAnOddCartridgeIsNamed()
    {
        var rnd = new Random(11);
        var vel = Enumerable.Range(0, 18).Select(_ => 830 + 8 * Gauss(rnd)).ToArray();
        vel[4] = 790;                                                      // one cartridge far out of line
        double vMean = vel.Average();
        var shots = Match(18, 7, 0.05, (i, d, _) =>
        {
            double mm = ShooterErrors.MoaMmAt100 * d / 100;
            return (0, 1.3 * (vel[i] - vMean) * ShooterErrors.MmPerMps(d, vMean) / mm, vel[i], null, null, null);
        });
        var r = MatchReading.Analyze(shots)!;
        var f = Find(r, "velocity")!;
        Assert.Equal(MatchOrigin.Cartridge, f.Origin);
        Assert.False(f.Weak);
        var times = double.Parse(System.Text.RegularExpressions.Regex.Match(f.Detail, @"([\d.]+) times what the drop").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(times, 1.1, 1.5);                                   // 1.3 was put in
        var odd = Find(r, "velocity_outliers")!;
        Assert.Contains("Shot 5 ", odd.Detail);
        var s5 = r.Shots[4];
        Assert.Equal("cartridge", s5.Cause);
        Assert.True(s5.CartridgeMoa > 0.5);
        Assert.Contains("the cartridge", s5.Text);
    }

    [Theory]
    [InlineData(0.1, "The wind is not corrected enough", "follow")]
    [InlineData(-0.1, "The wind is over-corrected", "go against")]
    public void TheWindThatFollowsOrOpposesTheShotsIsFound(double k, string title, string verb)
    {
        var rnd = new Random(2);
        var wind = Enumerable.Range(0, 18).Select(_ => Math.Round((rnd.NextDouble() - 0.5) * 10, 1)).ToArray();
        var r = MatchReading.Analyze(Match(18, 8, 0.05, (i, d, _) => (k * wind[i] * d / 100, 0, null, wind[i], null, null)))!;
        var f = Find(r, "wind")!;
        Assert.Equal(MatchOrigin.Conditions, f.Origin);
        Assert.Equal(title, f.Title);
        Assert.Contains(verb, f.Detail);
        int worst = Enumerable.Range(0, 18).OrderByDescending(i => Math.Abs(wind[i] * Ds[i % 6])).First();
        Assert.Equal("wind", r.Shots[worst].Cause);
        Assert.Contains("the wind", r.Shots[worst].Text);
    }

    [Fact]
    public void AWindThatIsCorrectedWellIsSaidToBeFine()
    {
        var rnd = new Random(3);
        var wind = Enumerable.Range(0, 18).Select(_ => Math.Round((rnd.NextDouble() - 0.5) * 10, 1)).ToArray();
        var r = MatchReading.Analyze(Match(18, 9, 0.05, (i, _, _) => (0, 0, null, wind[i], null, null)))!;
        Assert.Null(Find(r, "wind"));
        Assert.Contains(r.Fine, t => t.StartsWith("The wind is corrected well"));
    }

    [Fact]
    public void OneBadShotIsNamedAndDoesNotHideTheCartridgeEffect()
    {
        var rnd = new Random(5);
        var vel = Enumerable.Range(0, 18).Select(_ => 830 + 4 * Gauss(rnd)).ToArray();
        double vMean = vel.Average();
        var shots = Match(18, 12, 0.15, (i, d, _) =>
        {
            double mm = ShooterErrors.MoaMmAt100 * d / 100;
            return (0, 1.3 * (vel[i] - vMean) * ShooterErrors.MmPerMps(d, vMean) / mm + (i == 11 ? 1.5 : 0), vel[i], null, null, null);
        });
        var r = MatchReading.Analyze(shots)!;
        Assert.NotNull(Find(r, "velocity"));                               // the bad shot did not hide it
        var fl = Find(r, "flyers")!;
        Assert.Equal(MatchOrigin.Shooter, fl.Origin);
        Assert.Contains("Shot 12 ", fl.Detail);
        Assert.Equal("execution", r.Shots[11].Cause);
        Assert.Contains("the shooting", r.Shots[11].Text);
    }

    [Fact]
    public void SlowerCartridgesOnTheFarTargetsAreNotTakenForAnElevationError()
    {
        // The cartridges lose velocity from the near targets to the far ones, and the drop follows it (what the physics says); the scatter is too big for
        // the velocity to be proved by itself, but the elevation is right: the part of the drop that the velocity explains must not be blamed on the settings.
        var rnd = new Random(4);
        var vel = Enumerable.Range(0, 24).Select(i => 842 - 4.5 * (i % 6) + 1.0 * Gauss(rnd)).ToArray();
        double vMean = vel.Average();
        var shots = Match(24, 16, 0.3, (i, d, _) =>
        {
            double mm = ShooterErrors.MoaMmAt100 * d / 100;
            return (0, (vel[i] - vMean) * ShooterErrors.MmPerMps(d, vMean) / mm, vel[i], null, null, null);
        });
        var r = MatchReading.Analyze(shots)!;
        Assert.Null(Find(r, "elevation"));
        Assert.Null(Find(r, "zero"));
    }

    [Fact]
    public void LotsThatShootToDifferentPointsAreFound()
    {
        var shots = Match(20, 13, 0.08, (i, _, _) => (0, i % 2 == 0 ? 0.3 : -0.3, null, null, null, i % 2 == 0 ? "A" : "B"));
        var r = MatchReading.Analyze(shots)!;
        var f = Find(r, "lots")!;
        Assert.Equal(MatchOrigin.Cartridge, f.Origin);
        Assert.Matches(@"A: 0\.(2[5-9]|3[0-5]) MOA high", f.Detail);       // 0.30 was put in
        Assert.Matches(@"B: 0\.(2[5-9]|3[0-5]) MOA low", f.Detail);
    }

    [Fact]
    public void PositionsThatChangeThePointOfImpactAreFoundAndEqualOnesAreFine()
    {
        var shots = Match(20, 14, 0.08, (i, _, _) => (i < 10 ? 0.4 : -0.4, 0, null, null, i < 10 ? "prone" : "kneeling", null));
        var f = Find(MatchReading.Analyze(shots)!, "positions")!;
        Assert.Equal(MatchOrigin.Shooter, f.Origin);
        var same = Match(20, 14, 0.08, (i, _, _) => (0, 0, null, null, i < 10 ? "prone" : "kneeling", null));
        Assert.Contains(MatchReading.Analyze(same)!.Fine, t => t.StartsWith("The positions shoot to the same point"));
    }

    [Fact]
    public void ADifferenceSmallerThanHalfAClickIsNotAProblem()
    {
        // two lots 0.1 MOA apart are real but cannot be corrected on a 1/4 MOA scope
        var shots = Match(40, 15, 0.02, (i, _, _) => (0, i % 2 == 0 ? 0.05 : -0.05, null, null, null, i % 2 == 0 ? "A" : "B"));
        Assert.Null(Find(MatchReading.Analyze(shots)!, "lots"));
    }

    [Fact]
    public void ACleanMatchHasNothingToSay()
    {
        var r = MatchReading.Analyze(Match(16, 21, 0.08, (_, _, _) => (0, 0, null, null, null, null)))!;
        Assert.Empty(r.Findings);
        Assert.All(r.Shots, s => Assert.Equal("normal", s.Cause));
        Assert.Contains(r.Fine, t => t.StartsWith("The zero of the sight is right"));
    }

    [Fact]
    public void CleanMatchesRarelyAccuseAnything()
    {
        // 200 invented matches in which nothing is wrong: how often does the reading still find a solid problem?
        int accused = 0, total = 200;
        for (int seed = 0; seed < total; seed++)
        {
            var shots = Match(15, 1000 + seed, 0.2, (_, _, _) => (0, 0, null, null, seed % 2 == 0 ? "prone" : "kneeling", seed % 3 == 0 ? "A" : "B"));
            var r = MatchReading.Analyze(shots)!;
            if (r.Findings.Any(f => !f.Weak)) accused++;
        }
        Assert.InRange(accused, 0, (int)(0.12 * total));                 // measured about 6 %: each of the tests runs at 1 % (5 % for the single shots)
    }

    // ---- the numbers against scipy ----------------------------------------------------------------------------------------

    private static readonly double[] RefD = { 300, 400, 500, 600, 700, 800, 300, 400, 500, 600, 700, 800 };
    private static readonly double[] RefX = { 0.02, -0.05, 0.04, 0.0, -0.03, 0.05, -0.04, 0.03, -0.02, 0.01, 0.04, -0.03 };

    private static List<MatchShot> Fixed(double[] u) => RefD.Select((d, i) =>
    {
        double mm = ShooterErrors.MoaMmAt100 * d / 100;
        return new MatchShot(i + 1, "T" + (i + 1), d, RefX[i] * mm, u[i] * mm);
    }).ToList();

    [Fact]
    public void TheElevationSlopeAndItsProbabilityAreTheLinearRegressionOfScipy()
    {
        // scipy.stats.linregress on (distance − mean) / 100 and the heights: slope 0.0998571, p 5.78e-09, intercept p 6.3e-12
        var strong = Fixed(new[] { 0.10, 0.18, 0.31, 0.36, 0.52, 0.55, 0.05, 0.22, 0.28, 0.45, 0.47, 0.60 });
        var f = Find(MatchReading.Analyze(strong)!, "elevation")!;
        Assert.Contains("0.10 MOA more for every 100 m (0.50 MOA between 300 m and 800 m; the probability that it is chance is 0.00 %)", f.Detail);
        Assert.False(f.Weak);

        // the same with more scatter: slope 0.1007143, p = 3.3752 %: a hint to be confirmed, not a finding that stands
        var weak = Fixed(new[] { 0.30, 0.05, 0.55, 0.20, 0.85, 0.40, 0.10, 0.45, 0.15, 0.70, 0.35, 0.95 });
        var w = Find(MatchReading.Analyze(weak)!, "elevation")!;
        Assert.Contains("0.10 MOA more for every 100 m (0.50 MOA between 300 m and 800 m; the probability that it is chance is 3.38 %)", w.Detail);
        Assert.True(w.Weak);
        Assert.Equal(0.033752, w.P!.Value, 5);
    }

    [Fact]
    public void TheAnalysisOfVarianceIsTheOneOfScipy()
    {
        var a = new List<double> { 0.10, 0.22, 0.31, 0.18, 0.27 };
        var b = new List<double> { 0.52, 0.47, 0.61, 0.55, 0.43, 0.58 };
        var c = new List<double> { 0.33, 0.29, 0.41, 0.36 };
        Assert.Equal(6.901872421845635e-05, MatchReading.Anova(new[] { a, b }), 9);
        Assert.Equal(2.973256044467111e-05, MatchReading.Anova(new[] { a, b, c }), 9);
        Assert.Equal(1.0, MatchReading.Anova(new[] { a }));                 // one group: nothing to compare
    }

    // ---- the text ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void TheReportGroupsTheFindingsByOriginAndListsEveryShot()
    {
        var rnd = new Random(2);
        var wind = Enumerable.Range(0, 18).Select(_ => Math.Round((rnd.NextDouble() - 0.5) * 10, 1)).ToArray();
        var shots = Match(18, 30, 0.05, (i, d, dh) => (0.1 * wind[i] * d / 100, 0.4 + 0.2 * dh, null, wind[i], null, null));
        string text = MatchText.Report(MatchReading.Analyze(shots, new MatchOptions { Mode = ShootingMode.Mono, ScopeClick = "1/4 MOA" })!);
        Assert.Contains("18 shots on 18 targets, from 300 m to 800 m.", text);
        Assert.Contains("== The settings of the rifle and the scope ==", text);
        Assert.Contains("== The conditions (wind) ==", text);
        Assert.Contains("The zero of the sight is not right", text);
        Assert.Contains("Shot by shot", text);
        Assert.Contains("Main cause", text);
        Assert.Equal(18, text.Split('\n').Count(l => l.TrimStart().StartsWith("T") == false && System.Text.RegularExpressions.Regex.IsMatch(l.TrimStart(), @"^\d+\s+T\d+\s")));
        Assert.DoesNotContain("{0}", text);
    }

    [Fact]
    public void ShotsWithTheirOwnReasonKeepTheirOwnLine()
    {
        var rnd = new Random(2);
        var wind = Enumerable.Range(0, 18).Select(_ => Math.Round((rnd.NextDouble() - 0.5) * 10, 1)).ToArray();
        var r = MatchReading.Analyze(Match(18, 8, 0.05, (i, d, _) => (0.1 * wind[i] * d / 100, 0, null, wind[i], null, null)))!;
        string text = MatchText.Report(r);
        int worst = Enumerable.Range(0, 18).OrderByDescending(i => Math.Abs(wind[i] * Ds[i % 6])).First();
        Assert.Contains($"• Shot {worst + 1} (T{worst + 1}, ", text);       // each shot has its own wind: nothing to say once
    }

    [Fact]
    public void AHintIsMarkedToBeConfirmed()
    {
        var weak = Fixed(new[] { 0.30, 0.05, 0.55, 0.20, 0.85, 0.40, 0.10, 0.45, 0.15, 0.70, 0.35, 0.95 });
        string text = MatchText.Report(MatchReading.Analyze(weak)!);
        Assert.Contains("The elevation does not follow the distance (to be confirmed)", text);
        Assert.Contains("(to be confirmed) = the evidence is weak", text);
    }
}

public sealed class MatchInputTests
{
    [Fact]
    public void AllThePartsOfALineAreRead()
    {
        var p = MatchInput.Parse("# gara di prova\n10,5 -4.2 300 v=831,5 w=+3 pos=prone lot=A name=T4 10:14:02\n0 0 600 ora=10:15:30 PM vel=829\n\n3 4\n", defaultDistanceM: 150);
        Assert.Empty(p.Problems);
        Assert.Equal(3, p.Shots.Count);
        var a = p.Shots[0];
        Assert.Equal((1, "T4", 300.0, 10.5, -4.2, 831.5, 3.0, "prone", "A"), (a.Number, a.Target, a.DistanceM, a.XMm, a.UpMm, a.VelocityMps, a.WindMps, a.Position, a.Lot));
        Assert.Equal(10 * 3600 + 14 * 60 + 2.0, a.ClockSeconds);
        Assert.Equal((2, "T2", 600.0, 829.0, 22 * 3600 + 15 * 60 + 30.0), (p.Shots[1].Number, p.Shots[1].Target, p.Shots[1].DistanceM, p.Shots[1].VelocityMps!.Value, p.Shots[1].ClockSeconds!.Value));
        Assert.Equal((3, 150.0, null), (p.Shots[2].Number, p.Shots[2].DistanceM, p.Shots[2].VelocityMps));   // the distance of the window
    }

    [Fact]
    public void CoordinatesInInchesAndDistancesInYardsAreKeptInMillimetresAndMetres()
    {
        var p = MatchInput.Parse("1 -2 500\n3 4", defaultDistanceM: 150, mmPerUnit: 25.4, metresPerUnit: 0.9144);
        Assert.Equal((25.4, -50.8, 457.2), (p.Shots[0].XMm, p.Shots[0].UpMm, Math.Round(p.Shots[0].DistanceM, 6)));     // 500 yd are 457.2 m
        Assert.Equal((76.2, 101.6, 150.0), (Math.Round(p.Shots[1].XMm, 6), Math.Round(p.Shots[1].UpMm, 6), p.Shots[1].DistanceM));   // the distance of the window is already in metres
    }

    [Fact]
    public void ALineThatCannotBeReadIsReportedAndTheOthersStay()
    {
        var p = MatchInput.Parse("1 2 300\nsolo testo\n3 4 300 x=5\n5 6 300 v=abc\n7 8 300 w=\n9 10 300 25:00:00\n11 12 400");
        Assert.Equal(2, p.Shots.Count);
        Assert.Equal(new[] { 2, 3, 4, 5, 6 }, p.Problems.Select(t => int.Parse(t.Split(':')[0].Replace("Line ", ""))));
    }

    [Fact]
    public void NothingTypedGivesNothing()
    {
        Assert.Empty(MatchInput.Parse(null).Shots);
        Assert.Empty(MatchInput.Parse("# solo un commento\n\n").Shots);
    }
}

public sealed class MatchFromArchiveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gara_test_" + Guid.NewGuid().ToString("N"));
    private readonly ArchiveDb _a;
    private readonly long _session;

    public MatchFromArchiveTests()
    {
        Directory.CreateDirectory(_dir);
        _a = new ArchiveDb(Path.Combine(_dir, "archivio.db"));
        long sh = ArchiveImports.ShooterFor(_a, null);
        long load = ArchiveImports.NewLoad(_a, sh, "Scala", "6.5 Creedmoor");
        var strings = new[]
        {
            new ChronoString("41.5 gr", 41.5, 18, "", new[] { new ChronoShot(820, "10:14:02 AM", null), new ChronoShot(822, "10:15:10 AM", null) },
                Path.Combine(_dir, "a.xlsx"), new byte[] { 1 }),
        };
        _session = ArchiveImports.AddChronoSession(_a, sh, load, new DateTime(2026, 11, 15), "", strings, "prova").SessionId;
        var ids = new List<long>();
        using (var cmd = _a.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM shots ORDER BY id";
            using var r = cmd.ExecuteReader();
            while (r.Read()) ids.Add(r.GetInt64(0));
        }
        long g1 = _a.Insert("shot_groups", ("session_id", _session), ("label", "T1"), ("tab", "Gara"), ("distance_m", 300.0), ("origin_point", "punto di mira"));
        _a.Insert("hits", ("group_id", g1), ("idx", 1), ("x_mm", 10.0), ("y_mm", 20.0), ("shot_id", ids[0]));
        long g2 = _a.Insert("shot_groups", ("session_id", _session), ("label", "T2"), ("tab", "Gara"), ("distance_m", 600.0), ("origin_point", "punto di mira"));
        _a.Insert("hits", ("group_id", g2), ("idx", 1), ("x_mm", -5.0), ("y_mm", 8.0), ("shot_id", ids[1]));
        long g3 = _a.Insert("shot_groups", ("session_id", _session), ("label", "T3"), ("tab", "Gara"), ("origin_point", "punto di mira"));   // no distance
        _a.Insert("hits", ("group_id", g3), ("idx", 1), ("x_mm", 1.0), ("y_mm", 2.0));
        _a.Insert("hits", ("group_id", g3), ("idx", 2), ("frac_x", 0.5), ("frac_y", 0.5));                                                   // no scale
    }

    public void Dispose()
    {
        _a.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void EachGroupIsATargetNumberedInOrderWithItsDistanceAndTheLinkedVelocity()
    {
        var gs = ArchiveQueries.Groups(_a, _session);
        var m = PatternQueries.MatchFromGroups(_a, gs, 150);
        Assert.Equal(3, m.Shots.Count);
        Assert.Equal(new[] { 1, 2, 3 }, m.Shots.Select(s => s.Number));
        Assert.Equal(new[] { 300.0, 600.0, 150.0 }, m.Shots.Select(s => s.DistanceM));           // the third has none: the one of the window
        Assert.Equal(new double?[] { 820, 822, null }, m.Shots.Select(s => s.VelocityMps));
        Assert.Equal(10 * 3600 + 14 * 60 + 2.0, m.Shots[0].ClockSeconds);
        Assert.Equal(new[] { "Gara · T1", "Gara · T2", "Gara · T3" }, m.Shots.Select(s => s.Target));
        Assert.True(m.AimKnown);
        Assert.Contains(m.Notes, n => n.Contains("no distance, 150 m is used"));
        Assert.Contains(m.Notes, n => n.Contains("1 hits without a scale"));
    }

    [Fact]
    public void WithoutAPointOfAimTheReadingIsSaidNotToBeReliable()
    {
        _a.Insert("shot_groups", ("session_id", _session), ("label", "T4"), ("tab", "Gara"), ("distance_m", 500.0), ("origin_point", "centro dei colpi"));
        var m = PatternQueries.MatchFromGroups(_a, ArchiveQueries.Groups(_a, _session), 100);
        Assert.False(m.AimKnown);
        Assert.Contains(m.Notes, n => n.Contains("no point of aim marked"));
        Assert.False(PatternQueries.MatchFromGroups(_a, new List<ArchiveGroup>(), 100).AimKnown);
    }
}
