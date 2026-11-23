using System.Security.Cryptography;
using GrtPluginKit.Grt;
using GrtReloadingToolkit.Archive;
using GrtReloadingToolkit.Recovery;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrtReloadingToolkit.Tests;

public sealed class ArchiveTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "archivio_test_" + Guid.NewGuid().ToString("N"));

    public ArchiveTests() => Directory.CreateDirectory(_dir);

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

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    /// <summary>An old-style toolkit database and an old segui_carico.db (no shooter column yet), as on a real install.</summary>
    private (string Log, string Tracker) OldDatabases()
    {
        string log = Path.Combine(_dir, "reloading_log.db");
        Sql(log, @"
CREATE TABLE shooters (id INTEGER PRIMARY KEY, name TEXT, notes TEXT DEFAULT '', archived INTEGER DEFAULT 0);
INSERT INTO shooters VALUES (1,'Io','',0),(2,'Mario','',0);
CREATE TABLE firearms (id INTEGER PRIMARY KEY, name TEXT, caliber TEXT, notes TEXT, rounds_before INTEGER, retired INTEGER, created_at TEXT, rounds_limit INTEGER, shooter_id INTEGER);
INSERT INTO firearms VALUES (1,'Tikka','6.5 Creedmoor','',250,0,'2026-09-18',NULL,1);
CREATE TABLE components (id INTEGER PRIMARY KEY, kind TEXT, brand TEXT, name TEXT, lot TEXT, unit TEXT, qty_initial REAL, qty_current REAL,
  cost_total REAL, currency TEXT, expected_uses INTEGER, bullet_weight_gr REAL, notes TEXT, acquired_at TEXT, archived INTEGER, shooter_id INTEGER);
INSERT INTO components VALUES (1,'Powder','Vihtavuori','N550','L1','g',1000,940.5,80,'EUR',1,NULL,'','2026-09-01',0,1),
                              (2,'Bullet','Lapua','Scenar 139gr','','pcs',500,480,200,'EUR',1,139,'','2026-09-01',0,1);
CREATE TABLE ledger (id INTEGER PRIMARY KEY, component_id INTEGER, delta REAL, reason TEXT, at TEXT);
INSERT INTO ledger VALUES (1,1,-59.5,'caricati 20','2026-09-02'),(2,2,-20,'caricati 20','2026-09-02'),(3,99,-1,'orfano','2026-09-03');
CREATE TABLE journal (id INTEGER PRIMARY KEY, date TEXT, load_name TEXT, caliber TEXT, firearm TEXT, powder_id INTEGER, primer_id INTEGER,
  brass_id INTEGER, bullet_id INTEGER, charge_gr REAL, coal_mm REAL, cbto_mm REAL, rounds INTEGER, velocity_avg_ms REAL, sd_ms REAL, es_ms REAL,
  group_moa REAL, distance_m REAL, notes TEXT, grtload_path TEXT, created_at TEXT, firearm_id INTEGER, temperature_c REAL, shots_ms TEXT, shooter_id INTEGER);
INSERT INTO journal (id,date,load_name,caliber,powder_id,bullet_id,charge_gr,rounds,velocity_avg_ms,sd_ms,es_ms,group_moa,distance_m,firearm_id,temperature_c,shots_ms,shooter_id)
  VALUES (1,'2026-10-01','N550 41.5','6.5 Creedmoor',1,2,41.5,5,820,3,8,0.6,100,1,18,'818,820.5,823,817.5,821',1),
         (2,'2026-10-08','N550 41.5','6.5 Creedmoor',1,2,41.5,3,NULL,NULL,NULL,NULL,300,1,12,'',2);
CREATE TABLE case_head_measurements (id INTEGER PRIMARY KEY, date TEXT, firearm_id INTEGER, powder_id INTEGER, charge_gr REAL, head_diameter_mm REAL, notes TEXT, created_at TEXT);
INSERT INTO case_head_measurements VALUES (1,'2026-10-01',1,1,41.5,11.93,'',NULL);");

        string trk = Path.Combine(_dir, "segui_carico.db");
        Sql(trk, @"
CREATE TABLE loads (id INTEGER PRIMARY KEY, name TEXT, caliber TEXT, bullet TEXT, powder TEXT, charge_gr REAL, notes TEXT, archived INTEGER, created_at TEXT);
INSERT INTO loads VALUES (1,'Gara','6.5 Creedmoor','Scenar 139','N550',41.5,'',0,'2026-10-01');
CREATE TABLE outings (id INTEGER PRIMARY KEY, load_id INTEGER, date TEXT, temp_c REAL, pressure_hpa REAL, humidity_pct REAL, shots_ms TEXT,
  group_moa REAL, distance_m REAL, wind_mps REAL, wind_deg REAL, firearm TEXT, powder_lot TEXT, primer_lot TEXT, seating_mm REAL,
  brass_firings INTEGER, barrel_rounds INTEGER, notes TEXT, created_at TEXT);
INSERT INTO outings (id,load_id,date,temp_c,shots_ms,powder_lot) VALUES (1,1,'2026-10-15',9,'815 816 818',' L2');");
        return (log, trk);
    }

    private string Recovered()
    {
        string root = Path.Combine(_dir, "grt");
        Directory.CreateDirectory(Path.Combine(root, "loads"));
        Directory.CreateDirectory(Path.Combine(root, "Fulvio"));
        void Load(string path, double[] v)
        {
            var doc = GrtLoadDoc.CreateMinimal("prova", path);
            var c = new GrtCharge { Name = "41.5", ValueKg = 41.5 * 0.00006479891 };
            foreach (double x in v) c.Shots.Add(new GrtShot(x, null));
            doc.AddMeasurement("Cronografo", new[] { c });
            doc.AddNote("Nota", "testo " + v[0]);
            doc.Save(path);
        }
        Load(Path.Combine(root, "loads", "a.grtload"), new[] { 820.0, 821.0, 819.5 });
        Load(Path.Combine(root, "loads", "a_toolkit_20260101_1200.grtload"), new[] { 820.0, 821.0, 819.5 });   // a copy
        Load(Path.Combine(root, "Fulvio", "b.grtload"), new[] { 750.0, 752.0 });
        string outDir = Path.Combine(_dir, "recupero");
        RecoveryWriter.Write(HistoryRecovery.Run(root), outDir);
        return outDir;
    }

    [Fact]
    public void BuildsEverythingFromTheSourcesAndChecksIt()
    {
        var (log, trk) = OldDatabases();
        string rec = Recovered();
        string hLog = Hash(log), hTrk = Hash(trk);

        using var a = new ArchiveDb(Path.Combine(_dir, "archivio.db"));
        var rep = ArchiveBuilder.Build(a, new ArchiveSources { LogDb = log, TrackerDb = trk, RecoveryFolders = { rec } });

        Assert.True(rep.Ok, rep.Text());
        Assert.Equal(3, a.Count("shooters"));                         // Io, Mario, Fulvio (from the GRT folder)
        Assert.Equal(2, a.Count("components"));
        Assert.Equal(2, a.Count("ledger"));                            // the orphan movement stays only as an original row
        Assert.Equal(940.5 + 480, Convert.ToDouble(a.Scalar("SELECT TOTAL(qty_current) FROM components")), 6);
        // journal: two sessions of one recipe (different shooters -> two loads), the tracker's load, two GRT files (the copy adds nothing)
        Assert.Equal(2 + 1 + 2, a.Count("loads"));
        Assert.Equal(2 + 1 + 2, a.Count("sessions"));
        Assert.Equal(5 + 3 + 3 + 2, a.Count("shots"));
        Assert.Equal(818 + 820.5 + 823 + 817.5 + 821 + 815 + 816 + 818 + 820 + 821 + 819.5 + 750 + 752,
            Convert.ToDouble(a.Scalar("SELECT TOTAL(velocity_mps) FROM shots")), 6);
        Assert.Equal(1, a.Count("measurements"));
        Assert.Equal(2, a.Count("notes"));
        Assert.Equal(2, a.Count("files", "kind='carico GRT'"));
        Assert.True(a.Count("legacy_rows") >= 2 + 1 + 2 + 3 + 2 + 1 + 1 + 1);
        Assert.Equal("false", a.GetMeta("master"));
        Assert.Contains("Registrato nel diario: media 820", a.Scalar("SELECT notes FROM sessions WHERE origin='reloading_log.journal#1'")?.ToString());
        Assert.Equal(1L, a.Scalar("SELECT date_estimated FROM sessions WHERE origin LIKE 'grt:%' LIMIT 1"));

        // the sources are read only
        SqliteConnection.ClearAllPools();
        Assert.Equal(hLog, Hash(log));
        Assert.Equal(hTrk, Hash(trk));
    }

    [Fact]
    public void RefusesToBuildIntoAnArchiveThatAlreadyHoldsData()
    {
        using var a = new ArchiveDb(Path.Combine(_dir, "archivio.db"));
        a.Insert("shooters", ("name", "Io"));
        Assert.Throws<InvalidOperationException>(() => ArchiveBuilder.Build(a, new ArchiveSources()));
    }

    [Fact]
    public void AMissingSourceIsReportedNotInvented()
    {
        using var a = new ArchiveDb(Path.Combine(_dir, "archivio.db"));
        var rep = ArchiveBuilder.Build(a, new ArchiveSources { LogDb = Path.Combine(_dir, "manca.db"), RecoveryFolders = { Path.Combine(_dir, "manca") } });
        Assert.True(rep.Ok, rep.Text());
        Assert.Contains(rep.Log, l => l.Contains("non trovato"));
        Assert.Equal(1, a.Count("shooters"));
        Assert.Equal(0, a.Count("loads"));
    }

    [Fact]
    public void BackupKeepsOnlyTheNewestCopiesAndEachOneIsWhole()
    {
        string db = Path.Combine(_dir, "archivio.db");
        using (var a = new ArchiveDb(db)) a.Insert("shooters", ("name", "Io"));
        SqliteConnection.ClearAllPools();
        string dest = Path.Combine(_dir, "copie");
        File.WriteAllText(Path.Combine(dest + "_altro.txt"), "non toccare");
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(dest, "mio_file.db"), "non è una copia: non va cancellato");
        var t0 = new DateTime(2026, 11, 1, 8, 0, 0);
        for (int i = 0; i < 5; i++) ArchiveBackup.Copy(db, dest, keep: 3, now: t0.AddHours(i));

        var list = ArchiveBackup.List(dest);
        Assert.Equal(3, list.Count);
        Assert.EndsWith("archivio_2026-11-01_120000.db", list[0]);
        Assert.True(File.Exists(Path.Combine(dest, "mio_file.db")));
        using var cn = new SqliteConnection($"Data Source={list[0]};Mode=ReadOnly;Pooling=False");
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT name FROM shooters";
        Assert.Equal("Io", cmd.ExecuteScalar());
    }

    [Fact]
    public void TwoPictureNamesNeverCollide()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.Equal("a.jpg", HistoryRecovery.UniqueName("a.jpg", used));
        Assert.Equal("a (2).jpg", HistoryRecovery.UniqueName("a.jpg", used));
        // the comparison ignores case (Windows file names do), the name keeps the spelling it was given
        Assert.Equal("A (3).JPG", HistoryRecovery.UniqueName("A.JPG", used));
    }
}
