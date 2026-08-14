namespace Bootsharp.Cloudflare;

/// <summary>
/// The Workers-native hibernation WebSocket surface of a Durable Object: the socket
/// set, the per-socket attachment, the send/close pair and the auto-response absorber. Reached off
/// <see cref="IDurableObjectState.Hibernation"/>.
/// </summary>
/// <remarks>
/// <para>Deliberately not a <c>System.Net.WebSockets.WebSocket</c> and deliberately not a handle
/// per socket. A hibernation socket outlives the isolate that accepted it, so there is no
/// <c>ReceiveAsync</c> to be honest about, and a per-socket handle would be an isolate
/// -scoped import per connection with no event that ever releases it.</para>
/// <para>Sockets are addressed by <b>connection id</b>, which the accepting JavaScript sets as the
/// socket's first <c>acceptWebSocket</c> tag. Tags are immutable after accept
/// (<c>actor-state.c++:1233</c>) — which is exactly right for an identity and exactly wrong for
/// group membership, so groups live in the attachment instead ( connection state
/// lives").</para>
/// <para><b>Every member here is synchronous</b>, and that is load-bearing rather than incidental.
/// workerd holds the actor input gate only across Durable Object storage awaits; a second
/// <c>webSocketMessage</c> event enters at any other await point (measured — see
/// <c>src/js/test/do-interleave</c>). A lifetime manager that awaited between reading the socket
/// set and writing an attachment would be racing itself. It cannot, because none of this awaits.
/// </para>
/// </remarks>
[JSHandle(Scope = HandleScope.Isolate)]
public interface IHibernation
{
    /// <summary>Snapshot of every socket this actor has accepted, in <c>getWebSockets()</c> order.</summary>
    HibernatedSocket[] Sockets ();

    /// <summary>
    /// The named socket's attachment, or null when no such socket is accepted. Separate from
    /// <see cref="Sockets"/> so a targeted send does not pay for a full snapshot.
    /// </summary>
    string? Attachment (string connectionId);

    /// <summary>
    /// Replaces the named socket's attachment. Silently ignored when the socket is gone: a
    /// connection can close between any two synchronous statements of a hub method's continuation,
    /// and losing the write is the correct outcome — there is nothing left to attach it to.
    /// </summary>
    /// <remarks>workerd caps the serialized value at 16 KiB (<c>web-socket.h:776</c>).</remarks>
    void Attach (string connectionId, string attachment);

    /// <summary>
    /// Sends one text frame, whole. Returns whether the socket was there to receive it.
    /// </summary>
    /// <remarks>
    /// One call is one frame: the SignalR JS client has no partial-frame buffer and throws
    /// "Message is incomplete." on a frame not ending in <c>0x1E</c>
    /// (<c>TextMessageFormat.ts:15-17</c>), so a caller must never chunk.
    /// </remarks>
    bool Send (string connectionId, string message);

    /// <summary>Closes the named socket. Returns whether there was one to close.</summary>
    bool Close (string connectionId, int code, string? reason);

    /// <summary>
    /// Installs the auto-response pair: an incoming <b>text</b> frame equal to
    /// <paramref name="request"/> is answered with <paramref name="response"/> inside workerd's
    /// hibernation read loop, without waking the actor and without entering the isolate
    /// (<c>legacy-hibernation-manager.c++:317-376</c>). Both are capped at 2048 bytes.
    /// </summary>
    void AutoRespond (string request, string response);

    /// <summary>
    /// When the named socket last matched the auto-response pair, as epoch milliseconds, or 0 when
    /// it never has. The liveness signal that replaces upstream's heartbeat-driven client timeout.
    /// </summary>
    double AutoRespondedAt (string connectionId);

    /// <summary>Per-event handler wall-time budget for hibernation events; capped at 7 days.</summary>
    void SetEventTimeout (double milliseconds);
}

/// <summary>One accepted hibernation socket, as <see cref="IHibernation.Sockets"/> snapshots it.</summary>
/// <param name="ConnectionId">The socket's first accept tag, empty when it was accepted without one.</param>
/// <param name="Attachment">Whatever was last attached, or null.</param>
/// <param name="AutoRespondedAt">See <see cref="IHibernation.AutoRespondedAt"/>.</param>
/// <param name="Open">Whether the socket is still in <c>readyState</c> OPEN.</param>
public sealed record HibernatedSocket (
    string ConnectionId,
    string? Attachment,
    double AutoRespondedAt,
    bool Open);
