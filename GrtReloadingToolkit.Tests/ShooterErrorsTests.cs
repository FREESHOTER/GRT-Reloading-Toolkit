using GrtReloadingToolkit.Shooting;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>The statistics under the reading of the shooter's errors (against scipy) and the checks that Analisi DS does not make.</summary>
public sealed class ShooterErrorsTests
{
    [Fact]
    public void TheAnglesCanBeReadInMilliradians()
    {
        var shots = PatternInput.Parse("# s\n10 8\n12 7\n9 9\n11 8\n10 10\n12 9\n9 8\n11 9\n10 7\n12 8").Shots;       // all well to the right and up of the aim
        var moa = ShooterErrors.Analyze(shots, new PatternOptions { DistanceM = 100, ScopeClick = "1/10 mrad" })!;
        var mr = ShooterErrors.Analyze(shots, new PatternOptions { DistanceM = 100, ScopeClick = "1/10 mrad", AngleUnit = "mrad" })!;
        Assert.Equal(("moa", "mrad"), (moa.AngleUnit, mr.AngleUnit));
        Assert.Equal(100.0, mr.AngleMmAt100);                                                   // a milliradian is 100 mm at 100 m
        string tmoa = ShooterText.Report(moa), tmr = ShooterText.Report(mr);
        Assert.Contains("MOA", tmoa);
        Assert.Contains("mrad", tmr);
        Assert.DoesNotContain("MOA", tmr);                                                      // the clicks are in milliradians too
        // the clicks asked for do not depend on the unit the report is in
        Assert.Equal(moa.Clicks.Select(c => (c.Name, c.Right, c.Up)), mr.Clicks.Select(c => (c.Name, c.Right, c.Up)));
        Assert.Equal("1/10 mrad", Assert.Single(mr.Clicks).Name);
    }

    [Fact]
    public void TheStatisticsAreTheOnesScipyGives()
    {
        var a = new[] { 3.1, 4.2, 5.9, 2.2, 6.4, 5.5, 4.4, 3.8 };
        var b = new[] { 7.5, 6.1, 8.2, 7.9, 5.8, 9.0, 6.6 };
        Assert.Equal(5.0938867475255724e-05, ShooterErrors.TTestOneSample(a), 12);
        Assert.Equal(0.0057426984694990465, ShooterErrors.TTestOneSample(new[] { -2.0, -1.5, -3.1, -0.2, -2.6, -1.9 }), 12);
        Assert.Equal(0.0010603856052619892, ShooterErrors.TTestIndependent(a, b), 12);

        var x = new double[] { 10, 12, 15, 22, 25, 31, 33, 40, 41, 55 };
        var y = new[] { 3.0, 4.1, 2.2, 6.0, 5.5, 7.9, 6.1, 9.0, 8.8, 7.2 };
        var (rho, p) = ShooterErrors.Spearman(x, y);
        Assert.Equal(0.8424242424242423, rho, 12); Assert.Equal(0.0022200312259168407, p, 12);
        var (rho2, p2) = ShooterErrors.Spearman(new double[] { 10, 12, 12, 22, 25, 25, 33, 40, 41, 55 }, y);                    // ties get their average rank
        Assert.Equal(0.8353813836471727, rho2, 12); Assert.Equal(0.002620705797353036, p2, 12);

        var g1 = new List<double> { 2.0, 3.1, 2.7, 1.9, 2.5, 3.3 };
        var g2 = new List<double> { 1.2, 7.5, 2.0, 9.9, 0.8, 6.0, 4.4 };
        var g3 = new List<double> { 3.0, 3.2, 2.8, 3.1, 2.9 };
        Assert.Equal(1.481213474573716e-06, ShooterErrors.Bartlett(new[] { g1, g2, g3 }), 14);
        Assert.Equal(0.026608610466545567, ShooterErrors.Bartlett(new[] { g1, g3 }), 12);
    }

