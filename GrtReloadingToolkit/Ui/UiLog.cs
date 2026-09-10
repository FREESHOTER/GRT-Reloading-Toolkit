namespace GrtReloadingToolkit.Ui;

/// <summary>
/// Appends a timestamped line to a log TextBox from any thread, tolerating a form that is
/// closing / already disposed (the shared <c>GrtClient.Log</c> event can fire after a tool
/// window is gone).
/// </summary>
internal static class UiLog
{
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
