// API-identical reimplementations of the SignalR client-proxy abstractions (MIT,.NET Foundation).
// Upstream: src/SignalR/server/Core/src/{IClientProxy,ISingleClientProxy,IHubClients,IHubClients`T,
// IHubCallerClients,IHubCallerClients`T,IGroupManager,IHubContext,IHubContext`T}.cs and
// Internal/NonInvokingSingleClientProxy.cs. Signatures copied so user hub code is source-compatible.

namespace Microsoft.AspNetCore.SignalR;

/// <summary>A proxy abstraction for invoking hub methods.</summary>
public interface IClientProxy
{
    // Named SendCoreAsync rather than SendAsync so that arrays of references (string[]) do not
    // choose the object[] overload over the object one — upstream's reason, preserved.

    /// <summary>
    /// Invokes a method on the connection(s) represented by the <see cref="IClientProxy"/> instance.
    /// Does not wait for a response from the receiver.
    /// </summary>
    Task SendCoreAsync (string method, object?[] args, CancellationToken cancellationToken = default);
}

/// <summary>A proxy abstraction for invoking hub methods on the client and getting a result.</summary>
public interface ISingleClientProxy : IClientProxy
{
    /// <summary>
    /// Invokes a method on the connection represented by the <see cref="ISingleClientProxy"/>
    /// instance and waits for a result.
    /// </summary>
    /// <remarks>
    /// Client results are DEFERRED with the seam kept: the declaration exists, the
    /// three <see cref="HubLifetimeManager{THub}"/> virtuals exist, and the reused
    /// <c>JsonHubProtocol</c> already carries the <c>TryGetReturnType</c> call sites — so turning
    /// the feature on later is additive rather than a parser retrofit.
    /// </remarks>
    Task<T> InvokeCoreAsync<T> (string method, object?[] args, CancellationToken cancellationToken);
}

/// <summary>An abstraction that provides access to client connections.</summary>
/// <typeparam name="T">The client invoker type.</typeparam>
public interface IHubClients<T>
{
    /// <summary>All clients connected to the hub.</summary>
    T All { get; }
    /// <summary>All clients connected to the hub except the specified connections.</summary>
    T AllExcept (IReadOnlyList<string> excludedConnectionIds);
    /// <summary>The specified client connection.</summary>
    T Client (string connectionId);
    /// <summary>The specified client connections.</summary>
    T Clients (IReadOnlyList<string> connectionIds);
    /// <summary>All connections in the specified group.</summary>
    T Group (string groupName);
    /// <summary>All connections in all of the specified groups.</summary>
    T Groups (IReadOnlyList<string> groupNames);
    /// <summary>All connections in the specified group except the specified connections.</summary>
    T GroupExcept (string groupName, IReadOnlyList<string> excludedConnectionIds);
    /// <summary>All connections associated with the specified user.</summary>
    T User (string userId);
    /// <summary>All connections associated with all of the specified users.</summary>
    T Users (IReadOnlyList<string> userIds);
}

/// <summary>
/// An abstraction that provides access to client connections, including the one that sent the
/// current invocation.
/// </summary>
public interface IHubCallerClients<T> : IHubClients<T>
{
    /// <summary>The connection which triggered the current invocation.</summary>
    T Caller { get; }
    /// <summary>All connections except the one which triggered the current invocation.</summary>
    T Others { get; }
    /// <summary>
    /// All connections in the specified group, except the one which triggered the current invocation.
    /// </summary>
    T OthersInGroup (string groupName);
}

/// <summary>An abstraction that provides access to client connections.</summary>
public interface IHubClients : IHubClients<IClientProxy>
{
    /// <summary>A proxy for a single client that can also receive results.</summary>
    new ISingleClientProxy Client (string connectionId) =>
        new NonInvokingSingleClientProxy(((IHubClients<IClientProxy>)this).Client(connectionId),
            "IHubClients.Client(string connectionId)");
}

/// <summary>A clients caller abstraction for a hub.</summary>
public interface IHubCallerClients : IHubCallerClients<IClientProxy>
{
    /// <summary>A proxy for a single client that can also receive results.</summary>
    new ISingleClientProxy Client (string connectionId) =>
        new NonInvokingSingleClientProxy(((IHubCallerClients<IClientProxy>)this).Client(connectionId),
            "IHubCallerClients.Client(string connectionId)");

    /// <summary>A proxy for the calling client that can also receive results.</summary>
    new ISingleClientProxy Caller =>
        new NonInvokingSingleClientProxy(((IHubCallerClients<IClientProxy>)this).Caller,
            "IHubCallerClients.Caller");
}

/// <summary>A manager abstraction for adding and removing connections from groups.</summary>
public interface IGroupManager
{
    /// <summary>Adds a connection to the specified group.</summary>
    Task AddToGroupAsync (string connectionId, string groupName, CancellationToken cancellationToken = default);
    /// <summary>Removes a connection from the specified group.</summary>
    Task RemoveFromGroupAsync (string connectionId, string groupName, CancellationToken cancellationToken = default);
}

/// <summary>A context abstraction for a hub.</summary>
public interface IHubContext
{
    /// <summary>Invokes methods on clients connected to the hub.</summary>
    IHubClients Clients { get; }
    /// <summary>Adds and removes connections to named groups.</summary>
    IGroupManager Groups { get; }
}

/// <summary>A context abstraction for a hub.</summary>
public interface IHubContext<out THub> where THub : Hub
{
    /// <summary>Invokes methods on clients connected to the hub.</summary>
    IHubClients Clients { get; }
    /// <summary>Adds and removes connections to named groups.</summary>
    IGroupManager Groups { get; }
}

/// <summary>
/// The proxy the two default interface members above hand back: a client proxy that can send but
/// refuses to invoke, naming the member the caller reached it through.
/// </summary>
internal sealed class NonInvokingSingleClientProxy (IClientProxy proxy, string member) : ISingleClientProxy
{
    public Task SendCoreAsync (string method, object?[] args, CancellationToken cancellationToken = default) =>
        proxy.SendCoreAsync(method, args, cancellationToken);

    public Task<T> InvokeCoreAsync<T> (string method, object?[] args, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException($"The default implementation of {member} does not support client return results.");
}
