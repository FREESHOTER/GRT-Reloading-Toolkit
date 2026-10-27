using System.Text.RegularExpressions;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// Barrel calibration pairs a velocity read from GRT's tab with a charge read from the saved file, and its sweeps used
/// to sleep a fixed time per point hoping GRT had computed. The window is WinForms and lives in the Windows-only
/// project, so these hold the line on its source: every read of the user's tab is followed by the unsaved-changes
/// check, no fixed sleep is left, and the revolver note is in place with its translation.
/// </summary>
public class CalibrationGrtStateTests
{
    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GrtReloadingToolkit", "MANUAL.md")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Could not find the repo root above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string Src(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), "GrtReloadingToolkit", relative));

    [Fact]
    public void EveryReadOfTheUsersTabChecksForUnsavedChanges()
    {
        string src = Src("Ui/CalibrationForm.cs");
        int reads = Regex.Matches(src, @"var top = await _grt\.GetTabOnTopAsync\(\);").Count;
        int checks = Regex.Matches(src, @"if \(!await SavedOrConfirmedAsync\(top\.handle\)\) return( null)?;").Count;
        Assert.True(reads > 0, "CalibrationForm no longer reads the tab on top -- has it changed shape?");
        Assert.Equal(reads, checks);
    }

    [Fact]
    public void NoSweepPointSleepsAFixedTime()
    {
        string src = Src("Ui/CalibrationForm.cs");
        Assert.DoesNotMatch(new Regex(@"Task\.Delay\(\s*\d{3,}\s*\)"), src);
        Assert.DoesNotMatch(new Regex(@"LoadFileAsync\(tmp\)"), src);
        Assert.Matches(new Regex(@"LoadFileAndWaitForComputeAsync\(tmp, ComputeWait\)"), src);
    }

    [Fact]
    public void TheRevolverNoteIsShownAndTranslated()
    {
        string src = Src("Ui/CalibrationForm.cs");
        var m = Regex.Match(src, @"_revolverNote\.Text = Lang\.T\(""([^""]+)""\);");
        Assert.True(m.Success, "the revolver / cylinder-gap note is gone from the calibration window");
        Assert.Contains("Controls.Add(_revolverNote);", src);
        Assert.Contains("[\"" + m.Groups[1].Value + "\"] =", Src("Ui/Lang.cs"));
    }

    [Fact]
    public void TheNotesFollowTheirTextInsteadOfAFixedHeight()
    {
        string src = Src("Ui/CalibrationForm.cs");
        Assert.DoesNotMatch(new Regex(@"_(revolverNote|modelInfo)\.Height\s*=\s*\d{2,}"), src);
        Assert.Contains("FitHeight(_revolverNote);", src);
        Assert.Contains("FitHeight(_modelInfo);", src);
    }

    [Fact]
    public void TheManifestTurnsTheComputeEventOn()
    {
        string manifest = Src("plugin/com.grt.plugin.xml");
        Assert.Matches(new Regex(@"<event name=""TabComputed"" enabled=""true"" />"), manifest);
    }
}
