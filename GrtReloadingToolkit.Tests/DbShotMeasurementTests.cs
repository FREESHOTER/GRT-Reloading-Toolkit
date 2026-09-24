using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>SD Root-Cause's manual per-shot temperature store, keyed by (caliber, label, shot_index)
/// rather than by .grtload path -- see the class doc on <see cref="ShotMeasurement"/> for why.</summary>
public sealed class DbShotMeasurementTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("grt-db-tests-").FullName;

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private Db NewDb() => new(Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".db"));

    [Fact]
    public void SavingAndReadingBackRoundTripsByTheCompositeKey()
    {
        using Db db = NewDb();
        db.SaveShotTemperature("6.5 Creedmoor", "39.2 gr (Ladder 2026-09-22)", 0, 18.5);
        db.SaveShotTemperature("6.5 Creedmoor", "39.2 gr (Ladder 2026-09-22)", 1, 19.0);

        var rows = db.ShotTemperatures("6.5 Creedmoor", "39.2 gr (Ladder 2026-09-22)");

        Assert.Equal(2, rows.Count);
        Assert.Equal(0, rows[0].ShotIndex);
        Assert.Equal(18.5, rows[0].TemperatureC);
        Assert.Equal(1, rows[1].ShotIndex);
        Assert.Equal(19.0, rows[1].TemperatureC);
    }

    [Fact]
    public void SavingTheSameKeyTwiceUpdatesRatherThanDuplicating()
    {
        using Db db = NewDb();
        db.SaveShotTemperature("6.5 Creedmoor", "39.2 gr", 0, 18.5);
        db.SaveShotTemperature("6.5 Creedmoor", "39.2 gr", 0, 21.0); // re-measured, corrected value

        var rows = db.ShotTemperatures("6.5 Creedmoor", "39.2 gr");

        Assert.Single(rows);
        Assert.Equal(21.0, rows[0].TemperatureC);
    }

    [Fact]
    public void SavingNullClearsAPreviouslyEnteredValueWithoutDeletingTheRow()
    {
        using Db db = NewDb();
        db.SaveShotTemperature("6.5 Creedmoor", "39.2 gr", 0, 18.5);
        db.SaveShotTemperature("6.5 Creedmoor", "39.2 gr", 0, null);

        var rows = db.ShotTemperatures("6.5 Creedmoor", "39.2 gr");

        Assert.Single(rows);
        Assert.Null(rows[0].TemperatureC);
    }

    [Fact]
    public void DifferentLabelsOrCalibersDoNotCollide()
    {
        using Db db = NewDb();
        db.SaveShotTemperature("6.5 Creedmoor", "39.2 gr", 0, 18.5);
        db.SaveShotTemperature("6.5 Creedmoor", "40.0 gr", 0, 22.0);
        db.SaveShotTemperature(".223 Rem", "39.2 gr", 0, 25.0);

        Assert.Equal(18.5, Assert.Single(db.ShotTemperatures("6.5 Creedmoor", "39.2 gr")).TemperatureC);
        Assert.Equal(22.0, Assert.Single(db.ShotTemperatures("6.5 Creedmoor", "40.0 gr")).TemperatureC);
        Assert.Equal(25.0, Assert.Single(db.ShotTemperatures(".223 Rem", "39.2 gr")).TemperatureC);
    }

    [Fact]
    public void ReopeningAnOlderDatabaseAddsTheTableAdditivelyWithoutTouchingExistingData()
    {
        string path = Path.Combine(_dir, "existing.db");
        long componentId;
        using (var db = new Db(path))
            componentId = db.UpsertComponent(new Component { Kind = ComponentKind.Powder, Name = "N550", Unit = "g", QtyInitial = 500, QtyCurrent = 500 });

        // Re-opening runs Migrate() again -- the new table must appear without disturbing what a
        // pre-existing installation already had.
        using (var db2 = new Db(path))
        {
            Assert.NotNull(db2.Component(componentId));
            db2.SaveShotTemperature("6.5 Creedmoor", "39.2 gr", 0, 18.5);
            Assert.Single(db2.ShotTemperatures("6.5 Creedmoor", "39.2 gr"));
        }
    }
}
