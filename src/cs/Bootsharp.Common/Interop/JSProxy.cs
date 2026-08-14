namespace Bootsharp;

/// <summary>
/// Base class for generated proxies used to bind JS-originated instances.
/// </summary>
public abstract class JSProxy (int id)
{
    /// <summary>
    /// Unique identifier of the proxied JS instance.
    /// </summary>
    protected internal readonly int _id = id;

    /// <summary>
    /// Returns the result of an interop call, keeping this proxy reachable until that call is made.
    /// </summary>
    /// <remarks>
    /// The ID crosses the boundary as a bare integer, so reading <see cref="_id"/> is the proxy's
    /// last use: from there on the collector is free to finalize it, and the finalizer releases the
    /// ID on the JavaScript side. Without this the registry can drop an ID between the read and the
    /// call carrying it, and the call lands on an instance JavaScript no longer resolves.
    /// </remarks>
    /// <param name="result">The value returned by the interop call.</param>
    protected T Alive<T> (T result)
    {
        GC.KeepAlive(this);
        return result;
    }

    /// <inheritdoc cref="Alive{T}"/>
    protected void Alive () => GC.KeepAlive(this);
}
