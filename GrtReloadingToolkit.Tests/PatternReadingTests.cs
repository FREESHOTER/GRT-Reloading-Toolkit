using GrtReloadingToolkit.Archive;
using GrtReloadingToolkit.Ocw;
using GrtReloadingToolkit.Shooting;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>Shots typed by the shooter, the text of the reading, and the groups of the archive taken as a pattern.</summary>
public sealed class PatternInputTests
{
    [Fact]
    public void AllThePartsOfALineAreRead()
    {
        var p = PatternInput.Parse("1,5 -2.25\n3 4 831,5 10:14:02\n5 6 10:15:30 PM 829.0\n");
        Assert.Empty(p.Problems);
        Assert.Equal(3, p.Shots.Count);
        Assert.Equal((1.5, -2.25, null, null), (p.Shots[0].XMm, p.Shots[0].UpMm, p.Shots[0].VelocityMps, p.Shots[0].ClockSeconds));
        Assert.Equal((831.5, 10 * 3600 + 14 * 60 + 2.0), (p.Shots[1].VelocityMps!.Value, p.Shots[1].ClockSeconds!.Value));
        Assert.Equal((829.0, 22 * 3600 + 15 * 60 + 30.0), (p.Shots[2].VelocityMps!.Value, p.Shots[2].ClockSeconds!.Value));
        Assert.Equal(new[] { 1, 2, 3 }, p.Shots.Select(s => s.Number));
    }

    [Fact]
    public void ABlankLineOrAHashStartsASeries()
    {
        var p = PatternInput.Parse("1 1\n2 2\n\n3 3\n# Dopo il caldo\n4 4\n5 5\n#\n6 6");
        Assert.Empty(p.Problems);
        Assert.Equal(new[] { "1", "1", "2", "Dopo il caldo", "Dopo il caldo", "4" }, p.Shots.Select(s => s.Series));
        Assert.Equal(new[] { 0, 1, 0, 0, 1, 0 }, p.Shots.Select(s => s.Order));
        Assert.Equal(new[] { 1, 2, 1, 1, 2, 1 }, p.Shots.Select(s => s.Number));
    }

    [Fact]
    public void AMidnightHourIsTwelveAmAndNoonStaysNoon()
    {
        var p = PatternInput.Parse("0 0 12:30:00 AM\n0 0 12:30:00 PM");
        Assert.Equal(new double?[] { 30 * 60, 12 * 3600 + 30 * 60 }, p.Shots.Select(s => s.ClockSeconds));
    }

    [Fact]
    public void ALineThatCannotBeReadIsReportedWithItsNumberAndTheOthersStay()
    {
        var p = PatternInput.Parse("1 2\nsolo testo\n3 4 abc\n5 6 25:00:00\n7 8");
        Assert.Equal(2, p.Shots.Count);
        Assert.Equal(3, p.Problems.Count);
        Assert.StartsWith("Line 2:", p.Problems[0]);
        Assert.StartsWith("Line 3:", p.Problems[1]);
        Assert.StartsWith("Line 4:", p.Problems[2]);
    }

    [Fact]
    public void CoordinatesTypedInInchesAreKeptInMillimetres()
    {
        var p = PatternInput.Parse("0.5 -1\n2 0.25", mmPerUnit: 25.4);
        Assert.Equal(new[] { 12.7, 50.8 }, p.Shots.Select(s => s.XMm).ToArray().Select(v => Math.Round(v, 6)).ToArray());
        Assert.Equal(new[] { -25.4, 6.35 }, p.Shots.Select(s => Math.Round(s.UpMm, 6)).ToArray());
        Assert.Equal(1.0, PatternInput.Parse("1 1").Shots[0].XMm);                  // millimetres unless said otherwise
    }

    [Fact]
    public void NothingTypedGivesNothing()
    {
        Assert.Empty(PatternInput.Parse(null).Shots);
        Assert.Empty(PatternInput.Parse("  \n\n").Shots);
    }
}

public sealed class ShooterTextTests
{
    private static readonly string Sample = string.Join("\n",
        "# S1", "4.2 -3.1 831.0 10:14:02", "5.9 -5.2 829.5 10:14:40", "3.1 -4.4 830.2 10:15:20", "6.8 -2.9 832.1 10:16:10", "2.4 -6.0 828.7 10:16:50", "5.5 -3.8 831.4 10:17:30",
        "", "# S2", "7.1 -3.0 833.0 10:25:02", "9.0 -4.4 834.1 10:25:41", "6.2 -2.2 832.8 10:26:30", "8.4 -5.1 835.0 10:27:15", "10.3 -3.6 833.9 10:28:00", "7.7 -4.8 834.6 10:28:41");

    [Fact]
    public void TheReportHasTheNumbersTheSeriesTableAndWhatPointsToAMistake()
    {
        var report = ShooterErrors.Analyze(PatternInput.Parse(Sample).Shots, new PatternOptions { DistanceM = 100, Mode = ShootingMode.Mono, ScopeClick = "1/4 MOA" })!;
        string text = ShooterText.Report(report);
        Assert.Contains("12 shots in 2 series at 100 m.", text);
        Assert.Contains("Centre X (mm)", text);
        Assert.Contains("S1", text);
        Assert.Contains("S2", text);
        Assert.Contains("What points to a mistake (", text);
        Assert.Contains("1. The zero of the sight is not right", text);
        Assert.Contains("→", text);
        Assert.Contains("Each reading is a test on these shots", text);
        Assert.DoesNotContain("{0}", text);
    }

