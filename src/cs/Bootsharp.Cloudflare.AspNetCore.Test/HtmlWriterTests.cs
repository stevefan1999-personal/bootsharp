using Bootsharp.Cloudflare.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Bootsharp.Cloudflare.AspNetCore.Tests;

/// <summary>
/// The runtime half of the default rendering tier, exercised without a generator
/// which is exactly how a consumer that never opts in renders.
/// </summary>
public class HtmlWriterTests
{
    private static string Render (HtmlBody body)
    {
        var writer = new StringHtmlWriter();
        body(writer);
        return writer.ToString();
    }

    [Fact]
    public void EncodesTheFiveCharactersThatMeanSomethingToAParser ()
    {
        Assert.Equal("&amp;&lt;&gt;&quot;&#39;", Render(html => html.WriteText("&<>\"'")));
        Assert.Equal("&amp;&lt;&gt;&quot;&#39;", Render(html => html.WriteAttribute("&<>\"'")));
    }

    /// <summary>
    /// Non-ASCII stays as itself, unlike <see cref="System.Net.WebUtility.HtmlEncode(string?)"/>.
    /// </summary>
    /// <remarks>The one deliberate difference from the SSR this replaces: numeric references would
    /// triple the size of a non-Latin page and say the same thing, and the response states UTF-8.</remarks>
    [Fact]
    public void LeavesNonAsciiAlone ()
    {
        Assert.Equal("ブートシャープ — é", Render(html => html.WriteText("ブートシャープ — é")));
        // The exact byte the old page differed on: WebUtility escapes U+00A0–U+00FF and nothing above.
        Assert.Equal("&#233;", System.Net.WebUtility.HtmlEncode("é"));
    }

    [Fact]
    public void WritesNullAndEmptyAsNothing ()
    {
        Assert.Equal("", Render(html => html.WriteText((string?)null)));
        Assert.Equal("", Render(html => html.WriteText("")));
        Assert.Equal("", Render(html => html.WriteUrl(null)));
        Assert.Equal("", Render(html => html.WriteHtml(HtmlString.Empty)));
    }

    [Fact]
    public void FormatsValuesInvariantly ()
    {
        Assert.Equal("42", Render(html => html.WriteText(42)));
        Assert.Equal("1.50", Render(html => html.WriteText(1.5m, "0.00")));
        Assert.Equal("True", Render(html => html.WriteText(true)));
    }

    [Theory]
    [InlineData("/notes/1", "/notes/1")]
    [InlineData("https://example.com/a?b=c&d=e", "https://example.com/a?b=c&amp;d=e")]
    [InlineData("mailto:a@b.c", "mailto:a@b.c")]
    [InlineData("//cdn.example.com/x.js", "//cdn.example.com/x.js")]
    [InlineData("?flash=1", "?flash=1")]
    [InlineData("#top", "#top")]
    [InlineData("javascript:alert(1)", "about:invalid")]
    [InlineData("JaVaScRiPt:alert(1)", "about:invalid")]
    [InlineData("  \t javascript:alert(1)", "about:invalid")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=", "about:invalid")]
    [InlineData("vbscript:msgbox(1)", "about:invalid")]
    public void ChecksUrlSchemes (string url, string expected)
    {
        Assert.Equal(expected, Render(html => html.WriteUrl(url)));
    }

    [Fact]
    public void WritesRawMarkupVerbatim ()
    {
        Assert.Equal("<b>x</b>", Render(html => html.WriteHtml(HtmlString.Raw("<b>x</b>"))));
    }

    /// <summary>A fragment is written verbatim wherever it lands, including through the generic sink.</summary>
    [Fact]
    public void NeverDoubleEncodesAFragment ()
    {
        var fragment = HtmlString.From($"<p>{"a & b"}</p>");
        Assert.Equal("<p>a &amp; b</p>", fragment.Value);
        Assert.Equal("<p>a &amp; b</p>", Render(html => html.Write($"{fragment}")));
    }

    [Fact]
    public void ScansContextAtRenderTime ()
    {
        var value = "a & b";
        Assert.Equal("<p class=\"a &amp; b\">a &amp; b</p>",
            Render(html => html.Write($"<p class=\"{value}\">{value}</p>")));
    }

    [Fact]
    public void RefusesAHoleNoEncodingCanRescue ()
    {
        var value = "1";
        var error = Assert.Throws<InvalidOperationException>(() =>
            Render(html => html.Write($"<script>var x = {value};</script>")));
        Assert.Contains("whose content is not markup", error.Message);
    }

    [Fact]
    public void RefusesAnAlignment ()
    {
        var value = "a";
        Assert.Throws<InvalidOperationException>(() => Render(html => html.Write($"<p>{value,10}</p>")));
    }

    /// <summary>Everything after a raw-text element is markup again.</summary>
    [Fact]
    public void LeavesRawTextElements ()
    {
        var value = "&";
        Assert.Equal("<style>a{b:c}</style><p>&amp;</p>",
            Render(html => html.Write($"<style>a{{b:c}}</style><p>{value}</p>")));
    }

    /// <summary>
    /// <c>&lt;title&gt;</c> decodes character references, so a value in one is encoded rather than
    /// refused — and the encoding of <c>&lt;</c> is what stops it closing the element.
    /// </summary>
    [Fact]
    public void EncodesInsideEscapableRawText ()
    {
        var value = "</title><script>x";
        Assert.Equal("<title>&lt;/title&gt;&lt;script&gt;x</title>",
            Render(html => html.Write($"<title>{value}</title>")));
    }

    [Fact]
    public async Task ResultWritesTextHtml ()
    {
        var answer = await Worker.Execute(TypedResults.Html(html => html.Write($"<p>{"&"}</p>")));
        Assert.Equal(200, answer.Status);
        Assert.Equal("text/html; charset=utf-8", answer.Header("content-type"));
        Assert.Equal("<p>&amp;</p>", answer.Body);
    }

    [Fact]
    public async Task ResultCarriesAnExplicitStatus ()
    {
        var answer = await Worker.Execute(TypedResults.Html(HtmlString.Raw("<p>gone</p>"), 410));
        Assert.Equal(410, answer.Status);
        Assert.Equal("<p>gone</p>", answer.Body);
    }
}
