namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Appends a timestamped line to a log TextBox from any thread, tolerating a form that is
/// closing / already disposed (the shared <c>GrtClient.Log</c> event can fire after a tool
/// window is gone).
/// </summary>
internal static class UiLog
{
    /// <summary>
    /// A box to <see cref="SafeAppend"/> into: read-only, monospaced, scrolling. Docking and
    /// sizing are the form's business — this only fixes the look, which every log box shares.
    /// </summary>
    public static TextBox NewLogBox() => new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        BackColor = SystemColors.Window,
        Font = new Font(FontFamily.GenericMonospace, 8f),
    };

    public static void SafeAppend(this TextBox box, string line)
    {
        if (box is null || box.IsDisposed || box.Disposing || !box.IsHandleCreated) return;
        try
        {
            if (box.InvokeRequired) { box.BeginInvoke(() => SafeAppend(box, line)); return; }
            box.AppendText($"{DateTime.Now:HH:mm:ss}  {line}{Environment.NewLine}");
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }
}
