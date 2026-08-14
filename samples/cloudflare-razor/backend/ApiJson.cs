using System.Text.Json.Serialization;
using Cloudflare.Razor.Notes;

namespace Cloudflare.Razor;

public sealed record Health (bool Ok, string Runtime, string Environment, string Page, int Notes);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Health))]
[JsonSerializable(typeof(Note))]
[JsonSerializable(typeof(NoteInput))]
[JsonSerializable(typeof(IReadOnlyList<Note>))]
internal partial class ApiJsonContext : JsonSerializerContext;
