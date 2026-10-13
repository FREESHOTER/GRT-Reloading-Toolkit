namespace GrtReloadingToolkit.Ui;

/// <summary>The five phases of the recommended path ("The recommended path" in the manual).</summary>
internal enum PathPhase
{
    Preparation = 1,
    ChargeLadder = 2,
    SeatingDepth = 3,
    DistanceCheck = 4,
    Tracking = 5,
}

/// <summary>
/// How far along the recommended path the open load is, read from what is written in it. The same tool sits at more than
/// one place on the path (the chronograph import comes first for the charge ladder and again for the seating-depth test and
/// the check at distance), so which step is next depends on the phase, and the phase is a fact about the load, not about the
/// window. Pure logic: it takes the titles of the notes in the load, so the tests can run it on any machine.
///
/// <para>The rules, each from a note a tool of the toolkit writes: a "Distance Workflow" or "Group Analysis" note means the
/// check at distance has been done; a "Seating Depth Analysis" note means the seating-depth test has; a "Chosen Charge" note
/// means the charge has been picked; any measurement or an OCW / Chrono Statistics / Barrel Calibration note means the charge
/// ladder is under way; an empty load is at the start. The latest of these wins.</para>
/// </summary>
internal sealed record PathState(PathPhase Phase)
{
    /// <summary>What a window assumes before it has read the load: the charge ladder, which is where the path was wired first.</summary>
    public static readonly PathState Default = new(PathPhase.ChargeLadder);

    public static PathState From(IEnumerable<string> noteTitles, bool hasMeasurement)
    {
        var titles = noteTitles.Select(t => t.Trim()).ToList();
        bool Has(string prefix) => titles.Any(t => t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (Has("Distance Workflow") || Has("Group Analysis")) return new PathState(PathPhase.Tracking);
        if (Has("Seating Depth Analysis")) return new PathState(PathPhase.DistanceCheck);
        if (Has("Chosen Charge")) return new PathState(PathPhase.SeatingDepth);
        if (hasMeasurement || Has("OCW Analysis") || Has("Chrono Statistics") || Has("Barrel Calibration"))
            return new PathState(PathPhase.ChargeLadder);
        return new PathState(PathPhase.Preparation);
    }
}