using Microsoft.Extensions.Logging;

namespace Cloudflare.Data;

/// <summary>
/// The Cloudflare Worker. wrangler's <c>main</c> default export is generated from this type.
/// </summary>
/// <remarks>
/// Almost nothing is left here, and that is the shape to copy: the snapshot of method, url, headers
/// and body, the per-event DI scope and the <c>HttpContext</c> lifetime are all owned by
/// <see cref="WebApplication.InvokeAsync"/>. What remains is what is genuinely the
/// worker's — binding the ambient <c>env</c> for this invocation, and clearing it.
/// </remarks>
public sealed class Worker (WebApplication app, ILogger<Worker> logger)
    : WorkerEntrypoint<ICloudflareEnv, HttpResponseData>, IWorker
{
    public override async Task<HttpResponseData> Fetch (IJsRequest request, ICloudflareEnv env)
    {
        // Yield first, so the rest of the handler runs off the JS call that entered wasm: an await
        // on a workerd promise then resumes from the microtask queue rather than from inside it.
        await Task.Yield();
        // The one write of the ambient env. Everything downstream takes ICloudflareEnv from the
        // container, whose registration reads this slot once per scope.
        WorkerContext.Set(env);
        try { return await app.InvokeAsync(request); }
        catch (Exception error)
        {
            // InvokeAsync already turns an exception from the pipeline into a 500, so reaching here
            // means the failure was outside it — reading the request handle, or the env itself.
            logger.LogError(error, "worker fetch failed");
            return new HttpResponseData(
                StatusCodes.Status500InternalServerError,
                "{\"content-type\":\"text/plain; charset=utf-8\"}",
                "Internal server error");
        }
        finally { WorkerContext.Clear(); }
    }
}
