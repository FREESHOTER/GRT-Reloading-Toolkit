using GrtReloadingToolkit.Ui;
using SkiaSharp;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>The emblem of the home page and the program's icon made from it.</summary>
public sealed class HomeArtTests
{
    [Fact]
    public void EmblemIsARoundPictureWithATransparentCorner()
    {
        using var bmp = SKBitmap.Decode(HomeArt.EmblemPng(64))!;
        Assert.Equal(64, bmp.Width);
        Assert.Equal(0, bmp.GetPixel(1, 1).Alpha);          // outside the disc
        Assert.Equal(255, bmp.GetPixel(32, 40).Alpha);      // the cartridge in the middle
    }

    [Fact]
    public void IconFileHoldsEverySizeWindowsAsksFor()
    {
        byte[] ico = HomeArt.EmblemIco();
        Assert.Equal(new byte[] { 0, 0, 1, 0 }, ico[..4]);
        int count = BitConverter.ToUInt16(ico, 4);
        Assert.Equal(6, count);
        var sizes = new List<int>();
        for (int i = 0; i < count; i++)
        {
            int e = 6 + 16 * i;
            sizes.Add(ico[e] == 0 ? 256 : ico[e]);
            int len = BitConverter.ToInt32(ico, e + 8), off = BitConverter.ToInt32(ico, e + 12);
            Assert.True(off + len <= ico.Length);
        }
        Assert.Equal(new[] { 16, 24, 32, 48, 64, 256 }, sizes);
    }
}