    private static readonly (string S, int O, int N, double X, double U)[] FirstShotRows =
    {
        ("S1", 0, 1, -4.92, 3.89), ("S1", 1, 2, -1.14, -0.91), ("S1", 2, 3, -0.43, -4.04), ("S1", 3, 4, -0.46, -1.73), ("S1", 4, 5, 6.65, 0.45), ("S1", 5, 6, -0.71, -0.56),
        ("S2", 0, 1, 7.66, 6.89), ("S2", 1, 2, 0.96, -0.48), ("S2", 2, 3, 1.92, -0.4), ("S2", 3, 4, 0.05, 3.09), ("S2", 4, 5, 1.09, -1.01), ("S2", 5, 6, -0.37, 1.08),
        ("S3", 0, 1, -5.13, -9.54), ("S3", 1, 2, 2.0, -1.77), ("S3", 2, 3, -0.58, 1.77), ("S3", 3, 4, 1.16, 0.18), ("S3", 4, 5, 1.34, -5.66), ("S3", 5, 6, 2.04, -1.92),
    };

    private static PatternShot[] Shots(IEnumerable<(string S, int O, int N, double X, double U)> rows) => rows.Select(r => new PatternShot(r.S, r.O, r.N, r.X, r.U)).ToArray();

    [Fact]
    public void TheFirstShotOfEachSeriesIsComparedWithTheOthers()
    {
        var r = ShooterErrors.Analyze(Shots(FirstShotRows), new PatternOptions { AimKnown = false })!;
        var f = Assert.Single(r.Findings, x => x.Key == "first_shot");
        Assert.Equal(7.988237914355321e-05, f.P!.Value, 12);                                   // scipy ttest_ind on the distances from the group centres
        Assert.Contains("7.6 mm", f.Detail); Assert.Contains("2.7 mm", f.Detail);
        // the same shots with the first one taken out of the way are fine
        var calm = FirstShotRows.Select(s => s.O == 0 ? (s.S, s.O, s.N, X: s.X / 6, U: s.U / 6) : s);
        Assert.DoesNotContain(ShooterErrors.Analyze(Shots(calm), new PatternOptions { AimKnown = false })!.Findings, x => x.Key == "first_shot");
    }

    [Fact]
    public void TheFirstShotIsNotJudgedWithFewerThanThreeSeries()
    {
        var two = FirstShotRows.Where(s => s.S != "S3");
        Assert.DoesNotContain(ShooterErrors.Analyze(Shots(two), new PatternOptions { AimKnown = false })!.Findings, x => x.Key == "first_shot");
    }

    [Fact]
    public void FewerThanFiveShotsAreNotRead()
    {
        Assert.Null(ShooterErrors.Analyze(Shots(FirstShotRows.Take(4))));
        Assert.Null(ShooterErrors.Analyze(Array.Empty<PatternShot>()));
        Assert.NotNull(ShooterErrors.Analyze(Shots(FirstShotRows.Take(5))));
    }

    [Fact]
    public void WithoutTheAimPointTheZeroIsNeitherJudgedNorCalledRight()
    {
        var shifted = FirstShotRows.Select(s => (s.S, s.O, s.N, X: s.X + 20, s.U));            // a group 20 mm off wherever the origin is
        var unknown = ShooterErrors.Analyze(Shots(shifted), new PatternOptions { AimKnown = false })!;
        Assert.DoesNotContain(unknown.Findings, f => f.Key == "zero");
        Assert.DoesNotContain(unknown.Fine, t => t.Contains("zero of the sight"));
        Assert.Empty(unknown.Clicks);
        var known = ShooterErrors.Analyze(Shots(shifted), new PatternOptions { AimKnown = true, DistanceM = 100, ScopeClick = "1/4 MOA" })!;
        Assert.Contains(known.Findings, f => f.Key == "zero");
        var click = Assert.Single(known.Clicks);
        Assert.Equal(7.27, click.MmAtDistance, 2);                                             // a quarter MOA at 100 m
        Assert.Equal(-3, click.Right);                                                         // the centre is 20 mm to the right: three clicks the other way
        Assert.Equal("1/4 MOA", click.Name);
        // a centred group is called right
        var centred = ShooterErrors.Analyze(Shots(FirstShotRows), new PatternOptions { AimKnown = true })!;
        Assert.Contains(centred.Fine, t => t.Contains("zero of the sight is right"));
    }

