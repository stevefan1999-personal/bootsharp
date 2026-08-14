using Microsoft.AspNetCore.SignalR;

namespace Bootsharp.Cloudflare.SignalR;

/// <summary>
/// The Durable Object that hosts one hub scope: a room, tenant, document or shard the application
/// chose with <c>idFromName</c>. One Durable Object <b>class</b> per <see cref="Hub"/>, one
/// <b>instance</b> per scope.
/// </summary>
/// <remarks>
/// <para>Its four public methods are ordinary Bootsharp RPC methods — string in, string out — and
/// that is the whole trick. workerd reserves <c>webSocketMessage</c>, <c>webSocketClose</c>,
/// <c>webSocketError</c> and <c>alarm</c> as prototype members, which the entrypoint generator
/// refuses to project (<c>Projection/Rules.cs</c>); the emitted module wraps the generated class
/// with <c>hubDurableObject</c> from the shipped <c>js/signalr.mjs</c>, which supplies those four
/// handlers and forwards each to the RPC method below. A <c>[HubRoute]</c> on the class also
/// generates the worker-side negotiate and upgrade routing, so the app writes no JavaScript.
/// The calls go through exactly the gate every other actor call goes through.</para>
/// <para>One DO per connection would be wrong: it makes every group send a cross-DO fan-out.</para>
/// </remarks>
public abstract class HubDurableObject<THub, TEnv> : DurableObject<TEnv>
    where THub : Hub
    where TEnv : class
{
    private readonly HubConnectionHandler<THub> handler;

    protected HubDurableObject (IDurableObjectState ctx, TEnv env) : base(ctx, env)
    {
        handler = new HubConnectionHandler<THub>(CreateDispatcher(), new HibernationTransport(ctx.Hibernation), CreateOptions());
    }

    /// <summary>
    /// The generated dispatch table for <typeparamref name="THub"/>. An app writes
    /// <c>protected override HubDispatcher&lt;ChatHub&gt; CreateDispatcher () => new ChatHubDispatcher();</c>
    /// naming the class the hub generator emitted.
    /// </summary>
    protected abstract HubDispatcher<THub> CreateDispatcher ();

    /// <summary>Overridden to enable detailed errors, change the timeouts, or add a JSON resolver.</summary>
    protected virtual HubOptions CreateOptions () => new();

    /// <summary>An <c>IHubContext&lt;THub&gt;</c> for this scope, usable outside a hub method.</summary>
    protected IHubContext<THub> Hub => handler.Context;

    /// <summary>Registers a socket the JavaScript half has just accepted. Sends nothing.</summary>
    public Task Accept (string connectionId) => handler.AcceptAsync(connectionId);

    /// <summary>
    /// One received text frame, dispatched behind whatever is already in flight for this
    /// connection. The JavaScript handler awaits the returned task, which is what keeps the workerd
    /// event alive for the whole dispatch and gives the connection backpressure.
    /// </summary>
    public Task Deliver (string connectionId, string message) => handler.ReceiveAsync(connectionId, message);

    /// <summary>A closed or errored socket.</summary>
    public Task Disconnect (string connectionId, string? reason) => handler.DisconnectAsync(connectionId, reason);

    /// <summary>The alarm sweep; returns how many sockets it closed.</summary>
    public Task<int> Sweep () => handler.SweepAsync();
}

/// <summary>
/// <see cref="IConnectionTransport"/> over the Durable Object hibernation surface. Every member is
/// a straight forward of a synchronous workerd call — which is the property the lifetime manager's
/// read-modify-write correctness rests on.
/// </summary>
internal sealed class HibernationTransport (IHibernation hibernation) : IConnectionTransport
{
    public IReadOnlyList<TransportSocket> Sockets ()
    {
        var snapshot = hibernation.Sockets();
        var sockets = new TransportSocket[snapshot.Length];
        for (var index = 0; index < snapshot.Length; index++)
            sockets[index] = new TransportSocket(snapshot[index].ConnectionId, snapshot[index].Attachment,
                snapshot[index].AutoRespondedAt, snapshot[index].Open);
        return sockets;
    }

    public string? Attachment (string connectionId) => hibernation.Attachment(connectionId);
    public void Attach (string connectionId, string attachment) => hibernation.Attach(connectionId, attachment);
    public bool SendRaw (string connectionId, string frame) => hibernation.Send(connectionId, frame);
    public bool Close (string connectionId, int code, string? reason) => hibernation.Close(connectionId, code, reason);
    public double LastSeen (string connectionId) => hibernation.AutoRespondedAt(connectionId);
}
