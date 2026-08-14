using Microsoft.AspNetCore.Razor.Language;

namespace Bootsharp.Cloudflare.Razor;

/// <summary>
/// The directive vocabulary of the <c>.cshtml</c> tier — the authoring contract, stated as code.
/// </summary>
/// <remarks>
/// <para>
/// <b>A page is a method.</b> <c>Views/Home.cshtml</c> compiles to
/// <c>public static partial class Home</c> in <c>&lt;RootNamespace&gt;.Views</c>, carrying
/// <c>public static void Render(HtmlWriter html, …)</c>. Everything a page needs from the outside
/// arrives as a parameter of that method, and a handler renders a page by calling it:
/// </para>
/// <code>
/// var html = new StringHtmlWriter();
/// Views.Home.Render(html, new HomeModel(…));
/// return Results.Content(html.ToString(), "text/html");
/// </code>
/// <para>
/// <b>Parameters are declared by <see cref="Model"/> and <see cref="Param"/>.</b> <c>@model T</c>
/// declares one parameter named <c>Model</c>; <c>@param T name</c> declares any number of further
/// parameters, in source order, after it.
/// </para>
/// <para>
/// <b><c>@model</c> is repurposed, not refused</b>. It is sugar for a render parameter
/// named <c>Model</c>, and that is deliberately <em>syntax</em> familiarity without MVC
/// <em>semantics</em> — there is no <c>ViewData</c>/<c>ViewBag</c> behind it, and those names simply
/// do not resolve. The trade accepted: a page pasted from an MVC app binds its model but fails
/// loudly on anything else, which beats refusing a directive we can honour cleanly. A method
/// parameter is the better half of the trade anyway — it is typed, it is checked at the call site,
/// and a page that forgets to be given something does not compile.
/// </para>
/// <para>
/// <b>Composition is a method call.</b> A partial is <c>Card.Render(html, item)</c>. A layout is a
/// page taking <c>@param HtmlBody body</c> and calling <c>@{ body(html); }</c> where the content
/// goes; the content page is passed to it as a lambda. That is the whole substitution for
/// <c>Html.Partial</c>, <c>Layout</c>, <c>RenderBody</c> and <c>@section</c>, and it is the same one
/// the <c>[HtmlTemplate]</c> tier beside this one makes.
/// </para>
/// <para>
/// <b>Why <c>@model</c> is read as <c>@Model</c>.</b> Razor matches a directive on the token
/// following <c>@</c>, so a directive named <c>model</c> makes every <c>@model.Foo</c> expression in
/// the markup parse as a second <c>@model</c> directive followed by the literal text <c>.Foo</c>
/// measured, and silent: the value vanishes and the text leaks into the markup. Directives are
/// case-sensitive, so <c>@model Foo</c> declares and <c>@Model.Foo</c> reads. MVC's own convention
/// turns out to be forced by the same constraint. <c>@param</c> is safe from it because nobody
/// writes <c>@param.Something</c>.
/// </para>
/// <para>
/// <b>A code block alone on its line takes the line's whitespace with it.</b> Razor elides both the
/// leading indentation before a code block and the newline after it, so
/// <c>@if (x) {&lt;p&gt;…&lt;/p&gt;}</c> written on its own line emits the <c>&lt;p&gt;</c> and
/// nothing around it, while the same markup written as an implicit expression
/// <c>@Banner.Notice(…)</c>, a method returning <c>Bootsharp.Cloudflare.AspNetCore.Html.HtmlString</c>
/// leaves the surrounding whitespace exactly as authored. Neither is wrong and the difference is
/// inert in a browser, but it is a real difference in the bytes on the wire: it is measured at five
/// bytes per render for one such line. When the exact output matters — a golden-file test, a page
/// being converted from another tier and diffed against it — reach for the expression form. When it
/// does not, the block form is cheaper, because a fragment is built by the runtime template handler
/// and roots the request-time scanner that a compiled <c>@if</c> does not need.
/// </para>
/// <para>
/// <b>Why the MVC directives are registered at all.</b> <c>@inject</c>, <c>@page</c> and
/// <c>@section</c> are not Razor-language directives — they belong to the MVC extension this
/// generator never registers. Left unregistered they do not fail: they parse as an implicit
/// expression plus a run of literal text, which is how <c>@inject IFoo Foo</c> becomes
/// <c>html.WriteText(inject)</c> and a page renders the wrong thing. Registering them is what buys
/// the right to reject them by name, at their own line, with the substitution to use.
/// </para>
/// </remarks>
internal static class CshtmlDirectives
{
    /// <summary><c>@model T</c> — the page's model, bound to the <c>Model</c> parameter.</summary>
    public static readonly DirectiveDescriptor Model = DirectiveDescriptor.CreateDirective(
        "model", DirectiveKind.SingleLine, builder => {
            builder.AddTypeToken("TModel", "The model type.");
            builder.Description = "Declares the page model as a Render parameter; read it as @Model.";
        });

