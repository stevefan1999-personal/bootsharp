namespace Bootsharp.Cloudflare.Generate.Tests;

/// <summary>
/// Compilation units the driver tests run the generator over. The <c>Bootsharp.Cloudflare</c> half
/// stands in for the packaged runtime and the <c>Cloudflare.Backend</c> half for an app's own
/// surface: the generator binds to the library's types by namespace and name, finds the env
/// interface the app declares, and the emitted dispatch calls the runtime codecs by name — so both
/// have to be present for a generator run to mean anything.
/// </summary>
internal static class TestSources
{
    /// <summary>
    /// Entrypoint base types and binding handles, mirroring <c>Bootsharp.Cloudflare</c>. Only the
    /// shapes the generator matches on are kept — it looks up namespace, type name and signature.
    /// </summary>
    public const string Workers = """
        namespace Bootsharp.Cloudflare;

        using System.Threading.Tasks;

        public interface IExecutionContext { void Abort(string? reason); }

        public interface IKvNamespace { Task<string?> Get(string key); }

        public interface IJsRequest { string Url { get; } }

        public interface IScheduledController { string Cron { get; } void NoRetry(); }

        public interface IDurableObjectState { string Id { get; } }

        public interface IWorkflowStep { Task Sleep(string name, string duration); }

        public sealed record WorkflowEvent(string Payload);

        [System.AttributeUsage(System.AttributeTargets.Interface)]
        public sealed class WorkerEnvAttribute : System.Attribute;

        [System.AttributeUsage(System.AttributeTargets.Assembly)]
        public sealed class WorkerAssetsAttribute(params string[] pathPrefixes) : System.Attribute
        {
            public string[] PathPrefixes { get; } = pathPrefixes;
            public string Binding { get; set; } = "ASSETS";
        }

        public abstract class DurableObject<TEnv> where TEnv : class
        {
            protected DurableObject(IDurableObjectState ctx, TEnv env)
            {
                Ctx = ctx;
                Env = env;
            }

            protected IDurableObjectState Ctx { get; }
            protected TEnv Env { get; }
        }

        public interface IWorkflowEntrypoint
        {
            Task Run(WorkflowEvent evt, IWorkflowStep step);
        }

        public abstract class WorkflowEntrypoint<TEnv> : IWorkflowEntrypoint where TEnv : class
        {
            protected WorkflowEntrypoint(IExecutionContext ctx, TEnv env)
            {
                Ctx = ctx;
                Env = env;
            }

            protected IExecutionContext Ctx { get; }
            protected TEnv Env { get; }

            public abstract Task Run(WorkflowEvent evt, IWorkflowStep step);
        }

        public abstract class WorkerEntrypoint<TEnv, TResponse> where TEnv : class
        {
            public abstract Task<TResponse> Fetch(IJsRequest request, TEnv env);
            public virtual Task Queue(string messagesJson, TEnv env) => Task.CompletedTask;
            public virtual Task Scheduled(IScheduledController controller, TEnv env) => Task.CompletedTask;
        }
        """;

    /// <summary>
    /// The interop surface an app declares its worker against: Bootsharp's assembly attribute and
    /// the logging types whose use obliges it. Both are matched by full name, so a stub under the
    /// real namespaces is what the generator sees in a real app.
    /// </summary>
    public const string Interop = """
        namespace Bootsharp
        {
            using System;

            [AttributeUsage(AttributeTargets.Assembly)]
            public sealed class ImportAttribute(params Type[] types) : Attribute
            {
                public Type[] Types { get; } = types;
            }
        }

        namespace Bootsharp.Cloudflare.Logging
        {
            // Declared without naming each other: in a real app these come from a referenced
            // assembly, so the only source that mentions them is the app's own.
            public interface ILogSink { void Write(int level, string entryJson); }

            public sealed class CloudflareJsonLoggerProvider { }
        }
        """;

