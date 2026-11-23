using GrtReloadingToolkit.Shooting;
using Xunit;

namespace GrtReloadingToolkit.Tests;

// The reading of the shooter's errors against the shooting analysis of Analisi DS on the same (invented) targets. The expected values come from
// running Analisi DS's own compute_shooting_analysis on these shots; its texts print the probabilities with one decimal (two for the zero).
public sealed class ShooterErrorsParityTests
{
    private static PatternShot[] Shots((string S, int O, int N, double X, double U, double? V, double? T)[] rows) =>
        rows.Select(r => new PatternShot(r.S, r.O, r.N, r.X, r.U, r.V, r.T)).ToArray();

    private static void Near(double expected, double actual, double tol = 1e-9) => Assert.True(Math.Abs(expected - actual) <= tol * Math.Max(1, Math.Abs(expected)), $"expected {expected}, got {actual}");

    private static void Check(ShooterReport r, int n, double sigma, double sdX, double sdU, double cx, double cu, (double Slope, double P, double R2, double Theo, double SdV)? vel,
        (string S, int N, double Cx, double Cu, double? SdX, double? SdU, double Mr)[] series, (string Key, double? P)[] findings, int fineCount, (string S, int N)[] flyers)
    {
        Assert.Equal(n, r.N);
        Near(sigma, r.Sigma); Near(sdX, r.SdX); Near(sdU, r.SdUp); Near(cx, r.CentreX); Near(cu, r.CentreUp);
        if (vel is null) Assert.Null(r.Velocity);
        else
        {
            Assert.NotNull(r.Velocity);
            Near(vel.Value.Slope, r.Velocity!.SlopeMmPerMps); Near(vel.Value.P, r.Velocity.P); Near(vel.Value.R2, r.Velocity.R2); Near(vel.Value.SdV, r.Velocity.SdMps);
            if (r.Velocity.TheoreticalMmPerMps is { } th) Near(vel.Value.Theo, th); else Assert.True(double.IsNaN(vel.Value.Theo));
        }
        Assert.Equal(series.Length, r.Series.Count);
        for (int i = 0; i < series.Length; i++)
        {
            Assert.Equal(series[i].S, r.Series[i].Series); Assert.Equal(series[i].N, r.Series[i].N);
            Near(series[i].Cx, r.Series[i].CentreX); Near(series[i].Cu, r.Series[i].CentreUp); Near(series[i].Mr, r.Series[i].MeanRadius);
            if (series[i].SdX is { } sx) Near(sx, r.Series[i].SdX!.Value);
            if (series[i].SdU is { } su) Near(su, r.Series[i].SdUp!.Value);
        }
        // the same kinds of finding (the first-shot check is the one Analisi DS only makes on the FSTP card, so it is left out here)
        var mine = r.Findings.Where(f => f.Key != "first_shot").ToList();
        Assert.Equal(findings.Select(f => f.Key), mine.Select(f => f.Key));
        for (int i = 0; i < findings.Length; i++)
            if (findings[i].P is { } p)
            {
                double pct = mine[i].P!.Value * 100;
                double tol = findings[i].Key == "zero" ? 0.0051 : 0.0501;               // a printed percentage: two decimals for the zero, one for the rest
                Assert.True(Math.Abs(p - Math.Round(Math.Max(pct, findings[i].Key == "zero" ? 0.01 : 0), findings[i].Key == "zero" ? 2 : 1)) <= tol + 1e-9, $"{findings[i].Key}: expected {p} %, got {pct} %");
            }
        Assert.Equal(flyers.Select(f => (f.S, f.N)), r.Flyers.Select(f => (f.Series, f.Number)));
    }

