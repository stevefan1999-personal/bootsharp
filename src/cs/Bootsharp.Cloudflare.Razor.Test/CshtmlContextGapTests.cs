namespace Bootsharp.Cloudflare.Razor.Tests;

/// <summary>
/// Positions where this tier and the <c>[HtmlTemplate]</c> tier beside it do not agree — recorded,
/// not endorsed.
/// </summary>
/// <remarks>
/// <para>
/// puts both tiers behind one safety story: a hole is encoded for the context it sits in,
/// and a context where no encoding is safe is refused. The interpolated-string tier implements that
/// with <c>HtmlContextScanner</c>, which is a tokenizer with a <c>Refused</c> outcome — an unquoted
/// attribute value, an <c>on*</c> handler, a <c>&lt;script&gt;</c> body, an element name and an
/// attribute-name position all throw rather than render.
/// </para>
/// <para>
/// This tier asks the Razor parser instead, and the Razor parser answers a narrower question. It
/// distinguishes element content from attribute value, which is what
/// <see cref="CshtmlEncodingTests"/> covers and what the tier gets right; it has no notion of a
/// position that cannot be written to at all, so <see cref="CshtmlNodeWriter"/> has nothing to refuse
/// with and every hole is written through one of the three sinks. Where the scanner would refuse,
/// this tier picks the nearest sink and emits.
/// </para>
/// <para>
/// <b>These tests assert what the generator does today, and several of the values they assert are
/// live injections.</b> They exist so the gap is greppable, has a failing test the day it is closed,
/// and cannot be rediscovered as a surprise. Each names the payload it lets through. When the
/// generator learns to refuse these positions — the natural fix is a <c>CFW063</c> reported from the
/// node writer, which already knows the attribute name and could learn the enclosing element — every
/// test here should be rewritten as a diagnostic assertion, and this class should disappear.
/// </para>
/// </remarks>
public class CshtmlContextGapTests
{
    private const string page = "Views/Home.cshtml";
    private const string type = "App.Views.Home";

    /// <summary>
    /// GAP: an <c>on*</c> attribute's value is JavaScript, and this tier writes it as an ordinary
    /// attribute value.
    /// </summary>
    /// <remarks>Attribute encoding is the wrong encoder for a script: none of the five characters it
    /// escapes is needed to write <c>alert(1)</c>. The scanner tier refuses the same hole by name
    /// ("in the 'onclick' event handler attribute, whose value is JavaScript"), so a page that is safe
    /// as an interpolated string becomes an injection when it is pasted into a <c>.cshtml</c>.</remarks>
    [Theory]
    [InlineData("""<button onclick="@Model">go</button>""", """<button onclick="alert(1)">go</button>""")]
    [InlineData("""<div onmouseover='@Model'>x</div>""", """<div onmouseover='alert(1)'>x</div>""")]
    public void WritesAnEventHandlerAttributeThroughTheAttributeEncoder (string markup, string injected)
    {
        Assert.Equal(injected, CshtmlHarness.Render(type, ["alert(1)"],
            new Page(page, "@model string\n" + markup)));
    }

    /// <summary>
    /// GAP: a hole that is the whole value of an unquoted attribute is written into it, so whitespace
    /// in the value starts an attribute the page never wrote.
    /// </summary>
    /// <remarks>The sink is chosen correctly — <c>WriteUrl</c> for <c>href</c>, <c>WriteAttribute</c>
    /// for <c>title</c> — and it does not help, because neither encoder escapes a space and there is
    /// no closing quote to stop at. The scanner tier refuses this position outright and says to quote
    /// the attribute.</remarks>
    [Theory]
    [InlineData("""<a href=@Model>x</a>""", "/ok onmouseover=alert(1)", """<a href=/ok onmouseover=alert(1)>x</a>""")]
    [InlineData("""<p title=@Model>x</p>""", "x onmouseover=alert(1)", """<p title=x onmouseover=alert(1)>x</p>""")]
    public void WritesAnUnquotedAttributeValueThatCanStartAnotherAttribute (
        string markup, string value, string injected)
    {
        Assert.Equal(injected, CshtmlHarness.Render(type, [value], new Page(page, "@model string\n" + markup)));
    }

    /// <summary>
    /// GAP: a hole in a <c>&lt;script&gt;</c> or <c>&lt;style&gt;</c> body is HTML-encoded, which is
    /// neither safe nor correct there.
    /// </summary>
    /// <remarks>The content of these elements is not markup, so character references are not decoded:
    /// encoding buys nothing a script can be stopped by, and it corrupts any value that legitimately
    /// contains one of the five characters. A hole outside a JavaScript string literal is a plain
    /// injection, which is the case asserted here. The scanner tier refuses both elements with exactly
    /// that reasoning ("whose content is not markup: HTML encoding cannot make a value safe
    /// there").</remarks>
    [Theory]
    [InlineData("<script>var x = @Model;</script>", "1; alert(1)", "<script>var x = 1; alert(1);</script>")]
    [InlineData("<style>.a { width: @Model; }</style>", "1px} body{display:none", "<style>.a { width: 1px} body{display:none; }</style>")]
    public void HtmlEncodesARawTextBodyInsteadOfRefusingIt (string markup, string value, string injected)
    {
        Assert.Equal(injected, CshtmlHarness.Render(type, [value], new Page(page, "@model string\n" + markup)));
    }

