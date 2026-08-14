using System.Text.Json;

namespace Bootsharp.Cloudflare.SignalR.Tests;

/// <summary>
/// The regression test for the finding that reshaped this package. asked for a harness
/// proving ordered, non-interleaved hub dispatch; <c>src/js/test/do-interleave</c> refuted it
/// three frames down one socket awaiting 300/200/60 ms arrive 1,2,3 and complete 3,2,1 at depth 3,
/// because workerd releases the actor input gate at every await that is not Durable Object storage.
/// <see cref="ConnectionDispatchQueue"/> is what supplies the ordering workerd does not, and this is
/// the assertion that it does.
/// </summary>
public class OrderingTests
{
    private readonly FakeTransport transport = new();
    private readonly HubRecorder recorder = new();
    private readonly HubConnectionHandler<TestHub> handler;

    public OrderingTests ()
    {
        handler = new HubConnectionHandler<TestHub>(new TestHubDispatcher(recorder), transport);
    }

    [Fact]
    public async Task OneConnectionsFramesCompleteInArrivalOrderEvenWhenTheirAwaitsDoNot ()
    {
        await Connect("c1");
        recorder.Gate("a");
        recorder.Gate("b");
        recorder.Gate("c");
        // Arrival order a, b, c. Each is enqueued the way workerd's webSocketMessage handler does:
        // the task is taken but not awaited, so all three are in flight at once.
        var first = Send("c1", "1", "a");
        var second = Send("c1", "2", "b");
        var third = Send("c1", "3", "c");
        // Completion order c, b, a — the exact inversion the harness measured.
        recorder.Gates["c"].SetResult();
        recorder.Gates["b"].SetResult();
        recorder.Gates["a"].SetResult();
        await Task.WhenAll(first, second, third);
        Assert.Equal(["enter:a", "exit:a", "enter:b", "exit:b", "enter:c", "exit:c"], recorder.Calls);
        Assert.Equal(["a", "b", "c"], Results("c1"));
    }

    [Fact]
    public async Task ASecondFrameIsNotEnteredWhileTheFirstIsSuspended ()
    {
        // peakDepth > 1 IS interleaving, in the harness's terms. Here it must stay 1.
        await Connect("c1");
        recorder.Gate("a");
        recorder.Gate("b");
        var first = Send("c1", "1", "a");
        var second = Send("c1", "2", "b");
        Assert.Equal(["enter:a"], recorder.Calls);
        recorder.Gates["a"].SetResult();
        await first;
        recorder.Gates["b"].SetResult();
        await second;
        Assert.Equal(["enter:a", "exit:a", "enter:b", "exit:b"], recorder.Calls);
    }

    [Fact]
    public async Task TwoConnectionsAreNotSerializedAgainstEachOther ()
    {
        // Cross-connection concurrency is correct — it is what "one server process, many
        // connections" means — and one queue per Durable Object would park every connection behind
        // one slow hub method.
        await Connect("c1");
        await Connect("c2");
        recorder.Gate("a");
        recorder.Gate("b");
        var first = Send("c1", "1", "a");
        var second = Send("c2", "1", "b");
        Assert.Equal(["enter:a", "enter:b"], recorder.Calls);
        recorder.Gates["b"].SetResult();
        await second;
        recorder.Gates["a"].SetResult();
        await first;
        Assert.Equal(["a"], Results("c1"));
        Assert.Equal(["b"], Results("c2"));
    }

    [Fact]
    public async Task AFaultedFrameDoesNotPoisonTheQueueBehindIt ()
    {
        // Upstream drops one failed invocation and keeps the connection; a completion error has
        // already been reported to the client before the queue ever sees the fault.
        await Connect("c1");
        await handler.ReceiveAsync("c1", Wire.Frame(Invocation("1", "Explode", "")));
        await handler.ReceiveAsync("c1", Wire.Frame(Invocation("2", "Echo", "\"still here\"")));
        Assert.Equal("still here", Results("c1")[^1]);
    }

    [Fact]
    public async Task DisconnectForgetsTheQueueSoItsLastTaskIsNotHeldForTheIsolatesLife ()
    {
        await Connect("c1");
        await handler.ReceiveAsync("c1", Wire.Frame(Invocation("1", "Echo", "\"x\"")));
        await handler.DisconnectAsync("c1", null);
        Assert.False(((DurableObjectHubLifetimeManager<TestHub>)handler.Lifetime).TryGetConnection("c1", out _));
    }

    private async Task Connect (string connectionId)
    {
        transport.Accept(connectionId);
        await handler.AcceptAsync(connectionId);
        await handler.ReceiveAsync(connectionId, Wire.HandshakeRequest);
    }

    private Task Send (string connectionId, string invocationId, string key) =>
        handler.ReceiveAsync(connectionId, Wire.Frame(Invocation(invocationId, "Gated", $"\"{key}\"")));

    private string[] Results (string connectionId) =>
    [
        .. transport.MessagesOf(connectionId)
            .Select(static message => JsonDocument.Parse(message).RootElement)
            .Where(static element => element.TryGetProperty("result", out _))
            .Select(static element => element.GetProperty("result").GetString()!)
    ];

    private static string Invocation (string id, string target, string args) =>
        $"{{\"type\":1,\"invocationId\":\"{id}\",\"target\":\"{target}\",\"arguments\":[{args}]}}";
}
