using Bootsharp;
using Bootsharp.Cloudflare.Logging;
using Bootsharp.Inject;
using Cloudflare.Backend;
using Cloudflare.Backend.Hosting;
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
var app = builder.Build();
holder.App = app;
app.MapCloudflare();
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
