using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using GrtPluginKit.Ipc;
using Xunit;

namespace GrtReloadingToolkit.Tests;

/// <summary>
/// <see cref="GrtClient.LoadFileAndWaitForComputeAsync"/> against a stand-in for GRT on a loopback socket: it answers
/// Load_File and Get_TabOnTop the way GRT's plugin API documents, and sends Event_TabComputed when and for which tab a
/// test says. The wait must take the event of the tab the file opened in, even one that arrives before Load_File's own
/// answer, ignore another tab's, and fall back to the fixed wait when no event comes.
/// </summary>
public sealed class GrtClientComputeWaitTests
{
    private const long NewTab = 42;

    /// <summary>A minimal GRT: one client, answers by command name, with the events a test scripts around each answer.</summary>
    private sealed class FakeGrt : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly Task _serve;
        public int Port { get; }
        public long[] EventsBeforeLoadReply { get; init; } = Array.Empty<long>();
        public long[] EventsAfterTabOnTop { get; init; } = Array.Empty<long>();
        /// <summary>How long GRT takes to answer Load_File, after the events it sends first.</summary>
        public int LoadReplyDelayMs { get; init; }
        /// <summary>The MuzzleVelocity Get_TabResults reports, unit included, as GRT writes it.</summary>
        public string Velocity { get; init; } = "850.5 m/s";

        public FakeGrt(Action<FakeGrt>? configure = null)
        {
            configure?.Invoke(this);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _serve = Task.Run(ServeAsync);
        }

        private static string Event(long handle) => "{\"Event_TabComputed\":{\"windowhandle\":1,\"tabhandle\":" + handle + "}}";

        private async Task ServeAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync();
            var stream = client.GetStream();
            var buf = new byte[4096];
            var pending = new StringBuilder();
            async Task Send(string s) { var b = Encoding.UTF8.GetBytes(s); await stream.WriteAsync(b); await stream.FlushAsync(); }
            try
            {
                while (true)
                {
                    int n = await stream.ReadAsync(buf);
                    if (n <= 0) return;
                    pending.Append(Encoding.UTF8.GetString(buf, 0, n));
                    string text = pending.ToString();
                    pending.Clear();
                    if (text.Contains("\"Load_File\""))
                    {
                        foreach (long h in EventsBeforeLoadReply) await Send(Event(h));
                        if (LoadReplyDelayMs > 0) await Task.Delay(LoadReplyDelayMs);
                        await Send("{\"Result\":{\"command\":\"Load_File\",\"status\":\"success\"}}");
                    }
                    else if (text.Contains("\"Get_TabOnTop\""))
                    {
                        await Send("{\"Result\":{\"command\":\"Get_TabOnTop\",\"status\":\"success\",\"values\":{\"tabhandle\":" + NewTab
                                   + ",\"caption\":\"probe\",\"file\":\"C:/loads/probe.grtload\"}}}");
                        foreach (long h in EventsAfterTabOnTop) { await Task.Delay(50); await Send(Event(h)); }
                    }
                    else if (text.Contains("\"Get_TabResults\""))
                    {
                        await Send("{\"Result\":{\"command\":\"Get_TabResults\",\"status\":\"success\",\"values\":{\"tabhandle\":" + NewTab
                                   + ",\"chunked\":true,\"chunkStreamHandle\":0,\"chunkCount\":0,\"data\":{\"MuzzleVelocity\":\"" + Velocity
                                   + "\",\"MaxPressure\":\"3347.5 bar\"}}}}");
                    }
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }

        public void Dispose()
        {
            _listener.Stop();
            try { _serve.Wait(TimeSpan.FromSeconds(2)); } catch { }
        }
    }

    private static async Task<(bool Seen, TimeSpan Took)> Run(FakeGrt grt, TimeSpan maxWait)
    {
        using var client = new GrtClient(grt.Port);
        client.Connect();
        var sw = Stopwatch.StartNew();
        bool seen = await client.LoadFileAndWaitForComputeAsync("C:/loads/probe.grtload", maxWait);
        return (seen, sw.Elapsed);
    }

    [Fact]
    public async Task TheNewTabsEventEndsTheWaitEarly()
    {
        using var grt = new FakeGrt { EventsAfterTabOnTop = new[] { NewTab } };
        // generous on purpose: the check is "it did not wait out maxWait", and a machine busy with the rest of the suite can take seconds to connect
        var (seen, took) = await Run(grt, TimeSpan.FromSeconds(20));
        Assert.True(seen);
        Assert.True(took < TimeSpan.FromSeconds(12), $"waited {took.TotalMilliseconds:0} ms for an event that came at once");
    }

    [Fact]
    public async Task AnEventThatBeatsLoadFilesOwnAnswerIsNotMissed()
    {
        using var grt = new FakeGrt { EventsBeforeLoadReply = new[] { NewTab } };
        var (seen, _) = await Run(grt, TimeSpan.FromSeconds(5));
        Assert.True(seen);
    }

    [Fact]
    public async Task AnotherTabsEventDoesNotCount()
    {
        using var grt = new FakeGrt { EventsBeforeLoadReply = new[] { 7L }, EventsAfterTabOnTop = new[] { 8L } };
        var (seen, took) = await Run(grt, TimeSpan.FromMilliseconds(600));
        Assert.False(seen);
        Assert.True(took >= TimeSpan.FromMilliseconds(500), $"returned after {took.TotalMilliseconds:0} ms on another tab's event");
    }

    [Fact]
    public async Task WithoutAnyEventItWaitsTheFixedTimeAndSaysSo()
    {
        using var grt = new FakeGrt();
        var (seen, took) = await Run(grt, TimeSpan.FromMilliseconds(400));
        Assert.False(seen);
        Assert.True(took >= TimeSpan.FromMilliseconds(350), $"returned after {took.TotalMilliseconds:0} ms instead of the fixed wait");
        Assert.True(took < TimeSpan.FromSeconds(3), $"took {took.TotalMilliseconds:0} ms, far past the fixed wait");
    }

    [Fact]
    public async Task AnEventThatCameWhileGrtWasSlowToAnswerStillCounts()
    {
        // GRT computes and reports, then takes longer than the whole wait to answer Load_File: the event is already
        // queued when the deadline has passed, and must be read rather than thrown away.
        using var grt = new FakeGrt { EventsBeforeLoadReply = new[] { NewTab }, LoadReplyDelayMs = 700 };
        var (seen, _) = await Run(grt, TimeSpan.FromMilliseconds(300));
        Assert.True(seen);
    }

    [Theory]
    [InlineData("850.5 m/s", 850.5)]
    [InlineData("2800 fps", 853.44)]
    [InlineData("2800 ft/s", 853.44)]
    public async Task TheMuzzleVelocityComesBackInMetresPerSecond(string reported, double mps)
    {
        using var grt = new FakeGrt { Velocity = reported };
        using var client = new GrtClient(grt.Port);
        client.Connect();
        var r = await client.GetTabResultsAsync(NewTab);
        Assert.Equal(mps, r.MuzzleVelocityMps!.Value, 6);
        Assert.Equal("bar", r.MaxPressureUnit);
    }

    [Fact]
    public async Task TheOtherTabsEventBeforeTheRightOneIsSkipped()
    {
        using var grt = new FakeGrt { EventsBeforeLoadReply = new[] { 7L }, EventsAfterTabOnTop = new[] { NewTab } };
        var (seen, _) = await Run(grt, TimeSpan.FromSeconds(5));
        Assert.True(seen);
    }
}
