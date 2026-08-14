using Cloudflare.Razor.Data;
using Cloudflare.Razor.Notes;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;

namespace Cloudflare.Razor;

/// <summary>
/// Composition root vocabulary. Lifetimes are decided by one question: does this service reach a
/// JavaScript handle released when the event ends? If yes, it is scoped.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>The ambient worker env, read once per event scope.</summary>
    public static IServiceCollection AddCloudflareEnv (this IServiceCollection services) =>
        services.AddScoped<IWorkerEnv>(_ => WorkerContext.Env);

    /// <summary>
    /// FreeSql over D1. Scoped because the ORM's connection factory would otherwise hand the next
    /// event a pooled connection whose session handle was already released.
    /// </summary>
    public static IServiceCollection AddFreeSqlNotes (this IServiceCollection services) =>
        services
            .AddScoped<IFreeSql>(provider => FreeSqlD1.Open(provider.GetRequiredService<IWorkerEnv>().DB))
            .AddScoped<INoteRepository, FreeSqlNoteRepository>();
}
