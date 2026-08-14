using Microsoft.AspNetCore.SignalR;

namespace Bootsharp.Cloudflare.SignalR.Tests;

/// <summary>
/// <c>DefaultHubLifetimeManager</c>'s semantics with "one server process" reinterpreted as "one
/// Durable Object instance". The two properties worth asserting hardest are the ones that are not
/// upstream's: group membership lives in the socket attachment, and every send filters on the
/// handshake flag.
/// </summary>
public class LifetimeManagerTests
{
    private readonly FakeTransport transport = new();
    private readonly HubRecorder recorder = new();
    private readonly HubConnectionHandler<TestHub> handler;

    public LifetimeManagerTests ()
    {
        handler = new HubConnectionHandler<TestHub>(new TestHubDispatcher(recorder), transport);
    }

    [Fact]
    public async Task SendAllReachesEveryHandshakenConnection ()
    {
        await Connect("c1");
        await Connect("c2");
        await handler.Lifetime.SendAllAsync("receive", ["hi"]);
        Assert.Contains("\"target\":\"receive\"", transport.MessagesOf("c1")[^1]);
        Assert.Contains("\"target\":\"receive\"", transport.MessagesOf("c2")[^1]);
    }

    [Fact]
    public async Task SendAllSkipsASocketThatHasNotHandshakenYet ()
    {
        // The JS client routes the first frame it receives to its handshake parser unconditionally,
        // so an early broadcast does not arrive early — it breaks the connection.
        await Connect("c1");
        transport.Accept("c2");
        await handler.AcceptAsync("c2");
        await handler.Lifetime.SendAllAsync("receive", ["hi"]);
        Assert.Single(transport.FramesOf("c1"), static f => f.Contains("receive"));
        Assert.Empty(transport.FramesOf("c2"));
    }

    [Fact]
    public async Task SendAllSkipsAClosedSocket ()
    {
        await Connect("c1");
        await Connect("c2");
        transport.Close("c2", 1000, null);
        var before = transport.FramesOf("c2").Length;
        await handler.Lifetime.SendAllAsync("receive", ["hi"]);
        Assert.Equal(before, transport.FramesOf("c2").Length);
    }

    [Fact]
    public async Task SendAllExceptOmitsTheExcludedConnection ()
    {
        await Connect("c1");
        await Connect("c2");
        await handler.Lifetime.SendAllExceptAsync("receive", ["hi"], ["c2"]);
        Assert.Single(transport.FramesOf("c1"), static f => f.Contains("receive"));
        Assert.DoesNotContain(transport.FramesOf("c2"), static f => f.Contains("receive"));
    }

    [Fact]
    public async Task GroupSendReachesOnlyMembers ()
    {
        await Connect("c1");
        await Connect("c2");
        await handler.Lifetime.AddToGroupAsync("c1", "room");
        await handler.Lifetime.SendGroupAsync("room", "receive", ["hi"]);
        Assert.Single(transport.FramesOf("c1"), static f => f.Contains("receive"));
        Assert.DoesNotContain(transport.FramesOf("c2"), static f => f.Contains("receive"));
    }

    [Fact]
    public async Task RemovingFromAGroupStopsTheSends ()
    {
        await Connect("c1");
        await handler.Lifetime.AddToGroupAsync("c1", "room");
        await handler.Lifetime.RemoveFromGroupAsync("c1", "room");
        await handler.Lifetime.SendGroupAsync("room", "receive", ["hi"]);
        Assert.DoesNotContain(transport.FramesOf("c1"), static f => f.Contains("receive"));
    }

    [Fact]
    public async Task GroupMembershipLivesInTheAttachmentAndSurvivesTheIsolate ()
    {
        await Connect("c1");
        await handler.Lifetime.AddToGroupAsync("c1", "room");
        // A fresh handler is what a hibernation wake produces: new isolate objects, same sockets.
        var revived = new HubConnectionHandler<TestHub>(new TestHubDispatcher(recorder), transport);
        await revived.Lifetime.SendGroupAsync("room", "receive", ["after wake"]);
        Assert.Contains("after wake", transport.MessagesOf("c1")[^1]);
    }

    [Fact]
    public async Task GroupsAreJoinableFromInsideAHubMethod ()
    {
        await Connect("c1");
        await handler.ReceiveAsync("c1", Wire.Frame(
            "{\"type\":1,\"target\":\"Join\",\"arguments\":[\"room\"]}"));
        await handler.Lifetime.SendGroupAsync("room", "receive", ["hi"]);
        Assert.Contains("receive", transport.MessagesOf("c1")[^1]);
    }

    [Fact]
    public async Task ClientsAllFromInsideAHubMethodReachesEveryone ()
    {
        await Connect("c1");
        await Connect("c2");
        await handler.ReceiveAsync("c1", Wire.Frame(
            "{\"type\":1,\"target\":\"Broadcast\",\"arguments\":[\"everyone\"]}"));
        Assert.Contains("everyone", transport.MessagesOf("c1")[^1]);
        Assert.Contains("everyone", transport.MessagesOf("c2")[^1]);
    }

    [Fact]
    public async Task ConnectionSendAddressesOneSocket ()
    {
        await Connect("c1");
        await Connect("c2");
        await handler.Lifetime.SendConnectionAsync("c2", "receive", ["only you"]);
        Assert.DoesNotContain(transport.FramesOf("c1"), static f => f.Contains("receive"));
        Assert.Contains("only you", transport.MessagesOf("c2")[^1]);
    }

    [Fact]
    public async Task TheFrameIsSerializedOnceAndFannedOut ()
    {
        // Upstream's SerializedHubMessage cache collapsed to a string, which is all it is with one
        // protocol on the wire: the two sockets must receive byte-identical frames.
        await Connect("c1");
        await Connect("c2");
        await handler.Lifetime.SendAllAsync("receive", ["hi"]);
        Assert.Equal(transport.FramesOf("c1")[^1], transport.FramesOf("c2")[^1]);
    }

    [Fact]
    public async Task GroupCapIsRefusedLoudlyRatherThanTruncatedAtTheWorkerdBoundary ()
    {
        await Connect("c1");
        for (var index = 0; index < 64; index++)
            await handler.Lifetime.AddToGroupAsync("c1", $"room{index}");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.Lifetime.AddToGroupAsync("c1", "one too many"));
    }

    [Fact]
    public async Task CloseAsyncSendsTheCloseFrameBeforeClosingTheSocket ()
    {
        await Connect("c1");
        await handler.CloseAsync("c1", "going away");
        Assert.Contains("\"type\":7", transport.MessagesOf("c1")[^1]);
        Assert.Equal(("c1", 1000, "going away"), transport.Closes.Single());
    }

    [Fact]
    public async Task DisconnectRaisesTheHubCallbackWithTheReason ()
    {
        await Connect("c1");
        await handler.DisconnectAsync("c1", "network");
        Assert.False(((DurableObjectHubLifetimeManager<TestHub>)handler.Lifetime)
            .TryGetConnection("c1", out _));
    }

    [Fact]
    public async Task HubContextIsUsableWithNoCallingConnection ()
    {
        await Connect("c1");
        IHubContext<TestHub> context = new HubContext<TestHub>(handler.Lifetime);
        await context.Clients.All.SendAsync("receive", "from outside");
        Assert.Contains("from outside", transport.MessagesOf("c1")[^1]);
    }

    private async Task Connect (string connectionId)
    {
        transport.Accept(connectionId);
        await handler.AcceptAsync(connectionId);
        await handler.ReceiveAsync(connectionId, Wire.HandshakeRequest);
    }
}
