using GrtReloadingToolkit.Archive;
using GrtReloadingToolkit.Ocw;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>What the analysis tools read from the archive (Phase 3).</summary>
public sealed class ArchiveQueriesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "archivio3_test_" + Guid.NewGuid().ToString("N"));
    private readonly ArchiveDb _a;
    private readonly long _sh, _load, _session;

    public ArchiveQueriesTests()
    {
        Directory.CreateDirectory(_dir);
        _a = new ArchiveDb(Path.Combine(_dir, "archivio.db"));
        _sh = ArchiveImports.ShooterFor(_a, null);
        _load = ArchiveImports.NewLoad(_a, _sh, "Scala N550", "6.5 Creedmoor");
        var strings = new[]
        {
            new ChronoString("41.5 gr", 41.5, 18, "", new[] { new ChronoShot(820, "10:14:02 AM", null), new ChronoShot(822, "10:15:10 AM", null), new ChronoShot(819, null, null) },
                Path.Combine(_dir, "a.xlsx"), new byte[] { 1 }),
            new ChronoString("41.7 gr", 41.7, 18, "", new[] { new ChronoShot(826, null, null), new ChronoShot(827, null, null) },
                Path.Combine(_dir, "b.xlsx"), new byte[] { 2 }),
        };
        _session = ArchiveImports.AddChronoSession(_a, _sh, _load, new DateTime(2026, 11, 15), "", strings, "prova").SessionId;
        // two groups on the same session: one measured (100 m, mm), one without a scale
        long g1 = _a.Insert("shot_groups", ("session_id", _session), ("label", "41.5"), ("tab", "Gruppo di colpi"), ("distance_m", 100.0),
            ("step", 41.5), ("origin_point", "punto di mira"));
        foreach (var (i, x, y) in new[] { (1, 0.0, 0.0), (2, 29.0888, 0.0), (3, 0.0, 29.0888) })
            _a.Insert("hits", ("group_id", g1), ("idx", i), ("x_mm", x), ("y_mm", y), ("flyer", i == 3 ? 1 : 0));
        long g2 = _a.Insert("shot_groups", ("session_id", _session), ("label", "senza scala"), ("distance_m", 100.0), ("warning", "scala non impostata"));
        _a.Insert("hits", ("group_id", g2), ("idx", 1), ("frac_x", 0.5), ("frac_y", 0.5));
    }

    public void Dispose()
    {
        _a.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void SessionsListWhatTheyHold()
    {
        var s = Assert.Single(ArchiveQueries.Sessions(_a, _sh));
        Assert.Equal(("2026-11-15", 2, 5, 2, _load), (s.Date, s.Strings, s.Shots, s.Groups, s.LoadId!.Value));
        Assert.Single(ArchiveQueries.Sessions(_a, _sh, _load));
        Assert.Empty(ArchiveQueries.Sessions(_a, _sh, _load + 999));
        Assert.Equal("Scala N550 · 6.5 Creedmoor", Assert.Single(ArchiveQueries.LoadsWithSessions(_a, _sh)).Label);
    }

    [Fact]
    public void StringsComeBackInFiringOrderWithTheirClock()
    {
        var st = ArchiveQueries.Strings(_a, _session);
        Assert.Equal(new[] { 41.5, 41.7 }, st.Select(x => x.ChargeGr!.Value));
        Assert.Equal(new double[] { 820, 822, 819 }, st[0].Velocities);
        Assert.Equal(10 * 3600 + 14 * 60 + 2, ArchiveQueries.ClockSeconds(st[0].Shots[0].Note));
        Assert.Equal(22 * 3600 + 5 * 60, ArchiveQueries.ClockSeconds("ora 10:05:00 PM"));
        Assert.Equal(9 * 3600, ArchiveQueries.ClockSeconds("TIME=09:00:00"));   // as GRT keeps it
        Assert.Null(ArchiveQueries.ClockSeconds(null));
    }

    [Fact]
    public void AGroupBecomesMoaAtItsDistanceAndOneWithoutScaleIsLeftOut()
    {
        var gs = ArchiveQueries.Groups(_a, _session);
        var t = ArchiveQueries.ToTargetGroup(gs[0])!;
        Assert.Equal(41.5, t.ChargeGrains);
        Assert.Equal(3, t.Impacts.Count);
        Assert.Equal(1.0, t.Impacts[1].XMoa, 6);                       // 29.0888 mm at 100 m is one MOA
        Assert.Equal(2, ArchiveQueries.ToTargetGroup(gs[0], dropFlyers: true)!.Impacts.Count);
        Assert.Null(ArchiveQueries.ToTargetGroup(gs[1]));                // no mm: cannot be measured
    }

    [Fact]
    public void ALadderIsBuiltFromTheArchiveParts()
    {
        var st = ArchiveQueries.Strings(_a, _session);
        var vels = st.Select(x => new LadderVelocity(x.ChargeGr!.Value, x.Velocities)).ToList();
        var groups = ArchiveQueries.Groups(_a, _session).Select(g => ArchiveQueries.ToTargetGroup(g)).OfType<TargetGroup>().ToList();
        var rep = LadderLoader.FromParts(groups, vels, LadderMode.Charge);
        Assert.Equal(new[] { 41.5, 41.7 }, rep.Rows.Select(r => r.X));
        Assert.True(rep.Rows[0].HasVel && rep.Rows[0].HasTarget);
        Assert.True(rep.Rows[1].HasVel && !rep.Rows[1].HasTarget);
        Assert.Equal(3, rep.Rows[0].VelN);
    }

    [Fact]
    public void AnAnalysisIsKeptWithWhatItRead()
    {
        long id = ArchiveQueries.SaveAnalysis(_a, "Statistiche cronografo", "0.4.0", _load, _session, "{\"serie\":[1,2]}", "testo del rapporto");
        Assert.Equal("testo del rapporto", _a.Scalar($"SELECT result FROM analyses WHERE id={id}"));
        Assert.Equal(_session, (long)_a.Scalar($"SELECT session_id FROM analyses WHERE id={id}")!);
        Assert.Equal("true", _a.GetMeta("master"));
    }
}
