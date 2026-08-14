using Bootsharp.Cloudflare.Projection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Bootsharp.Cloudflare.Generate;

/// <summary>
/// What the generator resolved from one entrypoint class: the projection the ESM module is built
/// from, the extra argument detail only the generated C# dispatch consumes, and anything refused.
/// The last two never leave this assembly — diagnostics need source locations, and the dispatch is
/// C# the compiler is about to see, so both are source-time concerns.
/// </summary>
internal sealed record Resolution(
    Entrypoint Entrypoint,
    EquatableArray<Dispatch> Dispatches,
    EquatableArray<Defect> Defects,
    LocationInfo? Location);

/// <summary>
/// The C# half of a projected RPC method: the arguments the emitted switch reads out of the JSON
/// array. The ESM projection forwards those arguments untouched, which is why they are not part of
/// the shared model.
/// </summary>
internal sealed record Dispatch(Method Method, EquatableArray<Parameter> Parameters);

/// <param name="Kind">Encoded parameter shape: int/long/double/bool/string/string?.</param>
/// <param name="Default">C# literal used when the argument is absent, or null when the argument is required.</param>
internal sealed record Parameter(string Name, string Kind, string? Default);

/// <summary>
/// An interface the app marked with <c>[WorkerEnv]</c>: its fully qualified name, which the emitted
/// dispatch closes the generic runtime base over, and the bindings it declares.
/// </summary>
/// <param name="FullName">Globally qualified name, or empty when the app marked none.</param>
/// <param name="Namespace">Namespace the emitted dispatch is placed in, so that the app's own
/// half of the partial can be declared next to the bindings it is generated from.</param>
internal sealed record Env(
    string FullName,
    string Namespace,
    EquatableArray<Binding> Bindings,
    LocationInfo? Location)
{
    public static Env Empty { get; } = new("", "", EquatableArray<Binding>.Empty, null);
}

/// <summary>
/// One app-side declaration of the partial the generated dispatch lands in: where it was declared,
/// and the env it closed the packaged runtime base over. Both have to match the env the dispatch is
/// emitted against — a half in another namespace, or over another env, is a different type, and the
/// generated half then calls a registry and codecs that are not in scope.
/// </summary>
internal sealed record ActorRuntimeHalf(
    string Namespace,
    string EnvFullName,
    LocationInfo? Location);

/// <summary>
/// One property of the env interface, with what the adapter check needs on top of the shared
/// <see cref="EnvProperty"/>: whether the binding is a handle at all, and where to report it.
/// </summary>
internal sealed record Binding(
    string Name,
    string TypeNamespace,
    string TypeName,
    bool Handle,
    LocationInfo? Location);

/// <summary>
/// A shape the emitter refuses to project.: every public entrypoint member either gets
/// correct dispatch or a diagnostic — silently emitting non-compiling code is not an option.
/// </summary>
/// <param name="Severity">Error for anything that cannot work, which is nearly everything here;
/// a warning only where the projection has a defensible fallback and the point is to stop it
/// being silent.</param>
internal sealed record Defect(
    string Id,
    string Title,
    string Message,
    LocationInfo? Location,
    DiagnosticSeverity Severity = DiagnosticSeverity.Error);

/// <summary>
/// Equatable stand-in for <see cref="Location"/>: the incremental pipeline compares models between
/// runs, and <see cref="Location"/> holds a reference to a whole syntax tree.
/// </summary>
internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public static LocationInfo? From (ISymbol symbol) =>
        From(symbol.Locations.FirstOrDefault(static l => l.IsInSource));

    public static LocationInfo? From (SyntaxNode node) => From(node.GetLocation());

    private static LocationInfo? From (Location? location)
    {
        if (location?.SourceTree is null) return null;
        return new(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }

    public Location ToLocation () => Location.Create(FilePath, Span, LineSpan);
}
