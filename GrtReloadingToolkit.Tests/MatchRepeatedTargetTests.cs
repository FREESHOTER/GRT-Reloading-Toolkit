using GrtReloadingToolkit.Shooting;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>The same target visited again and again and missed the same way (the barilotti of a card, shot in the same order in every series).</summary>
public sealed class MatchRepeatedTargetTests
{
    /// <summary>
    /// An invented card of 30 shots at 100 m: barilotto, x and y in mm from the centre of the barilotto, velocity, time of day. Three series (the match and two
    /// returns) in the same order of barilotti: 10, 7, 8, 9, 4, 5, 6, 1, 2, 3. Everything is ordinary scatter round a small common offset, except B6, which is
    /// always off to the right and high: the shape of a card on which one barilotto is missed the same way in every series.
    /// </summary>
    private static readonly (int Bull, double X, double Y, double V, int Sec)[] Card =
    {
        (10, 12.18, -0.48, 832.9, 36000), (7, -4.34, 4.95, 828.3, 36040), (8, -1.66, -3.59, 824.6, 36080), (9, 8.74, 7.21, 823.9, 36120), (4, 4.78, 7.43, 829.2, 36160),
        (5, -4.38, 14.22, 833.6, 36200), (6, 17.59, 12.86, 835.7, 36240), (1, 2.23, 4.63, 822.8, 36280), (2, 3, -5.1, 829.6, 36320), (3, 19.49, 7.94, 836.5, 36360),
        (10, -4.41, 9.31, 831.7, 36600), (7, 0.94, 8.38, 828.5, 36640), (8, -2.1, 0.92, 837.9, 36680), (9, 3.8, 8.06, 838.9, 36720), (4, 6.31, -6.18, 834.5, 36760),
        (5, 4.72, 11.59, 839.3, 36800), (6, 14.1, 11.17, 825, 36840), (1, 2.76, -0.97, 834.3, 36880), (2, 2.13, 0.26, 822.8, 36920), (3, 5.83, 13.57, 841.2, 36960),
        (10, 10.27, 1.77, 836.3, 37200), (7, -0.57, 5.03, 834.8, 37240), (8, 5.9, 7.76, 842.7, 37280), (9, 7.52, 6.87, 826.1, 37320), (4, 3.65, 5.3, 830.4, 37360),
        (5, 4.82, 3.15, 825, 37400), (6, 10.79, 8.03, 831.5, 37440), (1, 4.72, -5.48, 829.9, 37480), (2, -2.06, 19.05, 832.1, 37520), (3, 2.47, -0.07, 847.9, 37560),
    };

    private static List<MatchShot> InventedCard() => Card.Select((t, i) => new MatchShot(i + 1, "B" + t.Bull, 100, t.X, t.Y, t.V, t.Sec)).ToList();

    private static double Gauss(Random r) => Math.Sqrt(-2 * Math.Log(1 - r.NextDouble())) * Math.Cos(2 * Math.PI * r.NextDouble());

    [Fact]
    public void AnInventedCardShowsBarilotto6MissedTheSameWayInEverySeries()
    {
        // B6 is the worst by far and the probability that the worst of ten barilotti is this far off by chance is a few per cent: a finding, but a weak one
        var r = MatchReading.Analyze(InventedCard(), new MatchOptions { Mode = ShootingMode.Mono, ScopeClick = "1/4 MOA" })!;
        var f = r.Findings.Single(x => x.Key == "target_repeat");
        Assert.Equal(MatchOrigin.Shooter, f.Origin);
        Assert.True(f.Weak);                                              // between 1 % and 5 %: to be confirmed in the next match
        Assert.InRange(f.P!.Value, 0.01, 0.04);
        Assert.Contains("B6: 3 visits (shots 7, 17, 27)", f.Detail);
        Assert.DoesNotContain("B9:", f.Detail);
        Assert.Contains("on average", f.Detail);
        Assert.Contains("to the right", f.Detail);
        Assert.Contains("high", f.Detail);
        Assert.Equal(new[] { 7, 17, 27 }, r.Shots.Where(s => s.Cause == "target").Select(s => s.Number));
        Assert.All(r.Shots.Where(s => s.Cause == "target"), s => Assert.Contains("always the same target", s.Text));
        Assert.All(r.Shots.Where(s => s.Cause == "target"), s => Assert.Contains("(to be confirmed)", s.Text));
        string text = MatchText.Report(r);
        Assert.Contains("Some targets are always missed the same way (to be confirmed)", text);
        Assert.Contains("same target", text);
    }

