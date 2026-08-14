using Microsoft.AspNetCore.SignalR;

namespace Bootsharp.Cloudflare.SignalR;

/// <summary>
/// The nine one-line proxies upstream fills <c>Internal/Proxies.cs</c> with, as one closure over the
/// lifetime-manager call each of them makes. Nine near-identical classes exist upstream because each
/// captures a different tuple; a delegate captures all of them, and the whole file collapses.
/// </summary>
internal sealed class ClientProxy (Func<string, object?[], CancellationToken, Task> send) : IClientProxy
{
    public Task SendCoreAsync (string method, object?[] args, CancellationToken cancellationToken = default) =>
        send(method, args, cancellationToken);
}

/// <summary>
/// <c>IHubCallerClients</c> over a <see cref="HubLifetimeManager{THub}"/>, for a hub method running
/// on behalf of <paramref name="callerConnectionId"/>.
/// </summary>
/// <remarks>
/// <c>Caller</c>, <c>Others</c> and <c>OthersInGroup</c> are the only members that need the caller;
/// the other nine are the plain <c>IHubClients</c> set, so
/// <see cref="HubClients{THub}"/> below serves both by passing a null caller.
/// </remarks>
internal sealed class HubClients<THub> (HubLifetimeManager<THub> manager, string? callerConnectionId)
    : IHubCallerClients, IHubClients where THub : Hub
{
    private static readonly string[] noExclusions = [];

    public IClientProxy All =>
        new ClientProxy((method, args, token) => manager.SendAllAsync(method, args, token));

    public IClientProxy AllExcept (IReadOnlyList<string> excludedConnectionIds) =>
        new ClientProxy((method, args, token) => manager.SendAllExceptAsync(method, args, excludedConnectionIds, token));

    public IClientProxy Client (string connectionId) =>
        new ClientProxy((method, args, token) => manager.SendConnectionAsync(connectionId, method, args, token));

    public IClientProxy Clients (IReadOnlyList<string> connectionIds) =>
        new ClientProxy((method, args, token) => manager.SendConnectionsAsync(connectionIds, method, args, token));

    public IClientProxy Group (string groupName) =>
        new ClientProxy((method, args, token) => manager.SendGroupAsync(groupName, method, args, token));

    public IClientProxy Groups (IReadOnlyList<string> groupNames) =>
        new ClientProxy((method, args, token) => manager.SendGroupsAsync(groupNames, method, args, token));

    public IClientProxy GroupExcept (string groupName, IReadOnlyList<string> excludedConnectionIds) =>
        new ClientProxy((method, args, token) => manager.SendGroupExceptAsync(groupName, method, args, excludedConnectionIds, token));

    public IClientProxy User (string userId) =>
        new ClientProxy((method, args, token) => manager.SendUserAsync(userId, method, args, token));

    public IClientProxy Users (IReadOnlyList<string> userIds) =>
        new ClientProxy((method, args, token) => manager.SendUsersAsync(userIds, method, args, token));

    public IClientProxy Caller => Client(Caller_ConnectionId);

    public IClientProxy Others => AllExcept([Caller_ConnectionId]);

    public IClientProxy OthersInGroup (string groupName) => GroupExcept(groupName, [Caller_ConnectionId]);

    /// <summary>
    /// The three caller-relative members are the only ones an <c>IHubContext</c> — which has no
    /// caller — cannot answer, so reaching them from one is a programming error rather than a
    /// silent broadcast to everybody.
    /// </summary>
    private string Caller_ConnectionId => callerConnectionId
        ?? throw new InvalidOperationException(
            "Caller-relative client proxies (Caller, Others, OthersInGroup) are only available " +
            "inside a hub method. An IHubContext has no calling connection.");

    /// <summary>Exclusion list shared by the caller-free paths, so they allocate nothing.</summary>
    internal static IReadOnlyList<string> None => noExclusions;
}

/// <summary>
/// <c>IGroupManager</c> over the lifetime manager: two members, both one line, exactly as upstream.
/// </summary>
internal sealed class GroupManager<THub> (HubLifetimeManager<THub> manager) : IGroupManager where THub : Hub
{
    public Task AddToGroupAsync (string connectionId, string groupName, CancellationToken cancellationToken = default) =>
        manager.AddToGroupAsync(connectionId, groupName, cancellationToken);

    public Task RemoveFromGroupAsync (string connectionId, string groupName, CancellationToken cancellationToken = default) =>
        manager.RemoveFromGroupAsync(connectionId, groupName, cancellationToken);
}

/// <summary><c>IHubContext&lt;THub&gt;</c> for code outside a hub method (a fetch route, an alarm).</summary>
internal sealed class HubContext<THub> (HubLifetimeManager<THub> manager) : IHubContext<THub>, IHubContext
    where THub : Hub
{
    public IHubClients Clients { get; } = new HubClients<THub>(manager, null);
    public IGroupManager Groups { get; } = new GroupManager<THub>(manager);
}

/// <summary>The <see cref="HubCallerContext"/> a dispatched hub method sees.</summary>
internal sealed class WorkerHubCallerContext (HubConnectionContext connection) : HubCallerContext
{
    public override string ConnectionId => connection.ConnectionId;
    public override string? UserIdentifier => connection.UserIdentifier;
    public override System.Security.Claims.ClaimsPrincipal? User => connection.User;
    public override IDictionary<object, object?> Items => connection.Items;
    public override Microsoft.AspNetCore.Http.Features.IFeatureCollection Features => connection.Features;
    public override CancellationToken ConnectionAborted => connection.ConnectionAborted;
    public override void Abort () => connection.Abort();
}
