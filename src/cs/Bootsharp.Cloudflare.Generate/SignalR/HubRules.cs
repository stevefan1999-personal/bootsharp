using System.Collections.Generic;
using Bootsharp.Cloudflare.Projection;

namespace Bootsharp.Cloudflare.Generate.SignalR;

/// <summary>
/// The classification rules of, kept apart from the entrypoint rules for the same
/// reason the two generators share one analyzer assembly: subsystem knowledge in one place, so a
/// name the emitter writes and a name the diagnostics quote cannot drift.
/// </summary>
internal static class HubRules
{
    public const string Library = "Bootsharp.Cloudflare.SignalR";

    /// <summary>The base every user hub derives from, reimplemented API-identically.</summary>
    public const string HubType = "Microsoft.AspNetCore.SignalR.Hub";

    /// <summary>Renames a hub method on the wire.</summary>
    public const string MethodNameAttribute = "Microsoft.AspNetCore.SignalR.HubMethodNameAttribute";

    /// <summary>Declares a type on a source-generated JSON context.</summary>
    public const string SerializableAttribute = "System.Text.Json.Serialization.JsonSerializableAttribute";

    /// <summary>The generated dispatch's base class.</summary>
    public const string DispatcherType = Library + ".HubDispatcher";

    /// <summary>The Durable Object base whose four transport methods the entrypoint generator projects.</summary>
    /// <remarks>Delegated to the shared rules: the publish task decides this too, from metadata.</remarks>
    public const string DurableObjectType = Rules.HubDurableObject;

    /// <summary>Name of the dispatch class emitted for a hub.</summary>
    public static string DispatcherName (string hubName) => hubName + "Dispatcher";

    /// <summary>
    /// The transport surface <c>HubDurableObject&lt;THub, TEnv&gt;</c> declares, as the entrypoint
    /// projection sees it. Lives in the shared rules because the publish task injects the same four
    /// methods into the ESM half; see <see cref="Rules.HubTransports"/>.
    /// </summary>
    public static HubTransport[] Transport => Rules.HubTransports;

    /// <summary>
    /// Payload types <c>HubPrimitivesContext</c> already ships, so an app is never asked to declare
    /// a serializer context for a hub that only passes primitives. Change this together with the
    /// attribute list on that class.
    /// </summary>
    /// <remarks>
    /// Spelled as <c>HubGenerator.PayloadName</c> spells them, which is the keyword rather than the
    /// metadata name: <c>SymbolDisplayFormat.FullyQualifiedFormat</c> carries <c>UseSpecialTypes</c>,
    /// so <c>System.String</c> renders as <c>string</c>. Both sides of the comparison — the payload
    /// and the app's <c>[JsonSerializable]</c> declarations — go through that one formatter, so
    /// matching its output is what makes the set mean anything.
    /// </remarks>
    public static readonly HashSet<string> Primitives = new(StringComparer.Ordinal)
    {
        "string", "bool", "int", "long", "double", "float", "decimal",
        "System.Guid", "System.DateTime", "System.DateTimeOffset",
        "string[]", "int[]", "double[]"
    };
}
