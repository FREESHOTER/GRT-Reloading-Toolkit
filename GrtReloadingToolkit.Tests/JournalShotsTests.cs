using GrtReloadingToolkit.Log;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>The journal entry keeps every shot of the string and the wind of the day (the journal's entry window fills the mean, SD and ES from them).</summary>
public sealed class JournalShotsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("grt-journal-shots-").FullName;

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private string NewPath() => Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".db");

    [Fact]
    public void AnEntryKeepsItsShotsAndWindThroughASaveAndAReload()
    {
        using var db = new Db(NewPath());
        var e = new JournalEntry { Date = "2026-03-01", LoadName = "x", Caliber = "6.5 Creedmoor", ChargeGr = 41.5, ShotsMs = "800.1,801.4, 799.8;802", WindMps = 3.2, WindDeg = 90, Rounds = 4 };
        db.SaveEntry(e, applyStock: false);

        var back = Assert.Single(db.Journal());
        Assert.Equal(new[] { 800.1, 801.4, 799.8, 802 }, back.Velocities());
        Assert.Equal(3.2, back.WindMps);
        Assert.Equal(90, back.WindDeg);
    }

    [Fact]
    public void ShotsThatAreNotPositiveNumbersAreIgnored()
        => Assert.Equal(new[] { 800.0, 801.5 }, JournalEntry.ParseShots("800, x, -3, 0, 801.5,,"));

    [Theory]
    [InlineData(800.0, true)]
    [InlineData(2650.0 * 0.3048, true)]       // 2650 ft/s
    [InlineData(5.0, false)]                  // a round count, or "800,5,801,2" split at every comma
    [InlineData(9000.0, false)]
    [InlineData(0.0, false)]
    public void OnlyAVelocityOfAShotIsPlausible(double mps, bool expected)
        => Assert.Equal(expected, JournalEntry.IsPlausibleShotMps(mps));

    [Fact]
    public void ACommaSplitListOfDecimalsYieldsSlipsThatThePlausibilityRuleCatches()
    {
        var parsed = JournalEntry.ParseShots("800,5,801,2");
        Assert.Equal(new[] { 800.0, 5, 801, 2 }, parsed);
        Assert.Equal(new[] { 800.0, 801 }, parsed.Where(JournalEntry.IsPlausibleShotMps));
    }

    [Fact]
    public void FormattedShotsReadBackTheSame()
        => Assert.Equal(new[] { 800.1, 799.85 }, JournalEntry.ParseShots(JournalEntry.FormatShots(new[] { 800.1, 799.85 })));

    [Fact]
    public void AnOldJournalWithoutTheNewColumnsIsMigratedAndKeepsItsRows()
    {
        string path = NewPath();
        using (var cn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"CREATE TABLE journal (id INTEGER PRIMARY KEY AUTOINCREMENT, date TEXT NOT NULL, load_name TEXT DEFAULT '', caliber TEXT DEFAULT '',
                firearm TEXT DEFAULT '', powder_id INTEGER, primer_id INTEGER, brass_id INTEGER, bullet_id INTEGER, charge_gr REAL DEFAULT 0, coal_mm REAL, cbto_mm REAL,
                rounds INTEGER DEFAULT 0, velocity_avg_ms REAL, sd_ms REAL, es_ms REAL, group_moa REAL, distance_m REAL, notes TEXT DEFAULT '', grtload_path TEXT DEFAULT '',
                created_at TEXT DEFAULT (datetime('now')), stock_applied INTEGER DEFAULT 0);
                INSERT INTO journal (date, load_name, caliber, charge_gr, rounds) VALUES ('2025-06-01', 'old', '.308', 43.0, 10);";
            cmd.ExecuteNonQuery();
        }
        using var db = new Db(path);
        var old = Assert.Single(db.Journal());
        Assert.Equal("old", old.LoadName);
        Assert.Empty(old.Velocities());
    }
}
