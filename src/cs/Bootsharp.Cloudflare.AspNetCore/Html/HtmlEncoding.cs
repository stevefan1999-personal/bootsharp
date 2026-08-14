namespace Bootsharp.Cloudflare.AspNetCore.Html;

/// <summary>
/// The encoders the writer's context-specific sinks are built from.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not <c>System.Text.Encodings.Web.HtmlEncoder</c>: that type escapes every
/// non-ASCII character to a numeric reference by default, which for a UTF-8 response is pure
/// payload growth — a page of Japanese text triples in size and says exactly the same thing.
/// <see cref="System.Net.WebUtility.HtmlEncode(string?)"/>, which the SSR this replaces used, does
/// it for U+00A0–U+00FF only (so <c>é</c> becomes <c>&amp;#233;</c> but <c>ブ</c> does not) — a
/// distinction with no meaning once the response states UTF-8. Here only the five characters that
/// mean something to an HTML parser are escaped.
/// </para>
/// <para>
/// Both the text and the attribute encoder escape the same five. Keeping them separate is not
/// redundancy: the context they are chosen by is what makes <see cref="Url"/> and the refusals
/// possible at all, and a future divergence (an attribute encoder that also escapes backtick for
/// old IE, say) lands in one place instead of at every call site.
/// </para>
/// </remarks>
internal static class HtmlEncoding
{
    /// <summary>Schemes a URL-valued attribute may carry.</summary>
    /// <remarks>Everything else — <c>javascript:</c> above all, but equally <c>vbscript:</c> and a
    /// <c>data:</c> URL that can carry markup — becomes <c>about:invalid</c>, the value the HTML
    /// specification reserves for "this is not a usable URL". A relative URL has no scheme and is
    /// always allowed, which is what most links are.</remarks>
    private static readonly string[] schemes = ["http", "https", "mailto", "tel", "ftp", "ftps", "sms", "ws", "wss"];

    private const string invalid = "about:invalid";

    public static void Encode (HtmlWriter writer, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        var span = value.AsSpan();
        var start = 0;
        for (var index = 0; index < span.Length; index++)
        {
            var replacement = Replacement(span[index]);
            if (replacement is null) continue;
            if (index > start) writer.WriteLiteral(span.Slice(start, index - start));
            writer.WriteLiteral(replacement);
            start = index + 1;
        }
        if (start < span.Length) writer.WriteLiteral(span.Slice(start));
    }

    /// <summary>Checks the scheme, then encodes the value as an attribute.</summary>
    public static void Url (HtmlWriter writer, string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        Encode(writer, Allowed(value!) ? value : invalid);
    }

    private static string? Replacement (char character) => character switch {
        '&' => "&amp;",
        '<' => "&lt;",
        '>' => "&gt;",
        '"' => "&quot;",
        '\'' => "&#39;",
        _ => null
    };

    private static bool Allowed (string value)
    {
        // Leading whitespace and control characters are stripped by browsers before the scheme is
        // read, so "\njavascript:alert(1)" is a javascript URL however it looks in the source.
        var start = 0;
        while (start < value.Length && (char.IsWhiteSpace(value[start]) || char.IsControl(value[start]))) start++;
        for (var index = start; index < value.Length; index++)
        {
            var character = value[index];
            // The first of these ends the part a scheme could have occupied: everything after is a
            // path, query or fragment, so the URL is relative and carries no scheme at all.
            if (character is '/' or '?' or '#') return true;
            if (character != ':') continue;
            var scheme = value.Substring(start, index - start).ToLowerInvariant();
            return Array.IndexOf(schemes, scheme) >= 0;
        }
        return true;
    }
}
