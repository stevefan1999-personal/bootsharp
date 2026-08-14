namespace Cloudflare.Data;

/// <summary>
/// The worker's wrangler bindings, expressed in C#. Property names are the binding names.
/// </summary>
/// <remarks>
/// App-owned by design: this interface IS the wrangler configuration, so the library
/// cannot name it and finds it by the <c>[WorkerEnv]</c> marker instead. This sample binds one D1
/// database and one plain var — the data layer is the whole point, so nothing else is bound.
/// </remarks>
[WorkerEnv]
public interface ICloudflareEnv
{
    /// <summary>
    /// wrangler <c>DB</c> binding. <see cref="ID1Database"/> carries
    /// <c>[JSHandle(Scope = HandleScope.Isolate)]</c>, so this handle outlives one event and a
    /// service holding it may be a singleton — which is emphatically NOT true of the session and
    /// statement handles reached through it. See the README's lifetime table.
    /// </summary>
    ID1Database DB { get; }

    string ENVIRONMENT { get; }
}
