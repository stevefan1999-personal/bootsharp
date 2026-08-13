namespace Bootsharp.Cloudflare;

/// <summary>
/// C# projection of <c>@cloudflare/workers-types</c> 5.20260813.1 (workerd).
/// </summary>
/// <remarks>
/// Bootsharp cannot import the .d.ts 1:1:
/// <list type="bullet">
/// <item>One C# method per JS name (TS overloads are flattened).</item>
/// <item>No generic instance types (<c>KVNamespace&lt;Key&gt;</c>, <c>DurableObjectNamespace&lt;T&gt;</c>).</item>
/// <item>JS reserved names: C# <c>Delete</c>/<c>Do</c> become <c>$delete</c>/<c>$do</c>.</item>
/// <item>Records are JSON-copied; interfaces are JS handles (<c>$i.import</c>).</item>
/// <item>Streams / Request / Response / ArrayBuffer stay on the JS host —
///   the Worker fetch path snapshots them into app-owned request/response records.</item>
/// <item><c>Task&lt;int&gt;</c> on an instance import is not awaited by generated JS;
///   RPC numbers use <see cref="RpcInt"/>.</item>
/// </list>
/// Web IDL omitted on purpose (already implemented by workerd, unmarshallable):
/// DOMException, Console, EventTarget, AbortSignal, Blob/File, Cache/CacheStorage,
/// Crypto/SubtleCrypto, HTMLRewriter, Headers/Body/Request/Response, streams,
/// URL/URLPattern, WebSocket, Performance, sockets, containers, WorkerLoader.
/// Cloudflare products with stream-heavy APIs (Images transform, Stream, Media,
/// Email raw MIME, Flagship details, Agent Memory) are thin JSON entrypoints in
/// <c>Platform.cs</c> — add <c>[assembly: Import]</c> when wrangler binds them.
/// </remarks>
public static class WorkersTypes
{
    public const string Version = "5.20260813.1";
}

/// <summary>
/// JS <c>ExecutionContext</c> (<c>ctx</c> on <c>WorkerEntrypoint</c> / <c>WorkflowEntrypoint</c>).
/// <c>waitUntil(promise)</c> is not projected — C# cannot pass a host Promise.
/// </summary>
public interface IExecutionContext
{
    void PassThroughOnException();
    void Abort(string? reason);
}

/// <summary>
/// Live <c>Request</c> handle. C# calls <see cref="Text"/> / reads URL via JSImport
/// instead of a JS-side snapshot.
/// </summary>
public interface IJsRequest
{
    string Method { get; }
    string Url { get; }
    string HeadersJson { get; }
    string? CfJson { get; }
    Task<string> Text();
}

/// <summary>JS <c>ScheduledController</c>.</summary>
public interface IScheduledController
{
    double ScheduledTime { get; }
    string Cron { get; }
    void NoRetry();
}

/// <summary>JS <c>AlarmInvocationInfo</c> (Durable Object <c>alarm</c> handler).</summary>
public sealed record AlarmInvocationInfo(bool IsRetry, int RetryCount, double ScheduledTime);

/// <summary>
/// Flattened subset of JS <c>IncomingRequestCfProperties</c> (the full type is
/// hundreds of country/colo union members). Copied onto <c>HttpRequestData.CfJson</c>.
/// </summary>
public sealed record IncomingRequestCf(
    string? Colo,
    string? Country,
    string? City,
    string? Timezone,
    string? HttpProtocol,
    string? TlsVersion,
    int? Asn);

/// <summary>JS <c>WorkerVersionMetadata</c> (<c>env.CF_VERSION_METADATA</c>).</summary>
public sealed record WorkerVersionMetadata(string Id, string Tag, string Timestamp);
