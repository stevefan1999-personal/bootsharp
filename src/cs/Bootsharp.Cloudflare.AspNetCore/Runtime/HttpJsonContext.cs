using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bootsharp.Cloudflare.AspNetCore;

/// <summary>
/// Closed-world JSON for the HTTP layer: RFC 7807 problem documents and the header map that
/// crosses the interop boundary. Two contexts because headers must keep their names verbatim
/// (camelCase would rename <c>Content-Type</c>) while problem documents follow the web defaults.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProblemDocument))]
internal sealed partial class ProblemJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
internal sealed partial class HeaderJsonContext : JsonSerializerContext;

/// <summary>RFC 7807 members this host writes. Extensions are not a source-generated shape.</summary>
internal sealed record ProblemDocument (
    string? Type,
    string Title,
    int Status,
    string? Detail,
    string? Instance,
    Dictionary<string, string[]>? Errors);