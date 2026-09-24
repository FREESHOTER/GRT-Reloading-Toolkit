using System.Text.RegularExpressions;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// docs/GRT-Reloading-Toolkit.html is the overview page — the one a new user reads before they
/// install anything. It is hand-written like the manuals, but unlike the manuals nothing was
/// watching it: it still said "The eight tools" and listed eight after Chronograph Statistics,
/// Seating Force, Load Leaderboard and Distance Workflow had all shipped, so v0.2.5 went out four
/// tools behind. <see cref="ManualSyncTests"/> covers the two HTML manuals; this covers the page
/// they link from, against the launcher, which is the only place the real tool list exists.
/// </summary>
public class LandingPageSyncTests
{
    /// <summary>Walks up from the test binary to the repo root, identified by the manual.</summary>
    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GrtReloadingToolkit", "MANUAL.md")))
            dir = dir.Parent;
        Assert.True(dir is not null, "Could not find the repo root above " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string Src(string relative) =>
        File.ReadAllText(Path.Combine(RepoRoot(), "GrtReloadingToolkit", relative));

    private const string Landing = "docs/GRT-Reloading-Toolkit.html";

    /// <summary>
    /// Every launcher button, in order, minus "Install GRT report templates" — that one sits below
    /// a Divider() rather than in a Section(), because it installs report pages into GRT instead of
    /// being a tool you open, and the page has never counted it as one.
    /// </summary>
    private static string[] LauncherTools() =>
        Regex.Matches(Src("Ui/LauncherForm.cs"), @"B\(Lang\.T\(""([^""]+)""")
             .Select(m => m.Groups[1].Value)
             .Where(t => !t.Contains("report templates", StringComparison.OrdinalIgnoreCase))
             .ToArray();

    private static int LandingPageCards() =>
        Regex.Matches(Src(Landing), @"<div class=""tool"">").Count;

    [Fact]
    public void TheLandingPageHasOneCardPerLauncherTool()
    {
        // A tool was added to the launcher and no card was written for it (or vice versa).
        Assert.Equal(LauncherTools().Length, LandingPageCards());
    }

    /// <summary>
    /// Both places the page writes the count out in words: the hero headline and the section
    /// heading above the cards. Fixing the heading alone still shipped a page whose first line
    /// said "Eight reloading tools" -- one count per assertion would have let that through, so
    /// both are checked here against the same card count.
    /// </summary>
    [Theory]
    [InlineData(@"<h1>([\w-]+) reloading tools, one GRT window\.</h1>")]
    [InlineData(@"<h2>The ([\w-]+) tools</h2>")]
    public void EveryToolCountOnThePageMatchesTheCards(string pattern)
    {
        // These spell the number out, so they cannot drift silently the way a digit would --
        // they read fine while being wrong. This is what actually went stale, twice. The pattern
        // allows a hyphen (not just \w+) once the count passed twenty and needed one ("twenty-one").
        string[] words =
        {
            "zero", "one", "two", "three", "four", "five", "six", "seven",
            "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty", "twenty-one", "twenty-two",
        };
        int cards = LandingPageCards();
        Assert.InRange(cards, 1, words.Length - 1);

        var m = Regex.Match(Src(Landing), pattern, RegexOptions.IgnoreCase);
        Assert.True(m.Success, "no match for " + pattern + " on " + Landing);
        Assert.Equal(words[cards], m.Groups[1].Value.ToLowerInvariant());
    }
}
