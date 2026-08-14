using System.Text.Json.Serialization;

namespace Cloudflare.Minimal;

public sealed record HealthView(bool Ok, string Runtime, string Environment);

public sealed record ValueView(string Key, string? Value);

public sealed record StoredView(string Key, bool Stored);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(HealthView))]
[JsonSerializable(typeof(ValueView))]
[JsonSerializable(typeof(StoredView))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;