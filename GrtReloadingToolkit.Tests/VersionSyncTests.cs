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
        var props = XDocument.Load(Path.Combine(RepoRoot(), "Directory.Build.props"));
        string? v = props.Descendants("Version").SingleOrDefault()?.Value.Trim();
        Assert.False(string.IsNullOrEmpty(v), "no single <Version> in Directory.Build.props");
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
}
