using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Bootsharp.Cloudflare.Razor.Tests;

/// <summary>A page as the build sees it: a path under the project directory, and its markup.</summary>
internal sealed record Page (string Path, string Markup);

/// <summary>One run of the generator over a set of pages.</summary>
internal sealed record CshtmlRun (
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableDictionary<string, string> Generated)
{
    public string[] Ids => [.. Diagnostics.Select(static d => d.Id).Order()];

    public string[] Messages => [.. Diagnostics.Select(static d => d.GetMessage())];

    public string Report => Diagnostics.Length == 0
        ? "no diagnostics"
        : string.Join(Environment.NewLine, Diagnostics.Select(static d => d.ToString()));

    /// <summary>Everything the run emitted, concatenated — what the assertions about emitted calls
    /// read.</summary>
    public string Code => string.Join(Environment.NewLine, Generated.OrderBy(static pair => pair.Key,
        StringComparer.Ordinal).Select(static pair => pair.Value));

    /// <summary>The single file a one-page run emitted, with line endings normalised so that a
    /// golden comparison pins the emitted shape rather than the host's newline convention.</summary>
    public string Emitted => Generated.Values.Single().Replace("\r\n", "\n");

    /// <summary>The <c>html.Write*</c> calls the pages compiled to, trimmed and in order.</summary>
    public string[] Writes =>
    [
        .. Code.Split('\n').Select(static line => line.Trim())
            .Where(static line => line.StartsWith("html.Write"))
    ];

    /// <summary>Where each diagnostic points, as <c>path(line,character)</c> — half of what makes a
    /// refusal usable is that it names the page rather than a generated file.</summary>
    public string[] Locations =>
    [
        .. Diagnostics.Select(static d =>
        {
            var position = d.Location.GetLineSpan();
            return $"{System.IO.Path.GetFileName(position.Path)}" +
                   $"({position.StartLinePosition.Line + 1},{position.StartLinePosition.Character + 1})";
        })
    ];
}

/// <summary>
/// Drives the <c>.cshtml</c> generator, then compiles and runs what it emitted.
/// </summary>
/// <remarks>
/// The assertions that matter are not about the emitted text. A page that compiles can still write
/// the wrong bytes — an attribute hole sent through the element-content encoder, a URL through the
/// attribute one — and the only way to see that is to render. <see cref="Render"/> therefore
/// compiles the generated code against the real <c>Bootsharp.Cloudflare.AspNetCore</c>, loads it, and
/// calls the page's <c>Render</c> through a <c>StringHtmlWriter</c>, which is exactly what a worker
/// handler does.
/// </remarks>
internal static class CshtmlHarness
{
    /// <summary>Where the pages pretend to live, so that relative paths and folder-derived
    /// namespaces are exercised rather than stubbed.</summary>
    public const string ProjectDirectory = "/app/";

    public const string RootNamespace = "App";

    public static CshtmlRun Run (params Page[] pages)
    {
        var compilation = CSharpCompilation.Create("CshtmlGeneratorTests", [], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var texts = pages.Select(static page => (AdditionalText)new PageText(ProjectDirectory + page.Path, page.Markup));
        IEnumerable<ISourceGenerator> generators = [new CshtmlGenerator().AsSourceGenerator()];
        var driver = CSharpGeneratorDriver
            .Create(generators, texts, ParseOptions, new BuildOptions())
            .RunGeneratorsAndUpdateCompilation(compilation, out _, out var diagnostics);
        var produced = driver.GetRunResult().Results.Single().GeneratedSources;
        return new CshtmlRun(diagnostics, produced.ToImmutableDictionary(
            static source => source.HintName, static source => source.SourceText.ToString()));
    }

    /// <summary>Compiles the pages, loads them, and renders one by name.</summary>
    /// <param name="page">The page's qualified type name, e.g. <c>App.Views.Home</c>.</param>
    /// <param name="arguments">What the page's <c>@model</c> and <c>@param</c> directives declared.</param>
    public static string Render (string page, object?[] arguments, params Page[] pages)
    {
        var writer = new AspNetCore.Html.StringHtmlWriter();
        Load(page, pages).GetMethod("Render")!.Invoke(null, [writer, .. arguments]);
        return writer.ToString();
    }

    /// <summary>Compiles the pages, loads them, and hands back one page's generated type.</summary>
    /// <remarks>The type rather than its output, for the claims that are about the class the page
    /// became — that an <c>@attribute</c> landed on it, what <c>Render</c>'s signature is — which are
    /// invisible in rendered HTML and only half-visible in the emitted text.</remarks>
    public static Type Load (string page, params Page[] pages)
    {
        var run = Run(pages);
        Assert.Empty(run.Diagnostics);
        var directory = Directory.CreateTempSubdirectory("bootsharp-cshtml-tests");
        try
        {
            // A fresh identity per run: assemblies stay loaded for the life of the test process, and
            // a second one under a name already loaded would be refused.
            var name = $"CshtmlGeneratorTests_{Guid.NewGuid():N}";
            var path = System.IO.Path.Combine(directory.FullName, $"{name}.dll");
            var trees = run.Generated.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => CSharpSyntaxTree.ParseText(pair.Value, ParseOptions, path: pair.Key));
            var compilation = CSharpCompilation.Create(name, trees, References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));
            var result = compilation.Emit(path);
            Assert.True(result.Success, string.Join(Environment.NewLine,
                result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));
            return Assembly.LoadFrom(path).GetType(page)
                ?? throw new InvalidOperationException($"{page} is not in the emitted assembly.");
        }
        finally { directory.Delete(true); }
    }

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest);

    /// <summary>The shared framework with this suite's own reference set layered over it, so the
    /// emitted pages compile against the same Bootsharp.Cloudflare.AspNetCore the tests run.</summary>
    private static readonly MetadataReference[] References = [.. Resolved()];

    private static IEnumerable<MetadataReference> Resolved ()
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(System.IO.Path.PathSeparator))
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                paths[System.IO.Path.GetFileNameWithoutExtension(path)] = path;
        foreach (var path in Directory.GetFiles(AppContext.BaseDirectory, "*.dll"))
            paths[System.IO.Path.GetFileNameWithoutExtension(path)] = path;
        return paths.Values.Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path));
    }

    /// <summary>An <c>AdditionalFiles</c> entry, which is the only way a page reaches the generator.</summary>
    private sealed class PageText (string path, string markup) : AdditionalText
    {
        public override string Path => path;
        public override SourceText GetText (CancellationToken token = default) => SourceText.From(markup);
    }

    /// <summary>The two build properties this package's targets make compiler-visible.</summary>
    private sealed class BuildOptions : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
                ["build_property.RootNamespace"] = RootNamespace,
                ["build_property.ProjectDir"] = ProjectDirectory
            });

        public override AnalyzerConfigOptions GetOptions (SyntaxTree tree) => Values.Empty;
        public override AnalyzerConfigOptions GetOptions (AdditionalText text) => Values.Empty;

        private sealed class Values (Dictionary<string, string> values) : AnalyzerConfigOptions
        {
            public static readonly Values Empty = new([]);

            public override bool TryGetValue (string key, out string value) => values.TryGetValue(key, out value!);
        }
    }
}
