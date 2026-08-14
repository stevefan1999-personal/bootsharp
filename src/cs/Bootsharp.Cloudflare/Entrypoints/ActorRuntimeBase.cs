using System.Globalization;
using System.Text.Json;

namespace Bootsharp.Cloudflare;

/// <summary>
/// Hand-written half of the actor dispatch surface: the instance registry plus the argument and
/// result codecs the generated switches call. Codecs are hand-rolled because reflection-based
/// JSON is disabled for NativeAOT trimming, so the shapes here are exactly the shapes the
/// generator accepts — anything else is rejected with a CFW diagnostic instead of emitted.
/// </summary>
/// <remarks>
/// The generated half is a partial of the app's own <c>ActorRuntime</c>, which derives from this
/// type and implements the app's exported runtime interface: a partial class cannot span two
/// assemblies, so the split is base/derived rather than partial/partial. Every member the emitter
/// calls is therefore <c>protected</c> — that is the contract with
/// <c>Bootsharp.Cloudflare.Generate.CsEmitter</c>, not an implementation detail.
/// </remarks>
public abstract class ActorRuntimeBase<TEnv> where TEnv : class
{
    private readonly Dictionary<int, Actor> _instances = [];
    private int _next = 1;

    protected int Track(object instance, TEnv env)
    {
        var id = _next++;
        _instances[id] = new Actor(instance, env);
        return id;
    }

    protected Actor GetActor(int id) =>
        _instances.TryGetValue(id, out var actor)
            ? actor
            : throw new InvalidOperationException($"Actor {id} is gone.");

    /// <summary>An actor together with the env it was constructed against.</summary>
    protected readonly record struct Actor(object Instance, TEnv Env);

    /// <summary>
    /// Binds <see cref="WorkerContext{TEnv}"/> for the duration of an actor call, exactly as the
    /// fetch path does, and restores the previous binding rather than clearing it — workflow step
    /// callbacks re-enter .NET while an outer actor call is still on the stack.
    /// </summary>
    protected readonly struct EnvScope : IDisposable
    {
        private readonly TEnv? _previous;

        public EnvScope(TEnv env)
        {
            _previous = Captured();
            WorkerContext<TEnv>.Set(env);
        }

        public void Dispose()
        {
            if (_previous is null) WorkerContext<TEnv>.Clear();
            else WorkerContext<TEnv>.Set(_previous);
        }

        // WorkerContext exposes only a throwing accessor, so "unbound" is observable this way alone.
        private static TEnv? Captured()
        {
            try { return WorkerContext<TEnv>.Env; }
            catch (InvalidOperationException) { return null; }
        }
    }

    protected static JsonElement[] ReadArgs(string argsJson)
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
    protected static bool HasArg(JsonElement[] args, int index) =>
        index < args.Length && args[index].ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null);

    protected static JsonElement Arg(JsonElement[] args, int index, string method, string name) =>
        index < args.Length && args[index].ValueKind != JsonValueKind.Undefined
            ? args[index]
            : throw new InvalidOperationException($"RPC '{method}' is missing argument '{name}'.");

    protected static int ArgInt(JsonElement[] args, int index, string method, string name) =>
        Arg(args, index, method, name).GetInt32();

    protected static long ArgLong(JsonElement[] args, int index, string method, string name) =>
        Arg(args, index, method, name).GetInt64();

    protected static double ArgDouble(JsonElement[] args, int index, string method, string name) =>
        Arg(args, index, method, name).GetDouble();

    protected static bool ArgBool(JsonElement[] args, int index, string method, string name) =>
        Arg(args, index, method, name).GetBoolean();

    protected static string ArgString(JsonElement[] args, int index, string method, string name) =>
        Arg(args, index, method, name).GetString()
        ?? throw new InvalidOperationException($"RPC '{method}' argument '{name}' must not be null.");

    protected static string? ArgStringOrNull(JsonElement[] args, int index, string method, string name)
    {
        var arg = Arg(args, index, method, name);
        return arg.ValueKind == JsonValueKind.Null ? null : arg.GetString();
    }

    protected static string JsonInt(int value) => value.ToString(CultureInfo.InvariantCulture);

    protected static string JsonLong(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>JSON has no NaN or infinity; both degrade to null rather than to invalid JSON.</summary>
    protected static string JsonDouble(double value) =>
        double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "null";

    protected static string JsonBool(bool value) => value ? "true" : "false";

    protected static string JsonString(string? value) =>
        value is null ? "null" : $"\"{JsonEncodedText.Encode(value)}\"";
}
