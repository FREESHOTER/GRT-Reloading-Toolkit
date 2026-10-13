namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Which steps of the recommended path the open load already shows traces of, for the "done" mark in the launcher. A step
/// is done when the note its tool writes (or the measurement its import writes) is in the load. Steps that leave no trace in
/// the load (printing a target, the journal, which lives in the toolkit's own database) have no mark rather than a wrong one.
/// Pure logic, like <see cref="PathState"/>: it takes titles, so the tests can run it anywhere.
/// </summary>
internal static class PathProgress
{
    /// <summary>
    /// True when the load holds what <paramref name="tool"/> (a <c>Tool</c> enum name) writes. Titles are matched by their
    /// start and without case, so the "-1" GRT adds to a repeated title does not hide them.
    /// </summary>
    public static bool IsDone(string tool, IEnumerable<string> noteTitles, IEnumerable<string> measurementTitles)
    {
        var notes = noteTitles.Select(t => t.Trim()).ToList();
        var meas = measurementTitles.Select(t => t.Trim()).ToList();
        bool Note(string prefix) => notes.Any(t => t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        bool Meas(string prefix) => meas.Any(t => t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return tool switch
        {
            "Brass" => Note("Case Volume") || Note("Seating Depth (geometry)"),
            "Label" => Note("Load Sheet"),
            "Athlon" => Meas("Athlon "),
            "ShotMarker" => Meas("ShotMarker"),
            "ChronoStats" => Note("Chrono Statistics"),
            "Ocw" => Note("OCW Analysis"),
            "Cal" => Note("Barrel Calibration"),
            "Seating" => Note("Seating Depth Analysis"),
            "GroupAnalysis" => Note("Group Analysis"),
            "Workflow" => Note("Distance Workflow"),
            "SdRootCause" => Note("SD Root-Cause"),
            "Diagnostics" => Note("Advanced Diagnostics"),
            "Diagnosis" => Note("Guided Diagnosis"),
            _ => false,
        };
    }
}