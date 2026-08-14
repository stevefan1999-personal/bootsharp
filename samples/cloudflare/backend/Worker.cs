using Microsoft.Extensions.Logging;

namespace Cloudflare.Backend;

/// <summary>
/// The Cloudflare Worker. wrangler <c>main</c> default export is generated from this type.
/// Fetch/Queue/Scheduled run in WASM and JSImport the live <c>Request</c>, <c>controller</c>
/// and <c>env</c> handles.
/// </summary>
public sealed class Worker(WebApplication app, ILogger<Worker> logger)
    : WorkerEntrypoint<ICloudflareEnv, HttpResponseData>, IWorker
{
    /// <summary>KV key holding the heartbeat written by <see cref="Scheduled"/>.</summary>
    public const string ScheduledHeartbeatKey = "last-scheduled";

    /// <summary>
    /// Hands the live request to the application.
    /// </summary>
    /// <remarks>
    /// The snapshot this method used to build by hand — method, url, path, query, headers, body
    /// is now taken inside <see cref="WebApplication.InvokeAsync"/>, which also owns the per-event
    /// DI scope and the <c>HttpContext</c> lifetime. What is left here is what is
    /// genuinely the worker's: setting the ambient <c>env</c> for the invocation and clearing it.
    /// </remarks>
    public override async Task<HttpResponseData> Fetch(IJsRequest request, ICloudflareEnv env)
    {
        await Task.Yield();
        WorkerContext.Set(env);
        try { return await app.InvokeAsync(request); }
        catch (Exception ex)
        {
            // InvokeAsync already turns an exception from the pipeline into a 500, so reaching here
            // means the failure was outside it — reading the request handle, or the env itself.
            // Operators get the detail through Workers Logs, the caller an opaque 500.
            logger.LogError(ex, "worker fetch failed");
            return new HttpResponseData(
                StatusCodes.Status500InternalServerError,
                "{\"content-type\":\"text/plain; charset=utf-8\"}",
                "Internal server error");
        }
        finally
        {
            WorkerContext.Clear();
        }
    }

    public override async Task Queue(string messagesJson, ICloudflareEnv env)
    {
        await Task.Yield();
        WorkerContext.Set(env);
        try
        {
            await env.KV.Put("queue:last", messagesJson, null);
        }
        finally
        {
            WorkerContext.Clear();
        }
    }

    /// <summary>
    /// Cron trigger (<c>triggers.crons</c> in wrangler.jsonc). Overriding the base virtual is what
    /// makes the generator emit the JS <c>scheduled</c> handler for this worker.
    /// </summary>
    public override async Task Scheduled(IScheduledController controller, ICloudflareEnv env)
    {
        await Task.Yield();
        WorkerContext.Set(env);
        try
        {
            await env.KV.Put(ScheduledHeartbeatKey, Heartbeat(controller), null);
        }
        finally
        {
            WorkerContext.Clear();
        }
    }

    /// <summary>
    /// A cron run has no response to return, so which trigger fired and when is persisted to KV
    /// that record is the only observable evidence the scheduled handler ran.
    /// </summary>
    private static string Heartbeat(IScheduledController controller)
    {
        var scheduledTimeMs = (long)controller.ScheduledTime;
        var scheduledAt = DateTimeOffset.FromUnixTimeMilliseconds(scheduledTimeMs);
        return "{\"cron\":\"" + Json.Escape(controller.Cron)
            + "\",\"scheduledTime\":" + scheduledTimeMs
            + ",\"scheduledAt\":\"" + scheduledAt.ToString("O") + "\"}";
    }
}
