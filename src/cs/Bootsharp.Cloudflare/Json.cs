using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bootsharp.Cloudflare;

/// <summary>
/// Source-generated JSON for this package. Reflection-based serialization is off
/// (<c>JsonSerializerIsReflectionEnabledByDefault=false</c>), so every type that crosses a
/// JSON boundary is listed on <see cref="CloudflareJsonContext"/>.
/// </summary>
public static class Json
{
    /// <summary>
    /// A nullable string as a JSON value: a quoted literal, or <c>null</c>. The encoding is
    /// <see cref="JsonSerializer"/>'s, not a hand-rolled escape table.
    /// </summary>
    public static string Quote (string? value) =>
        JsonSerializer.Serialize(value, CloudflareJsonContext.Default.String);
}

/// <summary>
/// Closed-world metadata for the package's own JSON. No naming policy: callers that build a
/// <see cref="Dictionary{TKey,TValue}"/> of <see cref="JsonElement"/> already chose the keys.
/// </summary>
[JsonSourceGenerationOptions]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
internal sealed partial class CloudflareJsonContext : JsonSerializerContext;
