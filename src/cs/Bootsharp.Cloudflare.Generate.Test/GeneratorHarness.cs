using System.Collections.Immutable;
using System.Reflection;
using System.Text.RegularExpressions;
using Bootsharp.Cloudflare.Publish;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Bootsharp.Cloudflare.Generate.Tests;

/// <summary>
/// Outcome of a single pipeline run: what the generator refused (<see cref="Defects"/>), what it
/// emitted, whether that emission compiles next to the sources it was projected from, and the ESM
/// module the publish task then projects out of the assembly that compilation produced.
/// </summary>
internal sealed record GeneratorRun(
    ImmutableArray<Diagnostic> Defects,
    string GeneratedCs,
    string GeneratedJs,
    ImmutableArray<Diagnostic> CompilationErrors)
{
    public string[] DefectIds => [.. Defects.Select(static d => d.Id).Order()];

    /// <summary>Helpers the emitted module imports from the packaged <c>js/runtime.mjs</c> asset.</summary>
    public string[] RuntimeImports =>
    [
        .. Regex.Match(GeneratedJs, @"import \{(?<names>[^}]*)\} from ""\./runtime\.mjs"";", RegexOptions.Singleline)
            .Groups["names"].Value
            .Split(',')
            .Select(static name => name.Trim())
            .Where(static name => name.Length > 0)
            .Order()
    ];

    /// <summary>Compiler errors rendered for an assertion message, so a failure names the cause.</summary>
    public string ErrorReport => CompilationErrors.Length == 0
        ? "no errors"
        : string.Join(Environment.NewLine, CompilationErrors.Select(static d => d.ToString()));
}

/// <summary>
/// Drives the whole emission pipeline over a throwaway compilation, in the order a publish drives
/// it: the Roslyn generator resolves the projection from symbols and emits the C# dispatch, the
/// compilation is emitted to an assembly, and the publish task projects the ESM module out of that
/// assembly's metadata. Running both halves is what keeps them from drifting — they share the
/// model and the classification rules, but each resolves them from its own source of truth.
/// </summary>
internal static class GeneratorHarness
{
    private static readonly CSharpParseOptions parseOptions = new(LanguageVersion.Latest);

    private static readonly string[] platformAssemblies =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(static path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
    ];

    private static readonly MetadataReference[] references =
        [.. platformAssemblies.Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))];

    /// <summary>The packaged half of the actor runtime as Bootsharp.Cloudflare actually declares it.</summary>
    public static string PackagedRuntime => ReadEmbedded("Bootsharp.Cloudflare.ActorRuntimeBase.cs");

    /// <summary>The packaged ESM runtime asset the emitted module imports from.</summary>
    public static string PackagedJsRuntime => ReadEmbedded("Bootsharp.Cloudflare.runtime.mjs");

    /// <summary>Runs against an app that hosts no actor, which is the leanest app half there is.</summary>
    public static GeneratorRun Run (params string[] sources) => RunApp(TestSources.App(), sources);

    /// <summary>
    /// Runs against an app that also declares its half of the actor dispatch partial. Hosting an
    /// actor and declaring that half are the same decision in a real app — the generated half only
    /// exists for an app with an actor — so the cases that supply one say so here.
    /// </summary>
    public static GeneratorRun RunActors (params string[] sources) =>
        RunApp(TestSources.App(actorRuntime: TestSources.ActorRuntime), sources);

    /// <summary>
    /// Runs against an app half other than the default one — the env interface, its bindings and the
    /// assembly attributes are app configuration, so the cases that exercise them replace it.
    /// </summary>
    public static GeneratorRun RunApp (string app, params string[] sources)
    {
        var trees = new[] { TestSources.Workers, TestSources.Interop, TestSources.Runtime, app }
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
            EmitJs(emitted),
            [.. emitted.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error)]);
    }

    /// <summary>
    /// Runs the publish half: emits the generated compilation to a real assembly and projects the
    /// worker module out of its metadata, exactly as the MSBuild task does after the native link.
    /// The import specifiers are stand-ins — the task computes them from the publish layout.
    /// </summary>
    private static string EmitJs (Compilation compilation)
    {
        var directory = Directory.CreateTempSubdirectory("bootsharp-cloudflare-tests");
        try
        {
            var path = Path.Combine(directory.FullName, $"{compilation.AssemblyName}.dll");
            var result = compilation.Emit(path);
            if (!result.Success) return "";
            using var projector = new MetadataProjector(path, platformAssemblies, static _ => { });
            return JsEmitter.Emit(
                projector.ProjectEntrypoints(),
                projector.ProjectEnv(),
                projector.ProjectOptions(),
                "../wasm/backend.wasm",
                "../js/index.mjs");
        }
        finally { directory.Delete(true); }
    }

    private static string Source (ImmutableArray<GeneratedSourceResult> produced, string hint) =>
        produced.SingleOrDefault(source => source.HintName == hint).SourceText?.ToString() ?? "";

    private static string ReadEmbedded (string name)
    {
        using var stream = typeof(GeneratorHarness).GetTypeInfo().Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"'{name}' is not embedded in the test assembly.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
