using Xunit;

namespace GrtReloadingToolkit.Tests;

public sealed class LaunchArgsTests
{
    [Fact]
    public void ReadsThePortGrtPasses()
    {
        var a = LaunchArgs.Parse(new[] { "--ipcport", "51234" });
        Assert.Equal(51234, a.Port);
        Assert.Null(a.IpcFile);
        Assert.True(a.ShouldConnect);
    }

    [Fact]
    public void NoArgumentsMeansNotStartedByGrt() =>
        Assert.False(LaunchArgs.Parse(Array.Empty<string>()).ShouldConnect);

    [Fact]
    public void AnUnreadablePortDoesNotConnect() =>
        Assert.False(LaunchArgs.Parse(new[] { "--ipcport", "x" }).ShouldConnect);

    // GRT on Linux hands over a Unix socket path instead of a port.
    [Fact]
    public void ReadsTheSocketFileGrtPasses()
    {
        var a = LaunchArgs.Parse(new[] { "--ipcfile", "/tmp/grt-1a2b.sock" });
        Assert.Equal("/tmp/grt-1a2b.sock", a.IpcFile);
        Assert.True(a.ShouldConnect);
    }

    [Fact]
    public void ASocketFileWinsOverAPort()
    {
        var a = LaunchArgs.Parse(new[] { "--ipcfile", "/tmp/grt.sock", "--ipcport", "51234" });
        Assert.Equal("/tmp/grt.sock", a.IpcFile);
        Assert.True(a.ShouldConnect);
    }

    [Fact]
    public void AnIpcFileWithoutAPathDoesNotConnect() =>
        Assert.False(LaunchArgs.Parse(new[] { "--ipcfile" }).ShouldConnect);
}
