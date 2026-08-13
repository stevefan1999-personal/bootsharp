namespace Cloudflare.Backend;

/// <summary>
/// Per-request <c>env</c>. C# JSImports workerd bindings on this handle
/// (no module-level <c>installHandlers</c>). Property names match wrangler bindings.
/// </summary>
/// <remarks>
/// App-owned by design: this interface IS the wrangler configuration expressed in C#, so it stays
/// in the app while <c>Bootsharp.Cloudflare</c> supplies the projections of the products it binds
///. <c>[WorkerEnv]</c> is how the generator finds it — the name and the namespace are
/// the app's to choose.
/// </remarks>
[WorkerEnv]
public interface ICloudflareEnv
{
    IKvNamespace KV { get; }
    ID1Database DB { get; }
    IR2Bucket BUCKET { get; }
    IQueue QUEUE { get; }
    ICounterNamespace COUNTER { get; }
    IWorkflow WORKFLOW { get; }
    string ENVIRONMENT { get; }
}

/// <summary>
/// wrangler <c>COUNTER</c> binding: TS <c>DurableObjectNamespace&lt;Counter&gt;</c>.
/// Generics cannot cross Bootsharp instance imports, so the stub type is explicit.
/// JS <c>newUniqueId</c> / <c>idFromName</c> / <c>idFromString</c> / <c>get</c> / <c>getByName</c>.
/// </summary>
public interface ICounterNamespace
{
    DurableObjectId NewUniqueId();
    DurableObjectId IdFromName(string name);
    DurableObjectId IdFromString(string id);
    ICounterStub Get(string id);
    ICounterStub GetByName(string name);
}

/// <summary>TS <c>DurableObjectStub&lt;Counter&gt;</c> — RPC methods on <see cref="Counter"/>.</summary>
public interface ICounterStub
{
    string Id { get; }
    string? Name { get; }
    Task<RpcInt> Get();
    Task<RpcInt> Increment();
    /// <summary>Typed DO SQLite transport: create/insert/count on a ticks table.</summary>
    Task<string> SqlDemo();
}
