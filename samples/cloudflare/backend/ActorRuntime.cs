using System.Globalization;
using System.Text.Json;

namespace Cloudflare.Backend;

/// <summary>
/// Guest-side registry used by generated JS classes (workers-rs wasm-bindgen equivalent).
/// Method switches are source-generated from <c>: DurableObject</c> / <c>: WorkflowEntrypoint</c>.
/// </summary>
public interface IActorRuntime
{
    int ConstructDurableObject(string className, IDurableObjectState ctx, ICloudflareEnv env);
    int ConstructWorkflow(string className, IExecutionContext ctx, ICloudflareEnv env);
    Task<string> CallDurableObject(int id, string method, string argsJson);
    Task<string> RunWorkflow(int id, string payloadJson, IWorkflowStep step);
}

/// <summary>
/// Hand-written half of the actor dispatch surface: the instance registry plus the argument and
/// result codecs the generated switches call. Codecs are hand-rolled because reflection-based
/// JSON is disabled for NativeAOT trimming, so the shapes here are exactly the shapes the
/// generator accepts — anything else is rejected with a CFW diagnostic instead of emitted.
/// </summary>
public sealed partial class ActorRuntime : IActorRuntime
{
    private readonly Dictionary<int, Actor> _instances = [];
    private int _next = 1;

    private int Track(object instance, ICloudflareEnv env)
    {
        var id = _next++;
        _instances[id] = new Actor(instance, env);
        return id;
    }

    private Actor GetActor(int id) =>
        _instances.TryGetValue(id, out var actor)
            ? actor
            : throw new InvalidOperationException($"Actor {id} is gone.");

    /// <summary>An actor together with the env it was constructed against.</summary>
    private readonly record struct Actor(object Instance, ICloudflareEnv Env);

    /// <summary>
    /// Binds <see cref="WorkerContext"/> for the duration of an actor call, exactly as the fetch
    /// path does, and restores the previous binding rather than clearing it — workflow step
    /// callbacks re-enter .NET while an outer actor call is still on the stack.
    /// </summary>
    private readonly struct EnvScope : IDisposable
    {
        private readonly ICloudflareEnv? _previous;

        public EnvScope(ICloudflareEnv env)
        {
            _previous = Captured();
            WorkerContext.Set(env);
        }

        public void Dispose()
        {
            if (_previous is null) WorkerContext.Clear();
            else WorkerContext.Set(_previous);
        }

        // WorkerContext exposes only a throwing accessor, so "unbound" is observable this way alone.
        private static ICloudflareEnv? Captured()
        {
            try { return WorkerContext.Env; }
            catch (InvalidOperationException) { return null; }
        }
    }

    private static JsonElement[] ReadArgs(string argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson)) return [];
        using var document = JsonDocument.Parse(argsJson);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("RPC arguments must be a JSON array.");
        var args = new JsonElement[root.GetArrayLength()];
        var index = 0;
        // Elements borrow the document buffer, which is released on dispose; clone them out.
        foreach (var element in root.EnumerateArray()) args[index++] = element.Clone();
        return args;
    }

    /// <summary>Whether a trailing optional parameter was supplied by the caller.</summary>
    private static bool HasArg(JsonElement[] args, int index) =>
        index < args.Length && args[index].ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);

    private static JsonElement Arg(JsonElement[] args, int index, string method, string name) =>
        index < args.Length && args[index].ValueKind != JsonValueKind.Undefined
            ? args[index]
            : throw new InvalidOperationException($"RPC '{method}' is missing argument '{name}'.");

    private static int ArgInt(JsonElement[] args, int index, string method, string name) =>
        Arg(args, index, method, name).GetInt32();

    private static long ArgLong(JsonElement[] args, int index, string method, string name) =>
        Arg(args, index, method, name).GetInt64();

    private static double ArgDouble(JsonElement[] args, int index, string method, string name) =>
        Arg(args, index, method, name).GetDouble();

    private static bool ArgBool(JsonElement[] args, int index, string method, string name) =>
        Arg(args, index, method, name).GetBoolean();

    private static string ArgString(JsonElement[] args, int index, string method, string name) =>
        Arg(args, index, method, name).GetString()
        ?? throw new InvalidOperationException($"RPC '{method}' argument '{name}' must not be null.");

    private static string? ArgStringOrNull(JsonElement[] args, int index, string method, string name)
    {
        var arg = Arg(args, index, method, name);
        return arg.ValueKind == JsonValueKind.Null ? null : arg.GetString();
    }

    private static string JsonInt(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string JsonLong(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>JSON has no NaN or infinity; both degrade to null rather than to invalid JSON.</summary>
    private static string JsonDouble(double value) =>
        double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "null";

    private static string JsonBool(bool value) => value ? "true" : "false";

    private static string JsonString(string? value) =>
        value is null ? "null" : $"\"{JsonEncodedText.Encode(value)}\"";

    private static string JsonRpcInt(RpcInt? value) => value is null ? "null" : JsonInt(value.Value);
}
