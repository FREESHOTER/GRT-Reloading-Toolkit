using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

public class LoadOwnerTests
{
    [Fact]
    public void ThePersonIsTheFolderUnderGrtWhenAPowderFolderFollows()
        => Assert.Equal("Mario", LoadOwner.FromPath("C:/GRT-2021.2030 (W11) V1.0/Mario/N160/Cariche 37.40-37.70-38,00.grtload"));

    [Fact]
    public void BackslashesWorkToo()
        => Assert.Equal("Mario", LoadOwner.FromPath(@"C:\GRT-2021.2030 (W11) V1.0\Mario\N160\x.grtload"));

    [Fact]
    public void ASingleFolderUnderGrtIsAPowderNotAPerson()
        => Assert.Null(LoadOwner.FromPath("C:/GRT-2021.2030 (W11) V1.0/N150/x.grtload"));

    [Fact]
    public void AKnownShooterNamedAnywhereInTheFolderPathWins()
        => Assert.Equal("Mario", LoadOwner.FromPath("D:/loads/mario/N150/x.grtload", new[] { "Io", "Mario" }));

    [Fact]
    public void NothingIsGuessedFromAPathWithoutAGrtFolderOrKnownName()
        => Assert.Null(LoadOwner.FromPath("D:/loads/anyone/N150/x.grtload"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("x.grtload")]
    public void EmptyOrBarePathsSayNothing(string? path) => Assert.Null(LoadOwner.FromPath(path));
}
