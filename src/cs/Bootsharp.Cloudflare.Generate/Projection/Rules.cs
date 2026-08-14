namespace Bootsharp.Cloudflare.Projection;

/// <summary>
/// The classification rules of and tier boundaries, expressed over plain
/// namespace/name strings so that the symbol-based front end (the Roslyn generator) and the
/// metadata-based one (the publish task) decide identically. Neither front end restates a rule.
/// </summary>
internal static class Rules
{
    /// <summary>Namespace of the runtime package whose base types and value shapes are matched.</summary>
    public const string Library = "Bootsharp.Cloudflare";

    /// <summary>Marks the app's env interface, wherever and under whatever name it declares it.</summary>
    public const string EnvAttribute = Library + ".WorkerEnvAttribute";

    /// <summary>Declares the app's static-asset prefixes and its assets binding name.</summary>
    public const string AssetsAttribute = Library + ".WorkerAssetsAttribute";

    /// <summary>Default of <see cref="AssetsAttribute"/>'s binding, restated for the metadata front
    /// end: an attribute's property default is applied by its constructor, which never runs here.</summary>
    public const string DefaultAssetsBinding = "ASSETS";

    /// <summary>Import the packaged runtime binds its log handler to, when the app declares it.</summary>
    public const string LogSink = Library + ".Logging.ILogSink";

    /// <summary>The concrete logger the app cannot use without importing <see cref="LogSink"/>.</summary>
    public const string LogProvider = Library + ".Logging.CloudflareJsonLoggerProvider";

    /// <summary>Bootsharp's assembly-level attribute listing the interfaces it imports from JS.</summary>
    public const string ImportAttribute = "Bootsharp.ImportAttribute";

    /// <summary>
    /// Every name <c>js/runtime.mjs</c> exports. Shared so the emitter can import the subset it
    /// calls and the generator can refuse an entrypoint whose projected wrapper would shadow one.
    /// </summary>
    public static readonly string[] RuntimeExports =
    [
        "configureRuntime", "enableWorkerTimers", "ensureBoot", "exclusive", "exemptHandle",
        "isAssetPath", "missing", "reentrant", "toResponse", "wrapDoId", "wrapID1Database",
        "wrapIKvNamespace", "wrapIQueue", "wrapIR2Bucket", "wrapIWorkflow", "wrapIdentity",
        "wrapRequest", "wrapScheduledController", "wrapState", "wrapStep"
    ];

    /// <summary>
    /// Whether the packaged runtime adapts a binding of this type. Matched on the full identity
    /// rather than the bare type name, so an app interface that merely happens to be called
    /// <c>IQueue</c> is passed through instead of being handed the Cloudflare adapter.
    /// </summary>
    public static bool Adapted (string space, string name) =>
        space == Library && name is
            "IKvNamespace" or "ID1Database" or "IR2Bucket" or "IQueue" or "IWorkflow";

    /// <summary>Name of the adapter wrapping a binding of the given interface type.</summary>
    public static string Adapter (string typeName) => "wrap" + typeName;

    /// <summary>
    /// Name of the class the actor dispatch is emitted into. The app declares the other half of it,
    /// deriving from <see cref="ActorRuntimeBaseType"/>: a partial class cannot span the package
    /// boundary the instance registry and the JSON codecs sit behind, so the seam between the two
    /// halves is inheritance rather than the partial itself.
    /// </summary>
    public const string ActorRuntimeType = "ActorRuntime";

    /// <summary>Packaged base of <see cref="ActorRuntimeType"/>, generic over the app's env.</summary>
    public const string ActorRuntimeBaseType = "ActorRuntimeBase";

    /// <summary>
    /// Whether a base type is <see cref="ActorRuntimeBaseType"/>, matched on the unbound name — the
    /// base is generic over the app's env, and a symbol's name carries no arity.
    /// </summary>
    public static bool IsActorRuntimeBase (string space, string name) =>
        space == Library && name == ActorRuntimeBaseType;

    /// <summary>The declaration an actor obliges the app to write, spelled as it would write it.</summary>
    public static string ActorRuntimeDeclaration (string envName) =>
        $"public sealed partial class {ActorRuntimeType} : {Library}.{ActorRuntimeBaseType}<{envName}>";

    /// <summary>Interface type an app declares for the namespace binding of a Durable Object.</summary>
    public static string NamespaceType (string durableName) => $"I{durableName}Namespace";

    /// <summary>Interface type an app declares for a stub of a Durable Object.</summary>
    public static string StubType (string durableName) => $"I{durableName}Stub";

    /// <summary>
    /// Module-scope names the emitted module declares or imports for itself, beyond
    /// <see cref="RuntimeExports"/>. A projected declaration taking one of these shadows it, which
    /// JavaScript reports as a redeclaration at bundle time rather than at compile time — so the
    /// generator refuses the C# name instead.
    /// </summary>
    public static readonly string[] ModuleScope =
    [
        "assetPaths", "wasmModule", "wrapEnv", "wrappedEnvs",
        "DurableObject", "WorkerEntrypoint", "WorkflowEntrypoint",
        // Imported from the SignalR package asset when a Durable Object hosts a hub.
        "hubDurableObject", "routeHub"
    ];

    /// <summary>
    /// JS prototype members workerd owns on an entrypoint class: RPC dispatch rejects them
    /// (worker-rpc.c++) or the validator claims them as handlers.
    /// </summary>
    public static readonly string[] Reserved =
    [
        "fetch", "connect", "alarm", "webSocketMessage",
        "webSocketClose", "webSocketError", "dup", "constructor"
    ];

