using Bootsharp.Cloudflare.AspNetCore.Html;
using Bootsharp.Cloudflare.Generate.MinimalApi;
using Bootsharp.Cloudflare.Projection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Bootsharp.Cloudflare.Generate.Html;

/// <summary>
/// Reads a <c>[HtmlTemplate]</c> method and compiles its markup.
/// </summary>
/// <remarks>
/// The whole of the compile-time half: it takes the one interpolated string the method's body is,
/// runs <see cref="HtmlContextScanner"/> over the literal segments — the same scanner the runtime
/// handler runs, linked by source so the two cannot drift — and records which sink each hole
/// resolved to. Everything after this is text formatting.
/// </remarks>
internal static class HtmlTemplateResolver
{
    public const string TemplateAttribute = "Bootsharp.Cloudflare.AspNetCore.Html.HtmlTemplateAttribute";
    private const string writerType = "Bootsharp.Cloudflare.AspNetCore.Html.HtmlWriter";
    private const string stringType = "Bootsharp.Cloudflare.AspNetCore.Html.HtmlString";

    public static HtmlTemplateCandidate Resolve (GeneratorAttributeSyntaxContext ctx)
    {
        var defects = new List<Defect>();
        if (ctx.TargetSymbol is not IMethodSymbol method || ctx.TargetNode is not MethodDeclarationSyntax syntax)
            return new(null, new(defects));
        var at = LocationInfo.From(ctx.TargetNode);
        if (!Shaped(method, defects, at)) return new(null, new(defects));
        if (Body(syntax) is not { } body)
            return Declined(defects, at,
                "its body is not a single writer.Write($\"…\") call on an interpolated string. Only " +
                "that shape can be read at compile time; the template still renders, one scan of its " +
                "literals per request, through the runtime handler.");
        if (!IsWriterWrite(body, method, ctx.SemanticModel, out var writer))
            return Declined(defects, at,
                "its body does not call Write on the method's own HtmlWriter parameter.");
        if (body.ArgumentList.Arguments.Count != 1 ||
            body.ArgumentList.Arguments[0].Expression is not InterpolatedStringExpressionSyntax markup)
            return Declined(defects, at, "the argument of Write is not a literal interpolated string.");
        var segments = Segments(markup, ctx.SemanticModel, defects, out var declined);
        if (declined is not null) return Declined(defects, at, declined);
        if (Inaccessible(markup, ctx.SemanticModel) is { } inaccessible)
            return Declined(defects, at,
                $"one of its holes reads '{inaccessible}', which the generated file cannot see — an " +
                "interceptor lives in its own namespace, not inside the template's type.");
        if (!Accessible(method.ContainingType) || method.Parameters.Any(p => !Accessible(p.Type)))
            return Declined(defects, at, "one of its parameter types is not visible outside its declaring type.");
        var model = new HtmlTemplateModel(
            method.Name,
            method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            new(method.Parameters.Select(p => new HtmlParameter(
                p.Name, p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))),
            new(segments),
            new(Usings(syntax, method)));
        // The writer parameter is what the emitted body writes into; keeping its name is what makes
        // the re-emitted hole expressions compile unchanged.
        return new(model with { Parameters = Renamed(model.Parameters, writer) }, new(defects));
    }

    /// <summary>The template a call site invokes, or null when it invokes something else.</summary>
    public static HtmlCallSite? Site (GeneratorSyntaxContext ctx)
    {
        if (ctx.Node is not InvocationExpressionSyntax invocation) return null;
        if (ctx.SemanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method) return null;
        if (!method.GetAttributes().Any(static a => a.AttributeClass?.ToDisplayString() == TemplateAttribute)) return null;
        var location = ctx.SemanticModel.GetInterceptableLocation(invocation);
        if (location is null) return null;
        var key = method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + method.Name;
        return new(key, new(location.Version, location.Data, location.GetDisplayLocation()));
    }

    private static bool Shaped (IMethodSymbol method, List<Defect> defects, LocationInfo? at)
    {
        if (method.IsStatic && method.ReturnsVoid && !method.IsGenericMethod &&
            method.Parameters.Any(p => p.Type.ToDisplayString() == writerType)) return true;
        defects.Add(new("CFW030", "Unsupported HTML template",
            "An [HtmlTemplate] method has to be a static, void-returning, non-generic method taking a " +
            "HtmlWriter. Rendering is a write into a sink rather than a string that is built and " +
            "returned, which is what lets the same template stream once the transport can " +
            ".", at));
        return false;
    }

    private static InvocationExpressionSyntax? Body (MethodDeclarationSyntax syntax)
    {
        if (syntax.ExpressionBody?.Expression is InvocationExpressionSyntax expression) return expression;
        if (syntax.Body is null || syntax.Body.Statements.Count != 1) return null;
        return (syntax.Body.Statements[0] as ExpressionStatementSyntax)?.Expression as InvocationExpressionSyntax;
    }

