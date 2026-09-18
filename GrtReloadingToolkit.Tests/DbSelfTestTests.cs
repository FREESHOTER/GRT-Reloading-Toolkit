using GrtReloadingToolkit;
using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// <see cref="DbSelfTest"/>'s whole job is to delete whatever file it's pointed at before writing
/// fixture data. A bare <c>--dbtest</c> (no path argument) once defaulted to <see cref="Db.DefaultPath"/>
/// -- the exact file the real plugin's Journal and Inventory read and write -- which meant running the
/// documented self-test command silently wiped a user's real reloading data with no confirmation and
/// no backup. This test exists so that regression can never come back unnoticed.
/// </summary>
public sealed class DbSelfTestTests
{
    [Fact]
    public void BareDbTestNeverResolvesToTheRealDatabase()
    {
        string resolved = DbSelfTest.ResolvePath(new[] { "--dbtest" });
        Assert.NotEqual(Db.DefaultPath, resolved);
    }

    [Fact]
    public void AnExplicitPathArgumentIsStillHonored()
    {
        string resolved = DbSelfTest.ResolvePath(new[] { "--dbtest", "custom.db" });
        Assert.Equal("custom.db", resolved);
    }
}
