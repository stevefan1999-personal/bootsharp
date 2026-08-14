using Bootsharp;
using Bootsharp.Cloudflare.Logging;
using Bootsharp.Inject;
using Cloudflare.Backend;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

[assembly: Export(typeof(IWorker), typeof(IActorRuntime))]
[assembly: Import(typeof(ILogSink))]
// Routes answered straight from the ASSETS binding, before.NET is booted. Declared once, here:
// the emitted worker module is the only consumer, so the app never restates the predicate
//. "/app" is the Blazor frontend, the rest are what it loads.
[assembly: WorkerAssets("/app", "/_framework", "/css", "/favicon.ico")]

var holder = new AppHolder();
var builder = WebApplication.CreateSlimBuilder();
builder.Services.AddSingleton<IActorRuntime, ActorRuntime>();
builder.Services.AddSingleton<SiteService>();
builder.Services.AddSingleton<IWorker>(sp => new Worker(
    holder.App ?? throw new InvalidOperationException("WebApplication not built."),
    sp.GetRequiredService<ILogger<Worker>>()));
builder.Services.AddBootsharp();
AddJsonLogging(builder.Services);
// Bootsharp.Cloudflare.AspNetCore starts with an empty JSON resolver chain by design:
// there is no reflective fallback behind it, so serialization is closed-world and the app says
// which context describes it.
builder.Services.ConfigureHttpJsonOptions(options => options.AddContext(ApiJsonContext.Default));
var app = builder.Build();
holder.App = app;
app.MapCloudflare();
// Seals the endpoint table and builds the pipeline. It does not block — workerd owns the process,
// so there is nothing to wait on — but it is where the JSON metadata every endpoint needs is
// resolved, which is what makes a type missing from ApiJsonContext a boot failure naming the type
// rather than a 500 on the first request that would have serialized it.
app.Run();
app.Services.RunBootsharp();

// Microsoft.Extensions.Logging's own AddLogging resolves filters through the options and
// configuration binders, whose reflection NativeAOT cannot honour. The worker has exactly one
// provider and no filter configuration, so it makes the three registrations it needs directly:
// the provider, a factory holding only that provider, and the open generic every ILogger<T> uses.
static void AddJsonLogging(IServiceCollection services)
{
    services.AddSingleton<ILoggerProvider>(sp => new CloudflareJsonLoggerProvider(sp.GetRequiredService<ILogSink>()));
    services.AddSingleton<ILoggerFactory>(sp => new LoggerFactory(sp.GetServices<ILoggerProvider>()));
    services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
}

internal sealed class AppHolder
{
    public WebApplication? App { get; set; }
}

public static class Prefs
{
    [RenameModule]
    public static string RenameModule(Type type, string @default) => "index";

    [RenameMember]
    public static string? RenameMember(MemberInfo member, string @default)
    {
        if (member.DeclaringType == typeof(ICloudflareEnv))
            return member.Name;
        return @default;
    }
}