    /// <summary>
    /// Packaged half of the actor runtime: the instance registry and the argument/result codecs the
    /// generated switch calls. Signatures mirror
    /// <c>src/cs/Bootsharp.Cloudflare/Entrypoints/ActorRuntimeBase.cs</c>, and
    /// <see cref="ActorDispatchTests"/> guards them against drifting apart.
    /// </summary>
    public const string Runtime = """
        namespace Bootsharp.Cloudflare;

        using System;
        using System.Collections.Generic;
        using System.Globalization;
        using System.Text.Json;

        public abstract class ActorRuntimeBase<TEnv> where TEnv : class
        {
            private readonly Dictionary<int, Actor> instances = new();
            private int next = 1;

            protected int Track(object instance, TEnv env)
            {
                var id = next++;
                instances[id] = new Actor(instance, env);
                return id;
            }

            protected Actor GetActor(int id) =>
                instances.TryGetValue(id, out var actor)
                    ? actor
                    : throw new InvalidOperationException($"Actor {id} is gone.");

            protected readonly record struct Actor(object Instance, TEnv Env);

            protected readonly struct EnvScope : IDisposable
            {
                public EnvScope(TEnv env) { }
                public void Dispose() { }
            }

            protected static JsonElement[] ReadArgs(string argsJson) => [];

            protected static bool HasArg(JsonElement[] args, int index) => index < args.Length;

            protected static JsonElement Arg(JsonElement[] args, int index, string method, string name) =>
                args[index];

            protected static int ArgInt(JsonElement[] args, int index, string method, string name) =>
                Arg(args, index, method, name).GetInt32();

            protected static long ArgLong(JsonElement[] args, int index, string method, string name) =>
                Arg(args, index, method, name).GetInt64();

            protected static double ArgDouble(JsonElement[] args, int index, string method, string name) =>
                Arg(args, index, method, name).GetDouble();

            protected static bool ArgBool(JsonElement[] args, int index, string method, string name) =>
                Arg(args, index, method, name).GetBoolean();

            protected static string ArgString(JsonElement[] args, int index, string method, string name) =>
                Arg(args, index, method, name).GetString()!;

            protected static string? ArgStringOrNull(JsonElement[] args, int index, string method, string name) =>
                Arg(args, index, method, name).GetString();

            protected static string JsonInt(int value) => value.ToString(CultureInfo.InvariantCulture);

            protected static string JsonLong(long value) => value.ToString(CultureInfo.InvariantCulture);

            protected static string JsonDouble(double value) => value.ToString("R", CultureInfo.InvariantCulture);

            protected static string JsonBool(bool value) => value ? "true" : "false";

            protected static string JsonString(string? value) => value is null ? "null" : $"\"{value}\"";
        }
        """;

    /// <summary>
    /// The app's own half. <c>ICloudflareEnv</c> lives here rather than in the library source on
    /// purpose: the bindings are app configuration, and finding the interface by the marker the app
    /// put on it — under whatever name, in whatever namespace — is what env resolution is for.
    /// </summary>
    /// <param name="attributes">Assembly attributes the app declares, which are configuration too.</param>
    /// <param name="bindings">Extra properties on the env interface, beyond the two every case has.</param>
    /// <param name="marker">Attribute list on the env interface; empty for an app that marks none.</param>
    /// <param name="extra">Further declarations, for cases that need a second env or a logger use.</param>
    /// <param name="actorRuntime">The app's half of the actor dispatch partial. It defaults to
    /// absent, because only an app hosting a Durable Object or a workflow declares it — the
    /// generated half exists only then, and a partial claiming <c>IActorRuntime</c> without it does
    /// not compile. Cases that host an actor pass <see cref="ActorRuntime"/>.</param>
    public static string App (
        string attributes = "",
        string bindings = "",
        string marker = "[WorkerEnv]",
        string extra = "",
        string actorRuntime = "") => $$"""
        using System.Threading.Tasks;
        using Bootsharp;
        using Bootsharp.Cloudflare;
        using Bootsharp.Cloudflare.Logging;
        {{attributes}}

        namespace Cloudflare.Backend;

        public sealed record HttpResponseData(int Status, string Body);

        {{marker}}
        public interface ICloudflareEnv
        {
            IKvNamespace KV { get; }
            {{bindings}}
        }

        {{actorRuntime}}

        {{extra}}
        """;

