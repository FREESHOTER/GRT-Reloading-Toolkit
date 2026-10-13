using GrtPluginKit.Grt;
using GrtPluginKit.Ipc;

namespace GrtReloadingToolkit.Ui;

/// <summary>
/// The line at the foot of a tool window that says where the recommended path goes next, with a button that opens that
/// tool. Before the tool's own job is done it reads "Next step: ..."; <see cref="MarkDone"/> turns it into
/// "Done. Next step: ...". It never opens anything by itself. Which tool is next depends on how far along the open load is
/// (<see cref="PathState"/>), so <see cref="TrackLoadAsync"/> reads the load and the bar follows it; when the load's phase has
/// nothing after this tool the bar hides itself.
/// </summary>
internal sealed class NextStepBar : Panel
{
    private readonly Label _text = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0) };
    private readonly Button _open = new() { AutoSize = true, Dock = DockStyle.Right, Margin = new Padding(0), Padding = new Padding(10, 0, 10, 0) };
    private readonly string _current;
    private string _nextTool = "";
    private string _nextName = "";
    private string? _hint;
    private bool _done;

    /// <param name="current">The <c>Tool</c> enum name of the window the bar sits in.</param>
    /// <returns>Null (no bar) when the path has nothing after this tool in any phase.</returns>
    public static NextStepBar? For(string current)
        => ToolPathMap.Sources.Contains(current) ? new NextStepBar(current) : null;

    private NextStepBar(string current)
    {
        _current = current;
        _hint = ToolPathMap.Hint(current) is { } h ? Lang.T(h) : null;
        Dock = DockStyle.Bottom;
        BackColor = Color.FromArgb(232, 240, 250);   // the pale blue of GRT's own highlight, not an alarm colour
        _open.Click += (_, _) => ToolNav.Open?.Invoke(_nextTool);
        Controls.Add(_text);
        Controls.Add(_open);
        Apply(PathState.Default);
    }

    /// <summary>Points the bar at the next tool for this load state; hidden when there is none.</summary>
    public void Apply(PathState state)
    {
        string? next = ToolPathMap.After(_current, state);
        Visible = next is not null;
        if (next is null) return;
        _nextTool = next;
        _nextName = Lang.T(ToolNames.EnglishName(next));
        _open.Text = string.Format(Lang.T("Open {0}"), _nextName);
        Height = _hint is null ? 36 : 52;
        Render();
    }

    /// <summary>
    /// Reads the load that is on top in GRT and follows its phase. Best effort and silent: a bar that cannot read the load
    /// keeps pointing where the charge-ladder path points, which is what it did before it looked.
    /// </summary>
    public async Task TrackLoadAsync(GrtClient? grt)
    {
        if (grt is not { Connected: true }) return;
        try
        {
            var top = await grt.GetTabOnTopAsync();
            if (string.IsNullOrWhiteSpace(top.file) || !File.Exists(top.file)) return;
            var doc = GrtLoadDoc.Load(GrtLoadDoc.EffectiveReadPath(top.file));
            if (IsDisposed) return;
            Apply(PathState.From(doc.NoteTitles(), doc.Measurements().Any()));
        }
        catch (Exception) { /* the bar stays as it is */ }
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
        if (_nextTool.Length == 0) return;
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