    private static readonly (string S, int O, int N, double X, double U, double? V, double? T)[] ManyErrorsShots =
    {
        ("S1", 0, 1, 10.25, -2.61, 828.8, 36012.0),
        ("S1", 1, 2, 15.73, 1.65, 832.3, 36029.0),
        ("S1", 2, 3, 5.02, -17.27, 833.6, 36073.0),
        ("S1", 3, 4, 9.05, -5.16, 831.8, 36097.0),
        ("S1", 4, 5, 8.11, -1.83, 830.2, 36138.0),
        ("S1", 5, 6, 19.99, -7.65, 826.2, 36153.0),
        ("S1", 6, 7, 16.6, -5.19, 827.9, 36178.0),
        ("S1", 7, 8, 19.99, -2.43, 832.1, 36221.0),
        ("S1", 8, 9, 15.93, -8.23, 829.4, 36251.0),
        ("S1", 9, 10, 37.82, -19.49, 826.2, 36300.0),
        ("S1", 10, 11, 22.09, -4.5, 830.5, 36330.0),
        ("S1", 11, 12, 25.42, 1.46, 827.8, 36371.0),
        ("S2", 0, 1, 0.6, -3.79, 832.0, 36034.0),
        ("S2", 1, 2, 19.82, -0.72, 837.0, 36062.0),
        ("S2", 2, 3, 9.55, -4.73, 828.9, 36095.0),
        ("S2", 3, 4, 5.59, -5.98, 835.3, 36120.0),
        ("S2", 4, 5, 34.65, -19.99, 832.4, 36131.0),
        ("S2", 5, 6, -12.44, -19.89, 834.8, 36185.0),
        ("S2", 6, 7, 25.66, -8.94, 835.0, 36229.0),
        ("S2", 7, 8, -9.45, -18.59, 835.8, 36285.0),
        ("S2", 8, 9, 11.01, -0.51, 833.1, 36293.0),
        ("S2", 9, 10, 17.72, -7.69, 836.3, 36330.0),
        ("S2", 10, 11, 18.19, 1.72, 833.2, 36340.0),
        ("S2", 11, 12, 40.11, -16.27, 827.3, 36390.0),
        ("S3", 0, 1, 2.91, 3.21, 836.3, 36031.0),
        ("S3", 1, 2, 24.33, 11.56, 840.8, 36085.0),
        ("S3", 2, 3, -6.77, 6.54, 835.2, 36137.0),
        ("S3", 3, 4, 23.99, 13.4, 836.8, 36192.0),
        ("S3", 4, 5, 27.51, 2.21, 833.8, 36201.0),
        ("S3", 5, 6, 11.65, -0.15, 840.3, 36229.0),
        ("S3", 6, 7, 10.8, -8.86, 834.4, 36269.0),
        ("S3", 7, 8, 11.69, -2.47, 835.1, 36303.0),
        ("S3", 8, 9, 12.97, 14.72, 839.3, 36361.0),
        ("S3", 9, 10, 8.86, -2.16, 834.2, 36372.0),
        ("S3", 10, 11, 14.88, -6.45, 831.0, 36399.0),
        ("S3", 11, 12, 36.64, -17.84, 833.7, 36452.0),
    };

    [Fact]
    public void ManyErrors_IsReadLikeAnalisiDs()
    {
        var r = ShooterErrors.Analyze(Shots(ManyErrorsShots), new PatternOptions { DistanceM = 100.0, Mode = ShootingMode.Mono, ScopeClick = "1/4 MOA" })!;
        Check(r, 36, 10.590542814037418, 12.564693428794671, 8.151544223785038, 15.179722222222223, -4.525555555555555,
            (0.9730638275752271, 0.0473355183490215, 0.11075758727836905, 0.1697068006655057, 2.707124638712403),
            new (string, int, double, double, double?, double?, double)[] { ("S1", 12, 17.166666666666668, -5.9375, 8.954729916574138, 6.5785686824028, 8.530920997740592), ("S2", 12, 13.417499999999999, -8.781666666666666, 16.049107097790944, 7.950958585242563, 14.962881377966974), ("S3", 12, 14.954999999999998, 1.1425, 11.655622521497355, 9.635751727999034, 12.73042374628776) },
            new (string, double?)[] { ("zero", 0.01), ("horizontal", 0.6), ("drift_x", 1.3), ("hold_long", 0.1) }, 3,
            new (string, int)[] {  });
        Assert.Equal(new (double Mm, int Right, int Up)[] { (7.272200000000001, -2, 1) }.Length, r.Clicks.Count);
        var got = r.Clicks.Select(c => (c.MmAtDistance, c.Right, c.Up)).ToArray();
        var want = new (double Mm, int Right, int Up)[] { (7.272200000000001, -2, 1) };
        for (int i = 0; i < want.Length; i++) { Near(want[i].Mm, got[i].MmAtDistance); Assert.Equal(want[i].Right, got[i].Right); Assert.Equal(want[i].Up, got[i].Up); }
    }

