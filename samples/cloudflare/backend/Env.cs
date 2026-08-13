namespace Cloudflare.Workers;

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
