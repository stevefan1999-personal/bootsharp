using Cloudflare.Backend.Data;
using Cloudflare.Backend.Hosting;
using Cloudflare.Backend.Ssr;
using FreeSql;

namespace Cloudflare.Backend;

/// <summary>
/// Request handlers. Cloudflare product bindings come from the per-request
/// <see cref="WorkerContext.Env"/> handle (C# JSImport into workerd).
/// </summary>
public sealed class SiteService
{
    private static IKvNamespace kv => WorkerContext.Env.KV;
    private static ID1Database db => WorkerContext.Env.DB;
    private static IR2Bucket r2 => WorkerContext.Env.BUCKET;
    private static IQueue queue => WorkerContext.Env.QUEUE;
    private static ICounterNamespace counter => WorkerContext.Env.COUNTER;
    private static IWorkflow workflow => WorkerContext.Env.WORKFLOW;
    private static string environment => WorkerContext.Env.ENVIRONMENT;

    public async Task<IResult> Home(HttpContext ctx)
    {
        var query = ParseQuery(ctx.Request.Query);
        query.TryGetValue("flash", out var flash);
        query.TryGetValue("error", out var error);
        return Results.Html(HomePage.Render(await LoadHome(flash, error)));
    }

    public async Task<IResult> Health(HttpContext ctx)
    {
        var runtime = ".NET " + Environment.Version;
        return Results.Json("{\"ok\":true,\"runtime\":\"" + Escape(runtime) + "\",\"framework\":\"bootsharp-nativeaot-llvm\",\"workersTypes\":\"" + WorkersTypes.Version + "\",\"environment\":\"" + Escape(environment) + "\"}");
    }

    public async Task<IResult> GetKv(HttpContext ctx)
    {
        var key = Query(ctx, "key") ?? "demo";
        var value = await kv.Get(key);
        return Results.Json("{\"key\":\"" + Escape(key) + "\",\"value\":" + ToJson(value) + "}");
    }

    public async Task<IResult> PutKv(HttpContext ctx)
    {
        var form = ParseForm(ctx.Request.Body);
        await kv.Put(form.GetValueOrDefault("key", "demo"), form.GetValueOrDefault("value", ""), null);
        return SeeHome("kv-updated");
    }

    public async Task<IResult> GetD1(HttpContext ctx)
    {
        var rows = await db.Prepare("SELECT id, body, created_at FROM notes ORDER BY id DESC LIMIT 20").All();
        return Results.Json(rows);
    }

    public async Task<IResult> GetD1Grid(HttpContext ctx)
    {
        var grid = await db.Prepare("SELECT id, body, created_at FROM notes ORDER BY id DESC LIMIT 5").Grid();
        return Results.Json("{\"columns\":[" + string.Join(",", grid.Columns.Select(c => "\"" + Escape(c) + "\"")) + "],\"rows\":" + grid.RowsJson + ",\"rowsRead\":" + grid.RowsRead + "}");
    }

    public async Task<IResult> GetFreeSql(HttpContext ctx)
    {
        using var fsql = FreeSqlNotes.Open(db);
        var notes = await fsql.Select<FreeSqlNote>().OrderByDescending(n => n.Id).Take(20).ToListAsync();
        var json = "[" + string.Join(",", notes.Select(n =>
            "{\"id\":" + n.Id + ",\"body\":" + ToJson(n.Body) + ",\"created_at\":" + ToJson(n.CreatedAt) + "}")) + "]";
        return Results.Json("{\"orm\":\"freesql\",\"notes\":" + json + "}");
    }

    public async Task<IResult> PostFreeSql(HttpContext ctx)
    {
        var form = ParseForm(ctx.Request.Body);
        using var fsql = FreeSqlNotes.Open(db);
        await fsql.Insert(new FreeSqlNote { Body = form.GetValueOrDefault("body", "") }).ExecuteAffrowsAsync();
        return SeeHome("freesql-inserted");
    }

    public async Task<IResult> PostD1(HttpContext ctx)
    {
        var form = ParseForm(ctx.Request.Body);
        await db.Prepare("INSERT INTO notes (body) VALUES (?)").Bind([form.GetValueOrDefault("body", "")]).Run();
        return SeeHome("d1-inserted");
    }

    public async Task<IResult> GetR2(HttpContext ctx)
    {
        var key = Query(ctx, "key") ?? "hello.txt";
        var obj = await r2.Get(key);
        var text = obj is null ? null : await obj.Text();
        return Results.Json("{\"key\":\"" + Escape(key) + "\",\"value\":" + ToJson(text) + "}");
    }

