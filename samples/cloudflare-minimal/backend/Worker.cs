using Microsoft.Extensions.Logging;

namespace Cloudflare.Minimal;

/// <summary>
/// The whole worker: one fetch handler, one KV binding, structured logging. wrangler's
/// <c>main</c> default export is generated from this class — there is no JavaScript to write.
/// </summary>
public sealed class Worker (ILogger<Worker> logger) : WorkerEntrypoint<IWorkerEnv, WorkerResponse>, IWorker
{
    private static IKvNamespace kv => WorkerContext.Env.KV;

    public override async Task<WorkerResponse> Fetch (IJsRequest request, IWorkerEnv env)
    {
        // Yield first, so the rest of the handler runs off the JS call that entered wasm: an await
        // on a workerd promise then resumes from the microtask queue rather than from inside it.
        await Task.Yield();
        WorkerContext.Set(env);
        try { return await Route(request); }
        catch (Exception error)
        {
            // Operators get the detail through Workers Logs, the caller an opaque 500. Nothing is
            // read off the request here: that handle may be what failed in the first place.
            logger.LogError(error, "worker fetch failed");
            return TextReply(500, "Internal server error");
        }
        finally { WorkerContext.Clear(); }
    }

    /// <summary>
    /// Routing, written out. Nothing in the browser-wasm reference set routes for us, and a worker
    /// this size does not need it to — AspNetCore layer is where endpoint routing and
    /// model binding arrive, priced against the baseline this sample establishes.
    /// </summary>
    private async Task<WorkerResponse> Route (IJsRequest request)
    {
        var url = new Uri(request.Url, UriKind.Absolute);
        return (request.Method, url.AbsolutePath) switch {
            ("GET", "/") => TextReply(200, $"Bootsharp on Cloudflare Workers. .NET {Environment.Version}."),
            ("GET", "/api/health") => Health(),
            ("GET", "/api/kv") => await ReadKv(Query(url, "key") ?? "demo"),
            ("PUT" or "POST", "/api/kv") => await WriteKv(Query(url, "key") ?? "demo", await request.Text()),
            _ => TextReply(404, "Not found")
        };
    }

    private WorkerResponse Health ()
    {
        var stage = WorkerContext.Env.ENVIRONMENT;
        // Template arguments become their own indexed fields in Workers Logs, so "which environment
        // answered this" is answerable by filtering on a field rather than by grepping messages.
        logger.LogInformation("health checked in {Environment}", stage);
        return JsonReply(200, $$"""
            {"ok":true,"runtime":{{Json.Quote(".NET " + Environment.Version)}},"environment":{{Json.Quote(stage)}}}
            """);
    }

    private async Task<WorkerResponse> ReadKv (string key)
    {
        var value = await kv.Get(key);
        logger.LogInformation("kv read {Key} {Found}", key, value is not null);
        return JsonReply(200, $$"""{"key":{{Json.Quote(key)}},"value":{{Json.Quote(value)}}}""");
    }

    private async Task<WorkerResponse> WriteKv (string key, string value)
    {
        await kv.Put(key, value, null);
        logger.LogInformation("kv wrote {Key}", key);
        return JsonReply(200, $$"""{"key":{{Json.Quote(key)}},"stored":true}""");
    }

    private static WorkerResponse JsonReply (int status, string body) =>
        new(status, """{"content-type":"application/json; charset=utf-8"}""", body);

    private static WorkerResponse TextReply (int status, string body) =>
        new(status, """{"content-type":"text/plain; charset=utf-8"}""", body);

    /// <summary>Value of one query parameter, or null when the request carries none.</summary>
    private static string? Query (Uri url, string name)
    {
        foreach (var pair in url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator < 0) continue;
            if (Uri.UnescapeDataString(pair[..separator]) == name)
                return Uri.UnescapeDataString(pair[(separator + 1)..].Replace('+', ' '));
        }
        return null;
    }
}
