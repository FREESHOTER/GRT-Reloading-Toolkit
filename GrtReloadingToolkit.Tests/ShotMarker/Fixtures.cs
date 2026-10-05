using System.Reflection;
using Xunit;

namespace ShotMarker.Core.Tests;

/// <summary>Locates the committed sample files. The test binary runs from
/// bin/Debug/net8.0, so the repo root is found by walking up to a file only the repo root
/// has. GrtReloadingToolkit/MANUAL.md is the marker the toolkit's own tests already use —
/// anchoring on the .sln instead would break the moment this project is built on its own.</summary>
public static class Fixtures
{
    public static string Root { get; } = FindRoot();

    public static string Path(string relative) =>
        System.IO.Path.Combine(Root, "fixtures", relative);

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(System.IO.Path.GetDirectoryName(
            Assembly.GetExecutingAssembly().Location)!);
        while (dir != null && !File.Exists(System.IO.Path.Combine(dir.FullName, "GrtReloadingToolkit", "MANUAL.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}

public class FixturesTests
{
    [Fact]
    public void FindsTheCommittedExports()
    {
        Assert.True(File.Exists(Fixtures.Path("shotmarker/SM_export_Sep_21.tar")));
        Assert.True(File.Exists(Fixtures.Path("shotmarker/SM_shotslog_Sep_21.csv")));
    }
}
