using System.Collections.Immutable;
using Bootsharp.Cloudflare.Generate.MinimalApi;
using Bootsharp.Cloudflare.Projection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Bootsharp.Cloudflare.Generate.Html;

/// <summary>
/// Compiles <c>[HtmlTemplate]</c> methods into straight-line writes.
/// </summary>
/// <remarks>
/// <para>
/// The third generator of this assembly, and the default rendering tier: most pages are HTML that
/// a worker writes, and paying 152 KB of Razor Components to write them is not the shape of a
/// worker. What it replaces is the interpolated-string SSR the
/// sample shipped, whose defect was never speed — it was that encoding was opt-in per hole, so one
/// forgotten call was an XSS hole with nothing to catch it.
/// </para>
/// <para>
/// The inversion is the point: a hole is encoded for the context the markup around it puts it in,
/// and writing markup verbatim takes an explicit <c>HtmlString</c>. The classification is the
/// runtime handler's own scanner, run at compile time; this generator only decides which sink each
/// hole gets and writes the calls, so an app that never opts into the generator renders identical
/// bytes, one scan slower.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class HtmlTemplateGenerator : IIncrementalGenerator
{
    public void Initialize (IncrementalGeneratorInitializationContext context)
    {
        var templates = context.SyntaxProvider
            .ForAttributeWithMetadataName(HtmlTemplateResolver.TemplateAttribute,
                static (node, _) => node is MethodDeclarationSyntax,
                static (ctx, _) => HtmlTemplateResolver.Resolve(ctx))
            .Collect();

        var sites = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax,
                static (ctx, _) => HtmlTemplateResolver.Site(ctx))
            .Where(static site => site is not null)
            .Select(static (site, _) => site!)
            .Collect();

        context.RegisterSourceOutput(templates.Combine(sites),
            static (spc, data) => Execute(spc, data.Left, data.Right));
    }

    private static void Execute (SourceProductionContext spc,
        ImmutableArray<HtmlTemplateCandidate> candidates, ImmutableArray<HtmlCallSite> sites)
    {
        foreach (var defect in candidates.SelectMany(static candidate => candidate.Defects.Items))
            spc.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(defect.Id, defect.Title, "{0}", Rules.Library, defect.Severity, true),
                defect.Location?.ToLocation() ?? Location.None,
                defect.Message));
        var called = sites.GroupBy(static site => site.Key, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key,
                static group => group.Select(static site => site.Location).Distinct().ToArray(),
                StringComparer.Ordinal);
        foreach (var model in candidates.Select(static candidate => candidate.Model).OfType<HtmlTemplateModel>())
        {
            // A template nothing calls needs no interceptor. It is not a defect — a page can be
            // rendered from another assembly, or from a test that hands it a writer directly.
            if (!called.TryGetValue(model.Key, out var locations)) continue;
            spc.AddSource(HtmlTemplateEmitter.FileName(model), HtmlTemplateEmitter.Emit(model, locations));
        }
    }
}
