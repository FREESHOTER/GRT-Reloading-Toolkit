using System.Text.RegularExpressions;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// Lang.Map is a collection initializer using indexer syntax -- <c>["en"] = "it"</c> -- and that
/// form OVERWRITES on a repeated key instead of throwing the way <c>{ "en", "it" }</c> would. So a
/// key written twice compiles, ships, and silently resolves to whichever entry comes last in the
/// file. That is how <c>["Load"]</c> came to exist twice, as "Carica" (the verb, for a button) and
/// "Carico" (the noun, for a grid column): the noun won by position, and the button read as a
/// mislabelled imperative to every Italian user without anything failing.
///
/// Read as text rather than by reflection, in the same style as ManualSyncTests and
/// VersionSyncTests, because this is a net8.0 test project and Lang lives in a net8.0-windows
/// assembly it cannot reference.
/// </summary>
public class LangTableTests
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

    /// <summary>Every <c>["key"] = </c> in Lang.cs, in file order, as (key, 1-based line).</summary>
    private static List<(string Key, int Line)> Entries()
    {
        string[] lines = File.ReadAllLines(Path.Combine(RepoRoot(), "GrtReloadingToolkit", "Ui", "Lang.cs"));
        var entries = new List<(string, int)>();
        for (int i = 0; i < lines.Length; i++)
        {
            // The value may wrap onto the next line, so anchor on the key and the '=' only.
            var m = Regex.Match(lines[i], @"^\s*\[""((?:[^""\\]|\\.)*)""\]\s*=");
            if (m.Success) entries.Add((m.Groups[1].Value, i + 1));
        }

        // Guard the guard: if the table is ever reshaped so the regex stops matching, an empty
        // list would make every assertion below pass vacuously.
        Assert.True(entries.Count > 300, $"only found {entries.Count} entries -- has Lang.cs changed shape?");
        return entries;
    }

    [Fact]
    public void NoEnglishKeyIsTranslatedTwice()
    {
        var dups = Entries()
            .GroupBy(e => e.Key)
            .Where(g => g.Count() > 1)
            .Select(g => $"  \"{g.Key}\" on lines {string.Join(", ", g.Select(e => e.Line))}")
            .ToList();

        Assert.True(dups.Count == 0,
            "Lang.Map defines the same English key more than once; the last one silently wins:\n"
            + string.Join("\n", dups));
    }
}
