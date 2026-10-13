using GrtReloadingToolkit.Ui;
using Xunit;

namespace GrtReloadingToolkit.Tests;

public sealed class LauncherPlacementTests
{
    // 1920x1080 with a 40 px taskbar: working area y 0..1040.
    [Fact]
    public void AWindowThatAlreadyFitsDoesNotMove()
        => Assert.Equal(100, LauncherPlacement.Top(0, 1040, 100, 679));

    [Fact]
    public void TheWindowSeenOnTheRealScreenIsLiftedOffTheTaskbar()
    {
        // top 370, height 679: the bottom ended at 1049, nine pixels under the taskbar
        int top = LauncherPlacement.Top(0, 1040, 370, 679);
        Assert.Equal(361, top);
        Assert.True(top + 679 <= 1040);
    }

    [Fact]
    public void OnASmallScreenTheWindowEndsAtTheBottomOfTheWorkingArea()
    {
        // 1366x768 with a 40 px taskbar: working area 0..728
        int top = LauncherPlacement.Top(0, 728, 214, 679);
        Assert.Equal(49, top);
        Assert.Equal(728, top + 679);
    }

    [Fact]
    public void NeverAboveTheWorkingArea()
        => Assert.Equal(30, LauncherPlacement.Top(30, 1040, 5, 900));

    [Fact]
    public void ATallerWindowThanTheWorkingAreaStartsAtItsTop()
        => Assert.Equal(0, LauncherPlacement.Top(0, 700, 120, 900));
}
