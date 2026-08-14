namespace Bootsharp.Cloudflare.Razor.Tests;

/// <summary>
/// The half of the tier that is not optional.
/// </summary>
/// <remarks>
/// Every construct asserted here parses without complaint and compiles to something plausible and
/// wrong: with no MVC extension registered, <c>@inject IFoo Foo</c> is the implicit expression
/// <c>inject</c> followed by the literal text <c>" IFoo Foo"</c>, and <c>@addTagHelper</c> leaves
/// <c>asp-*</c> attributes as literal markup that renders and does nothing. Silence is the failure
/// mode this suite exists to prevent, so each of them is refused by name, at its own line in the
/// page, with the substitution to use — and no code is emitted for a page that was refused.
/// </remarks>
public class CshtmlDiagnosticTests
{
    [Theory]
    [InlineData("@inject IFoo foo", "@param")]
    [InlineData("@page \"/home\"", "routing lives in the worker's endpoint map")]
    [InlineData("@section Scripts { <b>x</b> }", "HtmlBody")]
    [InlineData("@inherits MyBase", "static class with no base type")]
    [InlineData("@implements System.ICloneable", "static class with no base type")]
    [InlineData("@addTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers", "no ITagHelper runtime")]
    [InlineData("@removeTagHelper *, Microsoft.AspNetCore.Mvc.TagHelpers", "no ITagHelper runtime")]
    [InlineData("@tagHelperPrefix th:", "no ITagHelper runtime")]
    public void RefusesMvcConstructsByName (string line, string substitution)
    {
        var run = CshtmlHarness.Run(new Page("Views/Bad.cshtml", line + "\n<p>x</p>"));
        Assert.Equal(["CFW060"], run.Ids);
        Assert.Contains(substitution, Assert.Single(run.Messages));
        Assert.Equal(["Bad.cshtml(1,1)"], run.Locations);
        Assert.Empty(run.Generated);
    }

    /// <summary>
    /// Layouts are the one refused family that is not a directive: <c>Layout</c>, <c>RenderBody</c>
    /// and <c>RenderSection</c> are members of MVC's page base class, so here they are ordinary C#
    /// names that resolve to nothing. The build would fail either way; what the refusal buys is a
    /// message that names the substitution instead of CS0103.
    /// </summary>
    [Theory]
    [InlineData("@{ Layout = \"_Layout\"; }")]
    [InlineData("@{ RenderBody(); }")]
    [InlineData("@{ RenderSection(\"scripts\"); }")]
    [InlineData("@{ IsSectionDefined(\"scripts\"); }")]
    public void RefusesLayoutMembers (string line)
    {
        var run = CshtmlHarness.Run(new Page("Views/Bad.cshtml", "<p>a</p>\n" + line));
        Assert.Equal(["CFW060"], run.Ids);
        Assert.Contains("HtmlBody", Assert.Single(run.Messages));
        // The caret is on the name inside the code block, not on the block that contains it: these
        // are found by scanning the page's C# tokens, and a whole-block location would be useless in
        // the @{ } that holds a page's setup.
        Assert.Equal(["Bad.cshtml(2,4)"], run.Locations);
        Assert.Empty(run.Generated);
    }

    /// <summary>Scanned over the page's C# rather than its text, so the same words inside markup are
    /// what they look like: content.</summary>
    [Fact]
    public void DoesNotMistakeMarkupForALayout ()
    {
        var run = CshtmlHarness.Run(new Page("Views/Home.cshtml",
            "<p>The Layout = value is set by RenderBody() in MVC.</p>"));
        Assert.Empty(run.Diagnostics);
    }

    /// <summary>MVC's ambient-import files have no meaning without an MVC runtime, and compiling them
    /// as ordinary pages would be a confusing way to say so.</summary>
    [Theory]
    [InlineData("Views/_ViewImports.cshtml")]
    [InlineData("Views/_ViewStart.cshtml")]
    public void RefusesImportConventions (string path)
    {
        var run = CshtmlHarness.Run(new Page(path, "@using System"));
        Assert.Equal(["CFW060"], run.Ids);
        Assert.Contains("no ambient import on this tier", Assert.Single(run.Messages));
        Assert.Empty(run.Generated);
    }

