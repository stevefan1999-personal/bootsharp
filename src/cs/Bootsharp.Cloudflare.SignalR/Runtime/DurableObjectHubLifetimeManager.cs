using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace Bootsharp.Cloudflare.SignalR;

/// <summary>
/// <c>DefaultHubLifetimeManager</c>'s semantics with "one server process" reinterpreted as "one
/// Durable Object instance". Upstream keeps a <c>HubConnectionStore</c> and a
/// <c>HubGroupList</c> in two fields of a process-lifetime singleton; here the connection store is
/// <c>ctx.getWebSockets()</c> and group membership is each socket's hibernation attachment.
/// </summary>
/// <remarks>
/// <para><b>Scope, stated plainly:</b> <c>Clients.All</c> means every socket accepted by <i>this</i>
/// Durable Object instance — a room, tenant, document or shard the application chose. That is
/// exactly what <c>DefaultHubLifetimeManager</c> gives you on one ASP.NET Core server, and widening
/// it upstream needs a backplane too
/// (<c>RedisDependencyInjectionExtensions.cs:48</c> replaces this very class).
/// A cross-scope backplane implements this same contract and is a separate package.</para>
/// <para><b>Why nothing here awaits between a read and its dependent write.</b> Cross-connection
/// concurrency is real on workerd: a second <c>webSocketMessage</c> enters while the first hub
/// method is suspended at any await that is not Durable Object storage (measured
/// <c>src/js/test/do-interleave</c>). The per-connection FIFO orders one connection's frames, not
/// two connections' state edits. So the rule this class obeys is: <b>lifetime-manager
/// read-modify-write is synchronous or it is wrong</b>. <c>Sockets()</c>,
/// <c>Attachment()</c>, <c>Attach()</c> and <c>SendRaw()</c> are all synchronous, so every method
/// below completes its edit inside one JavaScript turn and returns an already-completed task.</para>
/// </remarks>
public sealed class DurableObjectHubLifetimeManager<THub> (
    IConnectionTransport transport,
    IHubProtocol protocol) : HubLifetimeManager<THub> where THub : Hub
{
    /// <summary>
    /// Connections whose handshake has completed, by connection id. Only used to answer "is this
    /// connection live in this isolate" — the durable answer is the attachment, which every send
    /// path reads instead, precisely so a broadcast still reaches a hibernated socket.
    /// </summary>
    private readonly Dictionary<string, HubConnectionContext> connections = new(StringComparer.Ordinal);

    internal IConnectionTransport Transport => transport;

    internal bool TryGetConnection (string connectionId, out HubConnectionContext connection) =>
        connections.TryGetValue(connectionId, out connection!);

    public override Task OnConnectedAsync (HubConnectionContext connection)
    {
        connections[connection.ConnectionId] = connection;
        var state = Read(connection.ConnectionId);
        state.Handshaken = true;
        state.UserIdentifier = connection.UserIdentifier;
        Write(connection.ConnectionId, state);
        return Task.CompletedTask;
    }

    public override Task OnDisconnectedAsync (HubConnectionContext connection)
    {
        connections.Remove(connection.ConnectionId);
        // Group membership lives in the attachment, which dies with the socket: there is no
        // HubGroupList to prune, and that is the whole point of putting it there.
        return Task.CompletedTask;
    }

    public override Task SendAllAsync (string methodName, object?[] args, CancellationToken cancellationToken = default) =>
        Broadcast(methodName, args, static _ => true);

    public override Task SendAllExceptAsync (string methodName, object?[] args, IReadOnlyList<string> excludedConnectionIds, CancellationToken cancellationToken = default) =>
        Broadcast(methodName, args, target => !Excluded(excludedConnectionIds, target.ConnectionId));

    public override Task SendConnectionAsync (string connectionId, string methodName, object?[] args, CancellationToken cancellationToken = default) =>
        Broadcast(methodName, args, target => target.ConnectionId == connectionId);

    public override Task SendConnectionsAsync (IReadOnlyList<string> connectionIds, string methodName, object?[] args, CancellationToken cancellationToken = default) =>
        Broadcast(methodName, args, target => connectionIds.Contains(target.ConnectionId, StringComparer.Ordinal));

    public override Task SendGroupAsync (string groupName, string methodName, object?[] args, CancellationToken cancellationToken = default) =>
        Broadcast(methodName, args, target => target.State.InGroup(groupName));

    public override Task SendGroupsAsync (IReadOnlyList<string> groupNames, string methodName, object?[] args, CancellationToken cancellationToken = default) =>
        Broadcast(methodName, args, target => groupNames.Any(target.State.InGroup));

    public override Task SendGroupExceptAsync (string groupName, string methodName, object?[] args, IReadOnlyList<string> excludedConnectionIds, CancellationToken cancellationToken = default) =>
        Broadcast(methodName, args, target =>
            target.State.InGroup(groupName) && !Excluded(excludedConnectionIds, target.ConnectionId));

    public override Task SendUserAsync (string userId, string methodName, object?[] args, CancellationToken cancellationToken = default) =>
        Broadcast(methodName, args, target => target.State.UserIdentifier == userId);

    public override Task SendUsersAsync (IReadOnlyList<string> userIds, string methodName, object?[] args, CancellationToken cancellationToken = default) =>
        Broadcast(methodName, args, target =>
            target.State.UserIdentifier is { } user && userIds.Contains(user, StringComparer.Ordinal));

    public override Task AddToGroupAsync (string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        var state = Read(connectionId);
        if (state.AddGroup(groupName)) Write(connectionId, state);
        return Task.CompletedTask;
    }

    public override Task RemoveFromGroupAsync (string connectionId, string groupName, CancellationToken cancellationToken = default)
    {
        var state = Read(connectionId);
        if (state.RemoveGroup(groupName)) Write(connectionId, state);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Serializes once and fans out — upstream's <c>SerializedHubMessage</c> collapsed to a string,
    /// which is all it is with one protocol on the wire.
    /// </summary>
    private Task Broadcast (string methodName, object?[] args, Func<Target, bool> matches)
    {
        string? frame = null;
        foreach (var socket in transport.Sockets())
        {
            if (!socket.Open) continue;
            var state = ConnectionState.Deserialize(socket.Attachment);
            // Never before the handshake response: the JS client routes the first frame it receives
            // to its handshake parser unconditionally, so an early broadcast does not arrive early,
            // it breaks the connection.
            if (!state.Handshaken) continue;
            if (!matches(new Target(socket.ConnectionId, state))) continue;
            frame ??= protocol.Frame(new InvocationMessage(methodName, args));
            transport.SendRaw(socket.ConnectionId, frame);
        }
        return Task.CompletedTask;
    }

    private ConnectionState Read (string connectionId) =>
        ConnectionState.Deserialize(transport.Attachment(connectionId));

    private void Write (string connectionId, ConnectionState state) =>
        transport.Attach(connectionId, state.Serialize());

    private static bool Excluded (IReadOnlyList<string> excluded, string connectionId) =>
        excluded.Contains(connectionId, StringComparer.Ordinal);

    private readonly record struct Target (string ConnectionId, ConnectionState State);
}
