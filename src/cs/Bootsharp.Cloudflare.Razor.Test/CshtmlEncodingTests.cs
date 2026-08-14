namespace Bootsharp.Cloudflare.Razor.Tests;

/// <summary>
/// The claim the tier rests on: which encoder a hole gets is decided by the markup around it, at
/// compile time, and the author cannot forget.
/// </summary>
/// <remarks>
/// <para>
/// Two halves, and both are asserted. The compile-time half is that the Razor parser has already
/// established whether a hole is element content, an attribute value or a URL, so the node writer can
/// name the sink in the emitted call — asserted against <c>Writes</c>. The runtime half is that the
/// sink it named is the shipped <c>HtmlWriter</c>'s, with this repo's own encoder behind it
/// asserted by rendering, because a generator that emitted <c>WriteAttribute</c> where it meant
/// <c>WriteUrl</c> would satisfy every assertion about the emitted text and still ship the bug.
/// </para>
/// <para>
/// The decisive case is <c>javascript:</c>. Attribute encoding is complete and useless against it
/// the payload never has to leave the attribute, so escaping the characters around it changes
/// nothing. Only a sink that knows the value is a URL can refuse the scheme, which is the whole
/// reason the sink is chosen from the attribute name rather than from the value.
/// </para>
/// <para>
/// The positions this tier does <em>not</em> classify are in <see cref="CshtmlContextGapTests"/>.
/// </para>
/// </remarks>
public class CshtmlEncodingTests
{
    private const string page = "Views/Home.cshtml";
    private const string type = "App.Views.Home";

    /// <summary>Every attribute a browser resolves as a URL, and therefore every attribute where a
    /// scheme in the value is executable. Each has to reach <c>WriteUrl</c>, not the attribute
    /// encoder.</summary>
    [Theory]
    [InlineData("href")]
    [InlineData("src")]
    [InlineData("action")]
    [InlineData("formaction")]
    [InlineData("cite")]
    [InlineData("poster")]
    [InlineData("data")]
    [InlineData("manifest")]
    [InlineData("ping")]
    [InlineData("background")]
    [InlineData("longdesc")]
    [InlineData("profile")]
    [InlineData("usemap")]
    [InlineData("codebase")]
    [InlineData("srcset")]
    [InlineData("icon")]
    // HTML attribute names are case-insensitive, and an author who shouts one should not lose the
    // scheme check for it.
    [InlineData("HREF")]
    [InlineData("SrC")]
    public void ChoosesTheUrlSinkForAUrlValuedAttribute (string attribute)
    {
        var run = CshtmlHarness.Run(new Page(page, $"@model string\n<x {attribute}=\"@Model\"></x>"));
        Assert.Empty(run.Diagnostics);
        Assert.Contains("html.WriteUrl(Model);", run.Writes);
    }

    /// <summary>An attribute a browser does not resolve gets the attribute encoder — refusing a
    /// scheme in a <c>title</c> would corrupt ordinary prose for no gain.</summary>
    [Theory]
    [InlineData("title")]
    [InlineData("class")]
    [InlineData("alt")]
    [InlineData("id")]
    [InlineData("aria-label")]
    [InlineData("placeholder")]
    public void ChoosesTheAttributeSinkForEveryOtherAttribute (string attribute)
    {
        var run = CshtmlHarness.Run(new Page(page, $"@model string\n<x {attribute}=\"@Model\"></x>"));
        Assert.Empty(run.Diagnostics);
        Assert.Contains("html.WriteAttribute(Model);", run.Writes);
    }

    [Fact]
    public void ChoosesTheTextSinkForElementContent ()
    {
        var run = CshtmlHarness.Run(new Page(page, "@model string\n<p>@Model</p>"));
        Assert.Equal(["""html.WriteLiteral("<p>");""", "html.WriteText(Model);", """html.WriteLiteral("</p>");"""],
            run.Writes);
    }

    /// <summary>The sink follows the attribute it is in and goes back to element content after it, so
    /// a page that mixes all three in one line gets all three right.</summary>
    [Fact]
    public void ReturnsToTheTextSinkAfterAnAttribute ()
    {
        var run = CshtmlHarness.Run(new Page(page, """
            @model string
            <a href="@Model" title="@Model">@Model</a>
            """));
        Assert.Equal(["html.WriteUrl(Model);", "html.WriteAttribute(Model);", "html.WriteText(Model);"],
            [.. run.Writes.Where(static write => !write.StartsWith("html.WriteLiteral"))]);
    }

    /// <summary>The five characters that mean something to an HTML parser, escaped in both text and
    /// attribute position; everything else, non-ASCII included, passes through. Escaping more would
    /// be payload growth that says exactly the same thing.</summary>
    [Fact]
    public void EscapesTheFiveMarkupCharactersAndNothingElse ()
    {
        Assert.Equal("""<p title="ブé&amp;&lt;&gt;&quot;&#39;">ブé&amp;&lt;&gt;&quot;&#39;</p>""",
            CshtmlHarness.Render(type, ["""ブé&<>"'"""],
                new Page(page, "@model string\n<p title=\"@Model\">@Model</p>")));
    }

