using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Cloudflare.Workers.Generator.Tests;

/// <summary>
/// Outcome of a single generator run: what it refused (<see cref="Defects"/>), what it emitted,
/// and whether the emission actually compiles next to the sources it was projected from.
/// </summary>
internal sealed record GeneratorRun(
    ImmutableArray<Diagnostic> Defects,
    string GeneratedCs,
    string GeneratedJs,
    ImmutableArray<Diagnostic> CompilationErrors)
{
    public string[] DefectIds => [.. Defects.Select(static d => d.Id).Order()];

    /// <summary>Compiler errors rendered for an assertion message, so a failure names the cause.</summary>
    public string ErrorReport => CompilationErrors.Length == 0
        ? "no errors"
        : string.Join(Environment.NewLine, CompilationErrors.Select(static d => d.ToString()));
}

/// <summary>
/// Drives <see cref="CloudflareWorkerGenerator"/> over a throwaway compilation. No analyzer config
/// is supplied, so <c>CloudflareJsOutputDir</c> stays unset and the run touches no file on disk
/// the emitted module is read back out of the C# literal the generator embeds for the guest.
/// </summary>
internal static class GeneratorHarness
{
    private static readonly CSharpParseOptions parseOptions = new(LanguageVersion.Latest);

    private static readonly MetadataReference[] references =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(static path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
    ];

    /// <summary>The hand-written half of the runtime as the backend actually declares it today.</summary>
    public static string BackendRuntime => ReadEmbedded("Backend.ActorRuntime.cs");

    public static GeneratorRun Run(params string[] sources)
    {
        var trees = new[] { TestSources.Workers, TestSources.Runtime }
            .Concat(sources)
            .Select(static source => CSharpSyntaxTree.ParseText(source, parseOptions));
        var compilation = CSharpCompilation.Create(
            "CloudflareWorkerGeneratorTests",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        IEnumerable<ISourceGenerator> generators = [new CloudflareWorkerGenerator().AsSourceGenerator()];
        var driver = CSharpGeneratorDriver
            .Create(generators, parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var emitted, out var defects);
        var produced = driver.GetRunResult().Results.Single().GeneratedSources;
        return new GeneratorRun(
            defects,
            Source(produced, "ActorRuntime.g.cs"),
            Js(Source(produced, "CloudflareWorkerJs.g.cs")),
            [.. emitted.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error)]);
    }

    private static string Source(ImmutableArray<GeneratedSourceResult> produced, string hint) =>
        produced.Single(source => source.HintName == hint).SourceText.ToString();

    /// <summary>
    /// Unwraps the ESM module from the verbatim C# string literal the generator emits for it.
    /// Reading the literal rather than a written file keeps the assertions valid once
    /// moves the file write out of Roslyn and into the publish pipeline.
    /// </summary>
    private static string Js(string literalSource)
    {
        const string opening = "Entrypoints = @\"";
        var start = literalSource.IndexOf(opening, StringComparison.Ordinal);
        if (start < 0) throw new InvalidOperationException(
            "The generator no longer embeds the worker module as a verbatim C# literal.");
        start += opening.Length;
        var end = literalSource.LastIndexOf("\";", StringComparison.Ordinal);
        return literalSource[start..end].Replace("\"\"", "\"");
    }

    private static string ReadEmbedded(string name)
    {
        using var stream = typeof(GeneratorHarness).GetTypeInfo().Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"'{name}' is not embedded in the test assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
