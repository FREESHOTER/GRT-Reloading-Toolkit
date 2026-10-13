using GrtReloadingToolkit.Log;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>The two tokens a shooter writes into text GRT already lets them write: COLD=1 on a shot, WIND=… on a shot-group tab or group.</summary>
public sealed class DataTokensTests
{
    [Theory]
    [InlineData("COLD=1", true)]
    [InlineData("TIME=10:14:02 AM COLD=1 SRC=x", true)]
    [InlineData("cold = 1", true)]
    [InlineData("COLD=10", false)]
    [InlineData("COLD=0", false)]
    [InlineData("SCOLD=1", false)]
    [InlineData("TIME=10:14", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ColdIsReadFromAShotNote(string? note, bool expected) => Assert.Equal(expected, DataTokens.IsCold(note));

    [Theory]
    [InlineData("WIND=3.2m/s", 3.2, null)]
    [InlineData("Gruppo 600 m WIND=3,2 m/s @90", 3.2, 90.0)]
    [InlineData("WIND=3.2", 3.2, null)]
    [InlineData("WIND=18km/h", 5.0, null)]
    [InlineData("WIND=10mph@270", 4.4704, 270.0)]
    [InlineData("WIND=10ft/s", 3.048, null)]
    [InlineData("GRT:600 m tab WIND=2.5m/s#40.2 gr", 2.5, null)]
    public void WindIsReadInMetresPerSecond(string text, double mps, double? deg)
    {
        var w = DataTokens.WindOf(text);
        Assert.NotNull(w);
        Assert.Equal(mps, w!.Value.Mps, 3);
        Assert.Equal(deg, w.Value.Degrees);
    }

    [Theory]
    [InlineData("no wind here")]
    [InlineData("TAILWIND=3")]
    [InlineData("")]
    [InlineData(null)]
    public void NoWindToken(string? text) => Assert.Null(DataTokens.WindOf(text));
}