    /// <summary><c>@param T name</c> — a further <c>Render</c> parameter, in declaration order.</summary>
    public static readonly DirectiveDescriptor Param = DirectiveDescriptor.CreateDirective(
        "param", DirectiveKind.SingleLine, builder => {
            builder.Usage = DirectiveUsage.FileScopedMultipleOccurring;
            builder.AddTypeToken("TParam", "The parameter type.");
            builder.AddMemberToken("name", "The parameter name.");
            builder.Description = "Declares an additional Render parameter, after the model.";
        });

    /// <summary>The MVC directives, registered so they can be refused by name rather than mis-parsed.</summary>
    public static readonly IReadOnlyList<RefusedDirective> Unsupported =
    [
        new(DirectiveDescriptor.CreateDirective("inject", DirectiveKind.SingleLine, builder => {
            builder.Usage = DirectiveUsage.FileScopedMultipleOccurring;
            builder.AddTypeToken();
            builder.AddMemberToken();
        }), "@inject is not supported; there is no view service locator on this tier — declare the " +
            "dependency as '@param IFoo foo' and pass it from the handler that renders the page"),
        new(DirectiveDescriptor.CreateDirective("page", DirectiveKind.SingleLine, builder => {
            builder.AddOptionalStringToken();
        }), "@page is not supported; routing lives in the worker's endpoint map, not in the view"),
        new(DirectiveDescriptor.CreateDirective("section", DirectiveKind.RazorBlock, builder => {
            builder.Usage = DirectiveUsage.FileScopedMultipleOccurring;
            builder.AddMemberToken();
        }), "@section is not supported; compose layouts by giving the layout page a " +
            "'@param HtmlBody body' and calling body(html) where the section would go")
    ];

    /// <summary>
    /// The tag-helper directives, refused by a scan of the page text rather than by a descriptor.
    /// </summary>
    /// <remarks>Their argument is an unquoted assembly pattern (<c>*, My.Assembly</c>) that no public
    /// <c>DirectiveTokenKind</c> in 6.0.36 accepts, so a registered descriptor rejects the right line
    /// for the wrong reason ("expects a string surrounded by double quotes"). The scan gets the
    /// message right, and on a refusal the message is the whole point.</remarks>
    public static readonly IReadOnlyList<string> Scanned =
        ["addTagHelper", "removeTagHelper", "tagHelperPrefix"];

    public const string TagHelperMessage =
        "tag helpers are not supported; there is no ITagHelper runtime on this tier — write the " +
        "markup out, or move the logic into a partial page called as Name.Render(html, …)";

    /// <summary>
    /// Members of MVC's <c>RazorPageBase</c> that express a layout. They are ordinary C# names here
    /// and would fail as CS0103 at the right line but with a message that explains nothing.
    /// </summary>
    public static readonly IReadOnlyList<(string Name, string Message)> LayoutMembers =
    [
        ("Layout", "Layout is not supported; a layout is an ordinary page here — give it a " +
            "'@param HtmlBody body', call body(html) where the content goes, and render it as " +
            "Layout.Render(html, html => Home.Render(html, model))"),
        ("RenderBody", "RenderBody() is not supported; a layout receives its content as a " +
            "'@param HtmlBody body' and writes it with body(html)"),
        ("RenderSection", "RenderSection() is not supported; pass one HtmlBody parameter per slot " +
            "instead of naming sections"),
        ("IsSectionDefined", "IsSectionDefined() is not supported; a slot a page may not fill is a " +
            "nullable HtmlBody parameter, tested with an ordinary null check")
    ];

    public static void Register (RazorProjectEngineBuilder builder)
    {
        builder.AddDirective(Model);
        builder.AddDirective(Param);
        foreach (var refused in Unsupported)
            builder.AddDirective(refused.Directive);
    }
}

/// <summary>A directive that parses, and the reason it is nonetheless refused.</summary>
internal sealed record RefusedDirective (DirectiveDescriptor Directive, string Message);
