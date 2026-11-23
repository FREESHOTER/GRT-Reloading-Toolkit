using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace GrtAthlonImporter.Ipc;

/// <summary>
/// Minimal client for the GRT plugin IPC channel (plain TCP on 127.0.0.1,
/// concatenated compact-JSON messages). Only the commands the Athlon importer
/// needs are implemented: Get_TabOnTop and Load_File.
/// </summary>
internal sealed class GrtClient : IDisposable
{
    private readonly int _port;
    private readonly TcpClient _tcp = new(AddressFamily.InterNetwork) { NoDelay = true };
    private readonly JsonFrameSplitter _splitter = new();
    private readonly SemaphoreSlim _reqGate = new(1, 1);
    private readonly object _sync = new();

    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private TaskCompletionSource<JsonElement>? _pending;

    public GrtClient(int port) => _port = port;

    public int Port => _port;
    public bool Connected => _tcp.Connected;

    /// <summary>Raised (on a background thread) for menu/toolbar clicks from GRT.</summary>
    public event Action? MenuOrToolbarActivated;

    /// <summary>Raised (on a background thread) with a human-readable log line.</summary>
    public event Action<string>? Log;

    public void Connect()
    {
        _tcp.Connect("127.0.0.1", _port);
        _stream = _tcp.GetStream();
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoopAsync(_cts.Token));
        Log?.Invoke($"connected to GRT on 127.0.0.1:{_port}");
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (!ct.IsCancellationRequested && _stream != null)
            {
                int n = await _stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                if (n <= 0) break;
                string text = Encoding.UTF8.GetString(buffer, 0, n);
                foreach (string msg in _splitter.Feed(text))
                    Dispatch(msg);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log?.Invoke("read loop stopped: " + ex.Message); }
    }

    private void Dispatch(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (Exception ex) { Log?.Invoke("bad JSON from GRT: " + ex.Message); return; }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            var first = doc.RootElement.EnumerateObject().FirstOrDefault();
            string name = first.Name;

            if (name == "Result")
            {
                Log?.Invoke("RCV: " + Trim(json));
                lock (_sync)
                {
                    var p = _pending;
                    _pending = null;
                    p?.TrySetResult(first.Value.Clone());
                }
                return;
            }

            if (name.StartsWith("Event_", StringComparison.Ordinal))
            {
                Log?.Invoke("EVT: " + name);
                if (name is "Event_MenuAction" or "Event_ToolbarAction")
                    MenuOrToolbarActivated?.Invoke();
            }
        }
    }

    private async Task<JsonElement> RequestAsync(string command, TimeSpan timeout)
    {
        if (_stream == null) throw new InvalidOperationException("not connected to GRT");

        await _reqGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_sync) _pending = tcs;

            Log?.Invoke("SND: " + command);
            byte[] bytes = Encoding.UTF8.GetBytes(command);
            await _stream.WriteAsync(bytes).ConfigureAwait(false);
            await _stream.FlushAsync().ConfigureAwait(false);

            using var cts = new CancellationTokenSource(timeout);
            await using (cts.Token.Register(() => tcs.TrySetException(new TimeoutException("GRT did not answer " + command))))
                return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            lock (_sync) _pending = null;
            _reqGate.Release();
        }
    }

    /// <summary>Returns (tabHandle, caption, filePath) of the tab currently on top in GRT.</summary>
    public async Task<(long handle, string caption, string file)> GetTabOnTopAsync()
    {
        JsonElement result = await RequestAsync("{\"Get_TabOnTop\":null}", TimeSpan.FromSeconds(3));
        if (GetStatus(result) != "success")
            throw new InvalidOperationException("Get_TabOnTop failed: " + GetMessage(result));

        if (!result.TryGetProperty("values", out JsonElement v))
            throw new InvalidOperationException("Get_TabOnTop: no 'values' in response");
        Log?.Invoke("     values: " + v.GetRawText());

        return (Str(v, "tabhandle") is { } h && long.TryParse(h, out long hn) ? hn : 0,
                Str(v, "caption") ?? "",
                Str(v, "file") ?? "");
    }

    /// <summary>Reads a field as text regardless of whether GRT encoded it as a JSON string, number or bool.</summary>
    private static string? Str(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out JsonElement e)) return null;
        return e.ValueKind switch
        {
            JsonValueKind.String => e.GetString(),
            JsonValueKind.Number => e.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            _ => e.GetRawText(),
        };
    }

    /// <summary>Asks GRT to open a .grtload file in a new tab of the current window.</summary>
    public async Task LoadFileAsync(string path)
    {
        string cmd = "{\"Load_File\":{\"filepath\":" + JsonSerializer.Serialize(path) + "}}";
        JsonElement result = await RequestAsync(cmd, TimeSpan.FromSeconds(5));
        if (GetStatus(result) != "success")
            throw new InvalidOperationException("Load_File failed: " + GetMessage(result));
    }

    private static string GetStatus(JsonElement result) =>
        result.TryGetProperty("status", out var s) ? s.GetString() ?? "" : "";

    private static string GetMessage(JsonElement result) =>
        result.TryGetProperty("message", out var m) ? m.GetString() ?? "" : "(no message)";

    private static string Trim(string s) => s.Length <= 400 ? s : s[..400] + "…";

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _stream?.Dispose(); } catch { }
        try { _tcp.Dispose(); } catch { }
        _cts?.Dispose();
        _reqGate.Dispose();
    }
}
