using System.Collections.Immutable;
using System.Text;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Bootsharp.Cloudflare.Razor;

/// <summary>
/// Compiles <c>.cshtml</c> pages into straight-line calls on
/// <c>Bootsharp.Cloudflare.AspNetCore.Html.HtmlWriter</c>.
/// </summary>
/// <remarks>
/// <para>
/// The third authoring tier, and the one that gives Razor <em>syntax</em> without the Razor
/// <em>runtime</em>. ASP.NET Core MVC does not exist as a package for this target and never will, so
/// the usual conclusion is that <c>.cshtml</c> is unavailable. It is not: the whole MVC surface of
/// <c>.cshtml</c> codegen is a single opt-in call, <c>RazorExtensions.Register(builder)</c>, which
/// this generator does not make. Skip it and the compiler emits against whatever base type, method
/// name and parameter list a document classifier pass asks for — the same three seams
/// (<see cref="CshtmlClassifierPass"/> → <see cref="CshtmlCodeTarget"/> →
/// <see cref="CshtmlNodeWriter"/>) Blazor uses for components, and that ASP.NET Core's own
/// diagnostics middleware used for its error pages years before that.
/// </para>
/// <para>
/// What reaches the worker is therefore a method: no <c>RazorPage&lt;TModel&gt;</c>, no
/// <c>[RazorInject]</c> properties, no <c>[RazorCompiledItem]</c> attribute, no reflection, no
/// <c>dynamic</c>, and no Razor assembly of any kind — the compiler is a build input of this
/// analyzer and nothing else. Measured against the <c>[HtmlTemplate]</c> tier beside it, the same
/// page costs +61 bytes of deployable bundle gzip and returns byte-identical HTML
///so choosing between the two is authoring taste rather than cost.
/// </para>
/// <para>
/// Encoding is not the author's decision and is not opt-in. Razor's parser has already established
/// whether a hole is element content, an attribute value or a URL, and the node writer picks the
/// matching <c>HtmlWriter</c> sink at compile time; <c>HtmlString</c> remains the single, greppable
/// way to write markup verbatim. That is the same contract, and the same encoder, as the
/// interpolated-string tier.
/// </para>
/// <para>
/// Tag helpers, <c>@inject</c>, <c>@section</c> and layouts are MVC-level and are <b>refused by
/// name</b>, never silently mis-compiled. <c>@model T</c> is the exception: it is <b>repurposed, not
/// refused</b> — sugar for a render parameter named <c>Model</c>, which is deliberately
/// <em>syntax</em> familiarity without MVC <em>semantics</em>, since there is no
/// <c>ViewData</c>/<c>ViewBag</c> behind it and those names simply do not resolve.
/// </para>
/// <para>
/// The authoring contract — what a page declares and how a handler calls it — is
/// <see cref="CshtmlDirectives"/>.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class CshtmlGenerator : IIncrementalGenerator
{
    private const string extension = ".cshtml";
    private const string writerType = "global::Bootsharp.Cloudflare.AspNetCore.Html.HtmlWriter";

    public void Initialize (IncrementalGeneratorInitializationContext context)
    {
        var options = context.AnalyzerConfigOptionsProvider.Select(static (provider, _) => CshtmlOptions.Read(provider));
        // Collected and re-expanded to drop a path listed twice: an explicit AdditionalFiles entry
        // alongside this package's own glob would otherwise ask for one hint name to be added twice,
        // which Roslyn refuses outright. Per-page caching survives it — what the pipeline compares
        // downstream is the CshtmlPage value, so editing one page still recompiles only that page.
        var pages = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .Collect()
            .SelectMany(static (files, _) => Distinct(files))
            .Combine(options)
            .Select(static (pair, token) => CshtmlPage.Read(pair.Left, pair.Right, token))
            .Where(static page => page is not null)
            .Select(static (page, _) => page!);
        // Every page sees the whole roster of generated names so it can refuse to be the second
        // declaration of one. Only the names travel, so editing a page's markup does not invalidate
        // any other page's output — adding, removing or moving one does.
        var names = pages.Select(static (page, _) => page.QualifiedName).Collect();
        context.RegisterSourceOutput(pages.Combine(names), static (production, pair) =>
            Emit(production, pair.Left, pair.Right));
    }

    private static IEnumerable<AdditionalText> Distinct (ImmutableArray<AdditionalText> files)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
            if (seen.Add(file.Path))
                yield return file;
    }

    private static void Emit (SourceProductionContext production, CshtmlPage page, ImmutableArray<string> names)
    {
        if (page.IsImportConvention)
        {
            Report(production, CshtmlDiagnostics.Refused, page, null,
                $"{System.IO.Path.GetFileName(page.Path)} is an MVC import convention and is not " +
                "supported; there is no ambient import on this tier — put @using lines in the page " +
                "that needs them, or declare a global using in the project");
            return;
        }
        if (names.Count(name => name == page.QualifiedName) > 1)
        {
            Report(production, CshtmlDiagnostics.Collision, page, null,
                $"more than one page compiles to '{page.QualifiedName}'; a page's type is its folder " +
                "plus its file name, so rename one of them or move it to another folder");
            return;
        }

        var text = SourceText.From(page.Text, Encoding.UTF8);
        var classifier = new CshtmlClassifierPass(page.Namespace, page.ClassName, writerType);
        var engine = RazorProjectEngine.Create(RazorConfiguration.Default, EmptyProjectFileSystem.Instance, builder => {
            // The line that decides everything is the one that is absent: RazorExtensions.Register
            // is never called, so there is no MVC import, no [RazorInject] property, no
            // RazorPage<TModel> base type and no tag-helper producer to require an ITagHelper runtime.
            CshtmlDirectives.Register(builder);
            builder.Features.Add(new SuppressRuntimeMetadata());
            builder.Features.Add(classifier.Capture);
            builder.Features.Add(classifier);
        });
        var source = RazorSourceDocument.Create(page.Text, page.RelativePath);
        var compiled = engine.Process(source, FileKinds.Legacy, ImmutableArray<RazorSourceDocument>.Empty, null)
            .GetCSharpDocument();

        var failed = false;
        foreach (var refusal in classifier.Refusals.Concat(TagHelpers(page, text)))
        {
            Report(production, refusal.Descriptor, page, Span(text, refusal.Source), refusal.Message);
            failed = true;
        }
        foreach (var diagnostic in compiled.Diagnostics)
            if (diagnostic.Severity == RazorDiagnosticSeverity.Error)
            {
                Report(production, CshtmlDiagnostics.Invalid, page, Span(text, diagnostic.Span), diagnostic.GetMessage());
                failed = true;
            }
        // Nothing is emitted for a page that failed. The diagnostic is the answer, and generated code
        // built from a document we have already declined would bury it under C# errors pointing at a
        // file the author never wrote — the failure this whole diagnostic set exists to prevent.
        if (failed) return;

        production.AddSource(page.HintName, SourceText.From(compiled.GeneratedCode, Encoding.UTF8));
    }

    /// <summary>
    /// The tag-helper directives, found by scanning the page rather than by parsing it.
    /// </summary>
    /// <remarks>Explained in <see cref="CshtmlDirectives.Scanned"/>: their argument is an unquoted
    /// assembly pattern that no directive token kind in this compiler accepts, so a registered
    /// descriptor would diagnose the right line for the wrong reason.</remarks>
    private static IEnumerable<Refusal> TagHelpers (CshtmlPage page, SourceText text)
    {
        foreach (var line in text.Lines)
        {
            var content = text.ToString(line.Span);
            var indent = content.Length - content.TrimStart().Length;
            foreach (var keyword in CshtmlDirectives.Scanned)
                if (content.TrimStart().StartsWith("@" + keyword, StringComparison.Ordinal))
                    yield return new Refusal(CshtmlDirectives.TagHelperMessage, new SourceSpan(
                        page.RelativePath, line.Start + indent, line.LineNumber, indent, keyword.Length + 1));
        }
    }

    private static void Report (SourceProductionContext production, DiagnosticDescriptor descriptor,
        CshtmlPage page, TextSpan? span, string message)
    {
        production.ReportDiagnostic(Diagnostic.Create(descriptor, Locate(page, span), message));
    }

    /// <summary>Points a diagnostic at the page rather than at generated code, which is the whole
    /// difference between "the tier does not support this" and "something went wrong somewhere".</summary>
    private static Location Locate (CshtmlPage page, TextSpan? span)
    {
        if (span is not { } text) return Location.Create(page.Path, default, default);
        var lines = SourceText.From(page.Text).Lines;
        return Location.Create(page.Path, text, lines.GetLinePositionSpan(text));
    }

    /// <summary>Razor spans are absolute-index based; a Roslyn location needs a clamped text span,
    /// because a directive at the very end of a file reports a length that runs past it.</summary>
    private static TextSpan? Span (SourceText text, SourceSpan? source)
    {
        if (source is not { } span || span.AbsoluteIndex < 0 || span.AbsoluteIndex > text.Length) return null;
        return new TextSpan(span.AbsoluteIndex, Math.Min(Math.Max(span.Length, 0), text.Length - span.AbsoluteIndex));
    }

    /// <summary>Drops the <c>[RazorCompiledItem]</c>/<c>[RazorSourceChecksum]</c> pair — the only
    /// reference to a runtime assembly the default feature set emits, and one that would bind to
    /// <c>Microsoft.AspNetCore.Razor.Runtime</c>, a package that does not exist for.NET 10.</summary>
    private sealed class SuppressRuntimeMetadata : RazorEngineFeatureBase, IConfigureRazorCodeGenerationOptionsFeature
    {
        public int Order { get; set; }

        public void Configure (RazorCodeGenerationOptionsBuilder options)
        {
            options.SuppressChecksum = true;
            options.SuppressMetadataAttributes = true;
        }
    }
}

/// <summary>The engine requires a project file system; this design never reads one, because imports
/// are an MVC convention it does not implement.</summary>
internal sealed class EmptyProjectFileSystem : RazorProjectFileSystem
{
    public static readonly EmptyProjectFileSystem Instance = new();

    public override IEnumerable<RazorProjectItem> EnumerateItems (string basePath) => [];
    public override RazorProjectItem GetItem (string path) => GetItem(path, null);
    public override RazorProjectItem GetItem (string path, string? fileKind) => throw new NotSupportedException();
}
