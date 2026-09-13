using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// build-plugin.ps1 is the only way the shipped plugin folder gets built, and it gets run on
/// Windows, where a .ps1 with no byte-order mark is decoded with the system ANSI code page
/// instead of UTF-8. Windows PowerShell 5.1 then reads the three UTF-8 bytes of an em dash as
/// three cp1252 characters, the last of which is a curly closing quote - and PowerShell accepts
/// that as a string delimiter. An em dash inside a double-quoted string therefore ends the
/// string early, the script fails to parse, and nothing is built at all. That was issue #10,
/// and it was invisible from macOS because pwsh assumes UTF-8 whatever the BOM says.
///
/// Plain ASCII is the one rule that holds across every code page, BOM and PowerShell edition,
/// and unlike "no non-ASCII inside a string literal" it can be checked without a PowerShell
/// parser. The prose loses nothing: a hyphen says what an em dash says.
/// </summary>
public class ScriptEncodingTests
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

    [Fact]
    public void PowerShellScriptsAreAscii()
    {
        string root = RepoRoot();
        List<string> scripts = Directory
            .EnumerateFiles(root, "*.ps1", SearchOption.AllDirectories)
            .Where(p => !p[root.Length..].Split(Path.DirectorySeparatorChar)
                          .Any(s => s is "bin" or "obj" or ".git"))
            .OrderBy(p => p)
            .ToList();

        // A search that quietly stops finding the scripts would pass forever.
        Assert.NotEmpty(scripts);

        foreach (string script in scripts)
        {
            byte[] bytes = File.ReadAllBytes(script);
            int at = Array.FindIndex(bytes, b => b > 0x7F);
            if (at < 0) continue;

            int line = 1 + bytes.Take(at).Count(b => b == (byte)'\n');
            Assert.Fail(
                $"{Path.GetRelativePath(root, script)} line {line} is not ASCII (byte 0x{bytes[at]:X2}). " +
                "Windows PowerShell 5.1 decodes a BOM-less .ps1 as ANSI, so this parses as something " +
                "else there - see ScriptEncodingTests. Use a plain ASCII equivalent: '-' for an em dash.");
        }
    }
}
