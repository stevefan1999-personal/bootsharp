using System.Collections.Immutable;
using System.Reflection;
using Bootsharp.Cloudflare.Generate.MinimalApi;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Bootsharp.Cloudflare.Generate.Tests;

/// <summary>
/// One run of the minimal-API generator: what it refused, what it emitted, and whether the emission
/// compiles next to the app it was generated from.
/// </summary>
internal sealed record MinimalApiRun(
    string Source,
    ImmutableArray<Diagnostic> Defects,
    string Generated,
    ImmutableArray<Diagnostic> CompilationErrors)
{
    public string[] DefectIds => [.. Defects.Select(static d => d.Id).Order()];

    /// <summary>
    /// The source text each diagnostic underlines, in id order.
    /// </summary>
    /// <remarks>Which span a refusal points at is half of what makes it usable: a pattern error on
    /// the handler, or a parameter error on the whole call, sends the reader to the wrong place. The
    /// text is asserted rather than the line and column so the expectation stays readable when the
    /// app template around it moves.</remarks>
    public string[] Underlined =>
    [
        .. Defects
            .OrderBy(static d => d.Id, StringComparer.Ordinal)
            .Select(d => Source.Substring(d.Location.SourceSpan.Start, d.Location.SourceSpan.Length))
    ];

    /// <summary>Severity of each diagnostic, in id order.</summary>
    public DiagnosticSeverity[] Severities =>
        [.. Defects.OrderBy(static d => d.Id, StringComparer.Ordinal).Select(static d => d.Severity)];

    public string DefectReport => Defects.Length == 0
        ? "no diagnostics"
        : string.Join(Environment.NewLine, Defects.Select(static d => d.ToString()));

    public string ErrorReport => CompilationErrors.Length == 0
        ? "no errors"
        : string.Join(Environment.NewLine, CompilationErrors.Select(static d => d.ToString()));

    /// <summary>Interceptor method declarations the run emitted, in order.</summary>
    public string[] Interceptors =>
    [
        .. Generated.Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("internal static RouteHandlerBuilder Map"))
            .Select(static line => line.Replace("internal static RouteHandlerBuilder ", "").Replace("(", ""))
    ];

    public int InterceptsLocationCount =>
        Generated.Split('\n').Count(static line => line.TrimStart().StartsWith("[InterceptsLocation("));
}

/// <summary>
/// Drives the minimal-API generator over a throwaway app compiled against the real
/// <c>Bootsharp.Cloudflare.AspNetCore</c>.
/// </summary>
/// <remarks>
/// The compilation opts into the interceptor namespace the same way the shipped
/// <c>Bootsharp.Cloudflare.AspNetCore.props</c> opts an app's build into it, so a run that emits
/// interceptors the compiler would reject fails here rather than in someone's worker.
/// </remarks>
internal static class MinimalApiHarness
{
    private const string interceptorsNamespace = "Bootsharp.Cloudflare.AspNetCore.Generated";

    /// <summary>File-local types need distinct paths, and an interceptor needs a stable one.</summary>
    private const string appPath = "App.cs";

