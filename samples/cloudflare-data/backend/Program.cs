using Bootsharp;
using Bootsharp.Cloudflare.Logging;
using Bootsharp.Inject;
using Cloudflare.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

// The interop surface: the C# the worker module calls into, and the one JavaScript function it
// calls back out to. No [WorkerAssets] — this worker serves no static assets and answers every
// request itself, so the emitted module carries no pre-boot route list at all.
[assembly: Export(typeof(IWorker))]
[assembly: Import(typeof(ILogSink))]

// The composition root, and the only place in this app that names an implementation type. Read it
// top to bottom: logging, then JSON, then the data layers, then the app's own entrypoint.
var holder = new AppHolder();
var builder = WebApplication.CreateSlimBuilder();

AddJsonLogging(builder.Services);

// Bootsharp.Cloudflare.AspNetCore starts with an empty JSON resolver chain by design:
// there is no reflective fallback behind it, so serialization is closed-world and the app says
// which context describes it.
builder.Services.ConfigureHttpJsonOptions(options => options.AddContext(ApiJsonContext.Default));

builder.Services
    .AddScopeDiagnostics()
    .AddCloudflareEnv()
    .AddAdoNotes()
    .AddOrmNotes();

// The worker entrypoint itself. It is a singleton because workerd hands the same isolate every
// event and the WebApplication behind it is built once; the env it receives per event is written
// to the ambient slot rather than captured here, which is what keeps that legal.
builder.Services.AddSingleton<IWorker>(provider => new Worker(
    holder.App ?? throw new InvalidOperationException("WebApplication was not built."),
    provider.GetRequiredService<ILogger<Worker>>()));
builder.Services.AddBootsharp();

var app = builder.Build();
holder.App = app;
app.MapNotesApi();
// Seals the endpoint table and builds the pipeline. It does not block — workerd owns the process
// but it is where the JSON metadata every endpoint needs is resolved, which is what makes a type
// missing from ApiJsonContext a boot failure naming the type rather than a 500 on the first
// request that would have serialized it.
app.Run();
app.Services.RunBootsharp();

// Microsoft.Extensions.Logging's own AddLogging resolves filters through the options and
// configuration binders, whose reflection NativeAOT cannot honour. This worker has exactly one
// provider and no filter configuration, so it makes the three registrations it needs directly: the
// provider, a factory holding only that provider, and the open generic every ILogger<T> uses.
//
// That last registration is unbound (typeof(ILogger<>)), which is invisible to the endpoint
// binder's compile-time DI scan — it compares closed type names. So ILogger<T> is never a route
// handler parameter in this app; it is a constructor parameter of the services the handlers call,
// which is where the interesting events happen anyway. Put one on a lambda and a GET fails to
// compile with CFW026, while a POST silently binds it as the request body and warns CFW029.
static void AddJsonLogging (IServiceCollection services)
{
    services.AddSingleton<ILoggerProvider>(provider =>
        new CloudflareJsonLoggerProvider(provider.GetRequiredService<ILogSink>()));
    services.AddSingleton<ILoggerFactory>(provider => new LoggerFactory(provider.GetServices<ILoggerProvider>()));
    services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
}

/// <summary>
/// Breaks the cycle between the application and the worker that serves from it.
/// </summary>
/// <remarks>
/// <see cref="Worker"/> needs the built <c>WebApplication</c>, and the application's container is
/// what constructs the worker — so the registration reads the app out of this holder, which is
/// filled the line after <c>Build()</c> and before anything resolves. A one-field class rather than
/// a captured local because the factory lambda must not close over a variable that is still null
/// when the delegate is created.
/// </remarks>
internal sealed class AppHolder
{
    public WebApplication? App { get; set; }
}

/// <summary>
/// How Bootsharp names what it emits. Both preferences are load-bearing for a Cloudflare worker.
/// </summary>
/// <remarks>
/// The emitted worker module reaches the guest through the root ES module — <c>mod.IWorker</c>,
/// <c>mod.ILogSink</c> — so every type is projected into one module named <c>index</c>; and the env
/// handle's members are looked up by the names the emitted <c>wrapEnv</c> gives them, which are the
/// wrangler binding names, so they are kept verbatim instead of being camel-cased.
/// </remarks>
public static class Prefs
{
    [RenameModule]
    public static string RenameModule (Type type, string @default) => "index";

    [RenameMember]
    public static string? RenameMember (MemberInfo member, string @default) =>
        member.DeclaringType == typeof(ICloudflareEnv) ? member.Name : @default;
}
