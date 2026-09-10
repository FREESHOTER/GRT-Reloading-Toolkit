namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Sets a <see cref="SplitContainer.SplitterDistance"/> that survives to the screen.
///
/// Assigning it in an object initializer quietly loses the value: the container is still at
/// its default 150x100 and has no parent, so WinForms clamps whatever is asked for down to
/// what fits that default — a horizontal split accepts at most
/// <c>Height - Panel2MinSize - SplitterWidth</c>, i.e. <c>100 - 25 - 4 = 71</c> px, and a
/// vertical one <c>150 - 25 - 4 = 121</c> px. Docking the container into a real form afterwards
/// resizes the panels proportionally but never revisits the clamp, so a "210 px grid on top"
/// ships as a 71 px sliver.
///
/// <see cref="WithDistance"/> defers the assignment until the container has a real size, then
/// stops listening, so a splitter the user has dragged is never yanked back.
/// </summary>
internal static class SplitFix
{
    public static SplitContainer WithDistance(this SplitContainer split, int distance)
    {
        void Apply(object? sender, EventArgs e)
        {
            int span = split.Orientation == Orientation.Horizontal ? split.Height : split.Width;
            int max = span - split.Panel2MinSize - split.SplitterWidth;
            if (max < split.Panel1MinSize) return;   // no room for a splitter at all yet

            split.SplitterDistance = Math.Clamp(distance, split.Panel1MinSize, max);
            if (max < distance) return;              // still cramped — take another run after the next resize

            split.HandleCreated -= Apply;
            split.SizeChanged -= Apply;
        }

        split.HandleCreated += Apply;
        split.SizeChanged += Apply;
        return split;
    }
}
