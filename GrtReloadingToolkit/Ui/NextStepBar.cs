namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The line at the foot of a tool window that says where the recommended path goes next, with a button that opens that
/// tool. Before the tool's own job is done it reads "Next step: ..."; <see cref="MarkDone"/> turns it into
/// "Done. Next step: ...". It never opens anything by itself.
/// </summary>
internal sealed class NextStepBar : Panel
{
    private readonly Label _text = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0) };
    private readonly Button _open = new() { AutoSize = true, Dock = DockStyle.Right, Margin = new Padding(0), Padding = new Padding(10, 0, 10, 0) };
    private readonly string _nextName;
    private string? _hint;
    private bool _done;

    /// <param name="current">The <c>Tool</c> enum name of the window the bar sits in.</param>
    /// <returns>Null (no bar) when the path has nothing after this tool.</returns>
    public static NextStepBar? For(string current)
    {
        string? next = ToolPathMap.After(current);
        return next is null ? null : new NextStepBar(next, ToolPathMap.Hint(current));
    }

    private NextStepBar(string nextTool, string? hint)
    {
        _nextName = Lang.T(ToolNames.EnglishName(nextTool));
        _hint = hint is null ? null : Lang.T(hint);
        Dock = DockStyle.Bottom;
        Height = hint is null ? 36 : 52;
        BackColor = Color.FromArgb(232, 240, 250);   // the pale blue of GRT's own highlight, not an alarm colour
        _open.Text = string.Format(Lang.T("Open {0}"), _nextName);
        _open.Click += (_, _) => ToolNav.Open?.Invoke(nextTool);
        Controls.Add(_text);
        Controls.Add(_open);
        Render();
    }

    /// <summary>The tool's own job has been done (note written, import finished).</summary>
    public void MarkDone() { _done = true; Render(); }

    /// <summary>Replaces the hint under the line, for a fact only known once the tool has run (the charge that was chosen).
    /// Already translated.</summary>
    public void SetHint(string? hint)
    {
        _hint = hint;
        Height = hint is null ? 36 : 52;
        Render();
    }

    private void Render()
    {
        _text.Font = new Font(Font, _done ? FontStyle.Bold : FontStyle.Regular);
        _text.ForeColor = _done ? Color.FromArgb(0, 88, 176) : SystemColors.ControlText;
        _text.Text = (_done ? Lang.T("Done.") + " " : "") + string.Format(Lang.T("Next step: {0}"), _nextName)
                     + (_hint is null ? "" : Environment.NewLine + _hint);
    }
}

/// <summary>How a tool window asks the process to open another tool; set once by <c>ToolkitContext</c>.</summary>
internal static class ToolNav
{
    public static Action<string>? Open;
}
