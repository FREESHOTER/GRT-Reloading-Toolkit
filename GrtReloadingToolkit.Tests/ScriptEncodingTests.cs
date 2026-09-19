using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// Guards the .ps1 files against the ways Windows PowerShell 5.1 quietly means something other
/// than what pwsh means, plus one way a script can silently ship a stale build regardless of
/// PowerShell edition. Three so far: source encoding, the zip separator Compress-Archive writes,
/// and a native command's exit code going unchecked.
///
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

    /// <summary>Every .ps1 belonging to this repo, build output aside.</summary>
    private static List<string> Scripts(string root)
    {
        // Restricted to this repo's own three project folders rather than everything under
        // RepoRoot(): the working tree keeps GrtReloadingToolkit as a sibling of unrelated
        // projects (GrtSensitivityLab, GrtModelOBT, ...) directly under the same parent
        // directory, instead of GitHub's clean repo root that holds nothing else. Scanning the
        // whole parent would flag scripts belonging to other repos entirely.
        string[] repoDirs = { "GrtReloadingToolkit", "grt-plugins-shared", "GrtReloadingToolkit.Tests" };
        List<string> scripts = repoDirs
            .Select(d => Path.Combine(root, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.ps1", SearchOption.AllDirectories))
            .Where(p => !p[root.Length..].Split(Path.DirectorySeparatorChar)
                          .Any(s => s is "bin" or "obj" or ".git"))
            .OrderBy(p => p)
            .ToList();

        // A search that quietly stops finding the scripts would pass forever.
        Assert.NotEmpty(scripts);
        return scripts;
    }

    [Fact]
    public void PowerShellScriptsAreAscii()
    {
        string root = RepoRoot();

        foreach (string script in Scripts(root))
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

    /// <summary>
    /// Compress-Archive on Windows PowerShell 5.1 writes the zip entry paths with backslashes.
    /// The format requires '/' (APPNOTE 4.4.17.1); Explorer and Expand-Archive forgive it, but
    /// Python's zipfile, macOS Archive Utility and Info-ZIP read "ReloadingToolkit\x.dll" as one
    /// flat filename, so a plugin zipped on Windows unpacks as a heap of oddly named files rather
    /// than a folder. pwsh writes '/', so the same script produced two different artifacts
    /// depending on who ran it. build-plugin.ps1 uses WriteZip, which names the separator itself.
    ///
    /// Only whole-line and block comments are skipped, so a mention in a trailing comment fails
    /// this test. That is the safe direction: unlike the ASCII rule above, where a hand-rolled
    /// parser risked false negatives, the worst case here is a loud failure on a line that meant
    /// no harm, and moving the word to its own comment line fixes it.
    /// </summary>
    [Fact]
    public void PowerShellScriptsDoNotUseCompressArchive()
    {
        string root = RepoRoot();

        foreach (string script in Scripts(root))
        {
            bool inBlockComment = false;
            string[] lines = File.ReadAllLines(script);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (inBlockComment)
                {
                    if (line.Contains("#>")) inBlockComment = false;
                    continue;
                }

                string trimmed = line.TrimStart();
                if (trimmed.StartsWith("<#"))
                {
                    if (!line.Contains("#>")) inBlockComment = true;
                    continue;
                }
                if (trimmed.StartsWith('#')) continue;
                if (!line.Contains("Compress-Archive", StringComparison.OrdinalIgnoreCase)) continue;

                Assert.Fail(
                    $"{Path.GetRelativePath(root, script)} line {i + 1} calls Compress-Archive, which " +
                    "writes backslash zip entry paths on Windows PowerShell 5.1 - the resulting zip does " +
                    "not unpack as a folder anywhere but Windows. See ScriptEncodingTests. Use the " +
                    "WriteZip helper in build-plugin.ps1, which writes '/' on every edition.");
            }
        }
    }

    /// <summary>
    /// `& dotnet.exe publish` is a native command: PowerShell's own `$ErrorActionPreference =
    /// "Stop"` does not see a non-zero exit code from it, only PowerShell's own terminating
    /// errors. Without an explicit `$LASTEXITCODE` check, a failed publish falls through to
    /// Assemble/WriteZip, which happily re-package whatever is already sitting in the output
    /// folders from a previous run and report success sizes for a build that never happened -
    /// found live, on a real machine, immediately after issue #12's wrong-dotnet.exe bug: the
    /// script "succeeded" while silently re-shipping yesterday's exe.
    ///
    /// This is a coarse count rather than per-call adjacency checking (which risks false
    /// negatives if a check moves a few lines away) - it only needs to catch a check being
    /// deleted outright, and a per-publish-call ratio is precise enough for that without being
    /// fragile to reformatting.
    /// </summary>
    [Fact]
    public void PublishCallsCheckExitCode()
    {
        string root = RepoRoot();
        string script = Path.Combine(root, "GrtReloadingToolkit", "build-plugin.ps1");
        Assert.True(File.Exists(script), $"expected {script} to exist");

        string text = File.ReadAllText(script);
        int publishCalls = System.Text.RegularExpressions.Regex.Matches(text, @"&\s*\$dotnet\s+publish\b").Count;
        int exitCodeChecks = System.Text.RegularExpressions.Regex.Matches(text, @"\$LASTEXITCODE").Count;

        Assert.True(publishCalls > 0, $"expected at least one '& $dotnet publish' call in {script} - did it move or get renamed?");
        Assert.True(exitCodeChecks >= publishCalls,
            $"{script} has {publishCalls} '& $dotnet publish' call(s) but only {exitCodeChecks} " +
            "$LASTEXITCODE check(s) - a failed publish would silently fall through to packaging " +
            "stale artifacts. See ScriptEncodingTests / issue #12.");
    }

    /// <summary>
    /// Issue #12 was a hardcoded "C:\Program Files\dotnet\dotnet.exe" breaking every install
    /// that isn't the default x64 machine-wide one; #13 fixed it with a PATH search that also
    /// checks each candidate actually has an SDK registered. Issue #22 found the exact same
    /// literal back on `main` after a later commit pasted in a stale local copy of this script -
    /// nothing had caught that the fix had regressed, because nothing asserted the fix stays.
    /// This does: it does not re-implement the detection logic, it just refuses to let the
    /// hardcoded path back into the file.
    /// </summary>
    [Fact]
    public void DotnetPathIsNotHardcoded()
    {
        string root = RepoRoot();
        string script = Path.Combine(root, "GrtReloadingToolkit", "build-plugin.ps1");
        Assert.True(File.Exists(script), $"expected {script} to exist");

        string text = File.ReadAllText(script);
        Assert.DoesNotMatch(@"\$dotnet\s*=\s*""C:\\Program Files", text);
        Assert.Contains("Get-Command dotnet", text);
    }
}
