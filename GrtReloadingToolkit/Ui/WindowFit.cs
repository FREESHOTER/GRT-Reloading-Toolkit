namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Keeps a tool window inside the working area of its screen. Most windows are given a fixed size in pixels that suits a 1080p
/// desktop; at a Windows scale of 150% (the toolkit is drawn at 100% and stretched, so the screen is 1280x720 as far as it is
/// concerned, 680 high without the taskbar) or on a 1366x768 laptop six of them are taller than the screen, and the bar at the bottom
/// with the next step and the buttons ends up under the taskbar.
/// </summary>
internal static class WindowFit
{
    /// <summary>
    /// The window's rectangle after it is made no larger than the working area and moved just enough to lie inside it: unchanged when
    /// it already fits. Rectangles are (left, top, width, height).
    /// </summary>
    public static (int X, int Y, int Width, int Height) Fit(
        (int X, int Y, int Width, int Height) win, (int X, int Y, int Width, int Height) work)
    {
        int w = Math.Min(win.Width, work.Width);
        int h = Math.Min(win.Height, work.Height);
        int x = Math.Max(work.X, Math.Min(win.X, work.X + work.Width - w));
        int y = Math.Max(work.Y, Math.Min(win.Y, work.Y + work.Height - h));
        return (x, y, w, h);
    }
}
