using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cloudflare.Backend;

/// <summary><c>GET /api/health</c>.</summary>
public sealed record HealthView(bool Ok, string Runtime, string Framework, string WorkersTypes, string Environment);

/// <summary><c>GET /api/kv</c> and <c>GET /api/r2</c>: one stored value, or null when absent.</summary>
public sealed record ValueView(string Key, string? Value);

/// <summary><c>GET /api/do</c>: the Durable Object counter.</summary>
public sealed record CounterView(string Name, int Value);

/// <summary>A row of the D1 <c>notes</c> table.</summary>
/// <remarks>
/// <c>created_at</c> keeps its SQL name through <see cref="JsonPropertyNameAttribute"/> rather than
/// becoming <c>createdAt</c>: <see cref="Microsoft.AspNetCore.Http.Json.JsonOptions"/> applies
/// ASP.NET Core's camelCase web defaults, and this response shape predates the conversion.
/// </remarks>
public sealed record NoteView(int Id, string Body, [property: JsonPropertyName("created_at")] string CreatedAt);

/// <summary><c>GET /api/freesql</c>: the same rows read through the ORM.</summary>
public sealed record FreeSqlView(string Orm, IReadOnlyList<NoteView> Notes);

/// <summary><c>POST /api/notes</c> request body.</summary>
public sealed record NoteInput(string Body);

/// <summary><c>GET /api/echo</c>: what the query string bound to.</summary>
public sealed record EchoView(string Text, int Times, string Result);

/// <summary>What the cookie route reports back: what arrived, so a round trip is observable.</summary>
public sealed record CookiesView(string? Session, int Count);

/// <summary><c>GET /api/d1-grid</c>: D1's positional grid plus the columns the statement named.</summary>
public sealed record GridView(string[] Columns, JsonElement Rows, long RowsRead);

/// <summary><c>GET /api/do-sql</c>: rows from the Durable Object SQLite transport.</summary>
public sealed record DoSqlView(string Orm, JsonElement Rows);

/// <summary><c>GET /api/scheduled</c>: the last cron heartbeat, or JSON null when none has run.</summary>
public sealed record ScheduledView(JsonElement LastScheduled);

/// <summary>The KV value the cron handler writes so a scheduled run is observable.</summary>
public sealed record ScheduledHeartbeat(string Cron, long ScheduledTime, DateTimeOffset ScheduledAt);

/// <summary>Workflow <c>params</c> payload.</summary>
public sealed record WorkflowParams(string UserId);

/// <summary>One R2 object as the home page lists it.</summary>
public sealed record R2ListItem(string Key, long Size);

/// <summary>
/// The JSON metadata this app serializes through.
/// </summary>
/// <remarks>
/// <para>
/// Reflection-based serialization is off (<c>JsonSerializerIsReflectionEnabledByDefault=false</c>),
/// and <c>Bootsharp.Cloudflare.AspNetCore</c> has no reflective resolver to fall back to — its
/// <see cref="Microsoft.AspNetCore.Http.Json.JsonOptions"/> starts with an empty resolver chain by
/// design. So every type that reaches a JSON result or a JSON request body is listed
/// here, and Program.cs joins this context to the chain. A type left out fails when the app builds
/// its endpoint table, naming the type — not with a 500 on the first request that needs it.
/// </para>
/// <para>
/// Separate from the context Bootsharp emits for the interop boundary: that one describes what
/// crosses into JavaScript, this one what crosses to the HTTP client.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HealthView))]
[JsonSerializable(typeof(ValueView))]
[JsonSerializable(typeof(CounterView))]
[JsonSerializable(typeof(NoteView))]
[JsonSerializable(typeof(FreeSqlView))]
[JsonSerializable(typeof(NoteInput))]
[JsonSerializable(typeof(EchoView))]
[JsonSerializable(typeof(CookiesView))]
[JsonSerializable(typeof(GridView))]
[JsonSerializable(typeof(DoSqlView))]
[JsonSerializable(typeof(ScheduledView))]
[JsonSerializable(typeof(ScheduledHeartbeat))]
[JsonSerializable(typeof(WorkflowParams))]
[JsonSerializable(typeof(R2ListItem))]
[JsonSerializable(typeof(R2ListItem[]))]
[JsonSerializable(typeof(JsonElement))]
public sealed partial class ApiJsonContext : JsonSerializerContext;

/// <summary>
/// Decodes the row D1 returns as JSON text into <see cref="NoteView"/>.
/// </summary>
/// <remarks>
/// A D1 row crosses the interop boundary as JSON text, because the TypeScript row type is the
/// caller's to name. Reading it through <see cref="ApiJsonContext"/> keeps the decode on the
/// source-generated path — the same metadata the response is written with. <c>first()</c> answers
/// with the JSON literal <c>null</c> when the query matched nothing, which deserializes to null.
/// </remarks>
internal static class NoteRows
{
    public static NoteView? Read(string? rowJson) =>
        string.IsNullOrEmpty(rowJson) ? null : JsonSerializer.Deserialize(rowJson, ApiJsonContext.Default.NoteView);
}

/// <summary>Parses JSON text the bindings already produced into a <see cref="JsonElement"/>.</summary>
internal static class JsonPayload
{
    public static JsonElement Parse (string? json) =>
        JsonSerializer.Deserialize(json ?? "null", ApiJsonContext.Default.JsonElement);
}