    private static readonly (string S, int O, int N, double X, double U, double? V, double? T)[] CleanShots =
    {
        ("S1", 0, 1, -0.9, -2.12, 830.0, 36055.0),
        ("S1", 1, 2, -1.77, 1.66, 830.2, 36105.0),
        ("S1", 2, 3, -3.52, 2.48, 831.1, 36127.0),
        ("S1", 3, 4, -7.4, -7.67, 826.0, 36176.0),
        ("S1", 4, 5, 1.29, -1.05, 829.3, 36216.0),
        ("S1", 5, 6, 0.01, -6.42, 822.4, 36271.0),
        ("S1", 6, 7, -3.04, -3.53, 828.6, 36289.0),
        ("S1", 7, 8, -2.13, 0.14, 829.9, 36341.0),
        ("S1", 8, 9, 0.5, -6.49, 830.2, 36393.0),
        ("S1", 9, 10, -2.37, 2.75, 832.6, 36427.0),
        ("S2", 0, 1, 2.51, 2.43, 829.4, 36028.0),
        ("S2", 1, 2, -2.5, -2.15, 835.0, 36067.0),
        ("S2", 2, 3, -2.12, 3.3, 833.4, 36098.0),
        ("S2", 3, 4, -2.98, -8.27, 836.4, 36117.0),
        ("S2", 4, 5, 5.23, -1.61, 831.6, 36132.0),
        ("S2", 5, 6, 6.29, -1.51, 831.9, 36147.0),
        ("S2", 6, 7, -0.59, -0.35, 834.1, 36201.0),
        ("S2", 7, 8, 2.81, 2.37, 831.7, 36222.0),
        ("S2", 8, 9, 0.18, -5.46, 832.0, 36246.0),
        ("S2", 9, 10, -7.94, -3.9, 834.0, 36261.0),
    };

    [Fact]
    public void Clean_IsReadLikeAnalisiDs()
    {
        var r = ShooterErrors.Analyze(Shots(CleanShots), new PatternOptions { DistanceM = 100.0, Mode = ShootingMode.Unknown, ScopeClick = null })!;
        Check(r, 20, 3.6412650426893003, 3.48903759414159, 3.7873788942867717, -0.922, -1.77,
            (0.32091839478070816, 0.3719358208502208, 0.04450911076997331, 0.17095492414315036, 2.4234164661529154),
            new (string, int, double, double, double?, double?, double)[] { ("S1", 10, -1.9330000000000003, -2.025, 2.4729202260575343, 3.8830836829624062, 3.9455315976573546), ("S2", 10, 0.08899999999999997, -1.5150000000000001, 4.269828125606723, 3.6891921729409662, 4.976760558924587) },
            new (string, double?)[] {  }, 5,
            new (string, int)[] {  });
    }

