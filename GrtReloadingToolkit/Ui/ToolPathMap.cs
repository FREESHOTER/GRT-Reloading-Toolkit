namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The recommended path through the toolkit, as data: for each tool, the tool that normally comes next (the schema in the
/// manual, "The recommended path"). Keys are the <c>Tool</c> enum names, so this file needs no WinForms and the
/// path can be checked by the tests on any machine. Only the steps that really have a successor in the program are here:
/// the range visits, and the ones the guidance does not reach yet, are not.
/// </summary>
internal static class ToolPathMap
{
    private static readonly Dictionary<string, string> Next = new(StringComparer.Ordinal)
    {
        ["Athlon"] = "ChronoStats",       // 4 import  -> 5 statistics
        ["ShotMarker"] = "ChronoStats",   // 4 import  -> 5 statistics
        ["ChronoStats"] = "Ocw",          // 5 statistics -> 6 ladder / OCW analyzer
        ["Ocw"] = "Cal",                  // 6 ladder / OCW -> 7 barrel calibration
        ["Cal"] = "Journal",              // 7 calibration -> 8 log the chosen charge in the Journal
    };

    /// <summary>The tool that follows <paramref name="tool"/> on the path, or null when the guidance stops there.</summary>
    public static string? After(string tool) => Next.TryGetValue(tool, out var n) ? n : null;

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

    /// <summary>Every tool with a successor.</summary>
    public static IReadOnlyCollection<string> Sources => Next.Keys;
}
