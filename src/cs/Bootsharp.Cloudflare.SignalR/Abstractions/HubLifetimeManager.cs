// Signature-identical reimplementation of Microsoft.AspNetCore.SignalR.HubLifetimeManager<THub>
// (MIT,.NET Foundation). Upstream: src/SignalR/server/Core/src/HubLifetimeManager.cs.
//
// This is THE documented extension point: upstream registers DefaultHubLifetimeManager with
// TryAddSingleton (SignalRDependencyInjectionExtensions.cs:25) and the Redis backplane replaces it
// wholesale with AddSingleton (RedisDependencyInjectionExtensions.cs:48). Copying the contract
// member for member is what keeps a future Bootsharp.Cloudflare.SignalR.Backplane — or a port of
// Redis/Azure semantics — a drop-in rather than a rewrite.

using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace Microsoft.AspNetCore.SignalR;

/// <summary>A lifetime manager abstraction for <see cref="Hub"/> instances.</summary>
public abstract class HubLifetimeManager<THub> where THub : Hub
{
    // Called by the framework and not something we'd cancel, so it doesn't take a cancellation token.
    /// <summary>Called when a connection is started.</summary>
    public abstract Task OnConnectedAsync (HubConnectionContext connection);

    /// <summary>Called when a connection is finished.</summary>
    public abstract Task OnDisconnectedAsync (HubConnectionContext connection);

    /// <summary>Sends an invocation message to all hub connections.</summary>
    public abstract Task SendAllAsync (string methodName, object?[] args, CancellationToken cancellationToken = default);

    /// <summary>Sends an invocation message to all hub connections excluding the specified connections.</summary>
    public abstract Task SendAllExceptAsync (string methodName, object?[] args, IReadOnlyList<string> excludedConnectionIds, CancellationToken cancellationToken = default);

    /// <summary>Sends an invocation message to the specified connection.</summary>
    public abstract Task SendConnectionAsync (string connectionId, string methodName, object?[] args, CancellationToken cancellationToken = default);

    /// <summary>Sends an invocation message to the specified connections.</summary>
    public abstract Task SendConnectionsAsync (IReadOnlyList<string> connectionIds, string methodName, object?[] args, CancellationToken cancellationToken = default);

    /// <summary>Sends an invocation message to the specified group.</summary>
    public abstract Task SendGroupAsync (string groupName, string methodName, object?[] args, CancellationToken cancellationToken = default);

    /// <summary>Sends an invocation message to the specified groups.</summary>
    public abstract Task SendGroupsAsync (IReadOnlyList<string> groupNames, string methodName, object?[] args, CancellationToken cancellationToken = default);

    /// <summary>Sends an invocation message to the specified group excluding the specified connections.</summary>
    public abstract Task SendGroupExceptAsync (string groupName, string methodName, object?[] args, IReadOnlyList<string> excludedConnectionIds, CancellationToken cancellationToken = default);

    /// <summary>Sends an invocation message to the specified user.</summary>
    public abstract Task SendUserAsync (string userId, string methodName, object?[] args, CancellationToken cancellationToken = default);

    /// <summary>Sends an invocation message to the specified users.</summary>
    public abstract Task SendUsersAsync (IReadOnlyList<string> userIds, string methodName, object?[] args, CancellationToken cancellationToken = default);

    /// <summary>Adds a connection to the specified group.</summary>
    public abstract Task AddToGroupAsync (string connectionId, string groupName, CancellationToken cancellationToken = default);

    /// <summary>Removes a connection from the specified group.</summary>
    public abstract Task RemoveFromGroupAsync (string connectionId, string groupName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an invocation message to the specified connection and waits for a response.
    /// </summary>
    /// <remarks>DEFERRED; the seam is kept so enabling it later is additive.</remarks>
    public virtual Task<T> InvokeConnectionAsync<T> (string connectionId, string methodName, object?[] args, CancellationToken cancellationToken) =>
        throw new NotImplementedException($"{GetType().Name} does not support client return values.");

    /// <summary>Sets the connection result for an in-progress <see cref="InvokeConnectionAsync{T}"/> call.</summary>
    /// <remarks>DEFERRED.</remarks>
    public virtual Task SetConnectionResultAsync (string connectionId, CompletionMessage result) =>
        throw new NotImplementedException($"{GetType().Name} does not support client return values.");

    /// <summary>
    /// Tells <see cref="IHubProtocol"/> implementations what the expected type from a connection
    /// result is.
    /// </summary>
    /// <remarks>DEFERRED. Returning false here is what makes the dispatcher's
    /// Completion arm fall through to "unexpected completion" rather than route a client result.</remarks>
    public virtual bool TryGetReturnType (string invocationId, [NotNullWhen(true)] out Type? type)
    {
        type = null;
        return false;
    }
}
