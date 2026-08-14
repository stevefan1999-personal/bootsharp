using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace Microsoft.AspNetCore.SignalR;

/// <summary>
/// Reimplementation of upstream's <c>HubConnectionContext</c> at roughly an eighth of its size
/// (922 lines → this). Upstream is built on a <c>ConnectionContext</c> duplex pipe, a
/// <c>SemaphoreSlim</c> write lock, a <c>ChannelBasedSemaphore</c> invocation limiter and two
/// <c>IConnectionHeartbeatFeature</c> ticks; none of those exist here and none of them mean
/// anything in a single-threaded isolate.
/// </summary>
/// <remarks>
/// <para>What replaces each of them:</para>
/// <list type="bullet">
/// <item>the pipe — workerd delivers whole, pre-framed <c>webSocketMessage</c> events;</item>
/// <item>the write lock — <c>ws.send</c> is synchronous and one call is one whole frame;</item>
/// <item>the invocation limiter (upstream default <b>1</b>, <c>HubOptions.cs:68-78</c>) — the
/// per-connection FIFO in <see cref="Bootsharp.Cloudflare.SignalR.ConnectionDispatchQueue"/>,
/// which reproduces that default rather than inventing a policy;</item>
/// <item>the keepalive tick — workerd's <c>setWebSocketAutoResponse</c> answers the client's ping
/// without waking the actor at all;</item>
/// <item>the client-timeout tick — the Durable Object alarm sweep.</item>
/// </list>
/// <para>The instance is per isolate, not per connection lifetime: a hibernation wake rebuilds it
/// from the socket attachment, which is why every durable field of it lives in
/// <see cref="Bootsharp.Cloudflare.SignalR.ConnectionState"/> and not here.</para>
/// </remarks>
public sealed class HubConnectionContext
{
    private readonly Bootsharp.Cloudflare.SignalR.IConnectionTransport transport;
    private readonly CancellationTokenSource aborted = new();
    private IDictionary<object, object?>? items;
    private IFeatureCollection? features;

    internal HubConnectionContext (
        string connectionId,
        Bootsharp.Cloudflare.SignalR.IConnectionTransport transport,
        IHubProtocol protocol,
        ClaimsPrincipal? user,
        string? userIdentifier)
    {
        ConnectionId = connectionId;
        this.transport = transport;
        Protocol = protocol;
        User = user;
        UserIdentifier = userIdentifier;
    }

    /// <summary>Gets the connection ID: the socket's immutable first accept tag.</summary>
    public string ConnectionId { get; }

    /// <summary>The negotiated hub protocol. JSON is the only one this host offers.</summary>
    public IHubProtocol Protocol { get; }

    /// <summary>The user the worker authenticated, or null when the connection is anonymous.</summary>
    public ClaimsPrincipal? User { get; internal set; }

    /// <summary>The user id sends are addressed by.</summary>
    public string? UserIdentifier { get; internal set; }

    /// <summary>
    /// Per-connection scratch space. Isolate-lived, unlike the attachment: it is upstream's
    /// <c>Items</c>, whose contract is already "for the scope of this connection" on a server that
    /// can lose it — a Worker just loses it more often.
    /// </summary>
    public IDictionary<object, object?> Items => items ??= new Dictionary<object, object?>();

    /// <summary>
    /// Feature collection. Real <c>Microsoft.Extensions.Features</c> types ( identity
    /// rule), empty by default: none of the connection features upstream installs — pipes,
    /// heartbeats, transfer format, stateful reconnect — exist on this transport, and half-installing
    /// them would let middleware probe successfully for behaviour that is not there.
    /// </summary>
    public IFeatureCollection Features => features ??= new FeatureCollection();

    /// <summary>Fires when the connection is aborted.</summary>
    public CancellationToken ConnectionAborted => aborted.Token;

    /// <summary>Closes the socket. Idempotent — <c>close()</c> on a closing socket is a no-op.</summary>
    public void Abort ()
    {
        if (!aborted.IsCancellationRequested) aborted.Cancel();
        transport.Close(ConnectionId, 1000, "");
    }

    /// <summary>Writes one message as one whole frame.</summary>
    internal bool Write (HubMessage message) =>
        transport.SendRaw(ConnectionId, Bootsharp.Cloudflare.SignalR.HubFraming.Frame(Protocol, message));

    /// <summary>Writes an already-serialized frame, for a broadcast that serialized once.</summary>
    internal bool Write (string frame) => transport.SendRaw(ConnectionId, frame);
}
