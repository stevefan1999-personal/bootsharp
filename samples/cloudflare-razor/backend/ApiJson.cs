using System.Text.Json.Serialization;

namespace Cloudflare.Razor;

[JsonSerializable(typeof(Health))]
internal partial class ApiJsonContext : JsonSerializerContext;
