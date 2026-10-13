namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The recommended path through the toolkit, as data: for each tool, the tool that normally comes next (the schema in the
/// manual, "The recommended path"), given how far along the open load is (<see cref="PathState"/>). Keys are the
/// <c>Tool</c> enum names, so this file needs no WinForms and the path can be checked by the tests on any machine. Only the
/// steps that really have a successor in the program are here: a visit to the range is not a tool, so the seating-depth
/// analyzer and the charge ladder analyzer's chosen charge lead to the range and the next import picks the path up again.
/// </summary>
internal static class ToolPathMap
{
    /// <summary>The tool that follows <paramref name="tool"/> on the path, or null when the guidance stops there.
    /// Without a state the load is taken to be on the charge ladder, where the path was wired first.</summary>
    public static string? After(string tool, PathState? state = null)
    {
        PathPhase phase = (state ?? PathState.Default).Phase;
        return tool switch
        {
            // 1 prepare the cases, then the card and the target for the ladder
            "Brass" => phase == PathPhase.Preparation ? "Label" : null,
            "Label" => phase == PathPhase.Preparation ? "TargetPrint" : null,
            // the target goes to the range; what comes back is imported
            "TargetPrint" => "Athlon",
            // 4 import: the same window, a different next step in each phase
            "Athlon" or "ShotMarker" => phase switch
            {
                PathPhase.SeatingDepth => "Seating",
                PathPhase.DistanceCheck or PathPhase.Tracking => "GroupAnalysis",
                _ => "ChronoStats",
            },
            "ChronoStats" => phase switch
            {
                PathPhase.SeatingDepth => "Seating",
                PathPhase.DistanceCheck or PathPhase.Tracking => "GroupAnalysis",
                _ => "Ocw",
            },
            "Ocw" => "Cal",                       // 6 ladder / OCW -> 7 barrel calibration
            "Cal" => "Journal",                   // 7 calibration -> 8 log the chosen charge in the Journal
            // 8 the Journal, once the charge is chosen, leads to the seating-depth target; at the end it leads to the card
            "Journal" => phase switch
            {
                PathPhase.SeatingDepth => "TargetPrint",
                PathPhase.DistanceCheck or PathPhase.Tracking => "Label",
                _ => null,
            },
            "GroupAnalysis" => "Workflow",        // 15 group analysis -> 16 distance workflow
            "Workflow" => "Journal",              // 16 distance workflow -> 18 the Journal, one line per outing
            _ => null,
        };
    }

    /// <summary>
    /// What the next tool needs besides what this one produced, when that is not obvious: the Ladder / OCW analyzer reads
    /// the velocities AND where the shots hit, and the statistics window has only produced the first. The English text is
    /// the translation key. Null when there is nothing to add.
    /// </summary>
    public static string? Hint(string tool) => tool switch
    {
        "ChronoStats" => "The analysis also needs the hit coordinates (OnTarget export, ShotMarker or a GRT Shot-group tab).",
        "Ocw" => "To go on, pick the charge: select its row and press \"Choose this charge\".",
        "Cal" => "Log the chosen charge in the Journal (Journal > Log from GRT): the leaderboard and the history of the load read from there.",
        _ => null,
    };

    /// <summary>Every tool that has a successor in at least one phase.</summary>
    public static IReadOnlyCollection<string> Sources { get; } =
        new[] { "Brass", "Label", "TargetPrint", "Athlon", "ShotMarker", "ChronoStats", "Ocw", "Cal", "Journal", "GroupAnalysis", "Workflow" };
}