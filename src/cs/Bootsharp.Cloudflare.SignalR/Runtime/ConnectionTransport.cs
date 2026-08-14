using System.Buffers;
using System.Text;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace Bootsharp.Cloudflare.SignalR;

/// <summary>
/// What a hub needs from the sockets underneath it, narrowed to the operations workerd actually
/// offers — all of them synchronous, which is what makes a read-modify-write of connection state
/// safe on a platform that interleaves events at every non-storage await (see
/// <see cref="IHibernation"/>.
/// </summary>
/// <remarks>
/// Separating this from <see cref="IHibernation"/> is what lets the whole hub layer — dispatcher,
/// lifetime manager, handshake driver — be exercised without a workerd handle, and it is the seam a
/// non-hibernation transport would implement.
/// </remarks>
public interface IConnectionTransport
{
    /// <summary>Every accepted socket, in <c>getWebSockets()</c> order.</summary>
    IReadOnlyList<TransportSocket> Sockets ();

    /// <summary>The named socket's attachment, or null when the socket is gone.</summary>
    string? Attachment (string connectionId);

    /// <summary>Replaces the named socket's attachment; ignored when the socket is gone.</summary>
    void Attach (string connectionId, string attachment);

    /// <summary>Sends one whole text frame. False when the socket is gone.</summary>
    bool SendRaw (string connectionId, string frame);

    /// <summary>Closes the named socket. False when there was none.</summary>
    bool Close (string connectionId, int code, string? reason);

    /// <summary>When the socket last matched the auto-response ping, epoch ms, or 0.</summary>
    double LastSeen (string connectionId);
}

/// <summary>One socket as the transport reports it.</summary>
public readonly record struct TransportSocket (string ConnectionId, string? Attachment, double LastSeen, bool Open);

/// <summary>Framing helpers shared by every send and receive path.</summary>
public static class HubFraming
{
    /// <summary>
    /// Serializes one hub message into its wire frame, <c>0x1E</c> terminator included. Called once
    /// per broadcast rather than once per socket — the single-protocol collapse of upstream's
    /// <c>SerializedHubMessage</c> cache.
    /// </summary>
    public static string Frame (this IHubProtocol protocol, HubMessage message) =>
        Encoding.UTF8.GetString(protocol.GetMessageBytes(message).Span);

    /// <summary>Reads a received text frame as the sequence the protocol parsers expect.</summary>
    public static ReadOnlySequence<byte> AsSequence (string message) => new(Encoding.UTF8.GetBytes(message));
}
