using System.Reflection;
using Bootsharp.Cloudflare.AspNetCore.Html;

namespace Bootsharp.Cloudflare.Razor.Tests;

/// <summary>
/// The directives this tier keeps, and the shape they compile a page into.
/// </summary>
/// <remarks>
/// The authoring contract is "a page is a method", so most of these assertions are about the method:
/// what its parameters are called, in what order they arrive, and what the class around it is. They
/// are read off the loaded type rather than off the emitted text, because the call site the contract
/// promises is a C# call — a page whose <c>Render</c> merely <em>looks</em> right in generated source
/// and binds differently is the failure worth catching.
/// </remarks>
public class CshtmlDirectiveTests
{
    private const string page = "Views/Home.cshtml";
    private const string type = "App.Views.Home";

    /// <summary>A page is a public static class with one static void <c>Render</c> taking the writer
    /// first — the shape a worker handler calls and the shape the golden output pins.</summary>
    [Fact]
    public void CompilesAPageToAStaticRenderMethod ()
    {
        var compiled = CshtmlHarness.Load(type, new Page(page, "<p>a</p>"));
        Assert.True(compiled.IsPublic);
        // A static class is abstract and sealed in metadata; there is no other flag for it.
        Assert.True(compiled is { IsAbstract: true, IsSealed: true });
        // No base type is the seam: MVC's classifier assigns RazorPage<TModel> here, and this one
        // assigns nothing, which is what keeps the page free of a runtime that has no build for wasm.
        Assert.Equal(typeof(object), compiled.BaseType);
        var render = compiled.GetMethod("Render")!;
        Assert.True(render is { IsPublic: true, IsStatic: true });
        Assert.Equal(typeof(void), render.ReturnType);
        var writer = Assert.Single(render.GetParameters());
        Assert.Equal(("html", typeof(HtmlWriter)), (writer.Name, writer.ParameterType));
    }

    /// <summary>
    /// <c>@model</c> is a render parameter named <c>Model</c>, and <c>@param</c> adds more.
    /// </summary>
    /// <remarks>The capital is forced rather than chosen: Razor matches a directive on the token after
    /// <c>@</c>, so a lowercase <c>Model</c> would make every <c>@model.Foo</c> in the markup parse as
    /// a second <c>@model</c> directive followed by the literal text <c>.Foo</c>. MVC's own convention
    /// turns out to be the same constraint.</remarks>
    [Fact]
    public void DeclaresRenderParametersFromModelAndParam ()
    {
        var render = CshtmlHarness.Load(type, new Page(page, """
            @model string
            @param int count
            @param HtmlBody body
            <p>@Model @count</p>
            """)).GetMethod("Render")!;
        Assert.Equal([("html", typeof(HtmlWriter)), ("Model", typeof(string)),
            ("count", typeof(int)), ("body", typeof(HtmlBody))],
            [.. render.GetParameters().Select(static p => (p.Name, p.ParameterType))]);
    }

    /// <summary>Source order, not directive kind: a <c>@param</c> written above the <c>@model</c>
    /// arrives above it, so the page reads the way the call site does.</summary>
    [Fact]
    public void KeepsParametersInSourceOrder ()
    {
        Assert.Equal("<p>1m</p>", CshtmlHarness.Render(type, [1, "m"],
            new Page(page, "@param int a\n@model string\n<p>@a@Model</p>")));
    }

    [Fact]
    public void DeclaresAGenericParameterType ()
    {
        Assert.Equal("<p>2</p>", CshtmlHarness.Render(type, [new List<string> { "a", "b" }],
            new Page(page, "@param System.Collections.Generic.List<string> items\n<p>@items.Count</p>")));
    }

    /// <summary><c>@using</c> is a Razor-language directive, not an MVC one, so it survives — and it
    /// lands inside the generated namespace where the page's holes can see it.</summary>
    [Fact]
    public void ImportsANamespaceWithUsing ()
    {
        Assert.Equal("<p>iv</p>", CshtmlHarness.Render(type, [], new Page(page, """
            @using System.Globalization
            <p>@CultureInfo.InvariantCulture.TwoLetterISOLanguageName</p>
            """)));
    }

    /// <summary>The writer's own namespace is imported into every page, so <c>HtmlBody</c> and
    /// <c>HtmlString</c> need no <c>@using</c> line — which matters because there is no
    /// <c>_ViewImports</c> on this tier to put one in.</summary>
    [Fact]
    public void ImportsTheWriterNamespaceWithoutBeingAsked ()
    {
        var run = CshtmlHarness.Run(new Page(page, "<p>a</p>"));
        Assert.Contains("using Bootsharp.Cloudflare.AspNetCore.Html;", run.Code);
    }

    /// <summary><c>@attribute</c> applies to the generated class, which is the seam for anything that
    /// is addressed by attribute — trimming roots, analyzer suppressions, an app's own marker.</summary>
    [Fact]
    public void AppliesAnAttributeDirectiveToTheGeneratedClass ()
    {
        var compiled = CshtmlHarness.Load(type,
            new Page(page, "@attribute [System.Obsolete(\"moved\")]\n<p>a</p>"));
        Assert.Equal("moved", compiled.GetCustomAttribute<ObsoleteAttribute>()!.Message);
    }

    /// <summary>An <c>@functions</c> block is members on the page's class: methods, properties, fields
    /// and nested types, all reachable from the markup above them.</summary>
    [Fact]
    public void CarriesFunctionsMembersOntoTheGeneratedClass ()
    {
        Assert.Equal("<p>t 3 n</p>\n", CshtmlHarness.Render(type, [], new Page(page, """
            <p>@Tag @Count @Nested.Name</p>
            @functions {
                static string Tag => "t";
                static int Count = 3;
                static class Nested { public const string Name = "n"; }
            }
            """)));
    }

    /// <summary>A helper in <c>@functions</c> can write markup of its own, which is how a page factors
    /// out a repeated fragment without a second file.</summary>
    [Fact]
    public void CallsAFunctionsHelperThatWritesToTheSameWriter ()
    {
        Assert.Equal("<ul><li>a</li><li>b</li></ul>\n", CshtmlHarness.Render(type, [], new Page(page, """
            <ul>@{ Row(html, "a"); Row(html, "b"); }</ul>
            @functions {
                static void Row (HtmlWriter html, string text) => html.Write($"<li>{text}</li>");
            }
            """)));
    }

    /// <summary>The class is <c>partial</c>, so a page can carry hand-written members in an ordinary
    /// <c>.cs</c> file beside it rather than in an <c>@functions</c> block.</summary>
    [Fact]
    public void DeclaresThePageClassPartial ()
    {
        var run = CshtmlHarness.Run(new Page(page, "<p>a</p>"));
        Assert.Contains("public static partial class Home", run.Code);
    }
}
