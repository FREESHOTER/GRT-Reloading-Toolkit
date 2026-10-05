using System.Xml.Linq;
using GrtPluginKit.Grt;
using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The user watched a "Barrel Calibration" tab appear straight after importing chronograph
/// velocities and took it for a fresh calculation. It was not: the toolkit starts every write from
/// the newest file it made, so yesterday's calibration note rode along into the new file, next to
/// today's measurements it does not describe. These tests pin the fix -- the old note is flagged,
/// never deleted (the toolkit keeps only the last two earlier files, so deleting could lose the
/// only copy) and never left looking current.
/// </summary>
public sealed class StaleNotesTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("grt-stalenotes-tests-").FullName;

    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    private const string CalText = "Barrel Calibration 2026-10-01\r\n24.46 836.0 835.1 +0.9\r\nmean offset: -0.2 m/s";

    private static GrtLoadDoc DocWithCalibration()
    {
        var doc = GrtLoadDoc.CreateMinimal("Test", "test.grtload");
        doc.AddNote("Nota", "my own note");
        doc.AddNote(StaleNotes.BarrelCalibrationTitle, CalText);
        return doc;
    }

    [Fact]
    public void TheOldCalibrationIsRenamedAndKeepsItsNumbers()
    {
        var doc = DocWithCalibration();

        Assert.Equal(1, StaleNotes.FlagBarrelCalibration(doc, new DateTime(2026, 10, 2)));

        Assert.Null(doc.FindNoteText(StaleNotes.BarrelCalibrationTitle));
        string old = doc.FindNoteText(StaleNotes.OldBarrelCalibrationTitle)!;
        Assert.Contains("24.46 836.0 835.1", old);
        Assert.Contains("mean offset: -0.2", old);
    }

    [Fact]
    public void TheFlagIsAtTheTopAndNamesTheImportDate()
    {
        var doc = DocWithCalibration();

        StaleNotes.FlagBarrelCalibration(doc, new DateTime(2026, 10, 2));

        string old = doc.FindNoteText(StaleNotes.OldBarrelCalibrationTitle)!;
        Assert.StartsWith("*** OLD", old);
        Assert.Contains("2026-10-02", old);
        Assert.True(old.IndexOf("OLD", StringComparison.Ordinal) < old.IndexOf("24.46", StringComparison.Ordinal));
    }

    [Fact]
    public void ALoadWithNoCalibrationNoteIsLeftAlone()
    {
        var doc = GrtLoadDoc.CreateMinimal("Test", "test.grtload");
        doc.AddNote("Nota", "my own note");

        Assert.Equal(0, StaleNotes.FlagBarrelCalibration(doc, DateTime.Now));

        Assert.Equal("my own note", doc.FindNoteText("Nota"));
        Assert.Null(doc.FindNoteText(StaleNotes.OldBarrelCalibrationTitle));
    }

    [Fact]
    public void OtherNotesAreNotTouched()
    {
        var doc = DocWithCalibration();
        doc.AddNote("OCW Analysis", "node 40.0-40.4");

        StaleNotes.FlagBarrelCalibration(doc, DateTime.Now);

        Assert.Equal("my own note", doc.FindNoteText("Nota"));
        Assert.Equal("node 40.0-40.4", doc.FindNoteText("OCW Analysis"));
    }

    [Fact]
    public void ANewCalibrationRunNeverOverwritesTheFlaggedNote()
    {
        // Barrel Calibration's own write is RemoveByTitlePrefix("Barrel Calibration") + AddNote.
        // The flagged note must survive that, or the "nothing is lost" promise is empty.
        var doc = DocWithCalibration();
        StaleNotes.FlagBarrelCalibration(doc, DateTime.Now);

        doc.RemoveByTitlePrefix(StaleNotes.BarrelCalibrationTitle);
        doc.AddNote(StaleNotes.BarrelCalibrationTitle, "Barrel Calibration 2026-10-02 (fresh)");

        Assert.Contains("24.46 836.0", doc.FindNoteText(StaleNotes.OldBarrelCalibrationTitle));
        Assert.Equal("Barrel Calibration 2026-10-02 (fresh)", doc.FindNoteText(StaleNotes.BarrelCalibrationTitle));
    }

    [Fact]
    public void RepeatedCycleKeepsOnlyTheNewestFlaggedNote()
    {
        // calibrate, import, calibrate again, import again -- must not stack one tab per cycle.
        var doc = DocWithCalibration();
        StaleNotes.FlagBarrelCalibration(doc, new DateTime(2026, 10, 2));
        doc.AddNote(StaleNotes.BarrelCalibrationTitle, "second calibration");

        StaleNotes.FlagBarrelCalibration(doc, new DateTime(2026, 10, 3));

        string old = doc.FindNoteText(StaleNotes.OldBarrelCalibrationTitle)!;
        Assert.Contains("second calibration", old);
        Assert.DoesNotContain("24.46 836.0", old);
        Assert.Equal(1, doc.RemoveByTitlePrefix(StaleNotes.OldBarrelCalibrationTitle));
    }

    [Fact]
    public void TheFlaggedNoteIsTakenOutOfGrtsReports()
    {
        string path = Path.Combine(_dir, "a.grtload");
        var doc = DocWithCalibration();
        StaleNotes.FlagBarrelCalibration(doc, DateTime.Now);
        doc.Save(path);

        var note = XDocument.Load(path).Descendants("note")
            .Single(n => Uri.UnescapeDataString((string?)n.Attribute("title") ?? "") == StaleNotes.OldBarrelCalibrationTitle);

        Assert.Equal("false", (string?)note.Attribute("showinreport"));
    }

    [Fact]
    public void TheEarlierFileIsNeverModified_OnlyTheNewOneHasTheFlag()
    {
        // The real flow: Chrono Import opens the newest toolkit file, flags, adds the measurement and
        // writes a NEW sibling. The file it started from must still hold the unflagged note.
        string basePath = Path.Combine(_dir, "load.grtload");
        DocWithCalibration().Save(basePath);

        var doc = GrtLoadDoc.OpenForToolkitEdit(basePath);
        StaleNotes.FlagBarrelCalibration(doc, DateTime.Now);
        doc.AddMeasurement("Athlon Measurement 2026-10-02", new[] { new GrtCharge { Name = "24.5 gr" } });
        string sibling = doc.SaveSibling("athlon");

        Assert.NotEqual(basePath, sibling);
        var before = GrtLoadDoc.Load(basePath);
        Assert.Equal(CalText, before.FindNoteText(StaleNotes.BarrelCalibrationTitle));
        Assert.Null(before.FindNoteText(StaleNotes.OldBarrelCalibrationTitle));

        var after = GrtLoadDoc.Load(sibling);
        Assert.Null(after.FindNoteText(StaleNotes.BarrelCalibrationTitle));
        Assert.Contains("24.46 836.0", after.FindNoteText(StaleNotes.OldBarrelCalibrationTitle));
        Assert.Single(after.Measurements(), m => m.Title.StartsWith("Athlon Measurement"));
    }
}
