using System.Xml.Linq;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// Directory.Build.props is the one place the version is written, and the window titles read it
/// back off the compiled assembly, so those cannot drift. Two files still spell it out as a
/// literal: the plugin manifest GRT itself reads, and the Win32 application manifest, which is a
/// build input and so cannot be stamped at package time. build-plugin.ps1 stamps the manifest it
/// copies into dist\, which covers a packaged release — these tests cover the literals in the
/// tree, which are what a hand-copied plugin folder and the built .exe report.
/// </summary>
public class VersionSyncTests
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

    /// <summary>"0.1.7" — the &lt;Version&gt; every project in the tree is built with.</summary>
    private static string DeclaredVersion()
    {
        // This file is shared between two differently-shaped trees: the GitHub-tracked repo
        // nests GrtReloadingToolkit inside one repo root that also holds grt-plugins-shared
        // (Directory.Build.props sits at that shared RepoRoot()), while the local working copy
        // keeps them as siblings directly under a shared parent alongside unrelated projects
        // (props lives inside GrtReloadingToolkit itself there, deliberately NOT hoisted up —
        // that would silently apply it to those unrelated sibling projects too). Check both
        // rather than hardcoding either, so the same file is correct in both trees.
        string atRoot = Path.Combine(RepoRoot(), "Directory.Build.props");
        string atToolkit = Path.Combine(RepoRoot(), "GrtReloadingToolkit", "Directory.Build.props");
        string path = File.Exists(atRoot) ? atRoot : atToolkit;
        var props = XDocument.Load(path);
        string? v = props.Descendants("Version").SingleOrDefault()?.Value.Trim();
        Assert.False(string.IsNullOrEmpty(v), $"no single <Version> in {path}");
        return v!;
    }

    [Fact]
    public void PluginManifestDeclaresTheBuiltVersion()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "GrtReloadingToolkit", "plugin", "com.grt.plugin.xml"));
        string? got = doc.Root!.Element("plugin")?.Attribute("version")?.Value;

        // GRT reads this file to decide what version of the plugin is installed.
        Assert.Equal(DeclaredVersion(), got);
    }

    [Fact]
    public void ApplicationManifestDeclaresTheBuiltVersion()
    {
        var doc = XDocument.Load(Path.Combine(RepoRoot(), "GrtReloadingToolkit", "app.manifest"));
        XNamespace asm = "urn:schemas-microsoft-com:asm.v1";
        string? got = doc.Root!.Element(asm + "assemblyIdentity")?.Attribute("version")?.Value;

        // Win32 assembly identities are always four-part; Directory.Build.props writes three.
        Assert.Equal(DeclaredVersion() + ".0", got);
    }

    /// <summary>
    /// The HTML manuals used to carry a hand-written "v0.2" badge that never moved past five patch
    /// releases (0.2.0 through 0.2.4) -- caught only when the user asked "il manuale deve seguire la
    /// release". Checking for the literal substring, not a specific tag, so it fails the same way
    /// whichever of the header badge or the footer line is the one left stale next time. The landing
    /// page was the same story only worse: it still said v0.1 at the 0.2.5 release, because it was
    /// added here last and nothing was watching it.
    /// </summary>
    [Theory]
    [InlineData("Reloading-Toolkit-Manual.html")]
    [InlineData("Reloading-Toolkit-Manual-IT.html")]
    [InlineData("GRT-Reloading-Toolkit.html")]
    public void HtmlPageVersionBadgeMatchesTheBuiltVersion(string file)
    {
        string html = File.ReadAllText(Path.Combine(RepoRoot(), "GrtReloadingToolkit", "docs", file));
        Assert.Contains($"v{DeclaredVersion()}", html);
    }
}
