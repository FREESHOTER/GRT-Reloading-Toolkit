using GrtReloadingToolkit.Ui;
using Xunit;

namespace GrtReloadingToolkit.Tests;

public sealed class WindowFitTests
{
    private static readonly (int X, int Y, int Width, int Height) Work150 = (0, 0, 1280, 680);   // 1920x1080 at 150%

    [Fact]
    public void AWindowThatFitsIsLeftAlone()
        => Assert.Equal((100, 40, 900, 560), WindowFit.Fit((100, 40, 900, 560), Work150));

    [Fact]
    public void ATooTallWindowIsShortenedToTheWorkingArea()
    {
        // ShotMarker import is 1240x740 and is centred, so its top starts at -30 on that screen
        var r = WindowFit.Fit((20, -30, 1240, 740), Work150);
        Assert.Equal(680, r.Height);
        Assert.Equal(0, r.Y);
        Assert.Equal(1240, r.Width);
    }

    [Fact]
    public void AWindowHangingPastTheBottomIsLiftedNotShrunk()
    {
        var r = WindowFit.Fit((100, 300, 900, 560), Work150);
        Assert.Equal(560, r.Height);
        Assert.Equal(120, r.Y);                             // 680 - 560: its bottom edge on the taskbar's
    }

    [Fact]
    public void ATooWideWindowIsNarrowedAndMovedToTheLeftEdge()
        => Assert.Equal((0, 0, 1280, 500), WindowFit.Fit((300, 0, 1500, 500), Work150));

    [Fact]
    public void AnOffsetWorkingAreaIsRespected()
    {
        // a taskbar on top: the working area starts at y=40
        var r = WindowFit.Fit((0, 0, 800, 900), (0, 40, 1366, 688));
        Assert.Equal((0, 40, 800, 688), r);
    }
}
