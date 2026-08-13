using Cloudflare.Backend.Hosting;

namespace Cloudflare.Backend;

public static class Routes
{
    public static void MapCloudflare(this WebApplication app)
    {
        app.MapGet<SiteService>("/", (site, ctx) => site.Home(ctx));
        app.MapGet<SiteService>("/api/health", (site, ctx) => site.Health(ctx));
        app.MapGet<SiteService>("/api/kv", (site, ctx) => site.GetKv(ctx));
        app.MapPost<SiteService>("/api/kv", (site, ctx) => site.PutKv(ctx));
        app.MapGet<SiteService>("/api/d1", (site, ctx) => site.GetD1(ctx));
        app.MapGet<SiteService>("/api/d1-grid", (site, ctx) => site.GetD1Grid(ctx));
        app.MapGet<SiteService>("/api/linq2db", (site, ctx) => site.GetLinq2Db(ctx));
        app.MapPost<SiteService>("/api/linq2db", (site, ctx) => site.PostLinq2Db(ctx));
        app.MapGet<SiteService>("/api/freesql", (site, ctx) => site.GetFreeSql(ctx));
        app.MapPost<SiteService>("/api/freesql", (site, ctx) => site.PostFreeSql(ctx));
        app.MapPost<SiteService>("/api/d1", (site, ctx) => site.PostD1(ctx));
        app.MapGet<SiteService>("/api/r2", (site, ctx) => site.GetR2(ctx));
        app.MapPost<SiteService>("/api/r2", (site, ctx) => site.PutR2(ctx));
        app.MapGet<SiteService>("/api/do", (site, ctx) => site.GetCounter(ctx));
        app.MapPost<SiteService>("/api/do", (site, ctx) => site.IncrementCounter(ctx));
        app.MapGet<SiteService>("/api/do-sql", (site, ctx) => site.GetDoSql(ctx));
        app.MapPost<SiteService>("/api/queue", (site, ctx) => site.SendQueue(ctx));
        app.MapPost<SiteService>("/api/workflow", (site, ctx) => site.StartWorkflow(ctx));
        app.MapGet<SiteService>("/api/scheduled", (site, ctx) => site.GetScheduled(ctx));
        app.MapGet<SiteService>("/app", (site, ctx) => site.Assets(ctx));
        app.MapGet<SiteService>("/app/{rest}", (site, ctx) => site.Assets(ctx));
    }
}
