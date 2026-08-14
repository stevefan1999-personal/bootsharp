using Microsoft.AspNetCore.Mvc;

namespace Cloudflare.Backend;

/// <summary>
/// This worker's endpoint table.
/// </summary>
/// <remarks>
/// Real <c>MapGet</c>/<c>MapPost</c> calls: <c>Bootsharp.Cloudflare.Generate</c> intercepts every
/// one of them and emits a bound request delegate, so the handler signatures below are what binds
///. <see cref="SiteService"/> is a registered service and arrives from DI; everything
/// else on a signature comes from the route pattern, the query string or the JSON body.
/// </remarks>
public static class Routes
{
    public static void MapCloudflare(this WebApplication app)
    {
        app.MapGet("/", (SiteService site, string? flash, string? error) => site.Home(flash, error));
        app.MapGet("/api/health", (SiteService site) => site.Health());
        app.MapGet("/api/kv", (SiteService site, string? key) => site.GetKv(key));
        app.MapPost("/api/kv", (SiteService site, HttpRequest request) => site.PutKv(request));
        app.MapGet("/api/d1", (SiteService site) => site.GetD1());
        app.MapGet("/api/d1-grid", (SiteService site) => site.GetD1Grid());
        app.MapPost("/api/d1", (SiteService site, HttpRequest request) => site.PostD1(request));
        app.MapGet("/api/freesql", (SiteService site) => site.GetFreeSql());
        app.MapPost("/api/freesql", (SiteService site, HttpRequest request) => site.PostFreeSql(request));
        app.MapGet("/api/r2", (SiteService site, string? key) => site.GetR2(key));
        app.MapPost("/api/r2", (SiteService site, HttpRequest request) => site.PutR2(request));
        app.MapGet("/api/do", (SiteService site) => site.GetCounter());
        app.MapPost("/api/do", (SiteService site) => site.IncrementCounter());
        app.MapGet("/api/do-sql", (SiteService site) => site.GetDoSql());
        app.MapPost("/api/queue", (SiteService site, HttpRequest request) => site.SendQueue(request));
        app.MapPost("/api/workflow", (SiteService site, HttpRequest request) => site.StartWorkflow(request));
        app.MapGet("/api/scheduled", (SiteService site) => site.GetScheduled());

        // The three routes the shim could not have expressed, kept together because together they
        // are the proof that compile-time binding works end to end.
        // A typed route parameter: ':int' is enforced by the matcher, so /api/notes/abc is a 404.
        app.MapGet("/api/notes/{id:int}", (SiteService site, int id) => site.GetNote(id));
        // A JSON request body, bound through the app's own JsonSerializerContext.
        app.MapPost("/api/notes", (SiteService site, NoteInput note) => site.CreateNote(note));
        // Query binding: 'text' is required (absent is a 400 the binder produces), 'times' has a
        // compile-time default that the generated delegate reproduces.
        app.MapGet("/api/echo", (SiteService site, [FromQuery] string text, [FromQuery] int times = 1) =>
            site.Echo(text, times));

        // Catch-all: '{*rest}' spans '/' where the shim's '{rest}' matched a single segment. Both
        // answer with the status-0 sentinel that hands the request back to the assets binding.
        app.MapGet("/app", (SiteService site) => site.Assets());
        app.MapGet("/app/{*rest}", (SiteService site) => site.Assets());
    }
}
