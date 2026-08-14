using Microsoft.Extensions.Logging;

namespace Cloudflare.Razor;

/// <summary>
/// wrangler's default export is generated from this type. The request snapshot, DI scope and
/// <c>HttpContext</c> lifetime live on <see cref="WebApplication.InvokeAsync"/>.
/// </summary>
public sealed class Worker (WebApplication app, ILogger<Worker> logger)
    : WorkerEntrypoint<IWorkerEnv, HttpResponseData>, IWorker
{
    public override async Task<HttpResponseData> Fetch (IJsRequest request, IWorkerEnv env)
    {
        await Task.Yield();
        WorkerContext.Set(env);
        try { return await app.InvokeAsync(request); }
        catch (Exception error)
        {
            logger.LogError(error, "worker fetch failed");
            return new HttpResponseData(
                StatusCodes.Status500InternalServerError,
                "{\"content-type\":\"text/plain; charset=utf-8\"}",
                "Internal server error");
        }
        finally { WorkerContext.Clear(); }
    }
}
