namespace Cloudflare.Backend;

/// <summary>HTTP request snapshot used by the ASP.NET Core shim after C# reads the live Request.</summary>
public sealed record HttpRequestData(
    string Method,
    string Url,
    string Path,
    string Query,
    string HeadersJson,
    string Body,
    string? CfJson = null);

/// <summary>HTTP response snapshot. Status 0 means "serve static assets".</summary>
public sealed record HttpResponseData(
    int Status,
    string HeadersJson,
    string Body);

/// <summary>
/// Exported WASM surface. The generated JS <c>WorkerEntrypoint</c> JSImports
/// <see cref="IJsRequest"/> and <see cref="ICloudflareEnv"/> into these methods.
/// </summary>
public interface IWorker
{
    Task<HttpResponseData> Fetch(IJsRequest request, ICloudflareEnv env);
    Task Queue(string messagesJson, ICloudflareEnv env);
    Task Scheduled(IScheduledController controller, ICloudflareEnv env);
}
