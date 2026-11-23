namespace GrtReloadingToolkit.Ui;

/// <summary>A tool as the home page and the window banners show it: caption key (English, with the old emoji), icon, step.</summary>
internal sealed record ToolEntry(string Key, Tool Tool, string Icon, string? Step = null);

/// <summary>
/// Where every tool sits: the seven phases of the recommended path (the last two, the training and the race, are about cartridges and the match rather than about one load), the other analyses, the things always at hand. One list
/// for the home page and for the banner on top of each tool window, so the two never disagree.
/// </summary>
internal static class ToolCatalog
{
    public static readonly (string Title, ToolEntry[] Tools, string? Note)[] Phases =
    {
        ("Preparation", new ToolEntry[]
        {
            new("📥  Import a load from GRT", Tool.GrtLoad, "grtin"),
            new("🔧  Brass prep (case vol / seating / neck)", Tool.Brass, "brass", "1"),
            new("📦  Cartridges (measure, predict, plan)", Tool.Cartridges, "cartridge"),
            new("🏷  Load card / label", Tool.Label, "label", "2"),
            new("🎯🖨  Print ladder/OCW target", Tool.TargetPrint, "target", "2"),
        }, null),
        ("Charge ladder", new ToolEntry[]
        {
            new("🎯  Chronograph import (Athlon / Garmin)", Tool.Athlon, "stopwatch", "4"),
            new("🎯📄  ShotMarker import (target + velocities)", Tool.ShotMarker, "shotmarker", "4"),
            new("📐  Chronograph statistics", Tool.ChronoStats, "stats", "5"),
            new("📈  Ladder / OCW analyzer", Tool.Ocw, "ladder", "6"),
            new("🎚  Barrel calibration", Tool.Cal, "sliders", "7"),
        }, null),
        ("Seating depth", new ToolEntry[]
        {
            new("📏  Seating-depth analyzer", Tool.Seating, "ruler", "12"),
        }, "Steps 9-11 use the target and the imports above, with the chosen charge."),
        ("Check at distance", new ToolEntry[]
        {
            new("🎯📐  Group analysis", Tool.GroupAnalysis, "group", "15"),
            new("🧭  Distance workflow", Tool.Workflow, "compass", "16"),
            new("🌡🎯  SD root-cause", Tool.SdRootCause, "sdcause", "17"),
            new("🔬  Advanced diagnostics", Tool.Diagnostics, "pulse", "17"),
        }, null),
        ("Final load, over time", new ToolEntry[]
        {
            new("📓  Load journal", Tool.Journal, "journal", "8 / 18"),
            new("📈  Follow a load", Tool.FollowLoad, "follow", "20"),
        }, null),
        ("Training", new ToolEntry[]
        {
            new("🏋  Training cartridges (other lots, plan)", Tool.TrainingCartridges, "cartridge"),
            new("📐  Chronograph statistics (training)", Tool.TrainingStats, "stats"),
            new("🌡🎯  SD budget (training)", Tool.TrainingBudget, "sdcause"),
            new("🎯🔍  Group reading (shooter errors)", Tool.GroupReading, "group"),
        }, "Cartridges other than the match's: every shot linked to them teaches the model what moves the velocity, and the predictions for the race get better."),
        ("Race", new ToolEntry[]
        {
            new("🏁  Race plan and check (match lot)", Tool.RaceCartridges, "cartridge"),
            new("🎯🔍  Match reading (one shot per target)", Tool.MatchReading, "group"),
        }, "The lot kept for the match: the plan, the prediction frozen before the shooting, the check after, and the reading of every shot."),
    };

    public static readonly ToolEntry[] More =
    {
        new("🩺  Guided diagnosis", Tool.Diagnosis, "diagnose"),
        new("⚖️  Seating force estimate (QC)", Tool.SeatingForce, "force"),
        new("🔎  Find best Ba", Tool.FindBa, "findba"),
        new("🌡  Powder temp coefficients", Tool.Temp, "thermo"),
        new("🏆  Load leaderboard", Tool.Leaderboard, "trophy"),
        new("🧪  Powder compare", Tool.PowderCompare, "flask"),
        new("📉  Velocity model (charge + temperature)", Tool.VelocityModel, "curve"),
        new("🔬  Model probe (how GRT answers one input)", Tool.ModelProbe, "probe"),
        new("🌀  Gyroscopic stability (twist)", Tool.Stability, "gyro"),
    };

    public static readonly ToolEntry[] Always =
    {
        new("📏  Units of measure (grains/grams, mm/inches, m/yards, MOA/mrad)", Tool.Units, "ruler"),
        new("📒  Inventory", Tool.Log, "box"),
        new("📚  Component library", Tool.Library, "books"),
        new("📚  Load book export", Tool.LoadBook, "books"),
        new("📄  Install GRT report templates", Tool.Reports, "doc"),
    };

    public static readonly ToolEntry Wizard = new("🪄  Guided new load (wizard)", Tool.Wizard, "wizard");

    /// <summary>Phase index 0-4 on the path, -1 another analysis, -2 always at hand, -3 the wizard; null for a tool not listed.</summary>
    public static (ToolEntry Entry, int Group)? Find(Tool t)
    {
        for (int i = 0; i < Phases.Length; i++)
            if (Phases[i].Tools.FirstOrDefault(e => e.Tool == t) is { } e) return (e, i);
        if (More.FirstOrDefault(e => e.Tool == t) is { } m) return (m, -1);
        if (Always.FirstOrDefault(e => e.Tool == t) is { } a) return (a, -2);
        return t == Tool.Wizard ? (Wizard, -3) : null;
    }
}
