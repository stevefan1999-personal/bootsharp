using System.Globalization;

namespace Bootsharp.Cloudflare.Razor.Tests;

/// <summary>
/// What markup and holes turn into, and what survives the trip unchanged.
/// </summary>
/// <remarks>
/// A server-rendered page is bytes, and the difference between two of them is often one space. These
/// assertions are deliberately exact rather than <c>Contains</c>: a retarget that started coalescing
/// differently, swallowing indentation, or routing a hole through <c>ToString()</c> instead of the
/// writer's own formatter would still produce a page that looks right in a browser and is wrong in a
/// diff.
/// </remarks>
public class CshtmlEmissionTests
{
    /// <summary>Markup with no hole in it is one call, not one per tag: the flattened attribute
    /// protocol is what lets a whole run of literal text stay a single write.</summary>
    [Fact]
    public void CoalescesMarkupWithNoHolesIntoOneLiteral ()
    {
        var run = CshtmlHarness.Run(new Page("Views/Home.cshtml",
            """<br/><hr /><img src="/a.png"><input value="x" />"""));
        Assert.Empty(run.Diagnostics);
        Assert.Equal(["""html.WriteLiteral("<br/><hr /><img src=\"/a.png\"><input value=\"x\" />");"""], run.Writes);
    }

    /// <summary>Void and self-closing tags are markup like any other — the tier has no element model
    /// and no opinion about which spelling an author uses.</summary>
    [Fact]
    public void PreservesSelfClosingAndVoidTagsExactly ()
    {
        const string markup = """<br/><hr /><img src="/a.png"><input value="x" />""";
        Assert.Equal(markup, CshtmlHarness.Render("App.Views.Home", [], new Page("Views/Home.cshtml", markup)));
    }

    /// <summary>Indentation, tabs and blank lines reach the response untouched. Whitespace is content
    /// in <c>&lt;pre&gt;</c>, and it is a diff everywhere else.</summary>
    [Fact]
    public void PreservesWhitespaceByteForByte ()
    {
        const string markup = "<p>\n   a   b\t\n</p>\n\n<p> </p>";
        Assert.Equal(markup, CshtmlHarness.Render("App.Views.Home", [], new Page("Views/Home.cshtml", markup)));
    }

    /// <summary>A directive line leaves nothing behind — not even the newline that ended it.</summary>
    [Fact]
    public void ConsumesTheDirectiveLineWhole ()
    {
        Assert.Equal("<p>x</p>", CshtmlHarness.Render("App.Views.Home", ["x"],
            new Page("Views/Home.cshtml", "@model string\n<p>@Model</p>")));
    }

    /// <summary>A Razor comment is compile-time and vanishes; an HTML comment is markup and ships,
    /// holes and all — which is why a hole inside one is still encoded.</summary>
    [Fact]
    public void DropsRazorCommentsAndShipsHtmlOnes ()
    {
        Assert.Equal("<p>kept<!-- &lt;b&gt; --></p>", CshtmlHarness.Render("App.Views.Home", ["<b>"],
            new Page("Views/Home.cshtml", "@model string\n<p>@* dropped *@kept<!-- @Model --></p>")));
    }

    [Fact]
    public void WritesADoubledAtAsOne ()
    {
        Assert.Equal("<p>a@b</p>", CshtmlHarness.Render("App.Views.Home", [],
            new Page("Views/Home.cshtml", "<p>a@@b</p>")));
    }

    /// <summary>The parentheses bound the expression rather than becoming part of it, so an operator
    /// that would otherwise end an implicit hole stays inside it.</summary>
    [Fact]
    public void WritesAnExplicitExpression ()
    {
        const string markup = "@param int n\n<p>@(n + 1)</p>";
        Assert.Contains("html.WriteText(n + 1);", CshtmlHarness.Run(new Page("Views/Home.cshtml", markup)).Writes);
        Assert.Equal("<p>2</p>", CshtmlHarness.Render("App.Views.Home", [1], new Page("Views/Home.cshtml", markup)));
    }

    [Fact]
    public void WritesTheResultOfAMethodCall ()
    {
        Assert.Equal("<p>AB</p>", CshtmlHarness.Render("App.Views.Home", ["ab"],
            new Page("Views/Home.cshtml", "@model string\n<p>@Model.ToUpperInvariant()</p>")));
    }

    /// <summary>A <c>@{ }</c> block declares ordinary locals in the render method, so a hole further
    /// down reads them — the page is one method, not a sequence of isolated fragments.</summary>
    [Fact]
    public void SharesLocalsBetweenACodeBlockAndAHole ()
    {
        Assert.Equal("<p>1</p>", CshtmlHarness.Render("App.Views.Home", [],
            new Page("Views/Home.cshtml", "@{ var x = 1; }<p>@x</p>")));
    }

    /// <summary>The <c>@:</c> line escape, which writes the rest of the line as markup from inside a
    /// code block. Its leading indentation is markup too, and is kept.</summary>
    [Fact]
    public void WritesABareLineWithTheColonEscape ()
    {
        Assert.Equal("    bare line\n", CshtmlHarness.Render("App.Views.Home", [],
            new Page("Views/Home.cshtml", "@if (true)\n{\n    @:bare line\n}")));
    }

    /// <summary>A <c>&lt;text&gt;</c> block groups markup without contributing an element of its
    /// own — the tag is a parser instruction and never reaches the response.</summary>
    [Fact]
    public void WritesATextBlockWithoutItsTag ()
    {
        Assert.Equal("bare N text", CshtmlHarness.Render("App.Views.Home", ["N"],
            new Page("Views/Home.cshtml", "@param string n\n@if (true) { <text>bare @n text</text> }")));
    }

    /// <summary>
    /// A non-string hole is formatted invariantly, because it goes to <c>WriteText&lt;T&gt;</c> rather
    /// than through the value's own <c>ToString()</c>.
    /// </summary>
    /// <remarks>The distinction is invisible until a worker runs somewhere with a comma decimal
    /// separator and starts emitting <c>1,5</c> into a JSON-ish attribute or a CSS length. Every
    /// isolate has to render the same bytes for the same model, so the culture is pinned here rather
    /// than inherited.</remarks>
    [Fact]
    public void FormatsANonStringHoleInvariantly ()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            Assert.Equal("""<p style="width:1.5px">1.5</p>""", CshtmlHarness.Render("App.Views.Home", [1.5],
                new Page("Views/Home.cshtml", """
                    @param double n
                    <p style="width:@(n)px">@n</p>
                    """)));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