    /// <summary>
    /// Entrypoint kind a base type declares, or null when it is not one of the runtime's bases.
    /// The bases are generic over the app's env, so the match is on the unbound name and the
    /// caller is responsible for stripping metadata arity.
    /// </summary>
    public static string? Kind (string space, string name) =>
        space != Library ? null : name switch {
            "WorkerEntrypoint" => "Worker",
            "DurableObject" => "DurableObject",
            "WorkflowEntrypoint" => "Workflow",
            _ => null
        };

    /// <summary>Handler slot a method name fills on the given kind, or null when it is not one.</summary>
    public static string? HandlerSlot (string kind, string name) => kind switch {
        "Worker" => name switch {
            "Fetch" => "fetch",
            "Queue" => "queue",
            "Scheduled" => "scheduled",
            _ => null
        },
        "Workflow" => name == "Run" ? "run" : null,
        _ => null
    };

    public static string HandlerNames (string kind) =>
        kind == "Worker" ? "Fetch, Queue, Scheduled" : "Run";

    public static string ToJs (string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    /// <summary>Encoded RPC return shape, or null when the value cannot cross the JSON boundary.</summary>
    public static string? ValueKind (string space, string name) => (space, name) switch {
        ("System", "Int32") => "int",
        ("System", "Int64") => "long",
        ("System", "Double") => "double",
        ("System", "Boolean") => "bool",
        ("System", "String") => "string",
        _ => null
    };

    /// <summary>
    /// Encoded RPC parameter shape, or null when the argument cannot arrive in the JSON array.
    /// Nullability only separates the two string readers of the generated C# dispatch, so a front
    /// end that has no annotation to read passes false and the JS projection is unaffected.
    /// </summary>
    public static string? ParameterKind (string space, string name, bool nullable) => (space, name) switch {
        ("System", "Int32") => "int",
        ("System", "Int64") => "long",
        ("System", "Double") => "double",
        ("System", "Boolean") => "bool",
        ("System", "String") => nullable ? "string?" : "string",
        _ => null
    };

    /// <summary>Strips the metadata arity suffix (<c>`1</c>) reflection appends to a generic name.</summary>
    public static string Unbound (string name)
    {
        var tick = name.IndexOf('`');
        return tick < 0 ? name : name.Substring(0, tick);
    }

    /// <summary>
    /// Durable Object base the SignalR package declares for a hub. It is named here
    /// rather than in that package's own rules because both front ends have to recognise it: the
    /// generator emits the C# dispatch for a hub-hosting actor, the publish task emits the ESM half,
    /// and a rule only one of them knows is a module whose two halves disagree.
    /// </summary>
    public const string HubDurableObject = "Bootsharp.Cloudflare.SignalR.HubDurableObject";

    /// <summary>
    /// The transport surface <c>HubDurableObject&lt;THub, TEnv&gt;</c> declares, as both projections
    /// see it. Both front ends read <i>declared</i> members only, and these are inherited — so
    /// without injecting them a hub-hosting Durable Object would project no RPC at all and its
    /// JavaScript half would call four methods that were never emitted.
    /// </summary>
    /// <remarks>
    /// Each is an ordinary RPC shape (string arguments, void/int result), which is exactly why the
    /// hosting glue needs no new emission machinery: the JavaScript in
    /// <c>Bootsharp.Cloudflare.SignalR/js/signalr.mjs</c> subclasses the generated Durable Object
    /// and forwards workerd's reserved hibernation handlers to these.
    /// </remarks>
    public static readonly HubTransport[] HubTransports =
    [
        new("Accept", "accept", "void", true, [("connectionId", "string")]),
        new("Deliver", "deliver", "void", true, [("connectionId", "string"), ("message", "string")]),
        new("Disconnect", "disconnect", "void", true, [("connectionId", "string"), ("reason", "string?")]),
        new("Sweep", "sweep", "int", true, [])
    ];

    /// <summary>
    /// Whether a class inherits <see cref="HubDurableObject"/>, given a walk of its base names. The
    /// walk itself differs per front end (symbols carry no arity, metadata spells it), so each
    /// passes the sequence it can produce and the decision stays in one place.
    /// </summary>
    public static bool HostsHub (IEnumerable<string> baseNames) =>
        baseNames.Any(name => name == HubDurableObject);

    /// <summary>
    /// Marks a hub-hosting Durable Object as routed by the generated worker: negotiate and the
    /// WebSocket upgrade are answered in the emitted fetch handler, so the app writes no JavaScript.
    /// </summary>
    public const string HubRouteAttribute = "Bootsharp.Cloudflare.SignalR.HubRouteAttribute";

    /// <summary>
    /// Export name of the hibernation wrapper. wrangler <c>class_name</c> names this, not the
    /// generated actor class: workerd reserves the hibernation handlers, so they live on a
    /// subclass the package ships.
    /// </summary>
    public static string HubExportName (string durableName) => durableName + "Hub";

    /// <summary>
    /// A hub route as the emitted fetch handler matches it: a leading and trailing slash, so
    /// <c>/chat</c>, <c>chat</c> and <c>/chat/</c> are one prefix.
    /// </summary>
    public static string? NormalizeHubRoute (string? prefix)
    {
        if (prefix is null || prefix.Length == 0) return null;
        var route = prefix[0] == '/' ? prefix : "/" + prefix;
        return route[route.Length - 1] == '/' ? route : route + "/";
    }
}

/// <param name="CsName">Name on <c>HubDurableObject</c>.</param>
/// <param name="JsName">Name the emitted Durable Object exposes.</param>
/// <param name="Return">Encoded return shape, as the projection spells it.</param>
/// <param name="Await">Whether it is Task-returning.</param>
/// <param name="Parameters">Name and encoded shape of each argument.</param>
internal sealed record HubTransport(
    string CsName,
    string JsName,
    string Return,
    bool Await,
    (string Name, string Kind)[] Parameters);