    private static bool IsWriterWrite (InvocationExpressionSyntax body, IMethodSymbol method,
        SemanticModel model, out string writer)
    {
        writer = "";
        if (body.Expression is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Write" } access) return false;
        if (model.GetSymbolInfo(access.Expression).Symbol is not IParameterSymbol parameter) return false;
        if (parameter.Type.ToDisplayString() != writerType) return false;
        if (!method.Parameters.Contains(parameter, SymbolEqualityComparer.Default)) return false;
        writer = parameter.Name;
        return true;
    }

    /// <summary>Splits the interpolated string into literals and classified holes.</summary>
    private static List<HtmlSegment> Segments (InterpolatedStringExpressionSyntax markup,
        SemanticModel model, List<Defect> defects, out string? declined)
    {
        declined = null;
        var scanner = new HtmlContextScanner();
        var segments = new List<HtmlSegment>();
        foreach (var content in markup.Contents)
        {
            if (content is InterpolatedStringTextSyntax text)
            {
                var literal = text.TextToken.ValueText;
                scanner.Advance(literal);
                segments.Add(new(literal, null, HtmlHoleKind.Text, false, null));
                continue;
            }
            if (content is not InterpolationSyntax hole) continue;
            if (hole.AlignmentClause is not null)
            {
                declined = "one of its holes has an alignment, which pads a value with spaces that " +
                           "would land in the markup rather than in a layout.";
                return segments;
            }
            var classified = scanner.Classify();
            var verbatim = model.GetTypeInfo(hole.Expression).Type?.ToDisplayString() == stringType;
            if (classified.Kind == HtmlHoleKind.Refused && !verbatim)
            {
                defects.Add(new("CFW031", "Unsafe position for a value in an HTML template",
                    $"This template writes a value {classified.Refusal}. No encoding makes that " +
                    "position safe, so the writer refuses it — move the value into element content " +
                    "or a quoted attribute value.", LocationInfo.From(hole)));
                continue;
            }
            segments.Add(new(null, Qualifier.Qualify(hole.Expression, model), classified.Kind, verbatim,
                hole.FormatClause?.FormatStringToken.ValueText));
        }
        return segments;
    }

    /// <summary>
    /// The first symbol a hole reads that the generated file could not read.
    /// </summary>
    /// <remarks>An interceptor is emitted into its own namespace, so a hole calling a private helper
    /// of the template's own type would emit code that does not compile. Finding that here turns a
    /// broken build into a declined template that still renders.</remarks>
    private static string? Inaccessible (InterpolatedStringExpressionSyntax markup, SemanticModel model)
    {
        foreach (var hole in markup.Contents.OfType<InterpolationSyntax>())
        foreach (var node in hole.Expression.DescendantNodesAndSelf())
        {
            if (node is not (IdentifierNameSyntax or MemberAccessExpressionSyntax or ObjectCreationExpressionSyntax)) continue;
            var symbol = model.GetSymbolInfo(node).Symbol;
            if (symbol is null or IParameterSymbol or ILocalSymbol or INamespaceSymbol or IRangeVariableSymbol) continue;
            if (!Accessible(symbol)) return symbol.ToDisplayString();
        }
        return null;
    }

    private static bool Accessible (ISymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingType)
        {
            if (current is ITypeParameterSymbol) return true;
            if (current is IArrayTypeSymbol array) return Accessible(array.ElementType);
            if (current.DeclaredAccessibility is Accessibility.Private or Accessibility.Protected
                or Accessibility.ProtectedAndInternal) return false;
        }
        return true;
    }

    /// <summary>The template file's using directives, plus its own namespace.</summary>
    /// <remarks>A hole expression is re-emitted verbatim, so every name it resolves through has to
    /// resolve the same way in the generated file. The containing namespace is added because source
    /// resolves sibling types through it without a directive of its own.</remarks>
    private static IEnumerable<string> Usings (MethodDeclarationSyntax syntax, IMethodSymbol method)
    {
        var usings = syntax.SyntaxTree.GetRoot() is CompilationUnitSyntax unit
            ? unit.Usings.Select(static u => u.ToString().Trim())
            : [];
        var space = method.ContainingNamespace;
        return space is null or { IsGlobalNamespace: true }
            ? usings
            : usings.Append($"using {space.ToDisplayString()};");
    }

    private static EquatableArray<HtmlParameter> Renamed (EquatableArray<HtmlParameter> parameters, string writer) =>
        new(parameters.Items.Select(p => p.Type == "global::" + writerType ? p with { Name = writer } : p));

    private static HtmlTemplateCandidate Declined (List<Defect> defects, LocationInfo? at, string reason)
    {
        defects.Add(new("CFW032", "HTML template is not compiled",
            $"This template is rendered at request time rather than compiled, because {reason}",
            at, DiagnosticSeverity.Warning));
        return new(null, new(defects));
    }
}
