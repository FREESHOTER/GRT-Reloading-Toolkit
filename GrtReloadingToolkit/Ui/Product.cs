namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The two products built from this code. The <b>plugin</b> ("GRT Reloading Toolkit") is started by GRT and works on the loads open in GRT. The
/// <b>program</b> ("Reload Lab") is started on its own and keeps the shooter's own archive: the cartridges, the training and the race, the readings of the
/// shots and the component library are its. Same source, two builds: the program is built with <c>-p:Product=Lab</c>, which defines <c>RELOAD_LAB</c>.
/// </summary>
internal static class Product
{
    public const string PluginName = "GRT Reloading Toolkit";
    public const string LabName = "Reload Lab";

#if RELOAD_LAB
    public static bool IsLab => true;
#else
    public static bool IsLab => false;
#endif

    /// <summary>The name the running build goes by.</summary>
    public static string Name => IsLab ? LabName : PluginName;

    /// <summary>The tools that belong to the program only: the plugin does not offer them (they are about cartridges, lots and the match, not about the load open in GRT).</summary>
    public static bool LabOnly(Tool t) => t is Tool.Cartridges or Tool.TrainingCartridges or Tool.RaceCartridges or Tool.TrainingStats
        or Tool.TrainingBudget or Tool.GroupReading or Tool.MatchReading or Tool.Library;

    /// <summary>Whether the running build offers this tool.</summary>
    public static bool Offers(Tool t) => IsLab || !LabOnly(t);
}
