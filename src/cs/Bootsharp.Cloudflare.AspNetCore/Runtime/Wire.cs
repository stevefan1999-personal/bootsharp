using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Bootsharp.Cloudflare.AspNetCore;

/// <summary>
/// The response snapshot the worker entrypoint hands back to JavaScript.
/// </summary>
/// <remarks>
/// Shaped to the contract <c>js/runtime.mjs</c>'s <c>toResponse</c> already reads — <c>status</c>,
/// <c>headersJson</c>, <c>body</c> — so this layer changes what produces the snapshot, not how it
/// crosses the boundary. It stays a buffered triple until milestone 0b delivers Tier-1
/// handles: a live <c>ReadableStream</c> cannot be marshalled today, which is the same constraint
/// that makes <see cref="TypedResults.Stream"/> and server-sent events throw here.
/// </remarks>
/// <param name="Status">HTTP status code.</param>
/// <param name="HeadersJson">Response headers as a flat JSON object, matching what workerd's
/// <c>Headers</c> constructor accepts.</param>
/// <param name="Body">Response body, decoded as UTF-8 text.</param>
public sealed record HttpResponseData (int Status, string HeadersJson, string Body);

/// <summary>
/// Translates between the flat JSON header object that crosses the interop boundary and
/// <see cref="IHeaderDictionary"/>.
/// </summary>
/// <remarks>
/// The old shim built response headers by string concatenation and did not escape values
///so a header value containing a quote produced JSON the JS side could not
/// parse. Both directions go through <see cref="Utf8JsonWriter"/> / <see cref="JsonDocument"/>
/// here, which is reflection-free and therefore AOT-clean.
/// </remarks>
public static class HeaderJson
{
    /// <summary>Reads the request's <c>headersJson</c> into a header dictionary.</summary>
    /// <remarks>
    /// workerd's <c>Headers.forEach</c> has already comma-joined repeated headers, so each name
    /// arrives once. A malformed document is a bug on the JS side, not user input: it propagates
    /// rather than being swallowed into an empty header set.
    /// </remarks>
    public static IHeaderDictionary Parse (string? headersJson)
    {
        var headers = new HeaderDictionary();
        if (string.IsNullOrEmpty(headersJson)) return headers;
        using var document = JsonDocument.Parse(headersJson);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return headers;
        foreach (var property in document.RootElement.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.String)
                headers[property.Name] = new StringValues(property.Value.GetString());
        return headers;
    }

    /// <summary>Renders a header dictionary as the flat JSON object the JS side parses.</summary>
    /// <remarks>
    /// Multi-valued headers are comma-joined, which is correct for every header except
    /// <c>Set-Cookie</c>; a cookie layer will need the JS side to accept an array before it can be
    /// represented, so this deliberately does not invent an encoding for it.
    /// </remarks>
    public static string Render (IHeaderDictionary headers)
    {
        var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (name, values) in headers)
            {
                if (values.Count == 0) continue;
                writer.WriteString(name, values.Count == 1 ? values[0] : values.ToString());
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}

/// <summary>
/// Parses a URL query string into an <see cref="IQueryCollection"/>.
/// </summary>
/// <remarks>
/// Microsoft's <c>QueryHelpers.ParseQuery</c> lives in <c>Microsoft.AspNetCore.WebUtilities</c>,
/// which this package does not carry ( verdicts: the multipart reader it exists for is
/// a separate opt-in layer). Repeated keys accumulate into one <see cref="StringValues"/>, which is
/// what the vendored <c>IQueryCollection</c> contract promises and what array-typed handler
/// parameters bind from.
/// </remarks>
public static class QueryStringParser
{
    public static IQueryCollection Parse (string? queryString)
    {
        if (string.IsNullOrEmpty(queryString) || queryString == "?") return QueryCollection.Empty;
        var accumulated = new Dictionary<string, List<string?>>(StringComparer.OrdinalIgnoreCase);
        var span = queryString.AsSpan(queryString[0] == '?' ? 1 : 0);
        foreach (var pair in span.Split('&'))
        {
            var item = span[pair];
            if (item.IsEmpty) continue;
            var separator = item.IndexOf('=');
            var name = separator < 0 ? item : item[..separator];
            var value = separator < 0 ? default : item[(separator + 1)..];
            var key = Decode(name);
            if (!accumulated.TryGetValue(key, out var values))
                accumulated[key] = values = [];
            values.Add(Decode(value));
        }
        var parsed = new Dictionary<string, StringValues>(accumulated.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, values) in accumulated)
            parsed[key] = new StringValues(values.ToArray());
        return new QueryCollection(parsed);
    }

    // '+' means space in application/x-www-form-urlencoded, which is what a query string is;
    // Uri.UnescapeDataString does not know that, so the two steps are separate.
    private static string Decode (ReadOnlySpan<char> value)
    {
        if (value.IsEmpty) return string.Empty;
        var text = value.IndexOf('+') < 0 ? value.ToString() : value.ToString().Replace('+', ' ');
        return text.IndexOf('%') < 0 ? text : Uri.UnescapeDataString(text);
    }
}