    [Fact]
    public void AReadingWithNoFindingSaysItIsNotAProof()
    {
        // a tight group centred on the aim point, one series: nothing to report
        string text = ShooterText.Report(ShooterErrors.Analyze(PatternInput.Parse("0.2 0.1\n-0.3 0.4\n0.1 -0.3\n-0.2 -0.2\n0.3 0.2\n-0.1 0.3").Shots, new PatternOptions())!);
        Assert.Contains("Nothing in the pattern of these shots points to a mistake", text);
        Assert.Contains("What was checked and is fine:", text);
    }
}

public sealed class PatternQueriesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rosata_test_" + Guid.NewGuid().ToString("N"));
    private readonly ArchiveDb _a;
    private readonly long _session;
    private readonly long _g1, _g2;

    public PatternQueriesTests()
    {
        Directory.CreateDirectory(_dir);
        _a = new ArchiveDb(Path.Combine(_dir, "archivio.db"));
        long sh = ArchiveImports.ShooterFor(_a, null);
        long load = ArchiveImports.NewLoad(_a, sh, "Scala", "6.5 Creedmoor");
        var strings = new[]
        {
            new ChronoString("41.5 gr", 41.5, 18, "", new[] { new ChronoShot(820, "10:14:02 AM", null), new ChronoShot(822, "10:15:10 AM", null), new ChronoShot(819, null, null) },
                Path.Combine(_dir, "a.xlsx"), new byte[] { 1 }),
        };
        _session = ArchiveImports.AddChronoSession(_a, sh, load, new DateTime(2026, 11, 15), "", strings, "prova").SessionId;
        var shotIds = new List<long>();
        using (var cmd = _a.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM shots ORDER BY id";
            using var r = cmd.ExecuteReader();
            while (r.Read()) shotIds.Add(r.GetInt64(0));
        }
        _g1 = _a.Insert("shot_groups", ("session_id", _session), ("label", "41.5"), ("tab", "Gruppo di colpi"), ("distance_m", 100.0), ("origin_point", "punto di mira"));
        long i = 0;
        foreach (var (x, y) in new[] { (1.0, 2.0), (3.0, -1.0), (0.5, 0.5) })
            _a.Insert("hits", ("group_id", _g1), ("idx", ++i), ("x_mm", x), ("y_mm", y), ("shot_id", shotIds[(int)i - 1]));
        _g2 = _a.Insert("shot_groups", ("session_id", _session), ("label", "41.5"), ("tab", "Gruppo di colpi"), ("distance_m", 200.0), ("origin_point", "centro dei colpi"));
        _a.Insert("hits", ("group_id", _g2), ("idx", 1), ("x_mm", 4.0), ("y_mm", 5.0));
        _a.Insert("hits", ("group_id", _g2), ("idx", 2), ("frac_x", 0.5), ("frac_y", 0.5));   // no scale
    }

    public void Dispose()
    {
        _a.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void AGroupBecomesASeriesWithTheVelocityAndTimeOfItsLinkedShots()
    {
        var gs = ArchiveQueries.Groups(_a, _session).Where(g => g.Id == _g1).ToList();
        var p = PatternQueries.FromGroups(_a, gs);
        Assert.Equal(3, p.Shots.Count);
        Assert.Equal(new[] { 820.0, 822.0, 819.0 }, p.Shots.Select(s => s.VelocityMps!.Value));
        Assert.Equal(10 * 3600 + 14 * 60 + 2.0, p.Shots[0].ClockSeconds!.Value);
        Assert.Null(p.Shots[2].ClockSeconds);
        Assert.True(p.AimKnown);
        Assert.Equal(100.0, p.DistanceM);
        Assert.Empty(p.Notes);
        Assert.Equal(new[] { 1, 2, 3 }, p.Shots.Select(s => s.Number));
    }

    [Fact]
    public void HitsWithoutAScaleAreLeftOutDifferentDistancesAreSaidAndAimIsKnownOnlyIfAllHaveIt()
    {
        var gs = ArchiveQueries.Groups(_a, _session);
        var p = PatternQueries.FromGroups(_a, gs);
        Assert.Equal(4, p.Shots.Count);                       // 3 + the one with a scale in the second group
        Assert.False(p.AimKnown);                             // the second group is measured from the centre of its hits
        Assert.Equal(2, p.Notes.Count);
        Assert.Contains(p.Notes, n => n.Contains("1 hits without a scale"));
        Assert.Contains(p.Notes, n => n.Contains("different distances"));
        Assert.Equal(2, p.Shots.Select(s => s.Series).Distinct().Count());   // two groups with the same name stay two series
        Assert.All(p.Shots.Where(s => s.VelocityMps != null), s => Assert.True(s.VelocityMps > 0));
    }

    [Fact]
    public void NoGroupsGiveAnEmptyPattern()
    {
        var p = PatternQueries.FromGroups(_a, new List<ArchiveGroup>());
        Assert.Empty(p.Shots);
        Assert.False(p.AimKnown);
        Assert.Null(p.DistanceM);
    }
}
