namespace GrtReloadingToolkit;

/// <summary>Where GRT told the plugin to connect. GRT's plugin API passes <c>--ipcport &lt;port&gt;</c> (TCP on
/// 127.0.0.1) on Windows and macOS, and <c>--ipcfile &lt;path&gt;</c> (a Unix socket file) on Linux; when both are
/// given the file wins. With neither, the program was not started by GRT.</summary>
internal sealed record LaunchArgs(int Port, string? IpcFile)
{
    public static LaunchArgs Parse(IReadOnlyList<string> args)
    {
        int port = 0;
        string? ipcFile = null;
        for (int i = 0; i + 1 < args.Count; i++)
        {
            if (args[i] == "--ipcfile" && args[i + 1].Length > 0) ipcFile = args[i + 1];
            if (args[i] == "--ipcport" && int.TryParse(args[i + 1], out int p)) port = p;
        }
        return new LaunchArgs(port, ipcFile);
    }

    public bool ShouldConnect => IpcFile != null || Port > 0;
}
