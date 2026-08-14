namespace Bootsharp.Cloudflare.Razor.Tests;

/// <summary>
/// Control flow, which on this tier is not a feature but an absence: the page is a method body, so
/// C#'s own statements are already there and the markup between them is a write.
/// </summary>
/// <remarks>
/// The property worth asserting is that nothing intervenes. MVC buffers a view's output so that a
/// layout can interleave it, and Blazor builds a render tree so a diff can be taken; both mean a
/// loop's markup goes somewhere before it goes out. Here <c>@foreach</c> is <c>foreach</c> and the
/// body writes straight into the caller's writer, which is what makes the compiled page a
/// straight-line program and what
/// <see cref="EmitsControlFlowAsPlainCSharpWithNothingInBetween"/> pins.
/// </remarks>
public class CshtmlControlFlowTests
{
    private const string page = "Views/Home.cshtml";
    private const string type = "App.Views.Home";

    [Theory]
    [InlineData(1, " <i>pos</i> ")]
    [InlineData(-1, " <i>neg</i> ")]
    public void RendersAnIfElse (int value, string expected)
    {
        Assert.Equal(expected, CshtmlHarness.Render(type, [value],
            new Page(page, "@param int n\n@if (n > 0) { <i>pos</i> } else { <i>neg</i> }")));
    }

    [Fact]
    public void RendersAFor ()
    {
        Assert.Equal(" <i>0</i>  <i>1</i>  <i>2</i> ", CshtmlHarness.Render(type, [],
            new Page(page, "@for (var n = 0; n < 3; n++) { <i>@n</i> }")));
    }

    [Fact]
    public void RendersAForeach ()
    {
        Assert.Equal("<ul><li>a</li><li>b</li></ul>", CshtmlHarness.Render(type, [new[] { "a", "b" }],
            new Page(page, "@model string[]\n<ul>@foreach (var item in Model) {<li>@item</li>}</ul>")));
    }

    [Fact]
    public void RendersAWhile ()
    {
        Assert.Equal(" <i>0</i>  <i>1</i> ", CshtmlHarness.Render(type, [],
            new Page(page, "@{ var n = 0; }@while (n < 2) { <i>@n</i> n++; }")));
    }

    [Theory]
    [InlineData(1, " <i>one</i> ")]
    [InlineData(2, " <i>other</i> ")]
    public void RendersASwitch (int value, string expected)
    {
        Assert.Equal(expected, CshtmlHarness.Render(type, [value],
            new Page(page, "@param int n\n@switch (n) { case 1: <i>one</i> break; default: <i>other</i> break; }")));
    }

    [Fact]
    public void RendersATryCatch ()
    {
        Assert.Equal(" <i>a</i> ", CshtmlHarness.Render(type, [],
            new Page(page, "@try { <i>a</i> } catch { <i>b</i> }")));
    }

    /// <summary>A <c>@using</c> <em>statement</em>, which shares its keyword with the import directive
    /// and is told apart by the parenthesis.</summary>
    [Fact]
    public void RendersAUsingStatement ()
    {
        Assert.Equal(" <i>a</i> ", CshtmlHarness.Render(type, [],
            new Page(page, "@using (var stream = new System.IO.MemoryStream()) { <i>a</i> }")));
    }

    /// <summary>Markup nests inside control flow inside markup to any depth, because none of it is a
    /// structure — it is statements and writes in one method.</summary>
    [Fact]
    public void RendersNestedControlFlow ()
    {
        Assert.Equal("<ul> <li>a</li>  <li> <b>bb</b> </li> </ul>",
            CshtmlHarness.Render(type, [new[] { "a", "bb" }], new Page(page, """
                @model string[]
                <ul>@foreach (var row in Model) { <li>@if (row.Length > 1) { <b>@row</b> } else { @row }</li> }</ul>
                """)));
    }

    /// <summary>The branch not taken writes nothing, which is only worth stating because a tier that
    /// buffered could get it wrong: both literals are in the emitted method, and one of them runs.</summary>
    [Fact]
    public void WritesOnlyTheBranchThatRuns ()
    {
        var markup = "@param bool on\n@if (on) { <b>yes</b> } else { <i>no</i> }";
        var run = CshtmlHarness.Run(new Page(page, markup));
        Assert.Contains("""html.WriteLiteral(" <b>yes</b> ");""", run.Writes);
        Assert.Contains("""html.WriteLiteral(" <i>no</i> ");""", run.Writes);
        Assert.Equal(" <b>yes</b> ", CshtmlHarness.Render(type, [true], new Page(page, markup)));
    }

    /// <summary>The statements reach the generated method verbatim, with no buffer, no render tree
    /// and no builder between the loop and the response.</summary>
    [Fact]
    public void EmitsControlFlowAsPlainCSharpWithNothingInBetween ()
    {
        var run = CshtmlHarness.Run(new Page(page, """
            @model string[]
            @foreach (var row in Model)
            {
                @if (row.Length > 0) { <b>@row</b> }
            }
            """));
        Assert.Empty(run.Diagnostics);
        Assert.Contains("foreach (var row in Model)", run.Code);
        Assert.Contains("if (row.Length > 0)", run.Code);
        foreach (var machinery in new[] {
                     "PushWriter", "PopWriter", "BeginWriteAttribute", "EndWriteAttribute",
                     "WriteAttributeValue", "RenderTreeBuilder", "OpenElement", "StringWriter"
                 })
            Assert.DoesNotContain(machinery, run.Code);
    }
}
