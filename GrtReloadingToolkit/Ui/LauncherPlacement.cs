namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Where the launcher's top edge goes once it has been resized. The window is centred while it still has its small default
/// size and only then sized to the tool list, so its top stays where the small window was centred and the new, taller
/// window can hang past the bottom of the desktop: measured on a 1920x1080 screen the bottom of the window ended 9 px under
/// the taskbar, and on a 1366x768 one it would end more than 160 px below the screen, with the last buttons under the
/// taskbar although the scroll bar is there.
/// </summary>
internal static class LauncherPlacement
{
    /// <summary>
    /// The top edge that keeps a window of <paramref name="height"/> inside the working area: unchanged when it already fits,
    /// otherwise lifted just enough, and never above the working area itself.
    /// </summary>
    public static int Top(int workTop, int workBottom, int top, int height)
        => Math.Max(workTop, Math.Min(top, workBottom - height));
}
