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

/// <summary>
/// Guest-side registry used by generated JS classes (workers-rs wasm-bindgen equivalent).
/// Method switches are source-generated from <c>: DurableObject</c> / <c>: WorkflowEntrypoint</c>.
/// </summary>
/// <remarks>
/// Exported rather than library-owned for the same reason as <see cref="IWorker"/>: its signatures
/// name <see cref="ICloudflareEnv"/>, and Bootsharp exports closed interfaces, not generic ones.
/// </remarks>
public interface IActorRuntime
{
    int ConstructDurableObject(string className, IDurableObjectState ctx, ICloudflareEnv env);
    int ConstructWorkflow(string className, IExecutionContext ctx, ICloudflareEnv env);
    Task<string> CallDurableObject(int id, string method, string argsJson);
    Task<string> RunWorkflow(int id, string payloadJson, IWorkflowStep step);
}
