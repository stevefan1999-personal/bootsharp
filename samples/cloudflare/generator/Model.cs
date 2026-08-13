using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Cloudflare.Workers.Generator;

internal sealed record Entrypoint(
    string Kind,
    string Name,
    string Namespace,
    EquatableArray<Method> Methods,
    EquatableArray<Defect> Defects);

/// <param name="Handler">Handler slot the method fills: fetch/queue/scheduled/run, or rpc.</param>
/// <param name="Return">Encoded return shape: void/int/long/double/bool/string/rpcInt.</param>
/// <param name="Await">Whether the invocation is Task-returning and must be awaited.</param>
internal sealed record Method(
    string CsName,
    string JsName,
    string Handler,
    string Return,
    bool Await,
    EquatableArray<Parameter> Parameters);

/// <param name="Kind">Encoded parameter shape: int/long/double/bool/string/string?.</param>
/// <param name="Default">C# literal used when the argument is absent, or null when the argument is required.</param>
internal sealed record Parameter(string Name, string Kind, string? Default);

internal sealed record EnvProperty(string Name, string TypeName);

/// <summary>
/// A shape the emitter refuses to project.: every public entrypoint member either gets
/// correct dispatch or a diagnostic — silently emitting non-compiling code is not an option.
/// </summary>
internal sealed record Defect(string Id, string Title, string Message, LocationInfo? Location);

/// <summary>
/// Equatable stand-in for <see cref="Location"/>: the incremental pipeline compares models between
/// runs, and <see cref="Location"/> holds a reference to a whole syntax tree.
/// </summary>
internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
{
    public static LocationInfo? From(ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(static l => l.IsInSource);
        if (location?.SourceTree is null) return null;
        return new(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }

    public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);
}

internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>
    where T : IEquatable<T>
{
    public static EquatableArray<T> Empty { get; } = new([]);

    private readonly T[] _items;

    public EquatableArray(IEnumerable<T> items) => _items = items.ToArray();

    public int Length => _items.Length;
    public T[] Items => _items;
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    public bool Equals(EquatableArray<T> other) => _items.SequenceEqual(other._items);
    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);
    public override int GetHashCode()
    {
        var hash = 0;
        foreach (var item in _items) hash = (hash * 397) ^ item.GetHashCode();
        return hash;
    }
}
