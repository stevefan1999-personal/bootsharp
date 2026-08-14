using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.CodeGeneration;
using Microsoft.AspNetCore.Razor.Language.Intermediate;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Bootsharp.Cloudflare.Razor;

/// <summary>
/// Seam one of the three the Razor compiler exposes: the pass that decides what a page compiles to.
/// </summary>
/// <remarks>
/// <para>
/// The whole MVC shape of <c>.cshtml</c> is set by a classifier pass — <c>MvcViewDocumentClassifierPass</c>
/// assigns the string <c>"global::Microsoft.AspNetCore.Mvc.Razor.RazorPage&lt;TModel&gt;"</c> as the
/// class's base type and renames the primary method to <c>ExecuteAsync</c>, and that is all the
/// coupling there is. Blazor replaces the same seam with its own pass at <c>Order = -100</c>; this
/// one claims the document at <c>-200</c>, ahead of both, and assigns no base type at all.
/// </para>
/// <para>
/// What comes out is a free function: <c>public static partial class Page</c> with
/// <c>public static void Render(HtmlWriter html, …)</c>. Nothing it emits references an assembly the
/// worker does not already have, which is the entire reason this tier exists.
/// </para>
/// </remarks>
internal sealed class CshtmlClassifierPass (string pageNamespace, string className, string writerType)
    : DocumentClassifierPassBase
{
    /// <summary>The name of the writer parameter, and therefore the receiver every emitted call uses.</summary>
    public const string WriterParameter = "html";

    /// <summary>The name <c>@model</c> binds, capitalised — see <see cref="CshtmlDirectives"/> for why
    /// this is a correctness constraint rather than a style choice.</summary>
    public const string ModelParameter = "Model";

    /// <summary>The generated method. A page is this method and nothing else.</summary>
    public const string RenderMethod = "Render";

    /// <summary>Always in scope in a page, so that <c>@param HtmlBody body</c> and
    /// <c>@(new HtmlString(…))</c> need no <c>@using</c> line of their own.</summary>
    private const string htmlNamespace = "Bootsharp.Cloudflare.AspNetCore.Html";

    protected override string DocumentKind => "bootsharp.html.writer.1.0";

    /// <summary>Ahead of the Components classifier (-100) and the MVC classifiers (0), the two other
    /// passes that would otherwise claim a <c>.cshtml</c> and impose their own base type.</summary>
    public override int Order => -200;

    /// <summary>Everything this pass declined to compile, surfaced by the generator as diagnostics.</summary>
    public List<Refusal> Refusals { get; } = [];

    private DocumentIntermediateNode? document;

    protected override bool IsMatch (RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode) => true;

    protected override CodeTarget CreateTarget (RazorCodeDocument codeDocument, RazorCodeGenerationOptions options) =>
        new CshtmlCodeTarget(TargetExtensions);

    /// <summary>
    /// The pass that hands this one the document node.
    /// </summary>
    /// <remarks><see cref="OnDocumentStructureCreated"/> receives the namespace, class and method but
    /// not the document, and the directives live on the document; <c>ExecuteCore</c> is sealed on the
    /// base. A second classifier pass ordered just ahead captures it — both run in the same phase, in
    /// <c>Order</c>, so the capture has always happened by the time this pass classifies.</remarks>
    public IRazorDocumentClassifierPass Capture => new CapturePass(this);

    private sealed class CapturePass (CshtmlClassifierPass owner) : IntermediateNodePassBase, IRazorDocumentClassifierPass
    {
        public override int Order => -300;

        protected override void ExecuteCore (RazorCodeDocument codeDocument, DocumentIntermediateNode documentNode) =>
            owner.document = documentNode;
    }

    protected override void OnDocumentStructureCreated (RazorCodeDocument codeDocument,
        NamespaceDeclarationIntermediateNode @namespace, ClassDeclarationIntermediateNode @class,
        MethodDeclarationIntermediateNode method)
    {
        @namespace.Content = pageNamespace;
        @namespace.Children.Insert(0, new UsingDirectiveIntermediateNode { Content = htmlNamespace });

        @class.ClassName = className;
        // No base type: MVC's RazorPageBase is a string this pass simply does not write.
        @class.BaseType = null;
        // Partial so a page can carry hand-written members in an ordinary .cs file beside it; static
        // because a page holds no state — everything it renders from arrives as a parameter.
        Replace(@class.Modifiers, "public", "static", "partial");

        method.MethodName = RenderMethod;
        method.ReturnType = "void";
        Replace(method.Modifiers, "public", "static");
        method.Parameters.Clear();
        method.Parameters.Add(new MethodParameter { ParameterName = WriterParameter, TypeName = writerType });
        foreach (var parameter in Parameters())
            method.Parameters.Add(parameter);

        RefuseDirectives();
        RefuseLayoutMembers();
        RefuseCollisions(method);
    }

    /// <summary>Turns <c>@model T</c> and <c>@param T name</c> into <c>Render</c> parameters, in source order.</summary>
    private IEnumerable<MethodParameter> Parameters ()
    {
        foreach (var node in Directives(CshtmlDirectives.Model.Directive, CshtmlDirectives.Param.Directive))
        {
            var tokens = node.Tokens.ToList();
            var model = node.Directive.Directive == CshtmlDirectives.Model.Directive;
            if (tokens.Count < (model ? 1 : 2)) continue;
            yield return new MethodParameter {
                TypeName = tokens[0].Content,
                ParameterName = model ? ModelParameter : tokens[1].Content
            };
        }
    }

    /// <summary>Rejects the MVC directives, and the two language-level ones a static class cannot carry.</summary>
    private void RefuseDirectives ()
    {
        foreach (var refused in CshtmlDirectives.Unsupported)
            foreach (var node in Directives(refused.Directive.Directive))
                Refusals.Add(new Refusal(refused.Message, node.Source));
        // Registered by the Razor language itself rather than by us, and structurally impossible
        // here: the page class is static, so it can carry neither a base type nor an interface. Left
        // alone they emit `public static partial class X : Base` and fail with CS0714/CS0535 pointing
        // at generated code instead of at the page.
        foreach (var node in Directives("inherits", "implements"))
            Refusals.Add(new Refusal(
                $"@{node.Directive.Directive} is not supported; a page compiles to a static class " +
                "with no base type — share code through ordinary static helpers or an @functions block",
                node.Source));
    }

    /// <summary>
    /// Rejects the layout members of MVC's <c>RazorPageBase</c>.
    /// </summary>
    /// <remarks>Scanned over the page's C# tokens rather than its raw text, so a mention inside
    /// literal markup or an HTML comment is not a match. These names resolve to nothing here, so the
    /// build fails either way; what the refusal buys is a message that names the substitution instead
    /// of CS0103. A page that legitimately declares its own <c>RenderBody</c> helper is the cost, and
    /// it is paid by renaming the helper.</remarks>
    private void RefuseLayoutMembers ()
    {
        foreach (var (token, content) in CSharpTokens())
            foreach (var (name, message) in CshtmlDirectives.LayoutMembers)
                foreach (Match match in Regex.Matches(content, $@"(?<![.\w]){Regex.Escape(name)}\s*[=(]"))
                    Refusals.Add(new Refusal(message, Offset(token.Source, content, match.Index, name.Length)));
    }

    /// <summary>
    /// Rejects declarations that would land twice on the generated class.
    /// </summary>
    /// <remarks>Decidable from the page alone, which is why it is checked here: a duplicate
    /// <c>@param</c>, a parameter shadowing the writer, or an <c>@functions</c> member named
    /// <c>Render</c> all become a C# error whose only location is inside a generated file. A
    /// collision with a hand-written partial beside the page is deliberately not checked — that file
    /// exists, so CS0111 names both halves and reads fine.</remarks>
    private void RefuseCollisions (MethodDeclarationIntermediateNode method)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in method.Parameters)
            if (!seen.Add(parameter.ParameterName))
                Refusals.Add(new Refusal(CshtmlDiagnostics.Collision,
                    $"'{parameter.ParameterName}' is declared twice; Render takes the writer as " +
                    $"'{WriterParameter}', the @model as '{ModelParameter}', and one parameter per " +
                    "@param — each name once",
                    ParameterSource(parameter.ParameterName)));
        foreach (var node in Directives("functions"))
        foreach (var member in Members(node))
            if (member == RenderMethod)
                Refusals.Add(new Refusal(CshtmlDiagnostics.Collision,
                    $"an @functions block declares '{RenderMethod}', which is the name of the method " +
                    "the page itself compiles to; rename the member",
                    node.Source));
    }

    /// <summary>The span of the directive that declared a parameter, for the collision message.</summary>
    private SourceSpan? ParameterSource (string name)
    {
        foreach (var node in Directives(CshtmlDirectives.Model.Directive, CshtmlDirectives.Param.Directive))
        {
            var tokens = node.Tokens.ToList();
            var declared = node.Directive.Directive == CshtmlDirectives.Model.Directive
                ? ModelParameter
                : tokens.Count > 1 ? tokens[1].Content : null;
            if (declared == name) return node.Source;
        }
        return null;
    }

    /// <summary>Names declared by an <c>@functions</c> block, read with the C# parser rather than a
    /// regex: the block is C#, and the compiler that will host this generator can already parse it.</summary>
    private static IEnumerable<string> Members (IntermediateNode functions)
    {
        var code = string.Concat(functions.Children.OfType<CSharpCodeIntermediateNode>()
            .SelectMany(node => node.Children.OfType<IntermediateToken>())
            .Where(token => token.IsCSharp).Select(token => token.Content));
        if (string.IsNullOrWhiteSpace(code)) yield break;
        var tree = CSharpSyntaxTree.ParseText($"class __Page {{{code}}}");
        var declaration = tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (declaration is null) yield break;
        foreach (var member in declaration.Members)
            switch (member)
            {
                case MethodDeclarationSyntax syntax: yield return syntax.Identifier.Text; break;
                case PropertyDeclarationSyntax syntax: yield return syntax.Identifier.Text; break;
                case TypeDeclarationSyntax syntax: yield return syntax.Identifier.Text; break;
                case FieldDeclarationSyntax syntax:
                    foreach (var variable in syntax.Declaration.Variables)
                        yield return variable.Identifier.Text;
                    break;
            }
    }

    /// <summary>Every C# token of the page, paired with its text.</summary>
    private IEnumerable<(IntermediateToken Token, string Content)> CSharpTokens ()
    {
        foreach (var node in Walk())
            if (node is IntermediateToken { IsCSharp: true } token && !string.IsNullOrEmpty(token.Content))
                yield return (token, token.Content);
    }

    /// <summary>Matched by directive name rather than by descriptor identity: the language's own
    /// directives are registered by the engine, and we never hold their descriptor instances.</summary>
    private IEnumerable<DirectiveIntermediateNode> Directives (params string[] wanted)
    {
        foreach (var node in Walk())
            if (node is DirectiveIntermediateNode directive &&
                Array.IndexOf(wanted, directive.Directive?.Directive) >= 0)
                yield return directive;
    }

    private IEnumerable<IntermediateNode> Walk () => document is null ? [] : Descend(document);

    private static IEnumerable<IntermediateNode> Descend (IntermediateNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Descend(child))
                yield return descendant;
    }

    /// <summary>Narrows a token's span to a match inside it, so the caret lands on the name rather
    /// than on the whole code block that contains it.</summary>
    private static SourceSpan? Offset (SourceSpan? span, string content, int index, int length)
    {
        if (span is not { } source) return null;
        var preceding = content.Substring(0, index);
        var lines = preceding.Count(character => character == '\n');
        var lastBreak = preceding.LastIndexOf('\n');
        var character = lastBreak < 0 ? source.CharacterIndex + index : index - lastBreak - 1;
        return new SourceSpan(source.FilePath, source.AbsoluteIndex + index,
            source.LineIndex + lines, character, length);
    }

    private static void Replace (IList<string> modifiers, params string[] values)
    {
        modifiers.Clear();
        foreach (var value in values) modifiers.Add(value);
    }
}

/// <summary>A construct the classifier declined, and where the page declared it.</summary>
/// <remarks>The descriptor travels with the refusal rather than being chosen by the reporter: a
/// construct that has no meaning here and a name declared twice are different mistakes, and a user
/// who documents or greps one of them by id should not be shown the other.</remarks>
internal sealed record Refusal (DiagnosticDescriptor Descriptor, string Message, SourceSpan? Source)
{
    public Refusal (string message, SourceSpan? source)
        : this(CshtmlDiagnostics.Refused, message, source) { }
}

/// <summary>Seam two: the code target, whose only job is to hand back seam three.</summary>
internal sealed class CshtmlCodeTarget (IReadOnlyList<ICodeTargetExtension> extensions) : CodeTarget
{
    public override IntermediateNodeWriter CreateNodeWriter () => new CshtmlNodeWriter();
    public override TExtension GetExtension<TExtension> () => extensions.OfType<TExtension>().FirstOrDefault();
    public override bool HasExtension<TExtension> () => GetExtension<TExtension>() != null;
}