    private static readonly (string S, int O, int N, double X, double U, double? V, double? T)[] WorseSeriesShots =
    {
        ("S1", 0, 1, -3.28, -0.65, 830.0, 36057.0),
        ("S1", 1, 2, 0.18, 0.12, 828.4, 36071.0),
        ("S1", 2, 3, 0.94, 6.07, 831.2, 36085.0),
        ("S1", 3, 4, 0.33, 2.92, 832.2, 36141.0),
        ("S1", 4, 5, 0.16, -1.46, 829.1, 36197.0),
        ("S1", 5, 6, 2.8, 2.83, 829.3, 36250.0),
        ("S1", 6, 7, -3.55, -0.56, 828.4, 36299.0),
        ("S1", 7, 8, 0.78, 1.82, 830.5, 36356.0),
        ("S1", 8, 9, 0.11, 2.05, 828.6, 36373.0),
        ("S2", 0, 1, 5.46, -6.24, 832.0, 36045.0),
        ("S2", 1, 2, 4.19, 4.83, 835.4, 36102.0),
        ("S2", 2, 3, 7.26, 1.69, 835.9, 36160.0),
        ("S2", 3, 4, -4.16, 2.52, 834.5, 36203.0),
        ("S2", 4, 5, -5.15, 0.77, 834.2, 36254.0),
        ("S2", 5, 6, 4.19, 2.91, 832.9, 36312.0),
        ("S2", 6, 7, 1.21, 2.73, 830.3, 36331.0),
        ("S2", 7, 8, 2.93, -0.81, 834.5, 36375.0),
        ("S2", 8, 9, -4.04, 0.01, 834.1, 36414.0),
        ("S3", 0, 1, 0.35, 15.31, 835.4, 36035.0),
        ("S3", 1, 2, -16.55, 2.73, 834.1, 36084.0),
        ("S3", 2, 3, -0.64, 9.31, 834.2, 36100.0),
        ("S3", 3, 4, 4.7, -0.99, 835.9, 36155.0),
        ("S3", 4, 5, 10.99, 12.35, 837.3, 36189.0),
        ("S3", 5, 6, 7.41, -5.09, 835.9, 36207.0),
        ("S3", 6, 7, -0.84, -3.71, 832.3, 36218.0),
        ("S3", 7, 8, -0.36, -5.54, 834.5, 36247.0),
        ("S3", 8, 9, -4.25, 7.05, 840.1, 36281.0),
        ("S4", 0, 1, -6.78, 9.89, 840.6, 36016.0),
        ("S4", 1, 2, -3.75, 2.25, 840.6, 36076.0),
        ("S4", 2, 3, 3.98, 2.46, 836.1, 36124.0),
        ("S4", 3, 4, -3.27, 6.98, 841.1, 36160.0),
        ("S4", 4, 5, -0.78, -1.61, 836.2, 36168.0),
        ("S4", 5, 6, 1.52, 1.4, 839.3, 36178.0),
        ("S4", 6, 7, 2.72, 6.09, 839.9, 36222.0),
        ("S4", 7, 8, 0.69, -5.15, 841.1, 36249.0),
        ("S4", 8, 9, -3.19, -0.47, 834.8, 36289.0),
    };

    [Fact]
    public void WorseSeries_IsReadLikeAnalisiDs()
    {
        var r = ShooterErrors.Analyze(Shots(WorseSeriesShots), new PatternOptions { DistanceM = 200.0, Mode = ShootingMode.Bipod, ScopeClick = "0.1 mrad" })!;
        Check(r, 36, 4.977127720605754, 4.983042842703866, 4.9712055602461485, 0.06416666666666672, 2.0780555555555558,
            (0.8704654052824061, 0.03708751879709176, 0.1216477624529937, 0.6753014498554846, 1.9045976119621946),
            new (string, int, double, double, double?, double?, double)[] { ("S1", 9, -0.1699999999999999, 1.46, 2.019028231600539, 2.3609531973336533, 2.5826789157327568), ("S2", 9, 1.3211111111111111, 0.9344444444444443, 4.638454603756634, 3.177743346744318, 4.931340988946532), ("S3", 9, 0.09000000000000005, 3.491111111111111, 7.834519768307437, 7.84540700735858, 9.779288205240158), ("S4", 9, -0.9844444444444443, 2.426666666666667, 3.5144598130833384, 4.650696184443787, 5.070947660115135) },
            new (string, double?)[] { ("zero", 1.47), ("flyers", null), ("concentration", 4.7) }, 3,
            new (string, int)[] { ("S3", 2) });
        Assert.Equal(new (double Mm, int Right, int Up)[] { (20.0, 0, 0) }.Length, r.Clicks.Count);
        var got = r.Clicks.Select(c => (c.MmAtDistance, c.Right, c.Up)).ToArray();
        var want = new (double Mm, int Right, int Up)[] { (20.0, 0, 0) };
        for (int i = 0; i < want.Length; i++) { Near(want[i].Mm, got[i].MmAtDistance); Assert.Equal(want[i].Right, got[i].Right); Assert.Equal(want[i].Up, got[i].Up); }
    }

