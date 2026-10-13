namespace GrtReloadingToolkit.Ui;

/// <summary>The English display name of a tool, the translation key the launcher and the bar share.</summary>
internal static class ToolNames
{
    public static string EnglishName(string tool) => tool switch
    {
        "Athlon" => "Chronograph import (Athlon / Garmin)",
        "ShotMarker" => "ShotMarker import",
        "ChronoStats" => "Chronograph statistics",
        "Ocw" => "Ladder / OCW analyzer",
        "Cal" => "Barrel calibration",
        "Journal" => "Load journal",
        _ => tool,
    };
}
