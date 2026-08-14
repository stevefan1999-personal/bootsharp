using System.Text.Json.Serialization;
using Cloudflare.Data.Notes;

namespace Cloudflare.Data;

/// <summary><c>GET /api/health</c>.</summary>
public sealed record HealthView (bool Ok, string Runtime, string Environment, Guid IsolateId);

/// <summary>
/// <c>GET /api/diag/scope</c> — the DI graph reporting on itself.
/// </summary>
/// <param name="ScopeId">The scope probe the endpoint was handed.</param>
/// <param name="RepositoryScopeId">The scope probe the repository's constructor was handed.</param>
/// <param name="OneScopePerRequest">
/// True when those two are the same instance. If the container ever resolved a second scoped
/// instance for the same event — the classic symptom of a service accidentally resolved from the
/// root provider — this is the field that goes false.
/// </param>
/// <param name="ScopeSequence">1-based index of this scope within the isolate.</param>
/// <param name="IsolateId">Constant across every response from one isolate.</param>
/// <param name="ScopesDisposed">
/// Scopes whose <c>DisposeAsync</c> has already completed. On the Nth request of an isolate this
/// reads N-1, because the response is built before the event's own scope is disposed — which is
/// only true if <c>WebApplication.InvokeAsync</c>'s <c>await using</c> really runs every time.
/// </param>
public sealed record ScopeReport (
    Guid ScopeId,
    Guid RepositoryScopeId,
    bool OneScopePerRequest,
    int ScopeSequence,
    Guid IsolateId,
    int ScopesDisposed);

/// <summary>
/// The JSON metadata this app serializes through.
/// </summary>
/// <remarks>
/// Reflection-based serialization is off and <c>Bootsharp.Cloudflare.AspNetCore</c> has no
/// reflective resolver behind it, so every type that reaches a JSON result or a JSON
/// request body is listed here and Program.cs joins this context to the resolver chain. A type left
/// out is a <c>CFW029</c> warning while the project compiles and a failure while the endpoint table
/// is built — naming the type, rather than a 500 on the first request that needed it.
/// <see cref="IReadOnlyList{T}"/> is listed in its own right because that, not <c>Note</c>, is what
/// the list endpoints declare as their return type, and metadata is resolved by the declared type.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HealthView))]
[JsonSerializable(typeof(ScopeReport))]
[JsonSerializable(typeof(Note))]
[JsonSerializable(typeof(NoteInput))]
[JsonSerializable(typeof(IReadOnlyList<Note>))]
public sealed partial class ApiJsonContext : JsonSerializerContext;
