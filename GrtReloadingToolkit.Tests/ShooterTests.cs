using GrtReloadingToolkit.Log;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>The shooter reloads for the club too: each person has their own inventory, firearms, journal and followed loads.</summary>
public sealed class ShooterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "shooter-" + Guid.NewGuid().ToString("N"));

    public ShooterTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private Db NewDb(string name = "log.db") => new(Path.Combine(_dir, name));

    private static Component Powder(string name) => new() { Kind = ComponentKind.Powder, Name = name, Unit = "g", QtyInitial = 500, QtyCurrent = 500 };

    [Fact]
    public void ANewDatabaseHasTheFirstShooterAndNoFilter()
    {
        using var db = NewDb();
        var first = Assert.Single(db.Shooters());
        Assert.Equal(1, first.Id);
        Assert.Null(db.CurrentShooterId);
        Assert.Equal(1, db.DefaultShooterId);
    }

    [Fact]
    public void AnOlderDatabaseOpensWithEverythingBelongingToTheFirstShooter()
    {
        string path = Path.Combine(_dir, "old.db");
        using (var cn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE components (id INTEGER PRIMARY KEY AUTOINCREMENT, kind TEXT NOT NULL, brand TEXT DEFAULT '', name TEXT NOT NULL, lot TEXT DEFAULT '',
  unit TEXT NOT NULL, qty_initial REAL NOT NULL, qty_current REAL NOT NULL, cost_total REAL DEFAULT 0, currency TEXT DEFAULT 'EUR', expected_uses INTEGER DEFAULT 1,
  bullet_weight_gr REAL, twist_mm REAL, barrel_length_mm REAL, notes TEXT DEFAULT '', acquired_at TEXT DEFAULT (datetime('now')), archived INTEGER DEFAULT 0);
CREATE TABLE journal (id INTEGER PRIMARY KEY AUTOINCREMENT, date TEXT NOT NULL, load_name TEXT DEFAULT '', caliber TEXT DEFAULT '', firearm TEXT DEFAULT '',
  powder_id INTEGER, primer_id INTEGER, brass_id INTEGER, bullet_id INTEGER, charge_gr REAL DEFAULT 0, coal_mm REAL, cbto_mm REAL, rounds INTEGER DEFAULT 0,
  velocity_avg_ms REAL, sd_ms REAL, es_ms REAL, group_moa REAL, distance_m REAL, notes TEXT DEFAULT '', grtload_path TEXT DEFAULT '',
  created_at TEXT DEFAULT (datetime('now')), stock_applied INTEGER DEFAULT 0);
CREATE TABLE firearms (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, caliber TEXT DEFAULT '', notes TEXT DEFAULT '', rounds_before INTEGER DEFAULT 0,
  retired INTEGER DEFAULT 0, created_at TEXT DEFAULT (datetime('now')));
INSERT INTO components(kind,name,unit,qty_initial,qty_current) VALUES ('Powder','N150','g',500,400);
INSERT INTO journal(date,load_name,caliber,rounds) VALUES ('2026-01-01','old load','.308',10);
INSERT INTO firearms(name,caliber) VALUES ('Old rifle','.308');";
            cmd.ExecuteNonQuery();
        }

        using var db = new Db(path);
        Assert.Equal(1, Assert.Single(db.Components()).ShooterId);
        Assert.Equal(1, Assert.Single(db.Journal()).ShooterId);
        Assert.Equal(1, Assert.Single(db.Firearms()).ShooterId);
        Assert.Single(db.Shooters());
    }

    [Fact]
    public void ReopeningDoesNotDuplicateTheFirstShooterOrChangeOwners()
    {
        string path = Path.Combine(_dir, "again.db");
        long mario;
        using (var db = NewDb("again.db"))
        {
            mario = db.UpsertShooter(new Shooter { Name = "Mario" });
            db.CurrentShooterId = mario;
            db.UpsertComponent(Powder("Mario's powder"));
        }
        using var again = new Db(path);
        Assert.Equal(2, again.Shooters().Count);
        Assert.Equal(mario, Assert.Single(again.Components(includeArchived: true)).ShooterId);
    }

    [Fact]
    public void TheFilterShowsOnlyThePersonsRowsAndEverybodyShowsAll()
    {
        using var db = NewDb();
        long mario = db.UpsertShooter(new Shooter { Name = "Mario" });

        db.CurrentShooterId = 1;
        db.UpsertComponent(Powder("mine"));
        db.UpsertFirearm(new Firearm { Name = "my rifle", Caliber = ".308" });
        db.SaveEntry(new JournalEntry { LoadName = "my load", Caliber = ".308" }, false);

        db.CurrentShooterId = mario;
        db.UpsertComponent(Powder("his"));
        db.UpsertFirearm(new Firearm { Name = "his rifle", Caliber = ".308" });
        db.SaveEntry(new JournalEntry { LoadName = "his load", Caliber = ".308" }, false);

        Assert.Equal("his", Assert.Single(db.Components()).Name);
        Assert.Equal("his rifle", Assert.Single(db.Firearms()).Name);
        Assert.Equal("his load", Assert.Single(db.Journal()).LoadName);

        db.CurrentShooterId = 1;
        Assert.Equal("mine", Assert.Single(db.Components()).Name);
        Assert.Equal("my load", Assert.Single(db.Journal()).LoadName);

        db.CurrentShooterId = null;
        Assert.Equal(2, db.Components().Count);
        Assert.Equal(2, db.Firearms().Count);
        Assert.Equal(2, db.Journal().Count);
    }

    [Fact]
    public void WhileEverybodyIsShownNewRowsGoToTheFirstShooter()
    {
        using var db = NewDb();
        db.UpsertShooter(new Shooter { Name = "Mario" });
        long id = db.UpsertComponent(Powder("new"));
        Assert.Equal(1, db.Component(id)!.ShooterId);
    }

    [Fact]
    public void AJournalEntryTakesItsFirearmsOwnerWhenItDoesNotSayWhoseItIs()
    {
        using var db = NewDb();
        long mario = db.UpsertShooter(new Shooter { Name = "Mario" });
        long rifle = db.UpsertFirearm(new Firearm { Name = "his rifle", Caliber = ".308", ShooterId = mario });

        db.CurrentShooterId = null;   // the Journal opened on everybody
        long id = db.SaveEntry(new JournalEntry { LoadName = "x", Caliber = ".308", FirearmId = rifle }, false);

        db.CurrentShooterId = mario;
        Assert.Equal(id, Assert.Single(db.Journal()).Id);
    }

    [Fact]
    public void EditingARowKeepsItsOwnerEvenWhenAnotherPersonIsOnScreen()
    {
        using var db = NewDb();
        long mario = db.UpsertShooter(new Shooter { Name = "Mario" });
        db.CurrentShooterId = mario;
        long id = db.UpsertComponent(Powder("his"));

        db.CurrentShooterId = null;
        var c = db.Component(id)!;
        c.Notes = "edited";
        db.UpsertComponent(c);

        Assert.Equal(mario, db.Component(id)!.ShooterId);
    }

    [Fact]
    public void FirearmLookupByNameStaysWithinThePerson()
    {
        using var db = NewDb();
        long mario = db.UpsertShooter(new Shooter { Name = "Mario" });
        db.CurrentShooterId = 1;
        long mine = db.ResolveFirearm("Tikka", ".308");
        db.CurrentShooterId = mario;
        long his = db.ResolveFirearm("Tikka", ".308");

        Assert.NotEqual(mine, his);
        Assert.Equal(his, db.ResolveFirearm("tikka", ".308"));
    }

    [Fact]
    public void SearchesForCalibratedLoadsStayWithinThePerson()
    {
        using var db = NewDb();
        long mario = db.UpsertShooter(new Shooter { Name = "Mario" });
        db.CurrentShooterId = 1;
        db.SaveEntry(new JournalEntry { LoadName = "mine", Caliber = ".308", Ba = 0.8, ChargeGr = 40, TemperatureC = 20, VelocityAvgMs = 800 }, false);
        db.CurrentShooterId = mario;
        db.SaveEntry(new JournalEntry { LoadName = "his", Caliber = "6.5 CM", Ba = 0.9, ChargeGr = 41, TemperatureC = 21, VelocityAvgMs = 820 }, false);

        Assert.Equal(new[] { "6.5 CM" }, db.CalibratedCalibers());
        Assert.Equal(new[] { "6.5 CM" }, db.VelocityModelCalibers());
        Assert.Empty(db.FindCalibrations(".308", null, null));
        Assert.Single(db.FindCalibrations("6.5 CM", null, null));

        db.CurrentShooterId = null;
        Assert.Equal(2, db.CalibratedCalibers().Count);
    }

    [Fact]
    public void AssigningMovesRowsToAnotherPerson()
    {
        using var db = NewDb();
        long mario = db.UpsertShooter(new Shooter { Name = "Mario" });
        long a = db.SaveEntry(new JournalEntry { LoadName = "a" }, false);
        long b = db.SaveEntry(new JournalEntry { LoadName = "b" }, false);
        db.AssignShooter("journal", new[] { a }, mario);

        db.CurrentShooterId = mario;
        Assert.Equal("a", Assert.Single(db.Journal()).LoadName);
        db.CurrentShooterId = 1;
        Assert.Equal(b, Assert.Single(db.Journal()).Id);
        Assert.Equal((0, 0, 1), db.ShooterCounts(mario));
        Assert.Throws<ArgumentException>(() => db.AssignShooter("ledger; DROP TABLE journal", new[] { a }, mario));
    }

    [Fact]
    public void AnArchivedPersonIsHiddenButKeepsTheirRows()
    {
        using var db = NewDb();
        long mario = db.UpsertShooter(new Shooter { Name = "Mario" });
        db.CurrentShooterId = mario;
        db.UpsertComponent(Powder("his"));
        var s = db.Shooters().Single(x => x.Id == mario);
        s.Archived = true;
        db.UpsertShooter(s);

        Assert.DoesNotContain(db.Shooters(), x => x.Id == mario);
        Assert.Contains(db.Shooters(includeArchived: true), x => x.Id == mario);
        Assert.Equal((1, 0, 0), db.ShooterCounts(mario));
    }

    [Fact]
    public void AShooterNeedsAName()
    {
        using var db = NewDb();
        Assert.Throws<ArgumentException>(() => db.UpsertShooter(new Shooter { Name = "  " }));
    }

    [Fact]
    public void FollowedLoadsAreFilteredByPersonToo()
    {
        string path = Path.Combine(_dir, "tracker.db");
        using var t = new TrackerDb(path);
        t.CurrentShooterId = 1;
        t.SaveLoad(new TrackedLoad { Name = "mine", Caliber = ".308" });
        t.CurrentShooterId = 2;
        t.SaveLoad(new TrackedLoad { Name = "his", Caliber = ".308" });

        Assert.Equal("his", Assert.Single(t.Loads()).Name);
        t.CurrentShooterId = 1;
        Assert.Equal("mine", Assert.Single(t.Loads()).Name);
        t.CurrentShooterId = null;
        Assert.Equal(2, t.Loads().Count);
    }

    [Fact]
    public void AnOlderTrackerFileOpensWithItsLoadsBelongingToTheFirstShooter()
    {
        string path = Path.Combine(_dir, "oldtracker.db");
        using (var cn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString()))
        {
            cn.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"CREATE TABLE loads (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, caliber TEXT DEFAULT '', bullet TEXT DEFAULT '',
                powder TEXT DEFAULT '', charge_gr REAL DEFAULT 0, notes TEXT DEFAULT '', archived INTEGER DEFAULT 0, created_at TEXT DEFAULT (datetime('now')));
                INSERT INTO loads(name,caliber) VALUES ('old','.308');";
            cmd.ExecuteNonQuery();
        }
        using var t = new TrackerDb(path);
        Assert.Equal(1, Assert.Single(t.Loads()).ShooterId);
    }
}
