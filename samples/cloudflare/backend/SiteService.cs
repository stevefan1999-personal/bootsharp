using System.Globalization;
using Cloudflare.Backend.Data;
using Cloudflare.Backend.Hosting;
using Cloudflare.Backend.Ssr;
using FreeSql;
using Microsoft.Extensions.Logging;

namespace Cloudflare.Backend;

/// <summary>
/// Request handlers. Cloudflare product bindings come from the per-request
/// <see cref="WorkerContext.Env"/> handle (C# JSImport into workerd).
/// </summary>
/// <remarks>
/// Every method takes the values it needs rather than an <see cref="HttpContext"/> it has to dig
/// through: the generator binds route values, query values and the JSON body at compile time
///so a handler's signature is its contract. The ones that still take an
/// <see cref="HttpRequest"/> are the HTML form posts, which read a urlencoded body this package
/// deliberately does not parse — see <see cref="FormBody"/>.
/// </remarks>
public sealed class SiteService(ILogger<SiteService> logger)
{
    private static IKvNamespace kv => WorkerContext.Env.KV;
    private static ID1Database db => WorkerContext.Env.DB;
    private static IR2Bucket r2 => WorkerContext.Env.BUCKET;
    private static IQueue queue => WorkerContext.Env.QUEUE;
    private static ICounterNamespace counter => WorkerContext.Env.COUNTER;
    private static IWorkflow workflow => WorkerContext.Env.WORKFLOW;
    private static string environment => WorkerContext.Env.ENVIRONMENT;

    /// <summary>Newest-first note rows; the three list routes and the SSR page differ only in LIMIT.</summary>
    private const string selectNotes = "SELECT id, body, created_at FROM notes ORDER BY id DESC";

    public async Task<IResult> Home(string? flash, string? error)
    {
        var model = await LoadHome(flash, error);
        // The result takes the template, not its output: the writer buffers today and streams once
        // the transport can, with no change here or in HomePage.
        return TypedResults.Html(html => HomePage.Render(html, model));
    }

    public IResult Health() => TypedResults.Ok(new HealthView(
        Ok: true,
        Runtime: ".NET " + Environment.Version,
        Framework: "bootsharp-nativeaot-llvm",
        WorkersTypes: WorkersTypes.Version,
        Environment: environment));

    public async Task<IResult> GetKv(string? key)
    {
        key ??= "demo";
        return TypedResults.Ok(new ValueView(key, await kv.Get(key)));
    }

    public async Task<IResult> PutKv(HttpRequest request)
    {
        var form = await FormBody.ReadAsync(request);
        await kv.Put(form.Get("key", "demo"), form.Get("value"), null);
        return Flash.Home("kv-updated");
    }

    /// <summary>Passes D1's own <c>success</c>/<c>meta</c>/<c>results</c> envelope through verbatim.</summary>
    /// <remarks>Decoding it only to re-encode the same document would cost two passes over the same
    /// bytes and would hide the <c>meta</c> block, which is half of what this route exists to show.
    /// <c>/api/notes/{id:int}</c> is where the typed shape is demonstrated instead, because there a
    /// single row is decoded anyway.</remarks>
    public async Task<IResult> GetD1() => JsonText(await db.Prepare(selectNotes + " LIMIT 20").All());

    public async Task<IResult> GetD1Grid()
    {
        var grid = await db.Prepare(selectNotes + " LIMIT 5").Grid();
        return JsonText("{\"columns\":[" + string.Join(",", grid.Columns.Select(c => "\"" + Json.Escape(c) + "\"")) +
                        "],\"rows\":" + grid.RowsJson + ",\"rowsRead\":" + grid.RowsRead + "}");
    }

    /// <summary>
    /// <c>GET /api/notes/{id:int}</c> — a typed route parameter end to end.
    /// </summary>
    /// <remarks>
    /// The <c>:int</c> constraint is enforced by the matcher, so <c>/api/notes/abc</c> is a 404 (no
    /// endpoint matched) rather than a 400 from a failed parse, which is ASP.NET Core's behaviour
    /// too. <paramref name="id"/> arrives already parsed — the handler never sees a string.
    /// </remarks>
    public async Task<IResult> GetNote(int id)
    {
        var note = await ReadNote(id);
        return note is null
            ? TypedResults.Problem(detail: $"No note with id {id}.", statusCode: StatusCodes.Status404NotFound)
            : TypedResults.Ok(note);
    }

