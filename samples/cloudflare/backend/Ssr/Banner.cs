using Bootsharp.Cloudflare.AspNetCore.Html;

namespace Cloudflare.Backend.Ssr;

/// <summary>
/// The home page's two conditional banners, as a fragment both tiers interpolate.
/// </summary>
/// <remarks>
/// <para>
/// The only place either page trusts markup, which is what <see cref="HtmlString"/> is for: the
/// fragment is encoded where it is built — <c>{text}</c> goes through the same interpolated-template
/// handler and the same encoder as a hole in a page — so it is written verbatim where it is used, and
/// grepping <c>HtmlString</c> is what makes that reviewable.
/// </para>
/// <para>
/// It is a fragment rather than an <c>@if</c> block in the page on purpose. An <c>@if</c> alone on
/// its line is a Razor <em>code block</em>, and Razor elides the line's leading indentation and its
/// trailing newline around one, so the page would render five bytes of inert whitespace differently
/// from the <c>[HtmlTemplate]</c> page it was converted from. An implicit expression like this call
/// leaves the surrounding whitespace exactly as written, which is what lets the conversion be
/// verified by diffing the two renderings byte for byte. The cost of that choice is real and
/// measured: a fragment is built by the runtime template handler, which roots the request-time
/// scanner a compiled <c>@if</c> does not need (see the README's SSR section).
/// </para>
/// </remarks>
public static class Banner
{
    public static HtmlString Notice (string kind, string? text) => text is { Length: > 0 }
        ? HtmlString.From($"""<p class="{kind}">{text}</p>""")
        : HtmlString.Empty;
}
