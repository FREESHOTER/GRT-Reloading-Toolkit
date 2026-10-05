using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// "Find best Ba" picks its caliber out of a DropDownList that this query fills, so whatever this
/// returns is exactly what the user can choose from — and an empty return is a box they cannot
/// type into and cannot fill. These pin which entries count, because the answer is not "all of
/// them": a journal written before the Ba column existed has a caliber on every row and a Ba on
/// none, which is the shape that made the dropdown look broken.
/// </summary>
public sealed class FindBaFilterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("grt-findba-tests-").FullName;

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private Db NewDb() => new(Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".db"));

    private static JournalEntry Entry(string caliber, double? ba) => new()
    {
        Date = "2026-09-17",
        LoadName = "test",
        Caliber = caliber,
        ChargeGr = 40.0,
        Rounds = 10,
        Ba = ba,
    };

    [Fact]
    public void AnEntryWithoutABaOffersNoCaliberToSearch()
    {
        var db = NewDb();
        db.SaveEntry(Entry(".284 Shehane/KMR", null), applyStock: false);
        Assert.Empty(db.CalibratedCalibers());
    }

    [Fact]
    public void ABaOnTheEntryIsWhatPutsItsCaliberInTheList()
    {
        var db = NewDb();
        var e = Entry(".284 Shehane/KMR", null);
        long id = db.SaveEntry(e, applyStock: false);
        Assert.Empty(db.CalibratedCalibers());

        // The fix the user applies by hand: open the entry, type the Ba its .grtload already
        // carries. One field, and the caliber appears.
        e.Id = id;
        e.Ba = 0.4372495;
        db.SaveEntry(e, applyStock: false);
        Assert.Equal(new[] { ".284 Shehane/KMR" }, db.CalibratedCalibers());
    }

    [Fact]
    public void ACalibrationIsFoundBackUnderItsOwnCaliberOnly()
    {
        var db = NewDb();
        db.SaveEntry(Entry(".284 Shehane/KMR", 0.4372495), applyStock: false);
        db.SaveEntry(Entry("6.5 Creedmoor", 0.51), applyStock: false);

        Assert.Equal(new[] { ".284 Shehane/KMR", "6.5 Creedmoor" }, db.CalibratedCalibers());
        Assert.Single(db.FindCalibrations(".284 Shehane/KMR", null, null));
        Assert.Empty(db.FindCalibrations(".223 Rem", null, null));
    }

    [Fact]
    public void AnyPowderAndBulletReallyMeansAnyNotOnlyUnlinkedEntries()
    {
        var db = NewDb();
        var linked = Entry("6.5 Creedmoor", 0.48784); linked.PowderId = 1; linked.BulletId = 2;
        var other = Entry("6.5 Creedmoor", 0.5); other.PowderId = 3; other.BulletId = 2;
        var unlinked = Entry("6.5 Creedmoor", 0.5814);
        db.SaveEntry(linked, applyStock: false);
        db.SaveEntry(other, applyStock: false);
        db.SaveEntry(unlinked, applyStock: false);

        Assert.Equal(3, db.FindCalibrations("6.5 Creedmoor", null, null).Count);          // any
        Assert.Single(db.FindCalibrations("6.5 Creedmoor", 1, null));                      // that powder
        Assert.Single(db.FindCalibrations("6.5 Creedmoor", 1, 2));                         // that powder and bullet
        Assert.Equal(2, db.FindCalibrations("6.5 Creedmoor", null, 2).Count);              // that bullet, any powder
    }

    [Fact]
    public void TheIsentropicExponentIsStoredWithTheBaItWasCalibratedWith()
    {
        var db = NewDb();
        var e = Entry("6.5 Creedmoor", 0.48784);
        e.A0 = 1.3631; e.K = 1.2201123;
        db.SaveEntry(e, applyStock: false);

        var back = db.Journal().Single();
        Assert.Equal(1.2201123, back.K!.Value, 7);
        Assert.Equal(1.3631, back.A0!.Value, 4);
        Assert.Equal(1.2201123, db.FindCalibrations("6.5 Creedmoor", null, null).Single().K!.Value, 7);
    }
}
