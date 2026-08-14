using System.Collections.Immutable;
using System.Reflection;
using Bootsharp.Cloudflare.Generate.Html;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Bootsharp.Cloudflare.Generate.Tests;

/// <summary>One run of the HTML template generator over a throwaway app.</summary>
internal sealed record HtmlTemplateRun(
    string Source,
    ImmutableArray<Diagnostic> Defects,
    string Generated,
    ImmutableArray<Diagnostic> CompilationErrors)
{
    public string[] DefectIds => [.. Defects.Select(static d => d.Id).Order()];

    public DiagnosticSeverity[] Severities =>
        [.. Defects.OrderBy(static d => d.Id, StringComparer.Ordinal).Select(static d => d.Severity)];

    public string DefectReport => Defects.Length == 0
        ? "no diagnostics"
        : string.Join(Environment.NewLine, Defects.Select(static d => d.ToString()));

    public string ErrorReport => CompilationErrors.Length == 0
        ? "no errors"
        : string.Join(Environment.NewLine, CompilationErrors.Select(static d => d.ToString()));

    /// <summary>Whether a template was compiled at all, rather than left to the runtime handler.</summary>
    public bool Intercepted => Generated.Contains("[InterceptsLocation(");

    /// <summary>The write calls the interceptor's body is, trimmed and in order.</summary>
    public string[] Writes =>
    [
        .. Generated.Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("html.Write") || line.StartsWith("writer.Write"))
    ];
}

/// <summary>
/// Drives the HTML template generator, and renders the result both ways.
/// </summary>
/// <remarks>
/// The assertion that matters most here is not what the generator emitted but that it emitted
/// something equivalent: <see cref="Rendered"/> compiles the app with the generated interceptors
/// and without them, renders the same model through both, and the tests compare. That is the
/// two-tier claim of stated as a test — the generator is an optimizer, and an app that
/// never opts into it gets the same bytes.
/// </remarks>
internal static class HtmlTemplateHarness
{
    private const string appPath = "Page.cs";

    public static HtmlTemplateRun Run (string source)
    {
        var compilation = CSharpCompilation.Create(
            "HtmlTemplateGeneratorTests",
            [CSharpSyntaxTree.ParseText(source, MinimalApiHarness.ParseOptions, path: appPath)],
            MinimalApiHarness.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        IEnumerable<ISourceGenerator> generators = [new HtmlTemplateGenerator().AsSourceGenerator()];
        var driver = CSharpGeneratorDriver
            .Create(generators, parseOptions: MinimalApiHarness.ParseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var emitted, out var defects);
        var produced = driver.GetRunResult().Results.Single().GeneratedSources;
        return new HtmlTemplateRun(
            source, defects,
            string.Join(Environment.NewLine, produced.Select(static source => source.SourceText.ToString())),
            [.. emitted.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error)]);
    }

    /// <summary>Renders the app's <c>Runner.Render()</c>, compiled or not.</summary>
    /// <param name="compiled">Whether the generated interceptors join the compilation.</param>
    public static string Rendered (string source, bool compiled = true)
    {
        var run = Run(source);
        if (run.CompilationErrors.Length > 0) throw new InvalidOperationException(run.ErrorReport);
        var directory = Directory.CreateTempSubdirectory("bootsharp-html-template-tests");
        try
        {
            var name = $"HtmlTemplateGeneratorTests_{Guid.NewGuid():N}";
            var path = Path.Combine(directory.FullName, $"{name}.dll");
            // The app tree keeps the path it had when the generator ran: an interceptable location is
            // that path plus a checksum of the file.
            SyntaxTree[] trees = compiled && run.Generated.Length > 0
                ? [CSharpSyntaxTree.ParseText(source, MinimalApiHarness.ParseOptions, path: appPath),
                    CSharpSyntaxTree.ParseText(run.Generated, MinimalApiHarness.ParseOptions, path: "Template.g.cs")]
                : [CSharpSyntaxTree.ParseText(source, MinimalApiHarness.ParseOptions, path: appPath)];
            var compilation = CSharpCompilation.Create(name, trees, MinimalApiHarness.References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));
            var result = compilation.Emit(path);
            if (!result.Success)
                throw new InvalidOperationException(string.Join(Environment.NewLine,
                    result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));
            MinimalApiHarness.Resolve();
            var runner = Assembly.LoadFrom(path).GetType("Runner")!;
            return (string)runner.GetMethod("Render")!.Invoke(null, [])!;
        }
        finally { directory.Delete(true); }
    }

    /// <summary>Renders both ways and fails unless they agree.</summary>
    public static string Both (string source)
    {
        var compiled = Rendered(source);
        var interpreted = Rendered(source, false);
        if (compiled != interpreted) throw new InvalidOperationException(
            $"The compiled template and the runtime handler disagree.{Environment.NewLine}" +
            $"compiled:    {compiled}{Environment.NewLine}interpreted: {interpreted}");
        return compiled;
    }
}