    internal static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Latest)
        .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", interceptorsNamespace)]);

    internal static readonly MetadataReference[] References = [.. Resolved()];

    public static MinimalApiRun Run (string source)
    {
        var compilation = CSharpCompilation.Create(
            "MinimalApiGeneratorTests",
            [CSharpSyntaxTree.ParseText(source, ParseOptions, path: appPath)],
            References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        IEnumerable<ISourceGenerator> generators = [new MinimalApiGenerator().AsSourceGenerator()];
        var driver = CSharpGeneratorDriver
            .Create(generators, parseOptions: ParseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var emitted, out var defects);
        var produced = driver.GetRunResult().Results.Single().GeneratedSources;
        return new MinimalApiRun(
            source,
            defects,
            produced.SingleOrDefault(static source => source.HintName == "BootsharpMinimalApi.g.cs")
                .SourceText?.ToString() ?? "",
            [.. emitted.GetDiagnostics().Where(static d => d.Severity == DiagnosticSeverity.Error)]);
    }

    /// <summary>
    /// The shared framework, with the package under test and its own dependencies layered over it.
    /// </summary>
    /// <remarks>Layered by simple name rather than concatenated: an assembly present in both — the
    /// framework's System.Text.Json, say — would otherwise be referenced twice and every type in it
    /// would be ambiguous.</remarks>
    private static IEnumerable<MetadataReference> Resolved ()
    {
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator))
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                paths[Path.GetFileNameWithoutExtension(path)] = path;
        foreach (var path in Directory.GetFiles(PackagePath(), "*.dll"))
            paths[Path.GetFileNameWithoutExtension(path)] = path;
        return paths.Values.Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path));
    }

    /// <summary>
    /// Compiles the app plus what the generator emitted, loads it, and runs one request through it.
    /// </summary>
    /// <remarks>
    /// The check the text assertions cannot make: an interceptor that compiles can still intercept
    /// the wrong call, bind the wrong value or never be reached at all — the failure mode
    /// exists to prevent, since an un-intercepted <c>Map*</c> throws rather than falling back. Running
    /// the request is the only way to see that the call site was replaced.
    /// </remarks>
    public static string Send (string source, string method, string url, string body = "")
    {
        var run = Run(source);
        if (run.CompilationErrors.Length > 0) throw new InvalidOperationException(run.ErrorReport);
        var directory = Directory.CreateTempSubdirectory("bootsharp-minimal-api-tests");
        try
        {
            // A fresh identity per run: the assemblies stay loaded for the life of the test process,
            // and a second one under a name already loaded would be refused.
            var name = $"MinimalApiGeneratorTests_{Guid.NewGuid():N}";
            var path = Path.Combine(directory.FullName, $"{name}.dll");
            var compilation = CSharpCompilation.Create(
                name,
                // The app tree keeps the path it had when the generator ran: an interceptable
                // location is that path plus a checksum, so re-parsing it as a different file would
                // leave every [InterceptsLocation] pointing at nothing.
                [CSharpSyntaxTree.ParseText(source, ParseOptions, path: appPath),
                    CSharpSyntaxTree.ParseText(run.Generated, ParseOptions, path: "BootsharpMinimalApi.g.cs")],
                References,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));
            var result = compilation.Emit(path);
            if (!result.Success)
                throw new InvalidOperationException(string.Join(Environment.NewLine,
                    result.Diagnostics.Where(static d => d.Severity == DiagnosticSeverity.Error)));
            Resolve();
            var runner = Assembly.LoadFrom(path).GetType("Runner")!;
            var task = (Task<string>)runner.GetMethod("Send")!.Invoke(null, [method, url, body])!;
            return task.GetAwaiter().GetResult();
        }
        finally { directory.Delete(true); }
    }

    private static bool resolving;

    /// <summary>
    /// Teaches the runtime where the package under test is, once. The emitted assembly is loaded
    /// from a temporary directory that holds nothing else, and the package is deliberately not on
    /// this project's own reference path (see the csproj).
    /// </summary>
    internal static void Resolve ()
    {
        if (resolving) return;
        resolving = true;
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            var path = Path.Combine(PackagePath(), $"{name.Name}.dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
    }

    internal static string PackagePath ()
    {
        var path = typeof(MinimalApiHarness).GetTypeInfo().Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(static attribute => attribute.Key == "Bootsharp.Cloudflare.AspNetCore.OutputPath")?.Value
            ?? throw new InvalidOperationException("The test assembly does not record where the package was built.");
        return Directory.Exists(path) ? path : throw new InvalidOperationException(
            $"'{path}' does not exist: build Bootsharp.Cloudflare.AspNetCore in this configuration first.");
    }
}
