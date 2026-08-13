namespace Cloudflare.Minimal;

/// <summary>
/// Response snapshot the emitted worker module turns back into a workerd <c>Response</c>.
/// </summary>
/// <remarks>
/// App-owned rather than packaged: there is no browser-wasm runtime pack for
/// Microsoft.AspNetCore.App, so the HTTP contract cannot live in Bootsharp.Cloudflare
/// until the AspNetCore layer vendors one. Until then every worker declares the record
/// its entrypoint returns, and the runtime reads three fields off it — status, headersJson, body.
/// </remarks>
/// <param name="Status">HTTP status. Zero means "not mine": the module then falls back to the
/// assets binding, or answers 404 when the worker has none.</param>
public sealed record WorkerResponse(int Status, string HeadersJson, string Body);

/// <summary>
/// The surface Bootsharp exports to the emitted module. The generated JS entrypoint JSImports the
/// live <c>Request</c> and <c>env</c> handles into it.
/// </summary>
public interface IWorker
{
    Task<WorkerResponse> Fetch (IJsRequest request, IWorkerEnv env);
}
