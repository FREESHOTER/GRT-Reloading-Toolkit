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
    private readonly Button _suggest = new() { AutoSize = true, Dock = DockStyle.Right, Margin = new Padding(0), Padding = new Padding(10, 0, 10, 0), Visible = false };
    private string _nextTool = "";
    private string _suggestTool = "";
    private IReadOnlyList<Suggestion> _suggestions = Array.Empty<Suggestion>();
    private string _nextName = "";
    private string? _hint;
    private bool _done;

    /// <param name="current">The <c>Tool</c> enum name of the window the bar sits in.</param>
    /// <returns>Null (no bar) when the path has nothing after this tool in any phase.</returns>
    public static NextStepBar? For(string current)
        => ToolPathMap.Sources.Contains(current) ? new NextStepBar(current) : null;

    /// <summary>Adds the bar to <paramref name="form"/> (after its other controls, so it takes the bottom edge) and starts
    /// following the open load. Null when the path has nothing after this tool in any phase.</summary>
    public static NextStepBar? Attach(Form form, string current, GrtClient? grt)
    {
        NextStepBar? bar = For(current);
        if (bar is null) return null;
        form.Controls.Add(bar);
        _ = bar.TrackLoadAsync(grt);
        return bar;
    }

    private NextStepBar(string current)
    {
        _current = current;
        _hint = ToolPathMap.Hint(current) is { } h ? Lang.T(h) : null;
        Dock = DockStyle.Bottom;
        BackColor = Color.FromArgb(232, 240, 250);   // the pale blue of GRT's own highlight, not an alarm colour
        _open.Click += (_, _) => ToolNav.Open?.Invoke(_nextTool);
        _suggest.Click += (_, _) => ToolNav.Open?.Invoke(_suggestTool);
        Controls.Add(_text);
        Controls.Add(_open);
        Controls.Add(_suggest);
        Apply(PathState.Default);
        Resize += (_, _) => Render();
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
        Render();
    }

    /// <summary>The optional tools worth a look now (<see cref="ToolSuggestions"/>); shown under the next step, with a button for the first.</summary>
    public void SetSuggestions(IReadOnlyList<Suggestion> suggestions)
    {
        _suggestions = suggestions;
        if (suggestions.Count > 0)
        {
            _suggestTool = suggestions[0].Tool;
            _suggest.Text = string.Format(Lang.T("Open {0}"), Lang.T(ToolNames.EnglishName(_suggestTool)));
        }
        _suggest.Visible = suggestions.Count > 0;
        Render();
    }

    /// <summary>What the journal holds for a caliber: (distinct powders, entries with a calibrated Ba). Set once by the toolkit
    /// context; null when there is no journal (the suggestions that need it then stay quiet).</summary>
    public static Func<string, (int Powders, int Calibrated)>? JournalFacts;

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
            var notes = doc.NoteTitles();
            var measurements = doc.Measurements().ToList();
            Apply(PathState.From(notes, measurements.Count > 0));
            var facts = JournalFacts?.Invoke(doc.CaliberName) ?? (0, 0);
            SetSuggestions(ToolSuggestions.For(_current, new PathSignals(
                notes.Any(t => t.StartsWith("Barrel Calibration", StringComparison.OrdinalIgnoreCase)),
                ToolSuggestions.TemperatureSpreadC(measurements.SelectMany(m => m.Charges).Select(c => c.Note)),
                facts.Item1, facts.Item2,
                measurements.SelectMany(m => m.Charges).Count(c => c.Shots.Count(sh => sh.VelocityMps > 50) >= GrtReloadingToolkit.Log.Diagnosis.MinShots))));
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
        Render();
    }

    private void Render()
    {
        if (_nextTool.Length == 0) return;
        _text.Font = new Font(Font, _done ? FontStyle.Bold : FontStyle.Regular);
        _text.ForeColor = _done ? Color.FromArgb(0, 88, 176) : SystemColors.ControlText;
        var sb = new System.Text.StringBuilder((_done ? Lang.T("Done.") + " " : "") + string.Format(Lang.T("Next step: {0}"), _nextName));
        if (_hint is not null) sb.Append(Environment.NewLine).Append(_hint);
        foreach (var s in _suggestions)
            sb.Append(Environment.NewLine).Append(string.Format(Lang.T("Also useful: {0} - {1}"), Lang.T(ToolNames.EnglishName(s.Tool)), Lang.T(s.Reason)));
        _text.Text = sb.ToString();
        // As tall as the text needs at the width left beside the buttons: a long suggestion wraps, and a window
        // made narrower wraps it more, so this is measured again whenever the width changes.
        int room = Math.Max(120, ClientSize.Width - _open.Width - (_suggest.Visible ? _suggest.Width : 0) - _text.Padding.Left - 12);
        int textHeight = TextRenderer.MeasureText(_text.Text, _text.Font, new Size(room, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
        Height = Math.Max(36, textHeight + 14);
    }
}

/// <summary>How a tool window asks the process to open another tool; set once by <c>ToolkitContext</c>.</summary>
internal static class ToolNav
{
    public static Action<string>? Open;
}
