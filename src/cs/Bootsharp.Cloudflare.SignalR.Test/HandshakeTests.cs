using System.Text.Json;

namespace Bootsharp.Cloudflare.SignalR.Tests;

/// <summary>
/// The handshake is the one part of the protocol with no room to be approximately right: the JS
/// client routes the very first frame it receives to its handshake parser unconditionally
/// (<c>HubConnection.ts:647-655</c>), so a frame that arrives before the response — or a response
/// that is not the 3 bytes <c>7B 7D 1E</c> — is a broken connection rather than a late one.
/// </summary>
public class HandshakeTests
{
    private readonly FakeTransport transport = new();
    private readonly HubRecorder recorder = new();
    private readonly HubConnectionHandler<TestHub> handler;

    public HandshakeTests ()
    {
        handler = new HubConnectionHandler<TestHub>(new TestHubDispatcher(recorder), transport);
    }

    [Fact]
    public async Task SuccessfulHandshakeAnswersWithExactlyTheThreeByteResponse ()
    {
        await Accept("c1");
        await handler.ReceiveAsync("c1", Wire.HandshakeRequest);
        Assert.Equal([Wire.HandshakeSuccess], transport.FramesOf("c1"));
    }

    [Fact]
    public async Task HandshakeResponseIsTheFirstFrameTheConnectionEverReceives ()
    {
        await Accept("c1");
        // A broadcast issued before the handshake must not reach the socket, which is what the
        // lifetime manager's Handshaken filter is for. The assertion is on frame ORDER, not on
        // presence: an early invocation frame would break the client's handshake parse.
        await handler.Lifetime.SendAllAsync("receive", ["early"]);
        await handler.ReceiveAsync("c1", Wire.HandshakeRequest);
        Assert.Equal(Wire.HandshakeSuccess, transport.FramesOf("c1")[0]);
    }

    [Fact]
    public async Task VersionTwoIsAcceptedAsWellAsVersionOne ()
    {
        await Accept("c1");
        await handler.ReceiveAsync("c1", "{\"protocol\":\"json\",\"version\":2}\u001e");
        Assert.Equal([Wire.HandshakeSuccess], transport.FramesOf("c1"));
    }

    [Fact]
    public async Task UnsupportedVersionIsRefusedWithAnErrorAndAClose ()
    {
        await Accept("c1");
        await handler.ReceiveAsync("c1", "{\"protocol\":\"json\",\"version\":9}\u001e");
        Assert.Contains("does not support version 9", Error(transport.MessagesOf("c1").Single()));
        Assert.Equal(("c1", 1002, "handshake failed"), transport.Closes.Single());
    }

    [Fact]
    public async Task UnsupportedProtocolIsRefusedWithAnErrorAndAClose ()
    {
        await Accept("c1");
        await handler.ReceiveAsync("c1", "{\"protocol\":\"messagepack\",\"version\":1}\u001e");
        Assert.Contains("'messagepack' is not supported", Error(transport.MessagesOf("c1").Single()));
        Assert.Equal(("c1", 1002, "handshake failed"), transport.Closes.Single());
    }

    [Fact]
    public async Task UnterminatedHandshakeFrameIsRefusedRatherThanBuffered ()
    {
        // workerd delivers whole frames and the client has no partial-frame buffer, so an
        // unterminated handshake is malformed rather than a partial read to be continued.
        await Accept("c1");
        await handler.ReceiveAsync("c1", "{\"protocol\":\"json\",\"version\":1}");
        Assert.Equal("Handshake was canceled.", Error(transport.MessagesOf("c1").Single()));
    }

    [Fact]
    public async Task MessagesBatchedBehindTheHandshakeInOneFrameAreDispatched ()
    {
        // parseHandshakeResponse splits at the first 0x1E and hands the remainder to the normal
        // parser, so batching is legal in both directions and dropping the tail would lose an
        // invocation the client considers sent.
        await Accept("c1");
        await handler.ReceiveAsync("c1",
            Wire.HandshakeRequest + Invocation("1", "Echo", "\"batched\""));
        var messages = transport.MessagesOf("c1");
        Assert.Equal("{}", messages[0]);
        Assert.Contains("batched", messages[1]);
    }

    [Fact]
    public async Task HandshakeFlagSurvivesInTheAttachmentRatherThanInTheIsolate ()
    {
        await Accept("c1");
        await handler.ReceiveAsync("c1", Wire.HandshakeRequest);
        // A wake rebuilds every C# object; the attachment is the only thing that survives it.
        var revived = new HubConnectionHandler<TestHub>(new TestHubDispatcher(recorder), transport);
        await revived.ReceiveAsync("c1", Invocation("1", "Echo", "\"after wake\""));
        Assert.Contains("after wake", transport.MessagesOf("c1")[^1]);
        // And the second handler must not answer the handshake a second time.
        Assert.Single(transport.MessagesOf("c1"), static m => m == "{}");
    }

    private Task Accept (string connectionId)
    {
        transport.Accept(connectionId);
        return handler.AcceptAsync(connectionId);
    }

    private static string Invocation (string id, string target, string args) =>
        Wire.Frame($"{{\"type\":1,\"invocationId\":\"{id}\",\"target\":\"{target}\",\"arguments\":[{args}]}}");

    private static string Error (string message) =>
        JsonDocument.Parse(message).RootElement.GetProperty("error").GetString()!;
}