    private static readonly (string S, int O, int N, double X, double U, double? V, double? T)[] RushedShots =
    {
        ("S1", 0, 1, -1.62, 4.71, 827.6, 36050.0),
        ("S1", 1, 2, -8.92, 12.8, 828.3, 36060.0),
        ("S1", 2, 3, 6.7, -8.52, 826.3, 36091.0),
        ("S1", 3, 4, -3.33, -3.07, 829.7, 36125.0),
        ("S1", 4, 5, -3.15, 4.35, 831.7, 36178.0),
        ("S1", 5, 6, -4.91, -6.83, 825.1, 36233.0),
        ("S1", 6, 7, -1.87, -1.57, 830.1, 36267.0),
        ("S1", 7, 8, -2.82, 7.97, 826.7, 36292.0),
        ("S1", 8, 9, -5.13, 6.74, 832.1, 36312.0),
        ("S1", 9, 10, -7.38, 10.39, 830.1, 36332.0),
        ("S1", 10, 11, -3.72, -6.19, 826.5, 36359.0),
        ("S1", 11, 12, 4.74, 10.8, 834.3, 36380.0),
        ("S1", 12, 13, 5.46, -7.23, 832.5, 36399.0),
        ("S1", 13, 14, 0.79, -1.8, 830.6, 36427.0),
        ("S2", 0, 1, -5.72, -3.38, 833.7, 36028.0),
        ("S2", 1, 2, -2.12, -5.12, 829.2, 36078.0),
        ("S2", 2, 3, -6.22, -4.12, 834.3, 36115.0),
        ("S2", 3, 4, -15.68, 19.9, 832.0, 36123.0),
        ("S2", 4, 5, -5.78, -4.01, 831.4, 36148.0),
        ("S2", 5, 6, -3.21, -0.63, 833.8, 36177.0),
        ("S2", 6, 7, -0.05, 3.23, 833.5, 36212.0),
        ("S2", 7, 8, 1.07, 3.85, 828.3, 36265.0),
        ("S2", 8, 9, -10.46, 8.29, 833.1, 36283.0),
        ("S2", 9, 10, 1.39, -3.71, 836.2, 36315.0),
        ("S2", 10, 11, -4.52, 1.83, 833.8, 36344.0),
        ("S2", 11, 12, -0.26, 2.61, 834.3, 36388.0),
        ("S2", 12, 13, 8.56, -2.45, 835.6, 36406.0),
        ("S2", 13, 14, -2.34, 2.32, 833.4, 36433.0),
    };

