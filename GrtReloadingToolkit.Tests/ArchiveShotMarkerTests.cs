using GrtReloadingToolkit.Archive;
using Microsoft.Data.Sqlite;
using ShotMarker.Core.Import;
using ShotMarker.Core.Render;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>A real ShotMarker export (the fixture of the ShotMarker import tests) going into the archive.</summary>
public sealed class ArchiveShotMarkerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "archivio_sm_test_" + Guid.NewGuid().ToString("N"));

    public ArchiveShotMarkerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void EveryStringBecomesASessionWithItsDateGroupAndVelocities()
    {
        string export = ShotMarker.Core.Tests.Fixtures.Path("shotmarker/SM_export_Sep_21.tar");
        var log = new List<string>();
        var strings = ImportJob.Plan(export, log);
        using var a = new ArchiveDb(Path.Combine(_dir, "archivio.db"));
        long sh = ArchiveImports.ShooterFor(a, null);
        long load = ArchiveImports.NewLoad(a, sh, "284 Shehane", "284 Shehane");
        byte[] bytes = File.ReadAllBytes(export);

        foreach (var s in strings)
        {
            byte[] png = TargetRenderer.Render(s, ImportJob.ResolveFace(s, log), RenderOptions.ForGrt).Png;
            var r = ArchiveImports.AddShotMarkerString(a, sh, load, s, 41.5, export, bytes, png);
            Assert.Empty(r.AlreadyInArchive);

            int plotted = s.Shots.Count(x => x.IsImported);
            int withVel = s.Shots.Count(x => x.IsImported && x.VelocityMps is { } v && double.IsFinite(v) && v > 0);
            Assert.Equal(s.Timestamp.LocalDateTime.ToString("yyyy-MM-dd"), a.Scalar($"SELECT date FROM sessions WHERE id={r.SessionId}"));
            Assert.Equal(0L, a.Scalar($"SELECT date_estimated FROM sessions WHERE id={r.SessionId}"));
            Assert.Equal(s.DistanceMetres, (double)a.Scalar($"SELECT distance_m FROM sessions WHERE id={r.SessionId}")!, 6);
            Assert.Equal(withVel, r.Shots);
            Assert.Equal((long)plotted, a.Scalar($"SELECT COUNT(*) FROM hits h JOIN shot_groups g ON g.id=h.group_id WHERE g.session_id={r.SessionId}"));
            Assert.Equal((long)withVel, a.Scalar($"SELECT COUNT(*) FROM hits h JOIN shot_groups g ON g.id=h.group_id WHERE g.session_id={r.SessionId} AND h.shot_id IS NOT NULL"));
            // the group reads back as a measurable group at its distance, on the charge typed for it
            var g = Assert.Single(ArchiveQueries.Groups(a, r.SessionId));
            var t = ArchiveQueries.ToTargetGroup(g)!;
            Assert.Equal(41.5, t.ChargeGrains);
            Assert.Equal(plotted, t.Impacts.Count);
            Assert.NotNull(a.Scalar($"SELECT picture_file_id FROM shot_groups WHERE id={g.Id}"));
        }
        Assert.Equal((long)strings.Count, a.Count("sessions"));
        Assert.Equal(1L, a.Count("files", "kind='export ShotMarker'"));             // the export kept once
        Assert.Equal("true", a.GetMeta("master"));
        Assert.All(strings, s => Assert.True(ArchiveImports.ShotMarkerAlreadyImported(a, s)));
        Assert.Single(ArchiveImports.AddShotMarkerString(a, sh, load, strings[0], 41.5, export, bytes, null).AlreadyInArchive);
    }
}
