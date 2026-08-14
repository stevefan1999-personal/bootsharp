// API-identical reimplementation of Microsoft.AspNetCore.SignalR.Hub (MIT,.NET Foundation).
// Upstream: src/SignalR/server/Core/src/Hub.cs. Shapes copied, bodies new — the owning assembly
// (Microsoft.AspNetCore.SignalR.Core) is IsPackable=false with a single net11.0 TFM and ships only
// inside the Microsoft.AspNetCore.App shared framework, for which no browser-wasm runtime pack
// exists. See docs/THIRD-PARTY-NOTICES for provenance and for the type-identity rule.

namespace Microsoft.AspNetCore.SignalR;

/// <summary>
/// A base class for a SignalR hub.
/// </summary>
/// <remarks>
/// One divergence from upstream, deliberate: the type carries no
/// <c>[DynamicallyAccessedMembers(PublicConstructors | PublicMethods)]</c>. Upstream needs it
/// because <c>DefaultHubDispatcher</c> discovers and invokes hub methods reflectively; dispatch
/// here is generated, so the annotation would root every hub's public surface for a reflection
/// path that does not exist.
/// </remarks>
public abstract class Hub : IDisposable
{
    private bool disposed;
    private IHubCallerClients clients = default!;
    private HubCallerContext context = default!;
    private IGroupManager groups = default!;

    /// <summary>
    /// Gets or sets an object that can be used to invoke methods on the clients connected to this hub.
    /// </summary>
    public IHubCallerClients Clients
    {
        get { CheckDisposed(); return clients; }
        set { CheckDisposed(); clients = value; }
    }

    /// <summary>Gets or sets the hub caller context.</summary>
    public HubCallerContext Context
    {
        get { CheckDisposed(); return context; }
        set { CheckDisposed(); context = value; }
    }

    /// <summary>Gets or sets the group manager.</summary>
    public IGroupManager Groups
    {
        get { CheckDisposed(); return groups; }
        set { CheckDisposed(); groups = value; }
    }

    /// <summary>Called when a new connection is established with the hub.</summary>
    public virtual Task OnConnectedAsync () => Task.CompletedTask;

    /// <summary>Called when a connection with the hub is terminated.</summary>
    public virtual Task OnDisconnectedAsync (Exception? exception) => Task.CompletedTask;

    /// <summary>
    /// Called when the authenticated user on the connection has been refreshed. Kept because
    /// removing a virtual upstream declares would silently stop calling a user's override; the
    /// host never raises it, since ASP.NET authorization is out of scope.
    /// </summary>
    public virtual Task OnAuthenticationRefreshedAsync () => Task.CompletedTask;

    /// <summary>Releases all resources currently used by this <see cref="Hub"/> instance.</summary>
    protected virtual void Dispose (bool disposing) { }

    /// <inheritdoc/>
    public void Dispose ()
    {
        if (disposed) return;
        Dispose(true);
        disposed = true;
    }

    private void CheckDisposed () => ObjectDisposedException.ThrowIf(disposed, this);
}

/// <summary>Customizes the name of a hub method.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public class HubMethodNameAttribute (string name) : Attribute
{
    /// <summary>The customized name of the hub method.</summary>
    public string Name { get; } = name;
}