    [Fact]
    public void Rushed_IsReadLikeAnalisiDs()
    {
        var r = ShooterErrors.Analyze(Shots(RushedShots), new PatternOptions { DistanceM = 300.0, Mode = ShootingMode.DoubleRest, ScopeClick = null })!;
        Check(r, 28, 6.22511884788794, 5.248934167058098, 7.067736515359742, -2.517857142857143, 1.47,
            (0.3778232303340568, 0.5016844817396593, 0.01754125256569657, 1.5373095493184077, 2.4312341456345736),
            new (string, int, double, double, double?, double?, double)[] { ("S1", 14, -1.797142857142857, 1.6107142857142855, 4.690058927908343, 7.478809514771838, 7.695827133624445), ("S2", 14, -3.238571428571428, 1.329285714285714, 5.753778500513122, 6.631229685575454, 6.587635501073548) },
            new (string, double?)[] { ("zero", 1.64), ("flyers", null), ("drift_x", 3.6), ("rushed", 0.4) }, 3,
            new (string, int)[] { ("S2", 4) });
        Assert.Equal(new (double Mm, int Right, int Up)[] { (21.816599999999998, 0, 0), (30.0, 0, 0) }.Length, r.Clicks.Count);
        var got = r.Clicks.Select(c => (c.MmAtDistance, c.Right, c.Up)).ToArray();
        var want = new (double Mm, int Right, int Up)[] { (21.816599999999998, 0, 0), (30.0, 0, 0) };
        for (int i = 0; i < want.Length; i++) { Near(want[i].Mm, got[i].MmAtDistance); Assert.Equal(want[i].Right, got[i].Right); Assert.Equal(want[i].Up, got[i].Up); }
    }

    private static readonly (string S, int O, int N, double X, double U, double? V, double? T)[] NoVelocityShots =
    {
        ("S1", 0, 1, -8.57, 2.74, null, null),
        ("S1", 1, 2, -5.45, 6.21, null, null),
        ("S1", 2, 3, -8.14, 8.33, null, null),
        ("S1", 3, 4, -7.05, 6.46, null, null),
        ("S1", 4, 5, -4.99, 7.91, null, null),
        ("S1", 5, 6, -6.55, 5.74, null, null),
        ("S1", 6, 7, -2.97, 2.11, null, null),
        ("S1", 7, 8, -4.52, 4.04, null, null),
        ("S2", 0, 1, -3.3, 1.41, null, null),
        ("S2", 1, 2, -5.39, 10.37, null, null),
        ("S2", 2, 3, -2.65, -1.04, null, null),
        ("S2", 3, 4, -7.09, 3.42, null, null),
        ("S2", 4, 5, -3.67, 2.67, null, null),
        ("S2", 5, 6, -5.91, -2.71, null, null),
        ("S2", 6, 7, -5.27, 5.92, null, null),
        ("S2", 7, 8, -5.8, -1.65, null, null),
    };

    [Fact]
    public void NoVelocity_IsReadLikeAnalisiDs()
    {
        var r = ShooterErrors.Analyze(Shots(NoVelocityShots), new PatternOptions { DistanceM = 100.0, Mode = ShootingMode.Unknown, ScopeClick = "1/8 MOA" })!;
        Check(r, 16, 2.7399103054922906, 1.719904482397621, 3.472196068155467, -5.4575000000000005, 3.8706249999999995,
            null,
            new (string, int, double, double, double?, double?, double)[] { ("S1", 8, -6.03, 5.4425, 1.9002330684117974, 2.283954903232549, 2.443717588358714), ("S2", 8, -4.885, 2.29875, 1.5183073281971418, 4.346934675311854, 3.8092148839710065) },
            new (string, double?)[] { ("zero", 0.01), ("vertical", 1.3), ("flyers", null) }, 1,
            new (string, int)[] { ("S2", 2) });
        Assert.Equal(new (double Mm, int Right, int Up)[] { (3.6361000000000003, 2, -1) }.Length, r.Clicks.Count);
        var got = r.Clicks.Select(c => (c.MmAtDistance, c.Right, c.Up)).ToArray();
        var want = new (double Mm, int Right, int Up)[] { (3.6361000000000003, 2, -1) };
        for (int i = 0; i < want.Length; i++) { Near(want[i].Mm, got[i].MmAtDistance); Assert.Equal(want[i].Right, got[i].Right); Assert.Equal(want[i].Up, got[i].Up); }
    }

}
