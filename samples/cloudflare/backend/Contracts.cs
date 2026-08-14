namespace Cloudflare.Backend;

/// <summary>
/// Exported WASM surface. The generated JS <c>WorkerEntrypoint</c> JSImports
/// <see cref="IJsRequest"/> and <see cref="ICloudflareEnv"/> into these methods.
/// </summary>
/// <remarks>
/// The request/response snapshot records this file used to declare are gone: the request never
/// becomes a record at all now (<c>WebApplication.InvokeAsync</c> takes the live
/// <see cref="IJsRequest"/> handle), and the response is
/// <see cref="Bootsharp.Cloudflare.AspNetCore.HttpResponseData"/> — the same three fields, owned by
/// the package that also owns the <c>js/runtime.mjs</c> <c>toResponse</c> reading them, so the
/// contract has one definition instead of two that could drift.
/// </remarks>
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