    /// <summary>
    /// The app's half of the actor dispatch: the exported interface and the partial the generated
    /// switches land in. An app hosting no actor declares neither (see <see cref="App"/>).
    /// </summary>
    public const string ActorRuntime = """
        public interface IActorRuntime
        {
            int ConstructDurableObject(string className, IDurableObjectState ctx, ICloudflareEnv env);
            int ConstructWorkflow(string className, IExecutionContext ctx, ICloudflareEnv env);
            Task<string> CallDurableObject(int id, string method, string argsJson);
            Task<string> RunWorkflow(int id, string payloadJson, IWorkflowStep step);
        }

        public sealed partial class ActorRuntime : ActorRuntimeBase<ICloudflareEnv>, IActorRuntime;
        """;

    /// <summary>
    /// The app half plus the namespace binding for <see cref="CounterActor"/>. Kept out of the
    /// default app so that a case which hosts no Durable Object declares no binding for one — which
    /// is what makes CFW018 (a binding whose actor is not there) a real signal.
    /// </summary>
    public static string AppWithCounter => App(
        bindings: "ICounterNamespace COUNTER { get; }",
        extra: "public interface ICounterNamespace { }",
        actorRuntime: ActorRuntime);

    /// <summary>A worker that only serves requests: no cron trigger, no queue consumer.</summary>
    public const string FetchOnlyWorker = """
        namespace Sample.Edge;

        using System.Threading.Tasks;
        using Bootsharp.Cloudflare;
        using Cloudflare.Backend;

        public sealed class Site : WorkerEntrypoint<ICloudflareEnv, HttpResponseData>
        {
            public override Task<HttpResponseData> Fetch(IJsRequest request, ICloudflareEnv env) =>
                Task.FromResult(new HttpResponseData(200, request.Url));
        }
        """;

    /// <summary>The same worker with a cron trigger: overriding <c>Scheduled</c> is the opt-in.</summary>
    public const string ScheduledWorker = """
        namespace Sample.Edge;

        using System.Threading.Tasks;
        using Bootsharp.Cloudflare;
        using Cloudflare.Backend;

        public sealed class Site : WorkerEntrypoint<ICloudflareEnv, HttpResponseData>
        {
            public override Task<HttpResponseData> Fetch(IJsRequest request, ICloudflareEnv env) =>
                Task.FromResult(new HttpResponseData(200, request.Url));

            public override Task Scheduled(IScheduledController controller, ICloudflareEnv env) =>
                Task.CompletedTask;
        }
        """;

    /// <summary>
    /// A Durable Object outside the namespace the dispatch is emitted into (the one the app's env
    /// interface lives in), so anything but a fully qualified reference would not compile.
    /// </summary>
    public const string CounterActor = """
        namespace Sample.Actors;

        using System.Threading.Tasks;
        using Bootsharp.Cloudflare;
        using Cloudflare.Backend;

        public sealed class Counter : DurableObject<ICloudflareEnv>
        {
            private int total;

            public Counter(IDurableObjectState ctx, ICloudflareEnv env) : base(ctx, env) { }

            public int Total() => total;

            public Task<int> Add(int amount) => Task.FromResult(total += amount);
        }
        """;

    /// <summary>A workflow, whose only projected member is the <c>Run</c> handler.</summary>
    public const string DemoWorkflow = """
        namespace Sample.Actors;

        using System.Threading.Tasks;
        using Bootsharp.Cloudflare;
        using Cloudflare.Backend;

        public sealed class Pipeline : WorkflowEntrypoint<ICloudflareEnv>
        {
            public Pipeline(IExecutionContext ctx, ICloudflareEnv env) : base(ctx, env) { }

            public override Task Run(WorkflowEvent evt, IWorkflowStep step) => Task.CompletedTask;
        }
        """;

