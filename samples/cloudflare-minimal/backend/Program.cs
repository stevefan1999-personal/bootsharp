using Bootsharp;
using Bootsharp.Cloudflare.Logging;
using Cloudflare.Minimal;
using Microsoft.Extensions.Logging;
using System.Reflection;

// The interop surface: C# the worker module calls into, and the one JavaScript function it calls
// back out to. No [WorkerAssets] — this worker serves no static assets, so no request is answered
// before.NET boots and the emitted module carries no route list at all.
[assembly: Export(typeof(IWorker))]
[assembly: Import(typeof(ILogSink))]

// Bootsharp gives JavaScript a static proxy per exported interface and needs the handler behind it
// before the first call. Wiring it by hand rather than through a container is deliberate: this
// worker has one service, and a DI container would be among the largest things in its bundle.
// Reach for Bootsharp.Inject's AddBootsharp()/RunBootsharp() once the graph earns it.
var sink = (ILogSink)Modules.Imports[typeof(ILogSink)].Instance;
var logging = new LoggerFactory([new CloudflareJsonLoggerProvider(sink)]);
Modules.Exports.Values.Single(module => module.Handler == typeof(IWorker))
    .Factory(new Worker(logging.CreateLogger<Worker>()));

/// <summary>
/// How Bootsharp names what it emits. Both preferences are load-bearing for a Cloudflare worker.
/// </summary>
/// <remarks>
/// The emitted worker module reaches the guest through the root ES module — <c>mod.IWorker</c>,
/// <c>mod.ILogSink</c> — so every type is projected into one module named <c>index</c>; and the
/// env handle's members are looked up by the names the emitted <c>wrapEnv</c> gives them, which
/// are the binding names, so they are kept verbatim instead of being camel-cased.
/// Deriving both from Bootsharp's own naming instead of restating them is tracked follow-up
///until then every Cloudflare worker declares this block.
/// </remarks>
public static class Prefs
{
    [RenameModule]
    public static string RenameModule (Type type, string @default) => "index";

    [RenameMember]
    public static string? RenameMember (MemberInfo member, string @default) =>
        member.DeclaringType == typeof(IWorkerEnv) ? member.Name : @default;
}
