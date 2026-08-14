namespace Bootsharp.Cloudflare.SignalR.Tests;

/// <summary>The wire constants the assertions quote, spelled once and escaped rather than literal.</summary>
internal static class Wire
{
    /// <summary>The record separator that terminates every SignalR frame.</summary>
    public const char Separator = '\u001e';

    /// <summary>The entire successful server handshake response: <c>7B 7D 1E</c>.</summary>
    public const string HandshakeSuccess = "{}\u001e";

    /// <summary>What a default JS client sends first (<c>HubConnection.ts</c> downgrades to v1).</summary>
    public const string HandshakeRequest = "{\"protocol\":\"json\",\"version\":1}\u001e";

    public static string Frame (string json) => json + Separator;
}

/// <summary>
/// <see cref="IConnectionTransport"/> over dictionaries, recording every frame in arrival order.
/// It is the whole reason the hub layer is testable without workerd: the seam is narrow enough that
/// a fake is a page, and every member is synchronous here for the same reason it is synchronous on
/// the real one — a lifetime-manager read-modify-write that could be interleaved is wrong (
/// §3, measured in src/js/test/do-interleave.
/// </summary>
internal sealed class FakeTransport : IConnectionTransport
{
    private readonly Dictionary<string, Socket> sockets = new(StringComparer.Ordinal);
    private readonly List<string> order = [];

    /// <summary>Frames sent to every socket, in the order the transport received them.</summary>
    public List<(string ConnectionId, string Frame)> Sent { get; } = [];

    /// <summary>Closes, in order: connection id, status code and reason.</summary>
    public List<(string ConnectionId, int Code, string? Reason)> Closes { get; } = [];

    /// <summary>Registers an open socket, as <c>ctx.acceptWebSocket</c> would.</summary>
    public FakeTransport Accept (string connectionId, double lastSeen = 0)
    {
        sockets[connectionId] = new Socket { LastSeen = lastSeen };
        order.Add(connectionId);
        return this;
    }

    /// <summary>Frames sent to one socket, without the connection id.</summary>
    public string[] FramesOf (string connectionId) =>
        [.. Sent.Where(sent => sent.ConnectionId == connectionId).Select(static sent => sent.Frame)];

    /// <summary>Every frame sent to one socket, split on the record separator.</summary>
    public string[] MessagesOf (string connectionId) =>
        [.. FramesOf(connectionId).SelectMany(static frame => frame.Split(Wire.Separator))
            .Where(static message => message.Length > 0)];

    public void SetLastSeen (string connectionId, double lastSeen) => sockets[connectionId].LastSeen = lastSeen;

    public IReadOnlyList<TransportSocket> Sockets () =>
        [.. order.Select(id => new TransportSocket(id, sockets[id].Attachment, sockets[id].LastSeen, sockets[id].Open))];

    public string? Attachment (string connectionId) =>
        sockets.TryGetValue(connectionId, out var socket) ? socket.Attachment : null;

    public void Attach (string connectionId, string attachment)
    {
        if (sockets.TryGetValue(connectionId, out var socket)) socket.Attachment = attachment;
    }

    public bool SendRaw (string connectionId, string frame)
    {
        if (!sockets.TryGetValue(connectionId, out var socket) || !socket.Open) return false;
        Sent.Add((connectionId, frame));
        return true;
    }

    public bool Close (string connectionId, int code, string? reason)
    {
        if (!sockets.TryGetValue(connectionId, out var socket)) return false;
        Closes.Add((connectionId, code, reason));
        socket.Open = false;
        return true;
    }

    public double LastSeen (string connectionId) =>
        sockets.TryGetValue(connectionId, out var socket) ? socket.LastSeen : 0;

    private sealed class Socket
    {
        public string? Attachment { get; set; }
        public double LastSeen { get; set; }
        public bool Open { get; set; } = true;
    }
}