    /// <summary>The case the tier exists for. Each of these would run if the value were escaped
    /// rather than refused, because escaping never makes it leave the attribute.</summary>
    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    // Browsers strip leading whitespace and control characters before reading the scheme, so these
    // are javascript URLs however they look in the source.
    [InlineData("\n javascript:alert(1)")]
    [InlineData("javascript:alert(1)")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    public void NeutralizesADangerousUrlRatherThanEscapingIt (string url)
    {
        Assert.Equal("""<a href="about:invalid">go</a>""", CshtmlHarness.Render(type, [url],
            new Page(page, "@model string\n<a href=\"@Model\">go</a>")));
    }

    /// <summary>The refusal is a scheme check, not a filter: the URLs a page actually links to arrive
    /// intact, encoded as an attribute value and nothing more.</summary>
    [Theory]
    [InlineData("/a?b=1&c=2", "/a?b=1&amp;c=2")]
    [InlineData("https://example.com/x", "https://example.com/x")]
    [InlineData("mailto:someone@example.com", "mailto:someone@example.com")]
    [InlineData("tel:+15550100", "tel:+15550100")]
    [InlineData("#anchor", "#anchor")]
    [InlineData("relative/path.html", "relative/path.html")]
    public void LeavesAnOrdinaryUrlAlone (string url, string expected)
    {
        Assert.Equal($"""<a href="{expected}">go</a>""", CshtmlHarness.Render(type, [url],
            new Page(page, "@model string\n<a href=\"@Model\">go</a>")));
    }

    /// <summary>An attribute built from several holes is several writes through the same sink, so a
    /// scheme smuggled into any one of them is still checked — flattening MVC's buffered attribute
    /// protocol does not open a seam where a value escapes the context it was written into.</summary>
    [Fact]
    public void ChecksEveryHoleOfAMultiHoleUrl ()
    {
        var markup = "@param string a\n@param string b\n<a href=\"@a/@b\">go</a>";
        var run = CshtmlHarness.Run(new Page(page, markup));
        Assert.Equal(2, run.Writes.Count(static write => write.StartsWith("html.WriteUrl")));
        Assert.Equal("""<a href="about:invalid/alert(1)">go</a>""",
            CshtmlHarness.Render(type, ["javascript:", "alert(1)"], new Page(page, markup)));
    }

    /// <summary>Literal text around a hole stays literal and only the hole is encoded, so an
    /// attribute the author wrote half of is not re-encoded into gibberish.</summary>
    [Fact]
    public void KeepsTheLiteralPartsOfAnAttributeLiteral ()
    {
        var run = CshtmlHarness.Run(new Page(page, "@param string a\n<a href=\"/base/@a?q=1\">go</a>"));
        Assert.Equal([
            """html.WriteLiteral("<a");""",
            """html.WriteLiteral(" href=\"");""",
            """html.WriteLiteral("/base/");""",
            "html.WriteUrl(a);",
            """html.WriteLiteral("?q=1");""",
            """html.WriteLiteral("\"");""",
            """html.WriteLiteral(">go</a>");"""
        ], run.Writes);
    }

    /// <summary><see cref="AspNetCore.Html.HtmlString"/> is the one way past the encoder, and it works
    /// in the two contexts where writing markup verbatim means anything.</summary>
    [Theory]
    [InlineData("""<p>@(new HtmlString("<b>raw</b>"))</p>""", "<p><b>raw</b></p>")]
    [InlineData("""<p title="@(new HtmlString("<b>"))">x</p>""", """<p title="<b>">x</p>""")]
    public void WritesHtmlStringVerbatim (string markup, string expected)
    {
        Assert.Equal(expected, CshtmlHarness.Render(type, [], new Page(page, markup)));
    }

    /// <summary>
    /// The escape hatch does not open in URL position.
    /// </summary>
    /// <remarks>
    /// <c>HtmlWriter</c> has no <c>WriteUrl(HtmlString)</c> overload, so a fragment in a URL-valued
    /// attribute binds the generic one and is scheme-checked and encoded like any other value. That is
    /// the right answer rather than an accident of overload resolution — "this text is already markup"
    /// is not a claim about whether a URL is safe to navigate to, and the two should not be spelled
    /// the same way. Pinned because a later convenience overload could quietly remove the asymmetry.
    /// </remarks>
    [Fact]
    public void DoesNotLetHtmlStringPastTheUrlCheck ()
    {
        Assert.Equal("""<a href="about:invalid">go</a>""", CshtmlHarness.Render(type, [],
            new Page(page, """<a href="@(new HtmlString("javascript:alert(1)"))">go</a>""")));
    }

    /// <summary>
    /// The one documented behavioural difference from MVC.
    /// </summary>
    /// <remarks>Flattening MVC's buffered <c>BeginWriteAttribute</c>/<c>EndWriteAttribute</c> protocol
    /// into straight-line writes is what makes the emitted program allocation-free, and it costs the
    /// one thing that protocol bought: MVC omits an attribute whose only value is null or false, and
    /// this tier writes it empty or stringified. Asserted rather than merely mentioned, so a change to
    /// it is a failing test rather than a surprise in someone's markup.</remarks>
    [Theory]
    [InlineData("@param string? a", null, """<x title="">y</x>""")]
    [InlineData("@param bool a", false, """<x title="False">y</x>""")]
    public void WritesAnEmptyAttributeWhereMvcWouldOmitIt (string declaration, object? value, string expected)
    {
        Assert.Equal(expected, CshtmlHarness.Render(type, [value],
            new Page(page, declaration + "\n<x title=\"@a\">y</x>")));
    }
}
