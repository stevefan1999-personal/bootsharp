using Cloudflare.Backend.Hosting;
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

    public override async Task<HttpResponseData> Fetch(IJsRequest request, ICloudflareEnv env)
    {
        await Task.Yield();
        WorkerContext.Set(env);
        try
        {
            var uri = new Uri(request.Url, UriKind.Absolute);
            var body = request.Method is "GET" or "HEAD" ? "" : await request.Text();
            var data = new HttpRequestData(
                request.Method,
                request.Url,
                uri.AbsolutePath,
                uri.Query,
                request.HeadersJson,
                body,
                request.CfJson);
            return await app.InvokeAsync(data);
        }
        catch (Exception ex)
        {
            // Operators get the detail through Workers Logs, the caller an opaque 500. Nothing is
            // read off the request here: that handle may be what failed in the first place.
            logger.LogError(ex, "worker fetch failed");
            return Results.Text("Internal server error", 500).ToResponse();
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
    /// A cron run has no response to return, so which trigger fired and when is persisted to KV —
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