    /// <summary>Every return and parameter shape the projection accepts, in one actor.</summary>
    public const string LedgerActor = """
        namespace Sample.Actors;

        using System.Threading.Tasks;
        using Bootsharp.Cloudflare;
        using Cloudflare.Backend;

        public sealed class Ledger : DurableObject<ICloudflareEnv>
        {
            public Ledger(IDurableObjectState ctx, ICloudflareEnv env) : base(ctx, env) { }

            public void Reset() { }
            public int Count() => 0;
            public long Stamp() => 0L;
            public double Average() => 0d;
            public bool Sealed() => true;
            public string Label() => "";
            public int Next() => 0;
            public Task Clear() => Task.CompletedTask;
            public Task<string> Note(string key, string? fallback) => Task.FromResult(fallback ?? key);
            public Task<bool> Record(int amount, long stamp, double weight, bool audited) => Task.FromResult(audited);
            public string Greet(string name, int times = 1) => name;
        }
        """;

    /// <summary>An RPC method whose result cannot cross the JSON boundary.</summary>
    public const string UnsupportedReturnActor = """
        namespace Sample.Actors;

        using System.Threading.Tasks;
        using Bootsharp.Cloudflare;
        using Cloudflare.Backend;

        public sealed record Snapshot(int Count);

        public sealed class Archive : DurableObject<ICloudflareEnv>
        {
            public Archive(IDurableObjectState ctx, ICloudflareEnv env) : base(ctx, env) { }

            public Task<Snapshot> Load() => Task.FromResult(new Snapshot(0));

            public int Count() => 0;
        }
        """;

    /// <summary>An RPC parameter whose type cannot arrive in the JSON argument array.</summary>
    public const string UnsupportedParameterActor = """
        namespace Sample.Actors;

        using System.Threading.Tasks;
        using Bootsharp.Cloudflare;
        using Cloudflare.Backend;

        public sealed class Blobs : DurableObject<ICloudflareEnv>
        {
            public Blobs(IDurableObjectState ctx, ICloudflareEnv env) : base(ctx, env) { }

            public Task Store(byte[] payload) => Task.CompletedTask;
        }
        """;

    /// <summary>An RPC method that would claim a JS prototype member workerd owns.</summary>
    public const string ReservedNameActor = """
        namespace Sample.Actors;

        using System.Threading.Tasks;
        using Bootsharp.Cloudflare;
        using Cloudflare.Backend;

        public sealed class Gateway : DurableObject<ICloudflareEnv>
        {
            public Gateway(IDurableObjectState ctx, ICloudflareEnv env) : base(ctx, env) { }

            public Task<string> Fetch() => Task.FromResult("");
        }
        """;

    /// <summary>
    /// A Durable Object whose projected namespace wrapper would take the name of a helper the
    /// module imports from the packaged runtime (<c>wrapIKvNamespace</c>).
    /// </summary>
    public const string CollidingActor = """
        namespace Sample.Actors;

        using Bootsharp.Cloudflare;
        using Cloudflare.Backend;

        public sealed class Kv : DurableObject<ICloudflareEnv>
        {
            public Kv(IDurableObjectState ctx, ICloudflareEnv env) : base(ctx, env) { }

            public int Size() => 0;
        }
        """;

    /// <summary>A second entrypoint class named as one already projected, in another namespace.</summary>
    public const string DuplicateWorker = """
        namespace Sample.Mirror;

        using System.Threading.Tasks;
        using Bootsharp.Cloudflare;
        using Cloudflare.Backend;

        public sealed class Site : WorkerEntrypoint<ICloudflareEnv, HttpResponseData>
        {
            public override Task<HttpResponseData> Fetch(IJsRequest request, ICloudflareEnv env) =>
                Task.FromResult(new HttpResponseData(200, request.Url));
        }
        """;

    /// <summary>A public worker member that fills no handler slot and has no RPC surface.</summary>
    public const string StrayMemberWorker = """
        namespace Sample.Edge;

        using System.Threading.Tasks;
        using Bootsharp.Cloudflare;
        using Cloudflare.Backend;

        public sealed class Site : WorkerEntrypoint<ICloudflareEnv, HttpResponseData>
        {
            public override Task<HttpResponseData> Fetch(IJsRequest request, ICloudflareEnv env) =>
                Task.FromResult(new HttpResponseData(200, request.Url));

            public Task Warmup() => Task.CompletedTask;
        }
        """;
}