    /// <summary>Every refusal in one page is reported in one build, rather than one per rebuild.</summary>
    [Fact]
    public void ReportsEveryRefusalAtItsOwnLine ()
    {
        var run = CshtmlHarness.Run(new Page("Views/Bad.cshtml", """
            @page "/x"
            @inject IFoo foo
            @addTagHelper *, My.Helpers
            """));
        Assert.Equal(["CFW060", "CFW060", "CFW060"], run.Ids);
        Assert.Equal(["Bad.cshtml(1,1)", "Bad.cshtml(2,1)", "Bad.cshtml(3,1)"], run.Locations.Order());
    }

    /// <summary>A syntax error is the Razor compiler's own message and span, relocated onto the page —
    /// the compiler is a build input here, so its diagnostics have to arrive as this generator's or
    /// they arrive as nothing.</summary>
    [Fact]
    public void SurfacesRazorErrorsAgainstThePage ()
    {
        var run = CshtmlHarness.Run(new Page("Views/Bad.cshtml", "<p>ok</p>\n@if (true) { <p>x</p>"));
        Assert.Equal(["CFW061"], run.Ids);
        Assert.Contains("missing a closing \"}\"", Assert.Single(run.Messages));
        Assert.Equal(["Bad.cshtml(2,2)"], run.Locations);
        Assert.Empty(run.Generated);
    }

    /// <summary>Two pages under one name would compile to one class declaring <c>Render</c> twice —
    /// a C# error whose only location is inside generated code.</summary>
    [Fact]
    public void RefusesTwoPagesUnderOneName ()
    {
        var run = CshtmlHarness.Run(
            new Page("Views/Home.cshtml", "<p>a</p>"),
            new Page("Views/_Home.cshtml", "<p>b</p>"));
        Assert.Equal(["CFW062", "CFW062"], run.Ids);
        Assert.All(run.Messages, message => Assert.Contains("App.Views.Home", message));
        // Both halves are named, because either one could be the one to move.
        Assert.Equal(["Home.cshtml(1,1)", "_Home.cshtml(1,1)"], run.Locations);
        Assert.Empty(run.Generated);
    }

    /// <summary>Reported against the declaration that introduced the name, which for a duplicate is
    /// the first of the two — the later one is the edit, but the pair is the mistake.</summary>
    [Fact]
    public void RefusesADuplicateParameterName ()
    {
        var run = CshtmlHarness.Run(new Page("Views/Home.cshtml", """
            @param string title
            @param string title
            <p>@title</p>
            """));
        Assert.Equal(["CFW062"], run.Ids);
        Assert.Contains("'title' is declared twice", Assert.Single(run.Messages));
        Assert.Equal(["Home.cshtml(1,1)"], run.Locations);
        Assert.Empty(run.Generated);
    }

    /// <summary>The writer's own parameter is a member of the generated signature like any other, and
    /// shadowing it would silently retarget every emitted write.</summary>
    [Fact]
    public void RefusesAParameterNamedLikeTheWriter ()
    {
        var run = CshtmlHarness.Run(new Page("Views/Home.cshtml", """
            @param string html
            <p>@html</p>
            """));
        Assert.Equal(["CFW062"], run.Ids);
        Assert.Contains("'html' is declared twice", Assert.Single(run.Messages));
        Assert.Equal(["Home.cshtml(1,1)"], run.Locations);
        Assert.Empty(run.Generated);
    }