    /// <summary>
    /// <c>POST /api/notes</c> — a JSON request body bound through source-generated metadata.
    /// </summary>
    /// <remarks>Answers <c>201 Created</c> with the row as stored, which is what makes the round
    /// trip observable: the id and the timestamp are the database's, not the caller's.</remarks>
    public async Task<IResult> CreateNote(NoteInput note)
    {
        if (string.IsNullOrWhiteSpace(note.Body))
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> {
                ["body"] = ["A note body is required."]
            });
        // RunGrid carries the insert's meta, so the row is read back by its own id rather than by
        // "whatever is newest" — which a concurrent insert would make the wrong row.
        var inserted = await db.Prepare("INSERT INTO notes (body) VALUES (?)").Bind([note.Body]).RunGrid();
        var created = await ReadNote((int)inserted.LastRowId);
        return created is null
            ? TypedResults.Problem(detail: "The note was inserted but could not be read back.")
            : TypedResults.Created($"/api/notes/{created.Id}", created);
    }

    /// <summary>
    /// <c>GET /api/echo</c> — query binding: one required value, one with a compile-time default.
    /// </summary>
    /// <remarks>A missing <c>text</c> is a 400 the generated binder produces before this method is
    /// entered; a non-numeric <c>times</c> is the same. Neither check is written here.</remarks>
    public IResult Echo(string text, int times) =>
        TypedResults.Ok(new EchoView(text, times, string.Join(" ", Enumerable.Repeat(text, Math.Clamp(times, 1, 10)))));

    public async Task<IResult> GetFreeSql()
    {
        using var fsql = FreeSqlNotes.Open(db);
        var notes = await fsql.Select<FreeSqlNote>().OrderByDescending(n => n.Id).Take(20).ToListAsync();
        return TypedResults.Ok(new FreeSqlView("freesql",
            [.. notes.Select(n => new NoteView(n.Id, n.Body, n.CreatedAt))]));
    }

    public async Task<IResult> PostFreeSql(HttpRequest request)
    {
        var form = await FormBody.ReadAsync(request);
        using var fsql = FreeSqlNotes.Open(db);
        await fsql.Insert(new FreeSqlNote { Body = form.Get("body") }).ExecuteAffrowsAsync();
        return Flash.Home("freesql-inserted");
    }

    public async Task<IResult> PostD1(HttpRequest request)
    {
        var form = await FormBody.ReadAsync(request);
        await db.Prepare("INSERT INTO notes (body) VALUES (?)").Bind([form.Get("body")]).Run();
        return Flash.Home("d1-inserted");
    }

    public async Task<IResult> GetR2(string? key)
    {
        key ??= "hello.txt";
        var obj = await r2.Get(key);
        return TypedResults.Ok(new ValueView(key, obj is null ? null : await obj.Text()));
    }

    public async Task<IResult> PutR2(HttpRequest request)
    {
        var form = await FormBody.ReadAsync(request);
        await r2.Put(form.Get("key", "hello.txt"), form.Get("value"), null);
        return Flash.Home("r2-put");
    }

    public async Task<IResult> GetCounter() =>
        TypedResults.Ok(new CounterView("global", await counter.GetByName("global").Get()));

    public async Task<IResult> IncrementCounter()
    {
        var n = await counter.GetByName("global").Increment();
        return Flash.Home("do-incremented-" + n);
    }

    public async Task<IResult> GetDoSql() =>
        JsonText("{\"orm\":\"do-sql\",\"rows\":" + await counter.GetByName("global").SqlDemo() + "}");

    public async Task<IResult> SendQueue(HttpRequest request)
    {
        var form = await FormBody.ReadAsync(request);
        await queue.Send(new QueueMessage(form.Get("body", "hello")));
        return Flash.Home("queue-sent");
    }

    public async Task<IResult> StartWorkflow(HttpRequest request)
    {
        var form = await FormBody.ReadAsync(request);
        var userId = form.Get("userId", "demo");
        var instance = await workflow.Create(new WorkflowInstanceCreateOptions
        {
            Params = "{\"userId\":\"" + Json.Escape(userId) + "\"}"
        });
        return Flash.Home("workflow-" + instance.Id);
    }

    /// <summary>
    /// Serves the heartbeat the cron handler stored, so a scheduled run is observable over HTTP.
    /// The stored value is already JSON, so it is embedded rather than re-encoded.
    /// </summary>
    public async Task<IResult> GetScheduled()
    {
        var heartbeat = await kv.Get(Worker.ScheduledHeartbeatKey);
        // Template arguments become their own fields in Workers Logs, so "did the cron ever run in
        // this environment" is answerable by filtering on the field rather than grepping messages.
        logger.LogInformation("scheduled heartbeat read {Found} in {Environment}", heartbeat is not null, environment);
        return JsonText("{\"lastScheduled\":" + (heartbeat ?? "null") + "}");
    }

    /// <summary>
    /// The post-guest fallback to the assets binding: the result tells <c>js/runtime.mjs</c> that
    /// this worker declined the request, and it re-issues it against <c>ASSETS</c>.
    /// </summary>
    /// <remarks>Rarely reached — <c>[WorkerAssets]</c> makes the JS side answer <c>/app</c> before
    /// .NET is booted at all — but it keeps the fallback true for any prefix not declared there.
    /// It used to be <c>StatusCode(0)</c>, which no <c>Response</c> can carry and any handler could
    /// have produced by accident.</remarks>
    public IResult Assets() => TypedResults.PassThroughToAssets();

    /// <summary>
    /// A binary response: the eight bytes of a PNG signature, which is not valid UTF-8.
    /// </summary>
    /// <remarks>Here to prove the byte channel end to end. Returned as text
    /// these bytes arrive as replacement characters — the silent degradation the buffered-text path
    /// used to have — so the smoke test asserts on the bytes themselves.</remarks>
    public IResult GetBytes() => TypedResults.Bytes(
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png", "signature.png");

    /// <summary>
    /// Two cookies and the request's own, which needs two <c>Set-Cookie</c> headers on one response.
    /// </summary>
    /// <remarks>The case the flat header wire shape could not carry: comma-joining them sets one
    /// malformed cookie. <see cref="HttpResponse.Cookies"/> works because the snapshot renders a
    /// repeated header as an array now.</remarks>
    public IResult GetCookies(HttpContext context)
    {
        context.Response.Cookies.Append("session", "abc");
        context.Response.Cookies.Append("theme", "dark", new CookieOptions { HttpOnly = true, Path = "/" });
        return TypedResults.Ok(new CookiesView(context.Request.Cookies["session"], context.Request.Cookies.Count));
    }

    /// <summary>Reads one <c>notes</c> row, or null when there is none.</summary>
    /// <remarks><c>ID1PreparedStatement.First()</c> answers with the row as a JSON object — the shape
    /// <see cref="NoteView"/> describes — where <c>All()</c> answers with D1's whole
    /// <c>success</c>/<c>meta</c>/<c>results</c> envelope, which is what <c>/api/d1</c> passes through.
    /// D1's bind() takes its values as strings across the interop boundary; the parse happened in the
    /// generated binder, so what is round-tripped here is already known to be an integer.</remarks>
    private static async Task<NoteView?> ReadNote(int id) => NoteRows.Read(
        await db.Prepare("SELECT id, body, created_at FROM notes WHERE id = ? LIMIT 1")
            .Bind([id.ToString(CultureInfo.InvariantCulture)]).First());

    private async Task<HomeModel> LoadHome(string? flash, string? error)
    {
        string? kvValue = null;
        var d1 = "[]";
        var r2List = "[]";
        var n = 0;
        string? loadError = error;
        try { kvValue = await kv.Get("demo"); } catch (Exception ex) { loadError = Join(loadError, "kv: " + ex.Message); }
        try { d1 = await db.Prepare(selectNotes + " LIMIT 10").All(); }
        catch (Exception ex) { loadError = Join(loadError, "d1: " + ex.Message); }
        try
        {
            var listed = await r2.List(new R2ListOptions { Prefix = "", Limit = 20 });
            r2List = "[" + string.Join(",", listed.Objects.Select(o => "{\"key\":\"" + Json.Escape(o.Key) + "\",\"size\":" + o.Size + "}")) + "]";
        }
        catch (Exception ex) { loadError = Join(loadError, "r2: " + ex.Message); }
        try { n = await counter.GetByName("global").Get(); } catch (Exception ex) { loadError = Join(loadError, "do: " + ex.Message); }
        return new HomeModel
        {
            Runtime = ".NET " + Environment.Version,
            Host = "cloudflare-workers",
            WorkersTypes = WorkersTypes.Version,
            KvValue = kvValue,
            D1Rows = d1,
            R2Objects = r2List,
            Counter = n,
            Flash = flash,
            Error = loadError
        };
    }

    // TypedResults.Content appends the charset, so this reads as one decision rather than as a
    // content-type literal repeated at every call site.
    private static IResult JsonText(string json) => TypedResults.Content(json, "application/json");

    private static string Join(string? left, string right) => string.IsNullOrEmpty(left) ? right : left + "; " + right;
}
