using System.Text;

namespace Cloudflare.Backend;

/// <summary>
/// JSON text helpers. Every payload here is assembled by hand — reflection-based serialization is
/// off (<c>JsonSerializerIsReflectionEnabledByDefault=false</c>) — so the escaping rules live in
/// one place instead of being re-derived per call site.
/// </summary>
internal static class Json
{
    /// <summary>
    /// Escapes a value for embedding in a JSON string literal. Control characters are escaped along
    /// with the structural ones: a tab or carriage return arriving from a database row or a log
    /// message would otherwise produce a document the JS side cannot parse.
    /// </summary>
    public static string Escape(string value)
    {
        if (!NeedsEscaping(value)) return value;
        var escaped = new StringBuilder(value.Length + 16);
        foreach (var character in value)
            switch (character)
            {
                case '"': escaped.Append("\\\""); break;
                case '\\': escaped.Append("\\\\"); break;
                case '\n': escaped.Append("\\n"); break;
                case '\r': escaped.Append("\\r"); break;
                case '\t': escaped.Append("\\t"); break;
                default:
                    if (character < ' ') escaped.Append("\\u").Append(((int)character).ToString("x4"));
                    else escaped.Append(character);
                    break;
            }
        return escaped.ToString();
    }

    /// <summary>Renders a nullable string as a JSON value: a quoted literal, or <c>null</c>.</summary>
    public static string Quote(string? value) => value is null ? "null" : "\"" + Escape(value) + "\"";

    private static bool NeedsEscaping(string value)
    {
        foreach (var character in value)
            if (character is '"' or '\\' || character < ' ')
                return true;
        return false;
    }
}