    public async Task<IResult> PutR2(HttpContext ctx)
    {
        var form = ParseForm(ctx.Request.Body);
        await r2.Put(form.GetValueOrDefault("key", "hello.txt"), form.GetValueOrDefault("value", ""), null);
        return SeeHome("r2-put");
    }

    public async Task<IResult> GetCounter(HttpContext ctx)
    {
        var n = (await counter.GetByName("global").Get()).Value;
        return Results.Json("{\"name\":\"global\",\"value\":" + n + "}");
    }

    public async Task<IResult> IncrementCounter(HttpContext ctx)
    {
        var n = (await counter.GetByName("global").Increment()).Value;
        return SeeHome("do-incremented-" + n);
    }

    public async Task<IResult> GetDoSql(HttpContext ctx)
    {
        var rows = await counter.GetByName("global").SqlDemo();
        return Results.Json("{\"orm\":\"do-sql\",\"rows\":" + rows + "}");
    }

    public async Task<IResult> SendQueue(HttpContext ctx)
    {
        var form = ParseForm(ctx.Request.Body);
        await queue.Send(new QueueMessage(form.GetValueOrDefault("body", "hello")));
        return SeeHome("queue-sent");
    }

    public async Task<IResult> StartWorkflow(HttpContext ctx)
    {
        var form = ParseForm(ctx.Request.Body);
        var userId = form.GetValueOrDefault("userId", "demo");
        var instance = await workflow.Create(new WorkflowInstanceCreateOptions
        {
            Params = "{\"userId\":\"" + Escape(userId) + "\"}"
        });
        return SeeHome("workflow-" + instance.Id);
    }

    /// <summary>
    /// Serves the heartbeat the cron handler stored, so a scheduled run is observable over HTTP.
    /// The stored value is already JSON, so it is embedded rather than re-encoded.
    /// </summary>
    public async Task<IResult> GetScheduled(HttpContext ctx)
    {
        var heartbeat = await kv.Get(Worker.ScheduledHeartbeatKey);
        return Results.Json("{\"lastScheduled\":" + (heartbeat ?? "null") + "}");
    }

    public Task<IResult> Assets(HttpContext ctx) => Task.FromResult(Results.Assets());

    private async Task<HomeModel> LoadHome(string? flash, string? error)
    {
        string? kvValue = null;
        var d1 = "[]";
        var r2List = "[]";
        var n = 0;
        string? loadError = error;
        try { kvValue = await kv.Get("demo"); } catch (Exception ex) { loadError = Join(loadError, "kv: " + ex.Message); }
        try { d1 = await db.Prepare("SELECT id, body, created_at FROM notes ORDER BY id DESC LIMIT 10").All(); }
        catch (Exception ex) { loadError = Join(loadError, "d1: " + ex.Message); }
        try
        {
            var listed = await r2.List(new R2ListOptions { Prefix = "", Limit = 20 });
            r2List = "[" + string.Join(",", listed.Objects.Select(o => "{\"key\":\"" + Escape(o.Key) + "\",\"size\":" + o.Size + "}")) + "]";
        }
        catch (Exception ex) { loadError = Join(loadError, "r2: " + ex.Message); }
        try { n = (await counter.GetByName("global").Get()).Value; } catch (Exception ex) { loadError = Join(loadError, "do: " + ex.Message); }
        return new HomeModel
        {
            Runtime = ".NET " + Environment.Version,
            Host = "cloudflare-workers",
            WorkersTypes = Cloudflare.Workers.WorkersTypes.Version,
            KvValue = kvValue,
            D1Rows = d1,
            R2Objects = r2List,
            Counter = n,
            Flash = flash,
            Error = loadError
        };
    }

    private static IResult SeeHome(string flash) => Results.Redirect("/?flash=" + Uri.EscapeDataString(flash));

    private static string? Query(HttpContext ctx, string key)
    {
        ParseQuery(ctx.Request.Query).TryGetValue(key, out var value);
        return value;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(query)) return result;
        var q = query[0] == '?' ? query[1..] : query;
        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0) result[Uri.UnescapeDataString(part)] = "";
            else result[Uri.UnescapeDataString(part[..eq])] = Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
        }
        return result;
    }

    private static Dictionary<string, string> ParseForm(string body) => ParseQuery(body);

    internal static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");

    private static string ToJson(string? value) => value is null ? "null" : "\"" + Escape(value) + "\"";

    private static string Join(string? left, string right) => string.IsNullOrEmpty(left) ? right : left + "; " + right;
}
