using System.Data.Common;
using Cloudflare.Data.Ado;
using Cloudflare.Data.Diagnostics;
using Cloudflare.Data.Notes;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;

namespace Cloudflare.Data;

/// <summary>
/// The composition root's vocabulary: one extension per layer, each registering the abstraction
/// and hiding the implementation type from everything but this file.
/// </summary>
/// <remarks>
/// <para>
/// Worth knowing before splitting these across assemblies: the Minimal API generator answers
/// "is this handler parameter a service or the request body?" by reading
/// <c>Add{Singleton,Scoped,Transient}</c> call sites in <em>this compilation</em>
///. Extension methods are fine — these call sites are compiled
/// here — but a registration made inside a <em>referenced assembly</em> is invisible, and the
/// documented hatch for that is <c>[FromServices]</c> on the parameter.
/// </para>
/// <para>
/// Every lifetime below is chosen against one question: does this service reach a JavaScript handle
/// whose id the runtime releases when the event ends? If yes it is scoped, without exception. The
/// README's table records the answer per service and the attribute that settles it.
/// </para>
/// </remarks>
public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Makes the worker's <c>env</c> an injectable dependency.
    /// </summary>
    /// <remarks>
    /// This is the single place the app reads the ambient <see cref="WorkerContext"/>, and it reads
    /// it per scope — which is what keeps the registration correct whether the env handle is the
    /// isolate-memoized one or a fresh per-event handle. Everything
    /// downstream takes <see cref="ICloudflareEnv"/> as a constructor parameter, so it is an
    /// interface a fake can implement in a CoreCLR test rather than a static nothing can displace.
    /// Resolving it outside a worker event throws where the scope is built, which is a loud failure
    /// at a known place rather than a stale handle read much later.
    /// </remarks>
    public static IServiceCollection AddCloudflareEnv (this IServiceCollection services) =>
        services.AddScoped<ICloudflareEnv>(_ => WorkerContext.Env);

    /// <summary>
    /// Registers an open ADO.NET connection over the D1 binding, one per worker event.
    /// </summary>
    /// <remarks>
    /// Scoped and not singleton, and the reason is in the bindings rather than in taste:
    /// <c>ID1Database</c> is <c>[JSHandle(Scope = HandleScope.Isolate)]</c> and would be legal to
    /// hold forever, but <c>D1DbConnection.Open</c> immediately calls <c>withSession</c>, and
    /// <c>ID1DatabaseSession</c> carries no <c>[JSHandle]</c> — so it defaults to
    /// <c>HandleScope.Invocation</c> and its id is released when the event that opened it ends.
    /// A connection opened in event N therefore cannot be used in event N+1: workerd answers
    /// "Cannot perform I/O on behalf of a different request". <c>Close()</c> cannot rescue it
    /// either, since it only drops the C# reference.
    /// </remarks>
    public static IServiceCollection AddD1Connection (this IServiceCollection services) =>
        services.AddScoped<DbConnection>(provider =>
        {
            var connection = new D1DbConnection(provider.GetRequiredService<ICloudflareEnv>().DB);
            // Opened here rather than lazily in the repository so that "an injected DbConnection is
            // ready to use" holds for every consumer, the way it does with a pooled provider.
            connection.Open();
            return connection;
        });

    /// <summary>The hand-written-SQL implementation, over <see cref="DbConnection"/>.</summary>
    public static IServiceCollection AddAdoNotes (this IServiceCollection services) =>
        services.AddD1Connection().AddScoped<IAdoNoteRepository, AdoNoteRepository>();

    /// <summary>
    /// Registers the FreeSql ORM over the same D1 binding.
    /// </summary>
    /// <remarks>
    /// Scoped, never a singleton. FreeSql's <c>UseConnectionFactory</c> feeds an internal ADO
    /// connection pool, so a singleton <see cref="IFreeSql"/> would hand event N+1 a pooled
    /// connection carrying event N's released D1 session handle — the same failure as a captive
    /// connection, arriving one layer further from where it was caused. The container disposes it
    /// with the scope because <see cref="IFreeSql"/> is <see cref="IDisposable"/>, which is the
    /// per-handler <c>using var fsql = …</c> of the older sample with ownership moved to the
    /// container.
    /// </remarks>
    public static IServiceCollection AddFreeSql (this IServiceCollection services) =>
        services.AddScoped<IFreeSql>(provider =>
            FreeSqlD1.Open(provider.GetRequiredService<ICloudflareEnv>().DB));

    /// <summary>The ORM implementation of the same contract.</summary>
    public static IServiceCollection AddOrmNotes (this IServiceCollection services) =>
        services.AddFreeSql().AddScoped<IOrmNoteRepository, FreeSqlNoteRepository>();

    /// <summary>
    /// Registers the two probes <c>GET /api/diag/scope</c> compares.
    /// </summary>
    /// <remarks>The pair is the lifetime assertion made executable: one singleton whose id must not
    /// move, one scoped instance whose id must move on every request and whose disposal must be
    /// counted.</remarks>
    public static IServiceCollection AddScopeDiagnostics (this IServiceCollection services) =>
        services
            .AddSingleton<IIsolateProbe, IsolateProbe>()
            .AddScoped<IScopeProbe, ScopeProbe>();
}
