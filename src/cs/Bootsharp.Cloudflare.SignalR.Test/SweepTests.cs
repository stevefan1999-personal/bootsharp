namespace Bootsharp.Cloudflare.SignalR.Tests;

/// <summary>
/// The Durable Object alarm sweep, which replaces upstream's <c>CheckClientTimeout</c> heartbeat.
/// A timer would not survive hibernation; an alarm does, and liveness is read from the
/// auto-response timestamp workerd stamps without ever waking the actor.
/// </summary>
public class SweepTests
{
    private readonly FakeTransport transport = new();
    private readonly HubRecorder recorder = new();
    private double clock;

    [Fact]
    public async Task ASocketThatNeverHandshakesIsClosedAfterTheHandshakeTimeout ()
    {
        var handler = Handler();
        transport.Accept("c1");
        await handler.AcceptAsync("c1");
        clock = 15_001;
        Assert.Equal(1, await handler.SweepAsync());
        Assert.Equal(("c1", 1001, "handshake timeout"), transport.Closes.Single());
    }

    [Fact]
    public async Task ASocketInsideTheHandshakeTimeoutIsLeftAlone ()
    {
        var handler = Handler();
        transport.Accept("c1");
        await handler.AcceptAsync("c1");
        clock = 14_999;
        Assert.Equal(0, await handler.SweepAsync());
        Assert.Empty(transport.Closes);
    }

    [Fact]
    public async Task AHandshakenSocketWithNoRecentPingIsClosedAfterTheClientTimeout ()
    {
        var handler = Handler();
        await Connect(handler, "c1");
        clock = 30_001;
        Assert.Equal(1, await handler.SweepAsync());
        Assert.Equal(("c1", 1001, "client timeout"), transport.Closes.Single());
    }

    [Fact]
    public async Task AnAbsorbedPingKeepsAHandshakenSocketAlive ()
    {
        // The whole cost model of: the ping never reaches the actor, so the only
        // evidence of liveness is getWebSocketAutoResponseTimestamp — which the sweep reads.
        var handler = Handler();
        await Connect(handler, "c1");
        transport.SetLastSeen("c1", 29_000);
        clock = 30_001;
        Assert.Equal(0, await handler.SweepAsync());
        Assert.Empty(transport.Closes);
    }

    [Fact]
    public async Task AnAlreadyClosedSocketIsNotClosedTwice ()
    {
        var handler = Handler();
        await Connect(handler, "c1");
        transport.Close("c1", 1000, "gone");
        clock = 30_001;
        Assert.Equal(0, await handler.SweepAsync());
        Assert.Single(transport.Closes);
    }

    [Fact]
    public async Task TimeoutsAreConfigurable ()
    {
        var handler = new HubConnectionHandler<TestHub>(new TestHubDispatcher(recorder), transport,
            new HubOptions { HandshakeTimeout = TimeSpan.FromSeconds(1) }, () => clock);
        transport.Accept("c1");
        await handler.AcceptAsync("c1");
        clock = 1_001;
        Assert.Equal(1, await handler.SweepAsync());
    }

    private HubConnectionHandler<TestHub> Handler () =>
        new(new TestHubDispatcher(recorder), transport, null, () => clock);

    private async Task Connect (HubConnectionHandler<TestHub> handler, string connectionId)
    {
        transport.Accept(connectionId);
        await handler.AcceptAsync(connectionId);
        await handler.ReceiveAsync(connectionId, Wire.HandshakeRequest);
    }
}
