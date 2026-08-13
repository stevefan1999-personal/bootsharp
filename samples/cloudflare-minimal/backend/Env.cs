namespace Cloudflare.Minimal;

/// <summary>
/// The worker's wrangler bindings, expressed in C#. Property names are the binding names.
/// </summary>
/// <remarks>
/// App-owned by design: this interface IS the wrangler configuration, so the library
/// cannot name it and finds it by the <c>[WorkerEnv]</c> marker instead — the name and the
/// namespace are yours. Add a binding by adding a property here and to wrangler.jsonc; the
/// projections of the products themselves (<see cref="IKvNamespace"/> and friends) come from
/// Bootsharp.Cloudflare, and the emitted module adapts each property by its declared type.
/// </remarks>
[WorkerEnv]
public interface IWorkerEnv
{
    IKvNamespace KV { get; }
    string ENVIRONMENT { get; }
}