    /// <summary>
    /// GAP: the two tiers keep separate lists of URL-valued attributes, and they disagree.
    /// </summary>
    /// <remarks>This tier's list adds <c>ping</c>, <c>codebase</c>, <c>srcset</c> and <c>icon</c>,
    /// which is the better half of the difference. It is missing <c>xlink:href</c>, the SVG link
    /// attribute the scanner tier does check — so the scheme check that stops
    /// <c>&lt;a href="javascript:…"&gt;</c> does not stop its SVG spelling. One list shared by both
    /// tiers is the obvious repair; the scanner's copy already lives in a file the generator links by
    /// source.</remarks>
    [Fact]
    public void DoesNotTreatXlinkHrefAsAUrlAttribute ()
    {
        Assert.Equal("""<a xlink:href="javascript:alert(1)">x</a>""",
            CshtmlHarness.Render(type, ["javascript:alert(1)"],
                new Page(page, "@model string\n<a xlink:href=\"@Model\">x</a>")));
    }

    /// <summary>
    /// GAP, latent: a hole in a <c>data-*</c> attribute reaches the element-content sink rather than
    /// the attribute one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Razor's legacy parser excludes <c>data-</c> prefixed attributes from its dynamic-attribute
    /// handling — an inheritance from the MVC feature that dropped null attributes, which was never
    /// meant to apply to data attributes. The consequence here is that the attribute never becomes an
    /// <c>HtmlAttributeIntermediateNode</c>, <c>WriteHtmlAttribute</c> is never called, and the hole
    /// keeps the default text sink.
    /// </para>
    /// <para>
    /// The bytes are correct today and this is not exploitable: <c>WriteText</c> and
    /// <c>WriteAttribute</c> both route to the same encoder and escape the same five characters,
    /// quote and apostrophe included. It is pinned because <c>HtmlEncoding</c>'s own documentation
    /// anticipates the two diverging, and on the day they do, every <c>data-*</c> attribute in every
    /// page silently gets the wrong one.
    /// </para>
    /// </remarks>
    [Fact]
    public void SendsADataDashAttributeHoleThroughTheTextSink ()
    {
        var run = CshtmlHarness.Run(new Page(page, "@model string\n<div data-x=\"@Model\"></div>"));
        Assert.Contains("html.WriteText(Model);", run.Writes);
        Assert.DoesNotContain("html.WriteAttribute(Model);", run.Writes);
        // Identical bytes to the attribute sink, which is why this is latent rather than live.
        Assert.Equal("""<div data-x="&amp;&lt;&gt;&quot;"></div>""",
            CshtmlHarness.Render(type, ["&<>\""], new Page(page, "@model string\n<div data-x=\"@Model\"></div>")));
    }

    /// <summary>
    /// GAP: a hole standing in for an element name or an attribute name is written as text.
    /// </summary>
    /// <remarks>The scanner tier refuses both ("in an element name"; "in an attribute name position,
    /// where a value could introduce an attribute the template never wrote"). Here the value is
    /// text-encoded, which stops it from closing the tag but not from adding attributes to it — the
    /// space in the asserted value is what does the damage.</remarks>
    [Theory]
    [InlineData("""<p @Model="1">x</p>""", "title", """<p title="1">x</p>""")]
    [InlineData("""<p @Model="1">x</p>""", "title=\"\" onx", """<p title=&quot;&quot; onx="1">x</p>""")]
    [InlineData("<@Model>x</@Model>", "p", "<p>x</p>")]
    public void WritesANameHoleThroughTheTextSink (string markup, string value, string rendered)
    {
        Assert.Equal(rendered, CshtmlHarness.Render(type, [value], new Page(page, "@model string\n" + markup)));
    }

    /// <summary>
    /// Not a gap — the contrast that shows the rest of the list is not arbitrary.
    /// </summary>
    /// <remarks><c>&lt;title&gt;</c> and <c>&lt;textarea&gt;</c> are escapable raw text: character
    /// references <em>are</em> decoded in them, so encoding <c>&lt;</c> is exactly what stops a value
    /// from closing the element. Both tiers write text there and both are right.</remarks>
    [Theory]
    [InlineData("<title>@Model</title>", "</title><script>alert(1)</script>",
        "<title>&lt;/title&gt;&lt;script&gt;alert(1)&lt;/script&gt;</title>")]
    [InlineData("<textarea>@Model</textarea>", "</textarea><b>",
        "<textarea>&lt;/textarea&gt;&lt;b&gt;</textarea>")]
    public void EncodesEscapableRawTextLikeTheTemplateTierDoes (string markup, string value, string expected)
    {
        Assert.Equal(expected, CshtmlHarness.Render(type, [value], new Page(page, "@model string\n" + markup)));
    }
}
