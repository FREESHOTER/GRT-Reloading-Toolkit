using GrtPluginKit.Grt;

namespace GrtReloadingToolkit.Log;

/// <summary>
/// A note this toolkit writes is a snapshot of the measurements that existed when it was written.
/// Every tool starts from the newest toolkit-family file (<see cref="GrtLoadDoc.OpenForToolkitEdit"/>),
/// so such a note rides along into every later file -- including one made by importing NEW
/// velocities, where it then sits beside data it does not describe and reads as if it had just been
/// calculated. The Barrel Calibration note is the dangerous one: measured vs simulated velocity per
/// charge, laid out exactly like a current result.
///
/// Deleting it would lose the only copy (the toolkit keeps just the last two earlier versions of a
/// file), and leaving it would pass old numbers off as current -- so it is renamed, flagged at the
/// top of its own text and taken out of GRT's reports instead.
/// </summary>
public static class StaleNotes
{
    public const string BarrelCalibrationTitle = "Barrel Calibration";

    /// <summary>Must NOT start with <see cref="BarrelCalibrationTitle"/> -- a new calibration run
    /// replaces "Barrel Calibration*" notes and must leave this one alone.</summary>
    public const string OldBarrelCalibrationTitle = "OLD Barrel Calibration";

    /// <summary>
    /// Call just before new measurements are added to <paramref name="doc"/>. Returns how many
    /// calibration notes were flagged (0 if the load had none). At most one flagged note is ever kept:
    /// an earlier "OLD" one is dropped first, so repeated calibrate-then-import cycles don't pile up
    /// tabs in GRT -- only the note that is being flagged right now survives.
    /// </summary>
    public static int FlagBarrelCalibration(GrtLoadDoc doc, DateTime importedOn)
    {
        if (doc.FindNoteText(BarrelCalibrationTitle) is null) return 0;
        doc.RemoveByTitlePrefix(OldBarrelCalibrationTitle);
        string banner = $"*** OLD - NOT FOR THE CURRENT MEASUREMENTS ***\r\n"
                      + $"Written before new velocities were imported on {importedOn:yyyy-MM-dd}. The numbers below "
                      + "describe the PREVIOUS measurements, not the ones now in this load. Redo the calibration.";
        return doc.RetitleNotes(BarrelCalibrationTitle, OldBarrelCalibrationTitle, banner);
    }
}
