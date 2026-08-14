using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Bootsharp.Cloudflare.Generate.Html;

/// <summary>
/// Rewrites a hole's expression so it means the same thing outside the type it was written in.
/// </summary>
/// <remarks>
/// A hole is re-emitted verbatim into the interceptor, which lives in the generator's own namespace
/// rather than inside the page class. So <c>Notice(model)</c> — a static helper sitting next to the
/// template, which is how anyone would factor a page — resolves in the source and resolves to
/// nothing in the generated file. Every unqualified reference to a type or a static member is
/// therefore replaced by its fully qualified form here, from the symbol the original binding
/// produced: the generated code says what the source meant, not what it looked like.
/// </remarks>
internal sealed class Qualifier (SemanticModel model) : CSharpSyntaxRewriter
{
    public static string Qualify (ExpressionSyntax expression, SemanticModel model) =>
        new Qualifier(model).Visit(expression).ToString();

    public override SyntaxNode? VisitIdentifierName (IdentifierNameSyntax node) => Rewrite(node) ?? node;

    public override SyntaxNode? VisitGenericName (GenericNameSyntax node)
    {
        // The type arguments are expressions in their own right and need the same treatment; the
        // rewritten node is built from them rather than from the original text.
        var visited = (GenericNameSyntax)base.VisitGenericName(node)!;
        return Rewrite(node, visited) ?? visited;
    }

    private SyntaxNode? Rewrite (SimpleNameSyntax node, SimpleNameSyntax? visited = null)
    {
        // The right-hand side of a member access is already qualified by what precedes it, and the
        // name of a named argument or of a member in an object initializer is not a reference at all.
        if (node.Parent is MemberAccessExpressionSyntax access && access.Name == node) return null;
        if (node.Parent is MemberBindingExpressionSyntax or NameColonSyntax or NameEqualsSyntax) return null;
        var symbol = model.GetSymbolInfo(node).Symbol;
        if (symbol is INamedTypeSymbol type)
            return Parse(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), node);
        if (symbol is not (IMethodSymbol or IPropertySymbol or IFieldSymbol) || !symbol.IsStatic) return null;
        // A static local function has no containing type to qualify through; it is also not something
        // the generated file could ever reach, so the accessibility pass declines the template first.
        if (symbol.ContainingType is null || symbol is IMethodSymbol { MethodKind: not MethodKind.Ordinary }) return null;
        var owner = symbol.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return Parse($"{owner}.{(visited ?? node)}", node);
    }

    private static SyntaxNode Parse (string text, SyntaxNode original) =>
        SyntaxFactory.ParseExpression(text).WithTriviaFrom(original);
}
