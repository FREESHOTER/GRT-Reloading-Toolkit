using System.Text.RegularExpressions;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The home page, the banner of each window, the launcher beside GRT and the code that opens the windows each keep their own list of tools. The catalog is the one the
/// home page and the banners read; these tests read the sources (there is no WinForms to instantiate here) so that a tool put in one list is not forgotten in the others.
/// </summary>
public sealed class ToolCatalogSyncTests
{
    private static string Src(string relative)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GrtReloadingToolkit", "MANUAL.md"))) dir = dir.Parent;
        Assert.True(dir is not null, "Could not find the repo root above " + AppContext.BaseDirectory);
        return File.ReadAllText(Path.Combine(dir!.FullName, "GrtReloadingToolkit", relative));
    }

    private static List<string> CatalogTools() => Regex.Matches(Src("Ui/ToolCatalog.cs"), @"new\(""[^""]+"", Tool\.(\w+),").Select(m => m.Groups[1].Value).ToList();

    [Fact]
    public void EveryToolOfTheCatalogHasAButtonInTheLauncherAndACaseThatOpensIt()
    {
        string launcher = Src("Ui/LauncherForm.cs"), program = Src("Program.cs");
        foreach (string t in CatalogTools().Where(t => t is not ("Wizard" or "GrtLoad" or "Reports")))
        {
            Assert.True(Regex.IsMatch(launcher, @"B\(Lang\.T\(""[^""]+""\), Tool\." + t + @"\b"), $"the launcher has no button for Tool.{t}");
            Assert.True(Regex.IsMatch(program, @"Tool\." + t + @" =>"), $"Program.Open has no case for Tool.{t}");
        }
    }

    [Fact]
    public void NoToolIsInTwoPhases()
    {
        var phases = Src("Ui/ToolCatalog.cs");
        int endOfPhases = phases.IndexOf("public static readonly ToolEntry[] More", StringComparison.Ordinal);
        var inPhases = Regex.Matches(phases[..endOfPhases], @"Tool\.(\w+),").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(inPhases.Distinct().Count(), inPhases.Count);                 // a tool can be in one phase only: the banner of its window names that phase
    }

    [Fact]
    public void TrainingAndRaceAreThePhasesAfterTheFiveOfTheLoad()
    {
        string catalog = Src("Ui/ToolCatalog.cs");
        var titles = Regex.Matches(catalog, @"^        \(""([^""]+)"", new ToolEntry\[\]", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(new[] { "Preparation", "Charge ladder", "Seating depth", "Check at distance", "Final load, over time", "Training", "Race" }, titles);
        // each of the two has its own analyses: the cartridges window on its tab and a reading of the shots
        Assert.Matches(@"""Training"", new ToolEntry\[\][^;]*TrainingCartridges[^;]*GroupReading", catalog);
        Assert.Matches(@"""Race"", new ToolEntry\[\][^;]*RaceCartridges[^;]*MatchReading", catalog);
    }

    [Fact]
    public void TheHomePageHasAColourForEveryPhase()
    {
        string catalog = Src("Ui/ToolCatalog.cs"), home = Src("Ui/HomeForm.cs");
        int phases = Regex.Matches(catalog, @"^        \(""[^""]+"", new ToolEntry\[\]", RegexOptions.Multiline).Count;
        string block = home[home.IndexOf("public static readonly Color[] Phase", StringComparison.Ordinal)..];
        block = block[..block.IndexOf("};", StringComparison.Ordinal)];
        Assert.Equal(phases, Regex.Matches(block, @"Color\.FromArgb").Count);
    }

    [Fact]
    public void TrainingAndRaceOpenTheCartridgesWindowOnTheirOwnTab()
    {
        string program = Src("Program.cs");
        Assert.Contains("Tool.TrainingCartridges => new CartridgesForm(Link()!, startTab: 2)", program);
        Assert.Contains("Tool.RaceCartridges => new CartridgesForm(Link()!, startTab: 3)", program);
        Assert.Contains("Tool.MatchReading => new GroupReadingForm(Link(), match: true)", program);
        // tab 2 is the Training tab and tab 3 the plan: the order the tabs are added in
        string form = Src("Ui/CartridgesForm.cs");
        Assert.Matches(@"AddRange\(new\[\] \{ piecesPage, cartPage, trainPage, predPage,", form);
    }
}
