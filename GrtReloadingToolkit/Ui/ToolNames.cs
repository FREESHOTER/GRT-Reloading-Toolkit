namespace GrtReloadingToolkit.Ui;

/// <summary>The English display name of a tool, the translation key the launcher and the bar share.</summary>
internal static class ToolNames
{
    public static string EnglishName(string tool) => tool switch
    {
        "Brass" => "Brass prep (case vol / seating / neck)",
        "Label" => "Load card / label",
        "TargetPrint" => "Print ladder/OCW target",
        "Athlon" => "Chronograph import (Athlon / Garmin)",
        "ShotMarker" => "ShotMarker import",
        "ChronoStats" => "Chronograph statistics",
        "Ocw" => "Ladder / OCW analyzer",
        "Seating" => "Seating-depth analyzer",
        "Cal" => "Barrel calibration",
        "Journal" => "Load journal",
        "GroupAnalysis" => "Group analysis",
        "Workflow" => "Distance workflow",
        "Diagnosis" => "Guided diagnosis (experimental)",
        "FindBa" => "Find best Ba",
        "Temp" => "Powder temp coefficients",
        "PowderCompare" => "Powder compare",
        _ => tool,
    };
}