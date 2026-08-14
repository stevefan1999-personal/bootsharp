using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Bootsharp.Cloudflare.AspNetCore;

/// <summary>
/// The response snapshot the worker entrypoint hands back to JavaScript.
/// </summary>
/// <remarks>
/// <para>
/// The contract <c>js/runtime.mjs</c>'s <c>toResponse</c> reads. It is a snapshot rather than a live
/// <c>Response</c>: a workerd <c>ReadableStream</c> cannot be held across the boundary, which is the
/// same constraint that makes <see cref="TypedResults.Stream"/> and server-sent events throw.
/// Within that, nothing about it is lossy any more — headers carry repeated values, the body carries
/// bytes, and "not mine" is a field rather than an impossible status code.
/// </para>
/// </remarks>
/// <param name="Status">HTTP status code.</param>
/// <param name="HeadersJson">Response headers as a JSON object whose values are either a string or
/// an array of strings; an array is appended header by header, which is how repeated
/// <c>Set-Cookie</c> survives (workerd's <c>Headers</c> constructor cannot take repeats).</param>
/// <param name="Body">Response body as UTF-8 text. Ignored when <paramref name="BodyBytes"/> is
/// present, and the convenience for hand-written entrypoints that answer with text.</param>
/// <param name="BodyBytes">Response body as bytes. When present this is the body: it is what the
/// layer's own snapshot always carries, so a response is never decoded and re-encoded on the way
/// out and binary content is byte-exact.</param>
/// <param name="PassThroughToAssets">Whether the worker declines the request, handing it to the
/// assets binding. Replaces the status-zero sentinel, which was indistinguishable from a bug and
/// unrepresentable in a <c>Response</c>.</param>
public sealed record HttpResponseData (
    int Status,
    string HeadersJson,
    string Body,
    byte[]? BodyBytes = null,
    bool PassThroughToAssets = false);

/// <summary>
/// Translates between the JSON header object that crosses the interop boundary and
/// <see cref="IHeaderDictionary"/>.
/// </summary>
/// <remarks>
/// The old shim built response headers by string concatenation and did not escape values
///so a header value containing a quote produced JSON the JS side could not
/// parse. Writes go through <see cref="HeaderJsonContext"/>; reads through
/// <see cref="JsonDocument"/>, which is a parser rather than a constructor.
/// </remarks>
public static class HeaderJson
{
    /// <summary>Reads a request's <c>headersJson</c> into a header dictionary.</summary>
    /// <remarks>
    /// workerd's <c>Headers.forEach</c> comma-joins repeated headers, so a name normally arrives
    /// once with one string; an array is accepted for the names where that join is not reversible
    /// (<c>Set-Cookie</c>, which workerd exposes through <c>getSetCookie()</c>). A malformed document
    /// is a bug on the JS side, not user input: it propagates rather than being swallowed into an
    /// empty header set.
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
            else if (property.Value.ValueKind == JsonValueKind.Array)
                headers[property.Name] = new StringValues(Strings(property.Value));
        return headers;
    }

    /// <summary>Renders a header dictionary as the JSON object the JS side turns into
    /// <c>Headers</c>.</summary>
    /// <remarks>
    /// A single-valued header renders as a string and a multi-valued one as an array. Comma-joining
    /// the second case — what this did while the wire shape was flat — is equivalent for every
    /// header defined as a comma-separated list and wrong for the one that is not: two
    /// <c>Set-Cookie</c> headers joined by a comma set one malformed cookie, which is why
    /// <see cref="HttpResponse.Cookies"/> refused to exist until this could be represented.
    /// </remarks>
    public static string Render (IHeaderDictionary headers)
    {
        var json = HeaderJsonContext.Default;
        var map = new Dictionary<string, JsonElement>();
        foreach (var (name, values) in headers)
        {
            if (values.Count == 0) continue;
            if (values.Count == 1)
                map[name] = JsonSerializer.SerializeToElement(values[0], json.String);
            else
                map[name] = JsonSerializer.SerializeToElement(Present(values), json.StringArray);
        }
        return JsonSerializer.Serialize(map, json.DictionaryStringJsonElement);
    }

    private static string[] Present (StringValues values)
    {
        var present = new List<string>(values.Count);
        foreach (var value in values)
            if (value is not null)
                present.Add(value);
        return present.ToArray();
    }

    private static string?[] Strings (JsonElement array)
    {
        var values = new List<string?>(array.GetArrayLength());
        foreach (var element in array.EnumerateArray())
            if (element.ValueKind == JsonValueKind.String)
                values.Add(element.GetString());
        return values.ToArray();
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
