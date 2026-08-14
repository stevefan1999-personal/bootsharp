namespace Bootsharp.Cloudflare.Projection;

/// <summary>
/// One C# entrypoint class as the emitted ESM module sees it. This model is shared
/// verbatim by the two front ends that produce it — the Roslyn generator, which resolves it from
/// symbols so it can attach diagnostics to source locations, and the publish task, which resolves
/// it from the compiled assembly's metadata so it can emit the module after the native link.
/// Everything only the generated C# dispatch needs is deliberately absent: the ESM projection
/// forwards arguments as an opaque array, so a front end that cannot cheaply recover argument
/// detail (metadata carries no nullable annotations) is still able to produce a complete model.
/// </summary>
/// <param name="Kind">Worker, DurableObject or Workflow.</param>
/// <param name="HostsHub">Whether the class derives from <c>HubDurableObject</c>, in which case
/// the emitted module wraps it with the shipped hibernation handlers.</param>
/// <param name="HubRoute">Normalized path prefix a hub is served at (<c>/chat/</c>), or null when
/// the worker does not route to this actor. Negotiate and the 101 upgrade are generated from it
/// so an app with a hub writes no JavaScript.</param>
internal sealed record Entrypoint(
    string Kind,
    string Name,
    string Namespace,
    EquatableArray<Method> Methods,
    bool HostsHub = false,
    string? HubRoute = null);

/// <param name="Handler">Handler slot the method fills: fetch/queue/scheduled/run, or rpc.</param>
/// <param name="Return">Encoded return shape: void/int/long/double/bool/string/rpcInt.</param>
/// <param name="Await">Whether the invocation is Task-returning and must be awaited.</param>
internal sealed record Method(
    string CsName,
    string JsName,
    string Handler,
    string Return,
    bool Await);

/// <summary>
/// One binding the app's env interface declares, named and typed as the app declares it. The type's
/// namespace travels with it because the packaged adapters are chosen on full type identity — an
/// app interface named <c>IQueue</c> is the app's, not Cloudflare's.
/// </summary>
internal sealed record EnvProperty(string Name, string TypeNamespace, string TypeName);

/// <summary>
/// Equatable array, so a model built by the Roslyn front end can be compared between incremental
/// runs — the arrays a plain record holds compare by reference and would defeat caching.
/// </summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>
    where T : IEquatable<T>
{
    public static EquatableArray<T> Empty { get; } = new([]);

    private readonly T[] items;

    public EquatableArray (IEnumerable<T> items) => this.items = items.ToArray();

    public int Length => items.Length;
    public T[] Items => items;
    public IEnumerator<T> GetEnumerator () => ((IEnumerable<T>)items).GetEnumerator();

    public bool Equals (EquatableArray<T> other) => items.SequenceEqual(other.items);
    public override bool Equals (object? obj) => obj is EquatableArray<T> other && Equals(other);
    public override int GetHashCode ()
    {
        var hash = 0;
        foreach (var item in items) hash = (hash * 397) ^ item.GetHashCode();
        return hash;
    }
}
