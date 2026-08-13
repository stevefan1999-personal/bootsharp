using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Cloudflare.Workers.Generator;

[Generator(LanguageNames.CSharp)]
public sealed class CloudflareWorkerGenerator : IIncrementalGenerator
{
    /// <summary>
    /// JS prototype members workerd owns on an entrypoint class: RPC dispatch rejects them
    /// (worker-rpc.c++) or the validator claims them as handlers.
    /// </summary>
    private static readonly string[] reserved =
    [
        "fetch", "connect", "alarm", "webSocketMessage",
        "webSocketClose", "webSocketError", "dup", "constructor"
    ];

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var entrypoints = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (ctx, _) => Resolve(ctx))
            .Where(static e => e is not null)
            .Select(static (e, _) => e!)
            .Collect();

        var env = context.CompilationProvider.Select(static (c, _) => ResolveEnv(c));

        var outputDir = context.AnalyzerConfigOptionsProvider.Select(static (opts, _) =>
        {
            opts.GlobalOptions.TryGetValue("build_property.CloudflareJsOutputDir", out var dir);
            opts.GlobalOptions.TryGetValue("build_property.CloudflareWasmImport", out var wasm);
            return (Dir: dir ?? "", Wasm: wasm ?? "../../../dist/wasm/backend.wasm");
        });

        context.RegisterSourceOutput(
            entrypoints.Combine(env).Combine(outputDir),
            static (spc, data) => Execute(spc, data.Left.Left, data.Left.Right, data.Right.Dir, data.Right.Wasm));
    }

    private static Entrypoint? Resolve(GeneratorSyntaxContext ctx)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)ctx.Node) is not INamedTypeSymbol symbol)
            return null;
        if (symbol.IsAbstract) return null;
        var kind = BaseName(symbol);
        if (kind is null) return null;
        var methods = new List<Method>();
        var defects = new List<Defect>();
        foreach (var method in symbol.GetMembers().OfType<IMethodSymbol>().Where(IsProjectable))
            Project(kind, method, methods, defects);
        var space = symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString();
        return new Entrypoint(kind, symbol.Name, space, new(methods), new(defects));
    }

    private static bool IsProjectable(IMethodSymbol method) =>
        method.MethodKind == MethodKind.Ordinary
        && method.DeclaredAccessibility == Accessibility.Public
        && !method.IsStatic
        && method.AssociatedSymbol is null
        && !OverridesObject(method);

    private static bool OverridesObject(IMethodSymbol method)
    {
        for (var current = method; current is not null; current = current.OverriddenMethod)
            if (current.ContainingType.SpecialType == SpecialType.System_Object)
                return true;
        return false;
    }

    /// <summary>
    /// Classifies one public method into a handler slot, an RPC method, or a diagnostic.
    /// Handler names map by convention; everything else on a Durable Object is RPC.
    /// </summary>
    private static void Project(string kind, IMethodSymbol method, List<Method> methods, List<Defect> defects)
    {
        var jsName = ToJs(method.Name);
        if (HandlerSlot(kind, method.Name) is { } slot)
        {
            methods.Add(new Method(method.Name, jsName, slot, "void", true, EquatableArray<Parameter>.Empty));
            return;
        }
        if (kind is not "DurableObject")
        {
            defects.Add(new Defect("CFW014", "Unsupported entrypoint member",
                $"'{method.Name}' is neither a {kind} handler ({HandlerNames(kind)}) nor a supported RPC method. " +
                "Make it non-public or move it to a collaborator type.",
                LocationInfo.From(method)));
            return;
        }
        if (reserved.Contains(jsName))
        {
            defects.Add(new Defect("CFW012", "Reserved RPC method name",
                $"'{method.Name}' would emit the reserved JS prototype member '{jsName}', which workerd " +
                "resolves as a handler or refuses to dispatch. Rename the method.",
                LocationInfo.From(method)));
            return;
        }
        if (ProjectRpc(method, jsName, defects) is { } rpc) methods.Add(rpc);
    }

    private static Method? ProjectRpc(IMethodSymbol method, string jsName, List<Defect> defects)
    {
        var (returned, awaited) = ReturnShape(method.ReturnType);
        if (returned is null)
            defects.Add(new Defect("CFW011", "Unsupported RPC return type",
                $"'{method.Name}' returns '{method.ReturnType.ToDisplayString()}'. Supported: void, Task, " +
                "int, long, double, bool, string and RpcInt, each optionally wrapped in Task<>.",
                LocationInfo.From(method)));
        var parameters = new List<Parameter>();
        var optionals = 0;
        foreach (var parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None || ParameterKind(parameter.Type) is not { } shape)
            {
                var cause = parameter.RefKind != RefKind.None
                    ? $"is passed by '{parameter.RefKind.ToString().ToLowerInvariant()}'"
                    : $"has type '{parameter.Type.ToDisplayString()}'";
                defects.Add(new Defect("CFW010", "Unsupported RPC parameter type",
                    $"Parameter '{parameter.Name}' of '{method.Name}' {cause}. Arguments arrive as a JSON " +
                    "array; supported by-value types are int, long, double, bool and string.",
                    LocationInfo.From(parameter)));
                continue;
            }
            if (parameter.IsOptional && !parameter.HasExplicitDefaultValue)
            {
                defects.Add(new Defect("CFW013", "Unsupported optional RPC parameter",
                    $"Optional parameter '{parameter.Name}' of '{method.Name}' has no explicit default value, " +
                    "so the emitter cannot fill it in when the caller omits the argument.",
                    LocationInfo.From(parameter)));
                continue;
            }
            if (parameter.IsOptional) optionals++;
            parameters.Add(new Parameter(parameter.Name, shape, parameter.IsOptional ? Literal(parameter) : null));
        }
        if (optionals > 1)
            defects.Add(new Defect("CFW013", "Unsupported optional RPC parameter",
                $"'{method.Name}' declares {optionals} optional parameters; only a single trailing optional " +
                "parameter is projected.",
                LocationInfo.From(method)));
        if (returned is null || optionals > 1 || parameters.Count != method.Parameters.Length) return null;
        return new Method(method.Name, jsName, "rpc", returned, awaited, new(parameters));
    }

    private static EquatableArray<EnvProperty> ResolveEnv(Compilation compilation)
    {
        var env = compilation.GetTypeByMetadataName("Cloudflare.Workers.ICloudflareEnv");
        if (env is null) return EquatableArray<EnvProperty>.Empty;
        return new(env.GetMembers().OfType<IPropertySymbol>()
            .Select(static p => new EnvProperty(p.Name, p.Type.Name)));
    }

    private static void Execute(
        SourceProductionContext spc,
        ImmutableArray<Entrypoint> entrypoints,
        EquatableArray<EnvProperty> env,
        string outputDir,
        string wasmImport)
    {
        var list = entrypoints.OrderBy(static e => e.Kind).ThenBy(static e => e.Name).ToArray();
        foreach (var defect in list.SelectMany(static e => e.Defects.Items))
            spc.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(defect.Id, defect.Title, "{0}", "Cloudflare.Workers", DiagnosticSeverity.Error, true),
                defect.Location?.ToLocation() ?? Location.None,
                defect.Message));
        spc.AddSource("ActorRuntime.g.cs", CsEmitter.Emit(list));
        var js = JsEmitter.Emit(list, env.Items, wasmImport);
        spc.AddSource("CloudflareWorkerJs.g.cs", JsEmitter.EmitCsLiteral(js));
        if (string.IsNullOrWhiteSpace(outputDir)) return;
        try
        {
            Directory.CreateDirectory(outputDir);
            File.WriteAllText(Path.Combine(outputDir, "entrypoints.ts"), js);
        }
        catch (Exception ex)
        {
            spc.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(
                    "CFW001",
                    "Failed to write generated Worker JS",
                    "Could not write Cloudflare worker JS to '{0}': {1}",
                    "Cloudflare.Workers",
                    DiagnosticSeverity.Warning,
                    true),
                Location.None,
                outputDir,
                ex.Message));
        }
    }

    private static string? BaseName(INamedTypeSymbol symbol)
    {
        for (var type = symbol.BaseType; type is not null; type = type.BaseType)
        {
            if (type.ContainingNamespace.ToDisplayString() != "Cloudflare.Workers") continue;
            if (type.Name == "WorkerEntrypoint") return "Worker";
            if (type.Name == "DurableObject") return "DurableObject";
            if (type.Name == "WorkflowEntrypoint") return "Workflow";
        }
        return null;
    }

    private static string? HandlerSlot(string kind, string name) => kind switch
    {
        "Worker" => name switch
        {
            "Fetch" => "fetch",
            "Queue" => "queue",
            "Scheduled" => "scheduled",
            _ => null
        },
        "Workflow" => name == "Run" ? "run" : null,
        _ => null
    };

    private static string HandlerNames(string kind) =>
        kind == "Worker" ? "Fetch, Queue, Scheduled" : "Run";

    private static string ToJs(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    private static (string? Kind, bool Await) ReturnShape(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { Name: "Task" } task
            && task.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks")
            return task.Arity == 1 ? (ValueKind(task.TypeArguments[0]), true) : ("void", true);
        if (type.SpecialType == SpecialType.System_Void) return ("void", false);
        return (ValueKind(type), false);
    }

    private static string? ValueKind(ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_Int32 => "int",
        SpecialType.System_Int64 => "long",
        SpecialType.System_Double => "double",
        SpecialType.System_Boolean => "bool",
        SpecialType.System_String => "string",
        _ => type is { Name: "RpcInt" } && type.ContainingNamespace.ToDisplayString() == "Cloudflare.Workers"
            ? "rpcInt"
            : null
    };

    private static string? ParameterKind(ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_Int32 => "int",
        SpecialType.System_Int64 => "long",
        SpecialType.System_Double => "double",
        SpecialType.System_Boolean => "bool",
        SpecialType.System_String => type.NullableAnnotation == NullableAnnotation.Annotated ? "string?" : "string",
        _ => null
    };

    private static string Literal(IParameterSymbol parameter) =>
        SymbolDisplay.FormatPrimitive(parameter.ExplicitDefaultValue!, true, false);
}
