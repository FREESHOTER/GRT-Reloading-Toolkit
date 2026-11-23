using System.Text.RegularExpressions;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// Windows Forms needs the main thread to be a single-threaded apartment: without it the file and folder dialogs and the clipboard freeze or fail
/// ("Cross-thread" hang in the Windows log). The attribute once slipped from <c>Main</c> onto the function written above it, and three releases went
/// out with the program freezing on "Open file". The exe project is not referenced by the tests, so this reads its source.
/// </summary>
public class MainThreadTests
{
    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GrtReloadingToolkit", "Program.cs")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Could not find the repo root above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    [Fact]
    public void MainIsMarkedSingleThreadedApartmentAndNothingElseIs()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "GrtReloadingToolkit", "Program.cs"));

        // the attribute stands directly above Main (a comment on the same line is fine)
        Assert.Matches(new Regex(@"\[STAThread\][^\n]*\n\s*(private|internal|public)\s+static\s+(void|int)\s+Main\s*\(", RegexOptions.Multiline), src);
        // and it is used once: a second one means it was copied, or has moved to the wrong function
        Assert.Single(Regex.Matches(src, @"\[STAThread\]"));
    }
}