    [Fact]
    public void TheDirectionOfTheLongestSpreadIsDescribed()
    {
        var diagonal = Enumerable.Range(0, 10).Select(i => new PatternShot("S", i, i + 1, i * 3.0 + (i % 2) * 0.2, i * 3.0 - (i % 2) * 0.2)).ToArray();
        var d = ShooterErrors.Analyze(diagonal, new PatternOptions { AimKnown = false })!;
        Assert.Equal(45, d.MajorAxisDegrees, 0);
        Assert.True(d.AxisRatio > 10);
        var horizontal = Enumerable.Range(0, 10).Select(i => new PatternShot("S", i, i + 1, i * 3.0, (i % 2) * 0.3)).ToArray();
        Assert.Equal(0, ShooterErrors.Analyze(horizontal, new PatternOptions { AimKnown = false })!.MajorAxisDegrees, 0);
        var vertical = Enumerable.Range(0, 10).Select(i => new PatternShot("S", i, i + 1, (i % 2) * 0.3, i * 3.0)).ToArray();
        Assert.Equal(90, Math.Abs(ShooterErrors.Analyze(vertical, new PatternOptions { AimKnown = false })!.MajorAxisDegrees), 0);
    }

    [Fact]
    public void TheAdviceFollowsTheWayTheRifleRests()
    {
        // a group much wider than tall, with the shots sideways of a different size on every series
        var wide = Enumerable.Range(0, 24).Select(i => new PatternShot("S" + (i / 8 + 1), i % 8, i % 8 + 1, ((i * 37) % 11 - 5) * 3.0, ((i * 13) % 7 - 3) * 0.4)).ToArray();
        string Horizontal(ShootingMode m) => ShooterErrors.Analyze(wide, new PatternOptions { AimKnown = false, Mode = m })!.Findings.Single(f => f.Key == "horizontal").Advice;
        Assert.Contains("front rest only", Horizontal(ShootingMode.Mono));
        Assert.Contains("rested in front and behind", Horizontal(ShootingMode.DoubleRest));
        Assert.Contains("bipod", Horizontal(ShootingMode.Bipod));
        Assert.Contains("Say how the rifle rests", Horizontal(ShootingMode.Unknown));
    }

    [Fact]
    public void TheHeightThatTheVelocityExplainsIsNotHeldAgainstTheShooter()
    {
        // equal spread sideways and in height, but the height follows the velocity: the shooter is not the cause of it
        var rnd = new Random(2);
        var shots = new List<PatternShot>();
        for (int s = 0; s < 3; s++)
            for (int k = 0; k < 10; k++)
            {
                double v = 830 + (rnd.NextDouble() - 0.5) * 20;
                shots.Add(new PatternShot("S" + s, k, k + 1, (rnd.NextDouble() - 0.5) * 8, (v - 830) * 0.8 + (rnd.NextDouble() - 0.5) * 2, v));
            }
        var withV = ShooterErrors.Analyze(shots, new PatternOptions { AimKnown = false })!;
        Assert.NotNull(withV.Velocity);
        Assert.True(withV.Velocity!.R2 > 0.9);
        Assert.DoesNotContain(withV.Findings, f => f.Key == "vertical");                       // the height is the cartridges' doing
        var withoutV = ShooterErrors.Analyze(shots.Select(s => s with { VelocityMps = null }).ToArray(), new PatternOptions { AimKnown = false })!;
        Assert.Contains(withoutV.Findings, f => f.Key == "vertical");                          // without the velocities it would be put down to the shooter
    }
}
