using System.Text.RegularExpressions;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The engines write their report texts through <c>Tr.T</c> / <c>Tr.F</c>, which look the English literal up in the UI's Italian table.
/// A literal with no entry would come out in English inside an Italian window and an Italian GRT note, and nothing would complain:
/// this is what complains.
/// </summary>
public class ResultTextTranslationTests
{
    private static string Root()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "GrtReloadingToolkit", "MANUAL.md"))) dir = dir.Parent;
        Assert.True(dir is not null, "repo root not found");
        return Path.Combine(dir!.FullName, "GrtReloadingToolkit");
    }

    [Fact]
    public void EveryEngineTextHasAnItalianEntry()
    {
        string root = Root();
        string lang = File.ReadAllText(Path.Combine(root, "Ui", "Lang.cs"));
        var have = new HashSet<string>(Regex.Matches(lang, @"\[""((?:[^""\\]|\\.)*)""\]\s*=").Select(m => m.Groups[1].Value));

        var literal = new Regex(@"\bTr\.(?:T|F)\(\s*(""(?:[^""\\]|\\.)*"")");
        var missing = new List<string>();
        foreach (string file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                              && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)
                              && Path.GetFileName(f) != "Tr.cs"))
            foreach (Match m in literal.Matches(File.ReadAllText(file)))
            {
                string key = m.Groups[1].Value[1..^1];
                if (!have.Contains(key)) missing.Add(Path.GetFileName(file) + ": " + key);
            }

        Assert.True(missing.Count == 0, "no Italian entry for:\n" + string.Join("\n", missing.Distinct()));
    }
}
