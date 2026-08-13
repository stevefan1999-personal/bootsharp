namespace Cloudflare.Workers.Generator.Tests;

/// <summary>
/// Compilation units the driver tests run the generator over. The <c>Cloudflare.Workers</c> and
/// <c>Cloudflare.Backend</c> halves stand in for the sample's hand-written surface: the generator
/// binds to those types by metadata name, and the emitted dispatch calls the runtime codecs by
/// name, so both have to be present for a generator run to mean anything.
/// </summary>
internal static class TestSources
{
    /// <summary>
    /// Entrypoint base types and binding handles, mirroring <c>backend/Workers/*.cs</c>. Only the
    /// shapes the generator matches on are kept — it looks up namespace, type name and signature.
    /// </summary>
    public const string Workers = """
        namespace Cloudflare.Workers;

        using System.Threading.Tasks;

        public interface IExecutionContext { void Abort(string? reason); }

        public interface IKvNamespace { Task<string?> Get(string key); }

        public interface ICounterNamespace { }

        public interface ICloudflareEnv
        {
            IKvNamespace KV { get; }
            ICounterNamespace COUNTER { get; }
        }

        public interface IJsRequest { string Url { get; } }

        public interface IScheduledController { string Cron { get; } void NoRetry(); }

        public interface IDurableObjectState { string Id { get; } }

        public interface IWorkflowStep { Task Sleep(string name, string duration); }

        public sealed record RpcInt(int Value);

        public sealed record WorkflowEvent(string Payload);

        public abstract class DurableObject
        {
            protected DurableObject(IDurableObjectState ctx, ICloudflareEnv env)
            {
                Ctx = ctx;
                Env = env;
            }

            protected IDurableObjectState Ctx { get; }
            protected ICloudflareEnv Env { get; }
        }

        public abstract class WorkflowEntrypoint
        {
            protected WorkflowEntrypoint(IExecutionContext ctx, ICloudflareEnv env)
            {
                Ctx = ctx;
                Env = env;
            }

            protected IExecutionContext Ctx { get; }
            protected ICloudflareEnv Env { get; }

            public abstract Task Run(WorkflowEvent evt, IWorkflowStep step);
        }

        public abstract class WorkerEntrypoint
        {
            public abstract Task<Cloudflare.Backend.HttpResponseData> Fetch(IJsRequest request, ICloudflareEnv env);
            public virtual Task Queue(string messagesJson, ICloudflareEnv env) => Task.CompletedTask;
            public virtual Task Scheduled(IScheduledController controller, ICloudflareEnv env) => Task.CompletedTask;
        }
        """;

    /// <summary>
    /// Hand-written half of <c>ActorRuntime</c>: the instance registry and the argument/result
    /// codecs the generated switch calls. Signatures mirror <c>backend/ActorRuntime.cs</c>, and
    /// <see cref="ActorDispatchTests"/> guards them against drifting apart.
    /// </summary>
    public const string Runtime = """
        namespace Cloudflare.Backend;

        using System;
        using System.Collections.Generic;
        using System.Globalization;
        using System.Text.Json;
        using System.Threading.Tasks;
        using Cloudflare.Workers;

        public sealed record HttpResponseData(int Status, string Body);

        public interface IActorRuntime
        {
            int ConstructDurableObject(string className, IDurableObjectState ctx, ICloudflareEnv env);
            int ConstructWorkflow(string className, IExecutionContext ctx, ICloudflareEnv env);
            Task<string> CallDurableObject(int id, string method, string argsJson);
            Task<string> RunWorkflow(int id, string payloadJson, IWorkflowStep step);
        }

        public sealed partial class ActorRuntime : IActorRuntime
        {
            private readonly Dictionary<int, Actor> instances = new();
            private int next = 1;

            private int Track(object instance, ICloudflareEnv env)
            {
                var id = next++;
                instances[id] = new Actor(instance, env);
                return id;
            }

            private Actor GetActor(int id) =>
                instances.TryGetValue(id, out var actor)
                    ? actor
                    : throw new InvalidOperationException($"Actor {id} is gone.");

            private readonly record struct Actor(object Instance, ICloudflareEnv Env);

            private readonly struct EnvScope : IDisposable
            {
                public EnvScope(ICloudflareEnv env) { }
                public void Dispose() { }
            }

            private static JsonElement[] ReadArgs(string argsJson) => [];

            private static bool HasArg(JsonElement[] args, int index) => index < args.Length;

            private static JsonElement Arg(JsonElement[] args, int index, string method, string name) =>
                args[index];

            private static int ArgInt(JsonElement[] args, int index, string method, string name) =>
                Arg(args, index, method, name).GetInt32();

            private static long ArgLong(JsonElement[] args, int index, string method, string name) =>
                Arg(args, index, method, name).GetInt64();

            private static double ArgDouble(JsonElement[] args, int index, string method, string name) =>
                Arg(args, index, method, name).GetDouble();

            private static bool ArgBool(JsonElement[] args, int index, string method, string name) =>
                Arg(args, index, method, name).GetBoolean();

            private static string ArgString(JsonElement[] args, int index, string method, string name) =>
                Arg(args, index, method, name).GetString()!;

            private static string? ArgStringOrNull(JsonElement[] args, int index, string method, string name) =>
                Arg(args, index, method, name).GetString();

            private static string JsonInt(int value) => value.ToString(CultureInfo.InvariantCulture);

            private static string JsonLong(long value) => value.ToString(CultureInfo.InvariantCulture);

            private static string JsonDouble(double value) => value.ToString("R", CultureInfo.InvariantCulture);

            private static string JsonBool(bool value) => value ? "true" : "false";

            private static string JsonString(string? value) => value is null ? "null" : $"\"{value}\"";

            private static string JsonRpcInt(RpcInt? value) => value is null ? "null" : JsonInt(value.Value);
        }
        """;

