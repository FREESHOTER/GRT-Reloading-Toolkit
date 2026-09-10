using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace GrtPluginKit.Ipc;

/// <summary>Scalar results of one GRT simulation (the charge/curve currently shown in the tab).</summary>
public sealed class GrtResults
{
    public double? MuzzleVelocityMps { get; init; }
    public double? MaxPressure { get; init; }
    public string MaxPressureUnit { get; init; } = "";
    public double? BarrelTimeMs { get; init; }
    public double? BurnRatio { get; init; }
    public double? LoadRatio { get; init; }
}

/// <summary>
/// Minimal client for the GRT plugin IPC channel (plain TCP on 127.0.0.1,
/// concatenated compact-JSON messages). Commands: Get_TabOnTop, Get_TabResults, Load_File.
/// </summary>
public sealed class GrtClient : IDisposable
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

    private readonly object _activationLock = new();
    private string? _pendingActivation;
    private Action<string>? _activated;

    /// <summary>Raised (background thread) for menu/toolbar clicks from GRT, with the item id
    /// (e.g. "com.grt.plugin.x.athlon"). Empty string if GRT sent no id.
    /// If GRT fires the launch click before anyone subscribes, that id is buffered and
    /// replayed to the first subscriber (see <see cref="PendingActivation"/>).</summary>
    public event Action<string>? MenuOrToolbarActivated
    {
        add
        {
            string? replay = null;
            lock (_activationLock)
            {
                _activated += value;
                if (_pendingActivation != null) { replay = _pendingActivation; _pendingActivation = null; }
            }
            if (replay != null) value?.Invoke(replay);
        }
        remove { lock (_activationLock) _activated -= value; }
    }

    /// <summary>The menu/toolbar id GRT sent before any handler was attached (the onDemand launch
    /// click), consumed once. Null once replayed or if none is buffered.</summary>
    public string? PendingActivation { get { lock (_activationLock) return _pendingActivation; } }

    /// <summary>Raised (background thread) with the GRT UI language code from Event_Attached.</summary>
    public event Action<string>? Attached;

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
                {
                    string id = first.Value.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                    Action<string>? h;
                    lock (_activationLock)
                    {
                        h = _activated;
                        if (h == null) { _pendingActivation = id; Log?.Invoke($"buffered activation '{id}' (no handler yet)"); }
                    }
                    h?.Invoke(id);
                }
                else if (name == "Event_Attached")
                {
                    string lc = first.Value.TryGetProperty("LanguageCode", out var l) ? l.GetString() ?? ""
                              : first.Value.TryGetProperty("languagecode", out var l2) ? l2.GetString() ?? "" : "";
                    Attached?.Invoke(lc);
                }
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

    /// <summary>Scalar simulation results for the charge/curve currently shown in the given tab.</summary>
    public async Task<GrtResults> GetTabResultsAsync(long tabHandle)
    {
        JsonElement result = await RequestAsync(
            "{\"Get_TabResults\":{\"tabhandle\":\"" + tabHandle.ToString(CultureInfo.InvariantCulture) + "\"}}",
            TimeSpan.FromSeconds(6));
        if (GetStatus(result) != "success")
            throw new InvalidOperationException("Get_TabResults failed: " + GetMessage(result));

        JsonElement v = result.GetProperty("values");
        JsonElement d = v.TryGetProperty("data", out var dd) ? dd : v;

        // GRT opened a chunk stream for the P/V/t curve — close it, we only want scalars.
        // MUST be an awaited request (not fire-and-forget): a raw write here races with the next
        // command on the socket and GRT then rejects the garbled bytes ("bad request").
        if (Str(v, "chunkStreamHandle") is { } cs && cs != "0")
        {
            try { await RequestAsync("{\"Close_ChunkStream\":{\"chunkStreamHandle\":\"" + cs + "\"}}", TimeSpan.FromSeconds(3)); }
            catch { /* best effort */ }
        }

        return new GrtResults
        {
            MuzzleVelocityMps = ValueOf(d, "MuzzleVelocity") ?? ValueOf(d, "EndVelocity"),
            MaxPressure = ValueOf(d, "MaxPressure"),
            MaxPressureUnit = UnitOf(d, "MaxPressure"),
            BarrelTimeMs = ValueOf(d, "BarrelTime") ?? ValueOf(d, "EndTime"),
            BurnRatio = ValueOf(d, "BurnRatio"),
            LoadRatio = ValueOf(d, "LoadRatio"),
        };
    }


    /// <summary>"850.5 m/s" → 850.5, "2,850 fps" → 2850 (first numeric token, unit stripped).</summary>
    private static double? ValueOf(JsonElement obj, string name)
        // fully qualified: the local Str(JsonElement, string) helper hides GrtPluginKit.Util.Str.
        => GrtPluginKit.Util.Str.ParseNumber(Str(obj, name));

    private static string UnitOf(JsonElement obj, string name)
    {
        string? s = Str(obj, name);
        if (string.IsNullOrWhiteSpace(s)) return "";
        int sp = s.IndexOf(' ');
        return sp >= 0 ? s[(sp + 1)..].Trim() : "";
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
