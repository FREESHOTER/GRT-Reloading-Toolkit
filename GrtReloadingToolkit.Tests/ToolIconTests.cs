using GrtReloadingToolkit.Ui;
using SkiaSharp;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// The launcher's icons are drawn by <see cref="ToolIconPainter"/> (GRT's blue, orange on hover) instead of coming from the
/// system emoji font, which draws each emoji in its own colours or as an empty box. These tests only need SkiaSharp.
/// </summary>
public sealed class ToolIconTests
{
    private static SKBitmap Decode(byte[] png) => SKBitmap.Decode(png)!;

    [Fact]
    public void EveryToolHasAnIconThatDrawsSomething()
    {
        Assert.Equal(28, ToolIconPainter.Names.Count);   // one per launcher button
        foreach (string name in ToolIconPainter.Names)
        {
            using var bmp = Decode(ToolIconPainter.RenderPng(name, false, 44));
            Assert.Equal(44, bmp.Width);
            Assert.Equal(44, bmp.Height);
            int inked = 0;
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                    if (bmp.GetPixel(x, y).Alpha > 128) inked++;
            Assert.True(inked > 60, $"icon '{name}' draws almost nothing ({inked} pixels)");
            Assert.True(inked < 44 * 44 * 0.6, $"icon '{name}' fills the whole square ({inked} pixels)");
        }
    }

    [Fact]
    public void NormalIconsAreGrtBlueAndHoverIconsAreOrange()
    {
        foreach (string name in ToolIconPainter.Names)
        {
            Assert.True(DominantHueIsBlue(ToolIconPainter.RenderPng(name, false, 44)), $"'{name}' is not blue");
            Assert.True(DominantHueIsOrange(ToolIconPainter.RenderPng(name, true, 44)), $"'{name}' is not orange on hover");
        }
    }

    private static (double r, double g, double b) MeanInk(byte[] png)
    {
        using var bmp = Decode(png);
        double r = 0, g = 0, b = 0, n = 0;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                if (c.Alpha < 200) continue;
                // white-filled knobs and dots are part of the drawing but carry no colour
                if (c.Red > 235 && c.Green > 235 && c.Blue > 235) continue;
                r += c.Red; g += c.Green; b += c.Blue; n++;
            }
        return n == 0 ? (0, 0, 0) : (r / n, g / n, b / n);
    }

    private static bool DominantHueIsBlue(byte[] png) { var (r, g, b) = MeanInk(png); return b > r + 25 && b > g; }
    private static bool DominantHueIsOrange(byte[] png) { var (r, g, b) = MeanInk(png); return r > g + 25 && r > b + 25; }

    [Fact]
    public void AnUnknownIconNameIsAnErrorNotABlankButton()
        => Assert.Throws<ArgumentException>(() => ToolIconPainter.RenderPng("nope", false, 22));

    [Theory]
    [InlineData("🔧  Brass prep (case vol / seating / neck)", "Brass prep (case vol / seating / neck)")]
    [InlineData("⚖️  Seating force estimate (QC)", "Seating force estimate (QC)")]
    [InlineData("🎯🖨  Print ladder/OCW target", "Print ladder/OCW target")]
    [InlineData("🌡🎯  SD root-cause", "SD root-cause")]
    [InlineData("🪄  Guided new load (wizard)", "Guided new load (wizard)")]
    [InlineData("Inventory", "Inventory")]
    public void TheEmojiIsDroppedFromACaptionButNothingElse(string input, string expected)
        => Assert.Equal(expected, LauncherCaption.From(input));
}
