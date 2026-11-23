namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The two products built from this code. The <b>plugin</b> ("GRT Reloading Toolkit") is the Toolkit inside GRT, as it has always been: started by GRT, working
/// on the load open in GRT. The <b>program</b> ("Reloading Toolkit Desktop") is the same Toolkit as a separate program for whoever does not want it inside GRT: it
/// does everything the plugin does and adds the novelties (the shooter's own archive, the cartridges, training and race, the readings of the shots, the component
/// library, the home page). It needs no GRT; from GRT it takes only what it needs so that nothing is typed twice (a load's file, GRT's units, the folder of
/// powders). Same source, two builds: the program is built with <c>-p:Product=Desktop</c>, which defines <c>TOOLKIT_DESKTOP</c>.
/// </summary>
internal static class Product
{
    public const string PluginName = "GRT Reloading Toolkit";
    public const string DesktopName = "Reloading Toolkit Desktop";

#if TOOLKIT_DESKTOP
    public static bool IsDesktop => true;
#else
    public static bool IsDesktop => false;
#endif

    /// <summary>The name the running build goes by.</summary>
    public static string Name => IsDesktop ? DesktopName : PluginName;

    /// <summary>The novelties of the program: the plugin stays the Toolkit as it was.</summary>
    public static bool DesktopOnly(Tool t) => t is Tool.Cartridges or Tool.TrainingCartridges or Tool.RaceCartridges or Tool.TrainingStats
        or Tool.TrainingBudget or Tool.GroupReading or Tool.MatchReading or Tool.Library;

    /// <summary>What runs GRT's own simulation, which needs GRT open: the program cannot do it, so it stays in the plugin.</summary>
    public static bool PluginOnly(Tool t) => t is Tool.Cal or Tool.ModelProbe;

    /// <summary>Whether the running build offers this tool.</summary>
    public static bool Offers(Tool t) => IsDesktop ? !PluginOnly(t) : !DesktopOnly(t);
}
