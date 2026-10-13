using GrtReloadingToolkit.Ui;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>The "Chosen Charge" note is read back by the target form, so its charge line is a contract.</summary>
public sealed class ChosenChargeReaderTests
{
    [Theory]
    [InlineData("Chosen Charge 2026-10-13\r\n--------------------\r\n\r\nCharge: 23.8 gr\r\nrest", "23.8 gr")]
    [InlineData("Chosen Charge 2026-10-13\n\nCarica: 2.57 g\n", "2.57 g")]
    [InlineData("Chosen Charge 2026-10-13\n\nsomething else\n", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void TheChargeLineIsFoundWhateverTheLanguageOrLineEnding(string? text, string? expected)
        => Assert.Equal(expected, ChosenChargeReader.ChargeFrom(text));
}