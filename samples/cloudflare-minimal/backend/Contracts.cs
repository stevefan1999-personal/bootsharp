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
/// <remarks>
/// Three fields is all this worker needs. The runtime reads two more off the snapshot when they are
/// there — <c>bodyBytes</c> for a body that is not text, and <c>passThroughToAssets</c> for a worker
/// that declines a request in favour of its assets binding — and this one declares neither: it
/// answers every request itself, in text. <c>Bootsharp.Cloudflare.AspNetCore</c>'s
/// <c>HttpResponseData</c> is the record that carries all five.
/// </remarks>
/// <param name="Status">HTTP status code.</param>
public sealed record WorkerResponse(int Status, string HeadersJson, string Body);

/// <summary>
/// The surface Bootsharp exports to the emitted module. The generated JS entrypoint JSImports the
/// live <c>Request</c> and <c>env</c> handles into it.
/// </summary>
public interface IWorker
{
    Task<WorkerResponse> Fetch (IJsRequest request, IWorkerEnv env);
}
