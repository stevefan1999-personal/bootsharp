using Bootsharp.Cloudflare.AspNetCore.Html;

namespace Bootsharp.Cloudflare.Razor.Tests;

/// <summary>
/// The end-to-end claim of the tier: a <c>.cshtml</c> becomes a method that writes correct HTML into
/// the shipped <see cref="HtmlWriter"/>, and nothing of ASP.NET Core MVC survives the trip.
/// </summary>
public class CshtmlCompilationTests
{
    [Fact]
    public void CompilesAPageEndToEnd ()
    {
        var html = CshtmlHarness.Render("App.Views.Home", ["Bootsharp", new[] { "one", "two" }],
            new Page("Views/Home.cshtml", """
                @model string
                @param string[] items
                <h1 class="title">Hello @Model</h1>
                <ul>
                @foreach (var item in items)
                {
                    <li class="row @(item.Length > 3 ? "long" : "short")">@item</li>
                }
                </ul>
                <p>@Suffix</p>
                @functions {
                    static string Suffix => "done";
                }
                """));
        Assert.Equal("""
            <h1 class="title">Hello Bootsharp</h1>
            <ul>
                <li class="row short">one</li>
                <li class="row short">two</li>
            </ul>
            <p>done</p>
            """, html.Trim());
    }

    /// <summary>
    /// The whole point of retargeting rather than reimplementing: what the compiler emits is calls on
    /// this repo's writer, not on a runtime that does not exist for this target.
    /// </summary>
    [Fact]
    public void EmitsAgainstTheShippedWriterAndNothingElse ()
    {
        var run = CshtmlHarness.Run(new Page("Views/Home.cshtml", """
            @model string
            <a href="@Model" title="@Model">@Model</a>
            """));
        Assert.Empty(run.Diagnostics);
        Assert.Contains("public static void Render(global::Bootsharp.Cloudflare.AspNetCore.Html.HtmlWriter html, " +
                        "string Model)", run.Code);
        foreach (var mvc in new[] {
                     "Microsoft.AspNetCore.Mvc", "RazorPage", "RazorInject", "RazorCompiledItem",
                     "RazorSourceChecksum", "IHtmlHelper", "ViewData", "ExecuteAsync"
                 })
            Assert.DoesNotContain(mvc, run.Code);
    }

    /// <summary>
    /// Which sink a hole gets is decided here, at compile time, from the markup around it — which is
    /// both why the tier needs no runtime scanner and why an author cannot forget to encode.
    /// </summary>
    [Fact]
    public void ChoosesTheEncodingSinkFromTheSurroundingMarkup ()
    {
        var run = CshtmlHarness.Run(new Page("Views/Home.cshtml", """
            @model string
            <a href="@Model" title="@Model">@Model</a>
            """));
        Assert.Contains("html.WriteUrl(Model);", run.Writes);
        Assert.Contains("html.WriteAttribute(Model);", run.Writes);
        Assert.Contains("html.WriteText(Model);", run.Writes);
    }

    [Fact]
    public void EncodesEveryHoleForItsContext ()
    {
        var html = CshtmlHarness.Render("App.Views.Home", ["<script>alert('x')</script> & \"friends\""],
            new Page("Views/Home.cshtml", """
                @model string
                <p title="@Model">@Model</p>
                """));
        Assert.Equal(
            "<p title=\"&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt; &amp; &quot;friends&quot;\">" +
            "&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt; &amp; &quot;friends&quot;</p>",
            html.Trim());
    }

    /// <summary>The one attack attribute encoding cannot stop, stopped by the repo's own encoder: a
    /// <c>javascript:</c> payload never has to leave the attribute to run.</summary>
    [Fact]
    public void RefusesAJavascriptUrlRatherThanEscapingIt ()
    {
        var html = CshtmlHarness.Render("App.Views.Home", ["javascript:alert(1)"],
            new Page("Views/Home.cshtml", """
                @model string
                <a href="@Model">go</a>
                """));
        Assert.Equal("<a href=\"about:invalid\">go</a>", html.Trim());
    }

    /// <summary><see cref="HtmlString"/> stays the single, greppable escape hatch — and it is in scope
    /// without an <c>@using</c>, because the generated namespace imports the writer's own.</summary>
    [Fact]
    public void WritesHtmlStringVerbatim ()
    {
        var html = CshtmlHarness.Render("App.Views.Home", [],
            new Page("Views/Home.cshtml", """
                <section>@(new HtmlString("<strong>trusted</strong>"))</section>
                """));
        Assert.Equal("<section><strong>trusted</strong></section>", html.Trim());
    }

    /// <summary>
    /// The substitution the tier makes for layouts, partials and sections all at once: composition is
    /// a method call, and a slot is an <see cref="HtmlBody"/> parameter.
    /// </summary>
    [Fact]
    public void ComposesALayoutThroughAnHtmlBodyParameter ()
    {
        var html = CshtmlHarness.Render("App.Views.Layout",
            ["Title", new HtmlBody(writer => writer.WriteText("<content>"))],
            new Page("Views/Layout.cshtml", """
                @param string title
                @param HtmlBody body
                <title>@title</title>
                <main>@{ body(html); }</main>
                """));
        Assert.Equal("<title>Title</title>\n<main>&lt;content&gt;</main>", html.Trim());
    }

    /// <summary>A page's type is its folder plus its file name, which is what lets two pages share a
    /// name; the leading underscore of a partial is a file-naming habit and nothing more.</summary>
    [Fact]
    public void DerivesTheNamespaceFromTheFolderAndTheNameFromTheFile ()
    {
        var run = CshtmlHarness.Run(
            new Page("Views/Home.cshtml", "<p>a</p>"),
            new Page("Views/Admin/Home.cshtml", "<p>b</p>"),
            new Page("Views/Shared/_Card.cshtml", "<p>c</p>"));
        Assert.Empty(run.Diagnostics);
        Assert.Contains("namespace App.Views", run.Code);
        Assert.Contains("namespace App.Views.Admin", run.Code);
        Assert.Contains("namespace App.Views.Shared", run.Code);
        Assert.Contains("class Card", run.Code);
        Assert.Equal(
            ["Views.Admin.Home.cshtml.g.cs", "Views.Home.cshtml.g.cs", "Views.Shared._Card.cshtml.g.cs"],
            run.Generated.Keys.Order());
    }

    /// <summary>A partial is a method call, which is the whole of the substitution for
    /// <c>Html.Partial</c> — and the reason a page needs no view engine to find one.</summary>
    [Fact]
    public void CallsAPartialAsAMethod ()
    {
        var html = CshtmlHarness.Render("App.Views.Home", [new[] { "a", "b" }],
            new Page("Views/Home.cshtml", """
                @model string[]
                <ul>@foreach (var item in Model) { Card.Render(html, item); }</ul>
                """),
            new Page("Views/_Card.cshtml", """
                @model string
                <li>@Model</li>
                """));
        Assert.Equal("<ul><li>a</li><li>b</li></ul>", html.Trim());
    }

    /// <summary>A page maps back to its own source, so a C# error inside a hole reports at the line
    /// the author wrote it on rather than somewhere in generated code.</summary>
    [Fact]
    public void MapsGeneratedCodeBackToThePage ()
    {
        var run = CshtmlHarness.Run(new Page("Views/Home.cshtml", """
            @model string
            <p title="@Model">@Model</p>
            """));
        Assert.Contains("#line 2 \"Views/Home.cshtml\"", run.Code);
    }
}
