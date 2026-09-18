using System.Globalization;
using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The backfill exists because a journal written before the ba column had a caliber on every row
/// and a Ba on none, which is what left "Find best Ba" with an empty, un-typeable caliber box (see
/// <see cref="FindBaFilterTests"/>). It reads the numbers back out of the .grtload each row already
/// names, so what has to hold is: it fills what it can, it leaves alone what it can't, it never
/// overwrites a Ba a person typed, and running it twice is the same as running it once.
/// </summary>
public sealed class BaBackfillTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("grt-backfill-tests-").FullName;

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private Db NewDb() => new(Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".db"));

    /// <summary>A .grtload the way Barrel Calibration leaves one: Ba (and a0) written into the propellant.</summary>
    private string WriteLoad(string name, double? ba, double? a0 = null)
    {
        string N(double d) => d.ToString("0.#########", CultureInfo.InvariantCulture);
        string inputs = (ba is { } b ? $"      <input name=\"Ba\" value=\"{N(b)}\" type=\"decimal\" descr=\"burn rate factor\" />\n" : "")
                      + (a0 is { } a ? $"      <input name=\"a0\" value=\"{N(a)}\" type=\"decimal\" descr=\"a0\" />\n" : "");
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\" ?>\n"
                   + "<GordonsReloadingTool>\n  <InnerBallistikInput>\n"
                   + "    <caliber>\n      <input name=\"CaliberName\" value=\"Test\" />\n    </caliber>\n"
                   + "    <propellant>\n      <input name=\"PropellantName\" value=\"Test\" />\n" + inputs + "    </propellant>\n"
                   + "    <appendix>\n    </appendix>\n  </InnerBallistikInput>\n</GordonsReloadingTool>\n";
        string path = Path.Combine(_dir, name + ".grtload");
        File.WriteAllText(path, xml);
        return path;
    }

    private static JournalEntry Entry(string caliber, string loadPath, double? ba = null) => new()
    {
        Date = "2026-09-11",
        LoadName = "test",
        Caliber = caliber,
        ChargeGr = 40.0,
        Rounds = 10,
        GrtloadPath = loadPath,
        Ba = ba,
    };

    /// <summary>The user's own case, end to end: one pre-Ba row, and the dropdown fills itself.</summary>
    [Fact]
    public void ABaLeftInTheLoadFileComesBackIntoTheEntry()
    {
        var db = NewDb();
        db.SaveEntry(Entry(".284 Shehane/KMR", WriteLoad("shehane", ba: 0.4372495, a0: 0.9)), applyStock: false);
        Assert.Empty(db.CalibratedCalibers());

        var scan = BaBackfill.Scan(db);
        var one = Assert.Single(scan);
        Assert.Equal(BaBackfill.Result.Found, one.Result);
        Assert.Equal(0.4372495, one.Ba!.Value, 9);
        Assert.Equal(0.9, one.A0!.Value, 9);

        // Scanning is a read: until Apply, the journal is exactly as it was.
        Assert.Empty(db.CalibratedCalibers());

        Assert.Equal(1, BaBackfill.Apply(db, scan));
        Assert.Equal(new[] { ".284 Shehane/KMR" }, db.CalibratedCalibers());
        Assert.Equal(0.4372495, Assert.Single(db.FindCalibrations(".284 Shehane/KMR", null, null)).Ba!.Value, 9);
    }

    /// <summary>Re-running must be a no-op, not a second write -- the whole point of the ba IS NULL guard.</summary>
    [Fact]
    public void RunningItAgainFindsNothingLeftToDo()
    {
        var db = NewDb();
        db.SaveEntry(Entry("6.5 Creedmoor", WriteLoad("creed", ba: 0.51)), applyStock: false);
        Assert.Equal(1, BaBackfill.Apply(db, BaBackfill.Scan(db)));

        Assert.Empty(BaBackfill.Scan(db));
        Assert.Equal(0, BaBackfill.Apply(db, BaBackfill.Scan(db)));
        Assert.Equal(0.51, Assert.Single(db.FindCalibrations("6.5 Creedmoor", null, null)).Ba!.Value, 9);
    }

    /// <summary>
    /// A Ba someone typed outranks whatever the load file says. The load here holds a different
    /// number on purpose: if the guard were missing, this test would read 0.62 back.
    /// </summary>
    [Fact]
    public void ABaAlreadyOnTheEntryIsNeverOverwritten()
    {
        var db = NewDb();
        string load = WriteLoad("typed", ba: 0.62);
        db.SaveEntry(Entry("308 Win", load, ba: 0.44), applyStock: false);

        Assert.Empty(BaBackfill.Scan(db));   // it isn't even a candidate
        Assert.Equal(0.44, Assert.Single(db.FindCalibrations("308 Win", null, null)).Ba!.Value, 9);

        // And even handed the row directly, the UPDATE refuses it.
        long id = db.FindCalibrations("308 Win", null, null)[0].Id;
        Assert.False(db.FillBa(id, 0.62, null));
        Assert.Equal(0.44, Assert.Single(db.FindCalibrations("308 Win", null, null)).Ba!.Value, 9);
    }

    /// <summary>
    /// An a0 that is already there survives too, while a missing one is taken from the load. They
    /// are separate columns and a user can have filled in either.
    /// </summary>
    [Fact]
    public void A0IsFilledInBesideBaWithoutDisturbingOne()
    {
        var db = NewDb();
        var e = Entry("6BR", WriteLoad("br", ba: 0.5, a0: 1.25));
        e.A0 = 2.5;
        db.SaveEntry(e, applyStock: false);

        Assert.Equal(1, BaBackfill.Apply(db, BaBackfill.Scan(db)));
        var back = Assert.Single(db.FindCalibrations("6BR", null, null));
        Assert.Equal(0.5, back.Ba!.Value, 9);
        Assert.Equal(2.5, back.A0!.Value, 9);
    }

    /// <summary>
    /// Every row it can't help is reported rather than dropped, and none of them is written. A
    /// moved file and an uncalibrated load are the two ordinary ways this goes, and a broken file
    /// must not stop the sweep reaching the good row behind it.
    /// </summary>
    [Fact]
    public void RowsItCannotHelpAreReportedAndLeftAlone()
    {
        var db = NewDb();
        string bad = Path.Combine(_dir, "truncated.grtload");
        File.WriteAllText(bad, "<?xml version=\"1.0\"?><GordonsReloadingTool><Inner");

        db.SaveEntry(Entry("no-ba", WriteLoad("plain", ba: null)), applyStock: false);
        db.SaveEntry(Entry("gone", Path.Combine(_dir, "deleted.grtload")), applyStock: false);
        db.SaveEntry(Entry("broken", bad), applyStock: false);
        db.SaveEntry(Entry("good", WriteLoad("good", ba: 0.47)), applyStock: false);

        var scan = BaBackfill.Scan(db);
        Assert.Equal(4, scan.Count);
        Assert.Equal(BaBackfill.Result.NoBaInLoad, scan.Single(c => c.Caliber == "no-ba").Result);
        Assert.Equal(BaBackfill.Result.FileMissing, scan.Single(c => c.Caliber == "gone").Result);
        Assert.Equal(BaBackfill.Result.Unreadable, scan.Single(c => c.Caliber == "broken").Result);
        Assert.NotNull(scan.Single(c => c.Caliber == "broken").Error);

        // The one good row is filled; the other three are still exactly as they were.
        Assert.Equal(1, BaBackfill.Apply(db, scan));
        Assert.Equal(new[] { "good" }, db.CalibratedCalibers());
        Assert.Equal(3, BaBackfill.Scan(db).Count);
    }

    /// <summary>An entry that names no load has nothing to be read back from, so it is not a candidate.</summary>
    [Fact]
    public void AnEntryWithNoLoadPathIsNotACandidate()
    {
        var db = NewDb();
        db.SaveEntry(Entry("hand-typed", ""), applyStock: false);
        Assert.Empty(BaBackfill.Scan(db));
    }
}