    [Fact]
    public void ATargetThatIsClearlyMissedEveryTimeIsAFindingThatStands()
    {
        // eight targets visited three times; target "B5" is always 0.6 MOA to the right and 0.4 MOA high, the rest is scatter
        var rnd = new Random(31);
        var shots = new List<MatchShot>();
        for (int series = 0; series < 3; series++)
            for (int b = 1; b <= 8; b++)
            {
                double x = 0.12 * Gauss(rnd) + (b == 5 ? 0.6 : 0), u = 0.12 * Gauss(rnd) + (b == 5 ? 0.4 : 0), mm = ShooterErrors.MoaMmAt100;
                shots.Add(new MatchShot(shots.Count + 1, "B" + b, 100, x * mm, u * mm, null, 36000 + 60 * shots.Count));
            }
        var r = MatchReading.Analyze(shots)!;
        var f = r.Findings.Single(x => x.Key == "target_repeat");
        Assert.False(f.Weak);
        Assert.Contains("B5: 3 visits (shots 5, 13, 21)", f.Detail);
        Assert.DoesNotContain("B4:", f.Detail);
        Assert.Equal(new[] { 5, 13, 21 }, r.Shots.Where(s => s.Cause == "target").Select(s => s.Number));
        Assert.All(r.Shots.Where(s => s.Cause == "target"), s => Assert.DoesNotContain("(to be confirmed)", s.Text));
    }

    [Fact]
    public void AShiftSmallerThanHalfAClickIsNotAProblemEvenWhenItIsReal()
    {
        // target B5 is always 0.06 MOA off with almost no scatter: real, but nobody can correct it on a 1/4 MOA scope
        var rnd = new Random(31);
        var shots = new List<MatchShot>();
        for (int series = 0; series < 3; series++)
            for (int b = 1; b <= 8; b++)
            {
                double x = 0.01 * Gauss(rnd) + (b == 5 ? 0.06 : 0), u = 0.01 * Gauss(rnd), mm = ShooterErrors.MoaMmAt100;
                shots.Add(new MatchShot(shots.Count + 1, "B" + b, 100, x * mm, u * mm));
            }
        Assert.DoesNotContain(MatchReading.Analyze(shots)!.Findings, f => f.Key == "target_repeat");
    }

    [Fact]
    public void ATargetShotOnlyOnceIsLeftOutOfTheComparison()
    {
        var rnd = new Random(8);
        var shots = new List<MatchShot>();
        for (int series = 0; series < 3; series++)
            for (int b = 1; b <= 8; b++) shots.Add(new MatchShot(shots.Count + 1, "B" + b, 100, 2 * Gauss(rnd), 2 * Gauss(rnd)));
        shots.Add(new MatchShot(shots.Count + 1, "X", 100, 40, 40));                    // a wild shot at a target that is not visited again
        var r = MatchReading.Analyze(shots)!;
        Assert.DoesNotContain(r.Findings.Where(f => f.Key == "target_repeat"), f => f.Detail.Contains("X:"));
    }

    [Fact]
    public void TargetsThatAreNotVisitedAgainAreNotCompared()
    {
        var rnd = new Random(2);
        var shots = Enumerable.Range(1, 12).Select(i => new MatchShot(i, "T" + i, 100, 3 * Gauss(rnd), 3 * Gauss(rnd))).ToList();
        var r = MatchReading.Analyze(shots)!;
        Assert.DoesNotContain(r.Findings, f => f.Key == "target_repeat");
        Assert.DoesNotContain(r.Fine, t => t.Contains("missed the same way every time"));       // nothing was tested
    }

    [Fact]
    public void RepeatedTargetsThatAreAllAlikeAreSaidToBeFine()
    {
        var rnd = new Random(8);
        var shots = new List<MatchShot>();
        for (int series = 0; series < 3; series++)
            for (int b = 1; b <= 8; b++) shots.Add(new MatchShot(shots.Count + 1, "B" + b, 100, 2 * Gauss(rnd), 2 * Gauss(rnd)));
        var r = MatchReading.Analyze(shots)!;
        Assert.DoesNotContain(r.Findings, f => f.Key == "target_repeat");
        Assert.Contains(r.Fine, t => t == "No target is missed the same way every time.");
    }

    [Fact]
    public void CleanCardsRarelyAccuseATarget()
    {
        // 200 invented cards of ten barilotti shot three times with nothing wrong: how often is one of them called always-missed?
        int accused = 0, total = 200;
        for (int seed = 0; seed < total; seed++)
        {
            var rnd = new Random(500 + seed);
            var shots = new List<MatchShot>();
            for (int series = 0; series < 3; series++)
                for (int b = 1; b <= 10; b++) shots.Add(new MatchShot(shots.Count + 1, "B" + b, 100, 6 * Gauss(rnd), 6 * Gauss(rnd)));
            var r = MatchReading.Analyze(shots)!;
            if (r.Findings.Any(f => f.Key == "target_repeat")) accused++;
        }
        Assert.InRange(accused, 0, (int)(0.10 * total));                  // the test is made at 5 % over the worst target: about 5 % is expected
    }
}