    /// <summary>A worker that only serves requests: no cron trigger, no queue consumer.</summary>
    public const string FetchOnlyWorker = """
        namespace Sample.Edge;

        using System.Threading.Tasks;
        using Cloudflare.Backend;
        using Cloudflare.Workers;

        public sealed class Site : WorkerEntrypoint
        {
            public override Task<HttpResponseData> Fetch(IJsRequest request, ICloudflareEnv env) =>
                Task.FromResult(new HttpResponseData(200, request.Url));
        }
        """;

    /// <summary>The same worker with a cron trigger: overriding <c>Scheduled</c> is the opt-in.</summary>
    public const string ScheduledWorker = """
        namespace Sample.Edge;

        using System.Threading.Tasks;
        using Cloudflare.Backend;
        using Cloudflare.Workers;

        public sealed class Site : WorkerEntrypoint
        {
            public override Task<HttpResponseData> Fetch(IJsRequest request, ICloudflareEnv env) =>
                Task.FromResult(new HttpResponseData(200, request.Url));

            public override Task Scheduled(IScheduledController controller, ICloudflareEnv env) =>
                Task.CompletedTask;
        }
        """;

    /// <summary>
    /// A Durable Object outside <c>Cloudflare.Backend</c>: the emitted dispatch lives in that
    /// namespace, so anything but a fully qualified reference would not compile.
    /// </summary>
    public const string CounterActor = """
        namespace Sample.Actors;

        using System.Threading.Tasks;
        using Cloudflare.Workers;

        public sealed class Counter : DurableObject
        {
            private int total;

            public Counter(IDurableObjectState ctx, ICloudflareEnv env) : base(ctx, env) { }

            public int Total() => total;

            public Task<int> Add(int amount) => Task.FromResult(total += amount);
        }
        """;

    /// <summary>Every return and parameter shape the projection accepts, in one actor.</summary>
    public const string LedgerActor = """
        namespace Sample.Actors;

        using System.Threading.Tasks;
        using Cloudflare.Workers;

        public sealed class Ledger : DurableObject
        {
            public Ledger(IDurableObjectState ctx, ICloudflareEnv env) : base(ctx, env) { }

            public void Reset() { }
            public int Count() => 0;
            public long Stamp() => 0L;
            public double Average() => 0d;
            public bool Sealed() => true;
            public string Label() => "";
            public RpcInt Next() => new(0);
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
        using Cloudflare.Workers;

        public sealed record Snapshot(int Count);

        public sealed class Archive : DurableObject
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
        using Cloudflare.Workers;

        public sealed class Blobs : DurableObject
        {
            public Blobs(IDurableObjectState ctx, ICloudflareEnv env) : base(ctx, env) { }

            public Task Store(byte[] payload) => Task.CompletedTask;
        }
        """;

    /// <summary>An RPC method that would claim a JS prototype member workerd owns.</summary>
    public const string ReservedNameActor = """
        namespace Sample.Actors;

        using System.Threading.Tasks;
        using Cloudflare.Workers;

        public sealed class Gateway : DurableObject
        {
            public Gateway(IDurableObjectState ctx, ICloudflareEnv env) : base(ctx, env) { }

            public Task<string> Fetch() => Task.FromResult("");
        }
        """;

    /// <summary>A public worker member that fills no handler slot and has no RPC surface.</summary>
    public const string StrayMemberWorker = """
        namespace Sample.Edge;

        using System.Threading.Tasks;
        using Cloudflare.Backend;
        using Cloudflare.Workers;

        public sealed class Site : WorkerEntrypoint
        {
            public override Task<HttpResponseData> Fetch(IJsRequest request, ICloudflareEnv env) =>
                Task.FromResult(new HttpResponseData(200, request.Url));

            public Task Warmup() => Task.CompletedTask;
        }
        """;
}
