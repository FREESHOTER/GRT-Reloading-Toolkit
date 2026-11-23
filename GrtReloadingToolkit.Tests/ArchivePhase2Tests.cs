using GrtPluginKit.Grt;
using GrtReloadingToolkit.Archive;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>The archive as the master: refreshed from the old databases, and written by the chronograph import.</summary>
public sealed class ArchivePhase2Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "archivio2_test_" + Guid.NewGuid().ToString("N"));

    public ArchivePhase2Tests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static void Sql(string path, string sql)
    {
        using var cn = new SqliteConnection($"Data Source={path};Pooling=False");
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private string OldLog()
    {
        string log = Path.Combine(_dir, "reloading_log.db");
        Sql(log, @"
CREATE TABLE shooters (id INTEGER PRIMARY KEY, name TEXT, notes TEXT DEFAULT '', archived INTEGER DEFAULT 0);
INSERT INTO shooters VALUES (1,'Io','',0);
CREATE TABLE firearms (id INTEGER PRIMARY KEY, name TEXT, caliber TEXT, notes TEXT, rounds_before INTEGER, retired INTEGER, created_at TEXT, rounds_limit INTEGER, shooter_id INTEGER);
INSERT INTO firearms VALUES (1,'Tikka','6.5 Creedmoor','',250,0,'2026-09-18',NULL,1),(2,'Sako','308','',0,0,'2026-09-18',NULL,1);
CREATE TABLE components (id INTEGER PRIMARY KEY, kind TEXT, brand TEXT, name TEXT, lot TEXT, unit TEXT, qty_initial REAL, qty_current REAL,
  cost_total REAL, currency TEXT, expected_uses INTEGER, bullet_weight_gr REAL, notes TEXT, acquired_at TEXT, archived INTEGER, shooter_id INTEGER);
INSERT INTO components VALUES (1,'Powder','Vihtavuori','N550','L1','g',1000,1000,80,'EUR',1,NULL,'','2026-09-01',0,1);
CREATE TABLE ledger (id INTEGER PRIMARY KEY, component_id INTEGER, delta REAL, reason TEXT, at TEXT);
CREATE TABLE journal (id INTEGER PRIMARY KEY, date TEXT, load_name TEXT, caliber TEXT, firearm TEXT, powder_id INTEGER, primer_id INTEGER,
  brass_id INTEGER, bullet_id INTEGER, charge_gr REAL, coal_mm REAL, cbto_mm REAL, rounds INTEGER, velocity_avg_ms REAL, sd_ms REAL, es_ms REAL,
  group_moa REAL, distance_m REAL, notes TEXT, grtload_path TEXT, created_at TEXT, firearm_id INTEGER, temperature_c REAL, shots_ms TEXT, shooter_id INTEGER);
INSERT INTO journal (id,date,load_name,caliber,powder_id,charge_gr,firearm_id,shots_ms,shooter_id) VALUES (1,'2026-10-01','N550 41.5','6.5 Creedmoor',1,41.5,1,'818 820 822',1);");
        return log;
    }

    private static ChronoString Str(string file, double charge, params double[] v)
        => new($"{charge} gr", charge, 18, "AVG-E=820", v.Select((x, i) => new ChronoShot(x, $"10:0{i}:00", null)).ToList(), file,
            System.Text.Encoding.UTF8.GetBytes(file + string.Join(",", v)));

    [Fact]
    public void TheRefreshFollowsTheOldDatabasesAndLeavesTheArchivesOwnDataAlone()
    {
        string log = OldLog();
        var src = new ArchiveSources { LogDb = log };
        using var a = new ArchiveDb(Path.Combine(_dir, "archivio.db"));
        Assert.True(ArchiveBuilder.Build(a, src).Ok);
        long compId = (long)a.Scalar("SELECT id FROM components")!;
        long sessionId = (long)a.Scalar("SELECT id FROM sessions")!;
        long shooter = ArchiveImports.ShooterFor(a, 1);
        var chrono = ArchiveImports.AddChronoSession(a, shooter, null, new DateTime(2026, 11, 10), "", new[] { Str("a.xlsx", 41.5, 819, 821) }, "prova");

        // the shooter keeps working in the old tools
        Sql(log, @"UPDATE components SET qty_current=940 WHERE id=1;
                   INSERT INTO ledger VALUES (1,1,-60,'caricati 25','2026-11-12');
                   DELETE FROM firearms WHERE id=2;
                   UPDATE journal SET shots_ms='818 820 822 819' WHERE id=1;
                   INSERT INTO journal (id,date,load_name,caliber,powder_id,charge_gr,firearm_id,shots_ms,shooter_id) VALUES (2,'2026-11-12','N550 41.5','6.5 Creedmoor',1,41.5,1,'817 821',1);");
        SqliteConnection.ClearAllPools();

        var rep = ArchiveBuilder.Refresh(a, src);
        Assert.True(rep.Ok, rep.Text());
        Assert.Equal(compId, (long)a.Scalar("SELECT id FROM components")!);                 // updated in place, same id
        Assert.Equal(940.0, a.Scalar("SELECT qty_current FROM components"));
        Assert.Equal(1L, a.Count("ledger"));
        Assert.Equal(1L, a.Count("firearms", "retired=1 AND notes LIKE '%eliminato nel vecchio archivio%'"));
        Assert.Equal(2L, a.Count("firearms"));                                              // archived, not deleted
        Assert.Equal(sessionId, (long)a.Scalar("SELECT id FROM sessions WHERE origin='reloading_log.journal#1'")!);
        Assert.Equal(4L, (long)a.Scalar("SELECT COUNT(*) FROM shots WHERE string_id IN (SELECT id FROM strings WHERE origin='reloading_log.journal#1')")!);
        Assert.Equal(1L, a.Count("strings", "origin='reloading_log.journal#1'"));          // replaced, not doubled
        Assert.Equal(1L, a.Count("sessions", "origin='reloading_log.journal#2'"));
        Assert.Equal(1L, a.Count("shooters"));
        // the chronograph session belongs to the archive alone: untouched
        Assert.Equal(1L, a.Count("sessions", $"id={chrono.SessionId}"));
        Assert.Equal(2L, (long)a.Scalar($"SELECT COUNT(*) FROM shots WHERE string_id IN (SELECT id FROM strings WHERE session_id={chrono.SessionId})")!);

        // a second refresh with nothing new changes nothing
        long before = a.Count("sessions") + a.Count("strings") + a.Count("shots") + a.Count("firearms");
        Assert.True(ArchiveBuilder.Refresh(a, src).Ok);
        Assert.Equal(before, a.Count("sessions") + a.Count("strings") + a.Count("shots") + a.Count("firearms"));
    }

    [Fact]
    public void AMissingOldDatabaseArchivesNothing()
    {
        string log = OldLog();
        using var a = new ArchiveDb(Path.Combine(_dir, "archivio.db"));
        ArchiveBuilder.Build(a, new ArchiveSources { LogDb = log });
        var rep = ArchiveBuilder.Refresh(a, new ArchiveSources { LogDb = Path.Combine(_dir, "sparito.db") });
        Assert.Equal(0L, a.Count("firearms", "retired=1"));
        Assert.Equal(1L, a.Count("sessions"));
        Assert.Contains(rep.Log, l => l.Contains("non trovato"));
    }

    [Fact]
    public void AChronographSessionIsWrittenWholeAndMakesTheArchiveTheMaster()
    {
        using var a = new ArchiveDb(Path.Combine(_dir, "archivio.db"));
        long sh = ArchiveImports.ShooterFor(a, null);
        long load = ArchiveImports.NewLoad(a, sh, "Gara 6.5", "6.5 Creedmoor");
        Assert.Equal("false", a.GetMeta("master"));
        var strings = new[] { Str("41.7.xlsx", 41.7, 822, 824, 823), Str("41.5.xlsx", 41.5, 818, 820) };

        var r = ArchiveImports.AddChronoSession(a, sh, load, new DateTime(2026, 11, 15), "Tikka", strings, "Athlon");

        Assert.Equal((2, 5), (r.Strings, r.Shots));
        Assert.Empty(r.AlreadyInArchive);
        Assert.Equal("true", a.GetMeta("master"));
        Assert.Equal("2026-11-15", a.Scalar($"SELECT date FROM sessions WHERE id={r.SessionId}"));
        Assert.Equal(18.0, a.Scalar($"SELECT temp_c FROM sessions WHERE id={r.SessionId}"));    // every string at 18 °C
        Assert.Equal(load, (long)a.Scalar($"SELECT load_id FROM sessions WHERE id={r.SessionId}")!);
        Assert.Equal(41.5, a.Scalar($"SELECT charge_gr FROM strings WHERE session_id={r.SessionId} ORDER BY id LIMIT 1"));  // by charge
        Assert.Equal("ora 10:00:00", a.Scalar("SELECT note FROM shots ORDER BY id LIMIT 1"));
        Assert.Equal(2L, a.Count("files", "kind='export cronografo'"));
        // the same files again are recognised by their content
        Assert.Equal(2, ArchiveImports.AlreadyImported(a, strings).Count);
        var again = ArchiveImports.AddChronoSession(a, sh, load, new DateTime(2026, 11, 15), "", strings, "Athlon");
        Assert.Equal(2, again.AlreadyInArchive.Count);
        Assert.Equal(2L, a.Count("files", "kind='export cronografo'"));                        // files kept once
    }

    [Fact]
    public void ALoadOpenInGrtBecomesOneArchiveLoadWithItsComponents()
    {
        string path = Path.Combine(_dir, "carico.grtload");
        var doc = GrtLoadDoc.CreateMinimal("prova", path);
        doc.Save(path);
        using var a = new ArchiveDb(Path.Combine(_dir, "archivio.db"));
        long sh = ArchiveImports.ShooterFor(a, null);
        var log = new List<string>();

        long first = ArchiveImports.LoadForGrtFile(a, path, sh, log);
        long second = ArchiveImports.LoadForGrtFile(a, path, sh, log);
        // a toolkit snapshot of the same load lands on the same archive load
        string snapshot = Path.Combine(_dir, "carico_toolkit_20261116_1200.grtload");
        File.Copy(path, snapshot);
        long third = ArchiveImports.LoadForGrtFile(a, snapshot, sh, log);

        Assert.Equal(first, second);
        Assert.Equal(first, third);
        Assert.Equal(1L, a.Count("loads"));
        Assert.Equal(1L, a.Count("files", "kind='carico GRT'"));
        Assert.True(a.Count("lib_links", $"load_id={first}") > 0);
    }
}
