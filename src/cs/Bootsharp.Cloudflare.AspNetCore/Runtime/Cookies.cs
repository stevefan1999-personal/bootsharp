using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Bootsharp.Cloudflare.AspNetCore;

/// <summary>
/// The request's <c>Cookie</c> header, parsed.
/// </summary>
/// <remarks>
/// Parsing goes through <see cref="CookieHeaderValue"/> from the referenced
/// <c>Microsoft.Net.Http.Headers</c> — packable, so identity rule points at the real
/// package rather than at a vendored look-alike, and it is the same parser ASP.NET Core uses.
/// Values are unescaped, mirroring the escaping <see cref="ResponseCookies"/> applies on the way
/// out: a cookie whose value contains a semicolon must survive the round trip.
/// </remarks>
internal sealed class RequestCookies (Dictionary<string, string> cookies) : IRequestCookieCollection
{
    internal static readonly RequestCookies Empty = new([]);

    /// <summary>Reads the cookies out of a request's headers.</summary>
    public static IRequestCookieCollection Parse (IHeaderDictionary headers)
    {
        var values = headers.Cookie;
        if (StringValues.IsNullOrEmpty(values)) return Empty;
        if (!CookieHeaderValue.TryParseList(values, out var parsed) || parsed.Count == 0) return Empty;
        var cookies = new Dictionary<string, string>(parsed.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var cookie in parsed)
            if (cookie.Name.HasValue)
                cookies[cookie.Name.Value] = Uri.UnescapeDataString(cookie.Value.Value ?? string.Empty);
        return new RequestCookies(cookies);
    }

    public int Count => cookies.Count;
    public ICollection<string> Keys => cookies.Keys;
    public bool ContainsKey (string key) => cookies.ContainsKey(key);
    public bool TryGetValue (string key, [NotNullWhen(true)] out string? value) => cookies.TryGetValue(key, out value);

    /// <summary>The cookie's value, or null when the request carries no such cookie.</summary>
    /// <remarks>Null rather than an exception: the contract differs from <c>IDictionary</c>'s here,
    /// and handlers written against ASP.NET Core rely on it.</remarks>
    public string? this [string key] => cookies.GetValueOrDefault(key);

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator () => cookies.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator () => GetEnumerator();
}

/// <summary>
/// The response's <c>Set-Cookie</c> headers.
/// </summary>
/// <remarks>
/// One header per cookie, which is what makes this possible at all: the response snapshot renders a
/// multi-valued header as a JSON array and <c>js/runtime.mjs</c> appends the entries one by one, so
/// several <c>Set-Cookie</c> headers reach the client as several headers. While the wire shape was
/// a flat object they could only have been comma-joined into one malformed cookie, which is why
/// both cookie surfaces threw rather than degrade.
/// </remarks>
internal sealed class ResponseCookies (IHeaderDictionary headers) : IResponseCookies
{
    public void Append (string key, string value) =>
        Append(key, value, new CookieOptions { Path = "/" });

    public void Append (string key, string value, CookieOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        // The value is escaped, the name is not: ASP.NET Core's own behaviour, and the escaping is
        // what RequestCookies.Parse reverses.
        Write(options.CreateCookieHeader(key, Uri.EscapeDataString(value)));
    }

    public void Delete (string key) =>
        Delete(key, new CookieOptions { Path = "/" });

    /// <summary>Expires the cookie: an empty value in the past is how a cookie is deleted.</summary>
    /// <remarks>The domain and path have to match the ones it was set with, which is why the
    /// options overload exists at all — a browser treats them as part of the cookie's identity.
    /// </remarks>
    public void Delete (string key, CookieOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var expired = new CookieOptions(options) {
            Expires = DateTimeOffset.UnixEpoch,
            MaxAge = TimeSpan.Zero
        };
        Write(expired.CreateCookieHeader(key, string.Empty));
    }

    private void Write (SetCookieHeaderValue cookie) =>
        headers.SetCookie = StringValues.Concat(headers.SetCookie, cookie.ToString());
}