    /// <summary>The same check catches the other half of the contract: <c>@model</c> occupies the name
    /// <c>Model</c>, so a <c>@param</c> claiming it is the same collision.</summary>
    [Fact]
    public void RefusesAParameterNamedLikeTheModel ()
    {
        var run = CshtmlHarness.Run(new Page("Views/Home.cshtml", """
            @model string
            @param int Model
            <p>x</p>
            """));
        Assert.Equal(["CFW062"], run.Ids);
        Assert.Contains("'Model' is declared twice", Assert.Single(run.Messages));
        Assert.Empty(run.Generated);
    }

    [Fact]
    public void RefusesAFunctionsMemberNamedRender ()
    {
        var run = CshtmlHarness.Run(new Page("Views/Home.cshtml", """
            <p>x</p>
            @functions {
                static void Render () { }
            }
            """));
        Assert.Equal(["CFW062"], run.Ids);
        Assert.Contains("is the name of the method the page itself compiles to", Assert.Single(run.Messages));
        Assert.Equal(["Home.cshtml(2,1)"], run.Locations);
        Assert.Empty(run.Generated);
    }

    /// <summary>An @functions block that collides with nothing keeps working — the check reads the
    /// block with the C# parser rather than grepping it.</summary>
    [Fact]
    public void AllowsAFunctionsBlockThatCollidesWithNothing ()
    {
        var run = CshtmlHarness.Run(new Page("Views/Home.cshtml", """
            <p>@Rendered</p>
            @functions {
                static string Rendered => "ok";
            }
            """));
        Assert.Empty(run.Diagnostics);
    }

    /// <summary>The tag-helper family is found by scanning lines rather than by a directive
    /// descriptor, so its column has to be computed rather than inherited — an indented one still
    /// points at the <c>@</c>.</summary>
    [Fact]
    public void RefusesATagHelperAtItsOwnColumn ()
    {
        var run = CshtmlHarness.Run(new Page("Views/Bad.cshtml", "<p>a</p>\n  @addTagHelper *, My.Helpers"));
        Assert.Equal(["CFW060"], run.Ids);
        Assert.Equal(["Bad.cshtml(2,3)"], run.Locations);
        Assert.Empty(run.Generated);
    }

    /// <summary>
    /// A refusal is scoped to the page that earned it.
    /// </summary>
    /// <remarks>Emission is per page, so one bad page does not take the build's other pages down with
    /// it. That is what makes the diagnostic readable in a real app: the author sees one error naming
    /// one file, rather than an error plus a cascade of "does not exist in the current context" from
    /// every handler that calls a page that stopped being generated.</remarks>
    [Fact]
    public void EmitsEveryOtherPageWhenOneIsRefused ()
    {
        var run = CshtmlHarness.Run(
            new Page("Views/Bad.cshtml", "@page \"/x\"\n<p>bad</p>"),
            new Page("Views/Good.cshtml", "<p>good</p>"));
        Assert.Equal(["CFW060"], run.Ids);
        Assert.Equal(["Bad.cshtml(1,1)"], run.Locations);
        Assert.Equal(["Views.Good.cshtml.g.cs"], run.Generated.Keys.Order());
    }

    /// <summary>
    /// One mistake, one diagnostic — refusing a construct does not also report it as a Razor error,
    /// and refusing a page does not report the constructs it never got to.
    /// </summary>
    /// <remarks>The two refusal paths run over the same document and could each claim the same line:
    /// the registered MVC directives are found in the intermediate tree, the tag-helper and layout
    /// families by scanning. A page that trips both families reports one diagnostic per mistake, each
    /// at its own line, and no CFW061 behind them.</remarks>
    [Fact]
    public void ReportsOneDiagnosticPerMistakeAndNothingBehindIt ()
    {
        var run = CshtmlHarness.Run(new Page("Views/Bad.cshtml", """
            @inject IFoo foo
            @{ Layout = "x"; }
            <p>a</p>
            """));
        Assert.Equal(["CFW060", "CFW060"], run.Ids);
        Assert.Equal(["Bad.cshtml(1,1)", "Bad.cshtml(2,4)"], run.Locations);
        Assert.Empty(run.Generated);
    }
}
