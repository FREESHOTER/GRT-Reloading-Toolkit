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
}
