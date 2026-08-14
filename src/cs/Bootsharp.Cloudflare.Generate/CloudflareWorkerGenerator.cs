using System.Collections.Immutable;
using System.Linq;
using Bootsharp.Cloudflare.Projection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Bootsharp.Cloudflare.Generate;

/// <summary>
/// Source-time half of the entrypoint pipeline: it resolves the projection from symbols, reports
/// every shape it refuses at the location that declared it, and emits the C# actor dispatch. The
/// ESM module is emitted by the publish task instead, which is why nothing here touches a file.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class CloudflareWorkerGenerator : IIncrementalGenerator
{
    public void Initialize (IncrementalGeneratorInitializationContext context)
    {
        var entrypoints = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (ctx, _) => Resolve(ctx))
            .Where(static e => e is not null)
            .Select(static (e, _) => e!)
            .Collect();

        // The app's half of the dispatch partial. Resolved from syntax rather than looked up in the
        // compilation so the check stays incremental: the compilation is a new value on every
        // keystroke, whereas this list only changes when a declaration of that half does.
        var halves = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax
                    { BaseList: not null, Identifier.ValueText: Rules.ActorRuntimeType },
                static (ctx, _) => ResolveActorRuntime(ctx))
            .Where(static h => h is not null)
            .Select(static (h, _) => h!)
            .Collect();

        var envs = context.SyntaxProvider
            .ForAttributeWithMetadataName(Rules.EnvAttribute,
                static (node, _) => node is InterfaceDeclarationSyntax,
                static (ctx, _) => ResolveEnv(ctx))
            .Where(static e => e is not null)
            .Select(static (e, _) => e!)
            .Collect();

        // The packaged runtime binds the log handler to a member the guest only has when it imports
        // the sink, so an app reaching for the Cloudflare logging surface without that import boots
        // into a TypeError. Both halves of that condition are collected here.
        var uses = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is IdentifierNameSyntax { Identifier.ValueText: "ILogSink" or "CloudflareJsonLoggerProvider" },
                static (ctx, _) => ResolveLogUse(ctx))
            .Where(static l => l is not null)
            .Select(static (l, _) => l!)
            .Collect();
        var imported = context.CompilationProvider.Select(static (c, _) => ImportsLogSink(c));

        context.RegisterSourceOutput(entrypoints.Combine(halves).Combine(envs).Combine(uses.Combine(imported)),
            static (spc, data) => {
                var (((resolved, declared), marked), (logUses, importsSink)) = data;
                Execute(spc, resolved, declared, marked, logUses, importsSink);
            });
    }

    private static Resolution? Resolve (GeneratorSyntaxContext ctx)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)ctx.Node) is not INamedTypeSymbol symbol)
            return null;
        if (symbol.IsAbstract) return null;
        var kind = BaseKind(symbol);
        if (kind is null) return null;
        var dispatches = new List<Dispatch>();
        var defects = new List<Defect>();
        foreach (var method in symbol.GetMembers().OfType<IMethodSymbol>().Where(IsProjectable))
            Project(kind, method, dispatches, defects);
        // A hub-hosting Durable Object inherits its transport surface rather than declaring it, and
        // GetMembers() returns declared members only — so without this its JavaScript half would
        // call four methods that were never emitted. See SignalR/HubRules.Transport.
        if (HostsHub(symbol)) dispatches.AddRange(HubTransport());
        var space = symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString();
        var entrypoint = new Entrypoint(kind, symbol.Name, space, new(dispatches.Select(static d => d.Method)));
        return new Resolution(entrypoint, new(dispatches), new(defects), LocationInfo.From(symbol));
    }

    /// <summary>Whether the class derives from <c>HubDurableObject&lt;THub, TEnv&gt;</c>.</summary>
    private static bool HostsHub (INamedTypeSymbol symbol) => Rules.HostsHub(Bases(symbol));

    /// <summary>Full names of every base, on the unbound name — a symbol's name carries no arity.</summary>
    private static IEnumerable<string> Bases (INamedTypeSymbol symbol)
    {
        for (var type = symbol.BaseType; type is not null; type = type.BaseType)
            yield return FullName(type);
    }

    /// <summary>
    /// The four inherited transport methods, projected exactly as if they had been declared: they
    /// are ordinary RPC shapes, which is what lets the SignalR hosting glue reuse the entrypoint
    /// pipeline instead of adding a second one.
    /// </summary>
    private static IEnumerable<Dispatch> HubTransport () =>
        Rules.HubTransports.Select(static m => new Dispatch(
            new Method(m.CsName, m.JsName, "rpc", m.Return, m.Await),
            new(m.Parameters.Select(static p => new Parameter(p.Name, p.Kind, null)))));

    private static bool IsProjectable (IMethodSymbol method) =>
        method.MethodKind == MethodKind.Ordinary
        && method.DeclaredAccessibility == Accessibility.Public
        && !method.IsStatic
        && method.AssociatedSymbol is null
        && !OverridesObject(method);

    private static bool OverridesObject (IMethodSymbol method)
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
    private static void Project (string kind, IMethodSymbol method, List<Dispatch> dispatches, List<Defect> defects)
    {
        var jsName = Rules.ToJs(method.Name);
        if (Rules.HandlerSlot(kind, method.Name) is { } slot)
        {
            dispatches.Add(new Dispatch(
                new Method(method.Name, jsName, slot, "void", true),
                EquatableArray<Parameter>.Empty));
            return;
        }
        if (kind is not "DurableObject")
        {
            defects.Add(new Defect("CFW014", "Unsupported entrypoint member",
                $"'{method.Name}' is neither a {kind} handler ({Rules.HandlerNames(kind)}) nor a supported RPC method. " +
                "Make it non-public or move it to a collaborator type.",
                LocationInfo.From(method)));
            return;
        }
        if (Rules.Reserved.Contains(jsName))
        {
            defects.Add(new Defect("CFW012", "Reserved RPC method name",
                $"'{method.Name}' would emit the reserved JS prototype member '{jsName}', which workerd " +
                "resolves as a handler or refuses to dispatch. Rename the method.",
                LocationInfo.From(method)));
            return;
        }
        if (ProjectRpc(method, jsName, defects) is { } rpc) dispatches.Add(rpc);
    }

    private static Dispatch? ProjectRpc (IMethodSymbol method, string jsName, List<Defect> defects)
    {
        var (returned, awaited) = ReturnShape(method.ReturnType);
        if (returned is null)
            defects.Add(new Defect("CFW011", "Unsupported RPC return type",
                $"'{method.Name}' returns '{method.ReturnType.ToDisplayString()}'. Supported: void, Task, " +
                "int, long, double, bool and string, each optionally wrapped in Task<>.",
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
        return new Dispatch(new Method(method.Name, jsName, "rpc", returned, awaited), new(parameters));
    }

    /// <summary>
    /// Reads one interface the app marked as its env, with the bindings it declares. Discovery is by
    /// attribute rather than by name: the binding set is app configuration, so the app
    /// names the interface and places it, and nothing is claimed merely for being called the same.
    /// </summary>
    private static Env? ResolveEnv (GeneratorAttributeSyntaxContext ctx)
    {
        if (ctx.TargetSymbol is not INamedTypeSymbol symbol) return null;
        return new Env(
            symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString(),
            new(symbol.GetMembers().OfType<IPropertySymbol>().Select(static p => new Binding(
                p.Name, Space(p.Type), p.Type.Name, p.Type.TypeKind == TypeKind.Interface,
                LocationInfo.From(p)))),
            LocationInfo.From(symbol));
    }

    /// <summary>
    /// Reads one app-side declaration of the dispatch partial, or null for a namesake that derives
    /// from something else. Matched by base type rather than by name alone: <c>ActorRuntime</c> is an
    /// ordinary identifier an app is free to use for a type of its own.
    /// </summary>
    private static ActorRuntimeHalf? ResolveActorRuntime (GeneratorSyntaxContext ctx)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)ctx.Node) is not INamedTypeSymbol symbol)
            return null;
        for (var type = symbol.BaseType; type is not null; type = type.BaseType)
            if (Rules.IsActorRuntimeBase(Space(type), type.Name) && type.Arity == 1)
                return new ActorRuntimeHalf(
                    symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString(),
                    type.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    LocationInfo.From(symbol));
        return null;
    }

    /// <summary>Where the app names the Cloudflare logging surface, or null for a namesake of it.</summary>
    private static LocationInfo? ResolveLogUse (GeneratorSyntaxContext ctx)
    {
        var info = ctx.SemanticModel.GetSymbolInfo(ctx.Node);
        var type = (info.Symbol ?? info.CandidateSymbols.FirstOrDefault()) switch {
            INamedTypeSymbol named => named,
            IMethodSymbol { MethodKind: MethodKind.Constructor } constructor => constructor.ContainingType,
            _ => null
        };
        if (type is null || FullName(type) is not (Rules.LogSink or Rules.LogProvider)) return null;
        return LocationInfo.From(ctx.Node);
    }

    /// <summary>Whether the app declares <c>[assembly: Import(typeof(ILogSink))]</c>.</summary>
    private static bool ImportsLogSink (Compilation compilation) => compilation.Assembly.GetAttributes()
        .Where(static a => a.AttributeClass is { } c && FullName(c) == Rules.ImportAttribute)
        .SelectMany(static a => a.ConstructorArguments)
        // The attribute takes `params Type[]`, so the types arrive as one array-valued argument.
        .SelectMany(static a => a.Kind == TypedConstantKind.Array ? a.Values : [a])
        .Any(static v => v.Value is INamedTypeSymbol type && FullName(type) == Rules.LogSink);

    private static void Execute (SourceProductionContext spc, ImmutableArray<Resolution> resolved,
        ImmutableArray<ActorRuntimeHalf> halves, ImmutableArray<Env> envs,
        ImmutableArray<LocationInfo> logUses, bool importsLogSink)
    {
        var list = resolved
            .OrderBy(static r => r.Entrypoint.Kind, StringComparer.Ordinal)
            .ThenBy(static r => r.Entrypoint.Name, StringComparer.Ordinal)
            .ToArray();
        var defects = list.SelectMany(static r => r.Defects.Items)
            .Concat(Collisions(list))
            .Concat(LogSinkDefect(logUses, importsLogSink))
            .ToList();
        var env = ChooseEnv(envs, list, defects);
        // The dispatch exists to construct and call actors, and it is one half of a partial class
        // whose other half — deriving from the packaged ActorRuntimeBase — the app declares. Three
        // conditions have to hold before it can compile: an app with no Durable Object and no
        // workflow needs no dispatch at all, without an env there is no handle to construct an actor
        // against, and without the app's half the emitted switches inherit nothing they call. The
        // last two are diagnosed and neither emits — a file the app never wrote cannot be fixed by
        // reading its compiler errors. They are checked in that order so an app missing the env is
        // told to mark it before being told to declare a half over it, which has to name the env
        // type. The ESM module is emitted by the publish task either way, because a worker that
        // hosts no actor is a legitimate (fetch-only) worker — lean core.
        var emit = Hosts(list) && env.FullName.Length > 0 && DeclaresRuntimeHalf(halves, env, list, defects);
        foreach (var defect in defects)
            spc.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(defect.Id, defect.Title, "{0}", Rules.Library, defect.Severity, true),
                defect.Location?.ToLocation() ?? Location.None,
                defect.Message));
        if (emit) spc.AddSource("ActorRuntime.g.cs", CsEmitter.Emit(list, env));
    }

    /// <summary>
    /// Whether the app declared its half of the dispatch partial against the env the dispatch is
    /// emitted against, reporting the declaration it has to add when it did not.
    /// </summary>
    /// <remarks>
    /// Without this the generated half was emitted regardless, and a first actor app got a dozen raw
    /// CS0103/CS0246 errors — <c>Track</c>, <c>GetActor</c>, <c>EnvScope</c>, <c>ReadArgs</c>,
    /// <c>ArgInt</c>, <c>JsonInt</c> — out of <c>ActorRuntime.g.cs</c>, a file it never wrote and
    /// cannot edit. None of them named the one missing declaration; every one of them disappears
    /// with it.
    /// </remarks>
    private static bool DeclaresRuntimeHalf (ImmutableArray<ActorRuntimeHalf> halves, Env env,
        IReadOnlyList<Resolution> list, List<Defect> defects)
    {
        var ordered = halves.OrderBy(static h => h.Namespace, StringComparer.Ordinal).ToArray();
        if (Array.Exists(ordered, h => h.Namespace == env.Namespace && h.EnvFullName == env.FullName))
            return true;
        var actor = list.First(static r => r.Entrypoint.Kind is "DurableObject" or "Workflow");
        var where = env.Namespace.Length > 0 ? $"namespace '{env.Namespace}'" : "the global namespace";
        defects.Add(new Defect("CFW050", "Actor runtime half is missing",
            $"'{actor.Entrypoint.Name}' is an actor, and its dispatch is emitted into the app's own " +
            $"'{Rules.ActorRuntimeType}' partial: a partial class cannot span the package boundary, so the " +
            "half that inherits the instance registry and the JSON codecs is the app's to declare. Declare " +
            $"'{Rules.ActorRuntimeDeclaration(Unqualified(env.FullName))}' in {where}, along with the " +
            "runtime interface this app exports to the generated module.",
            // An incompatible half — one closed over another env, or declared beside another
            // namespace's bindings — is the likelier mistake of the two and has a line to point at.
            Array.Find(ordered, h => h.Namespace == env.Namespace)?.Location
            ?? ordered.FirstOrDefault()?.Location ?? actor.Location));
        return false;
    }

    /// <summary>A globally qualified name as source would spell it, for a diagnostic message.</summary>
    private static string Unqualified (string fullName) =>
        fullName.StartsWith("global::", StringComparison.Ordinal) ? fullName.Substring("global::".Length) : fullName;

    private static bool Hosts (IReadOnlyList<Resolution> list) =>
        list.Any(static r => r.Entrypoint.Kind is "DurableObject" or "Workflow");

    /// <summary>
    /// Picks the app's env and reports what the choice ruled out: a second marked interface, an
    /// actor with no env to construct against, and a binding the emitted wrapper cannot adapt.
    /// </summary>
    private static Env ChooseEnv (ImmutableArray<Env> envs, IReadOnlyList<Resolution> list, List<Defect> defects)
    {
        var actor = list.FirstOrDefault(static r => r.Entrypoint.Kind is "DurableObject" or "Workflow");
        var ordered = envs.OrderBy(static e => e.FullName, StringComparer.Ordinal).ToArray();
        if (ordered.Length == 0)
        {
            if (actor is not null)
                defects.Add(new Defect("CFW016", "Worker env interface is missing",
                    $"'{actor.Entrypoint.Name}' is an actor, and constructing one needs the app's env. " +
                    "Mark the interface declaring the worker's bindings with [WorkerEnv].", null));
            return Env.Empty;
        }
        foreach (var extra in ordered.Skip(1))
            defects.Add(new Defect("CFW017", "Worker env interface is ambiguous",
                $"'{extra.FullName}' is also marked [WorkerEnv], and a worker has one env. " +
                $"'{ordered[0].FullName}' was used; unmark the others.", extra.Location));
        var namespaces = list
            .Where(static r => r.Entrypoint.Kind == "DurableObject")
            .Select(static r => Rules.NamespaceType(r.Entrypoint.Name))
            .ToArray();
        foreach (var binding in ordered[0].Bindings.Items)
            if (binding.Handle && !Rules.Adapted(binding.TypeNamespace, binding.TypeName)
                && !namespaces.Contains(binding.TypeName))
                // A warning, not an error: the handle is still passed through, which is right for a
                // binding whose JS shape the guest can import directly. It stops being right the
                // moment the type was meant to be a Durable Object namespace whose class was
                // renamed — and that is the case this exists to make visible rather than silent.
                defects.Add(new Defect("CFW018", "Worker binding has no adapter",
                    $"Binding '{binding.Name}' is a '{binding.TypeName}', which neither Bootsharp.Cloudflare " +
                    "adapts nor names a Durable Object of this app, so the raw handle is passed through." +
                    ActorHint(binding.TypeName), binding.Location, DiagnosticSeverity.Warning));
        return ordered[0];
    }

    /// <summary>
    /// The likeliest cause of an unadapted handle: a binding declared against the <c>I…Namespace</c>
    /// convention whose Durable Object was renamed, leaving the convention pointing at nothing.
    /// </summary>
    private static string ActorHint (string typeName) =>
        typeName.StartsWith("I", StringComparison.Ordinal) && typeName.EndsWith("Namespace", StringComparison.Ordinal)
            ? " If it names a Durable Object namespace, the class has to be called " +
              $"'{typeName.Substring(1, typeName.Length - "INamespace".Length)}'."
            : "";

    /// <summary>
    /// Names an entrypoint would declare at the emitted module's top level. A C# type name is free
    /// to be anything; the module it projects into is one JavaScript scope shared with the runtime
    /// import list, so a clash is a bundle-time redeclaration nothing else would catch.
    /// </summary>
    private static IEnumerable<Defect> Collisions (IReadOnlyList<Resolution> list)
    {
        var taken = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in Rules.RuntimeExports.Concat(Rules.ModuleScope))
            taken[name] = "the worker runtime";
        foreach (var resolution in list)
        {
            var entrypoint = resolution.Entrypoint;
            foreach (var declared in Declares(entrypoint))
            {
                if (taken.TryGetValue(declared, out var owner))
                    yield return new Defect("CFW019", "Colliding worker module declaration",
                        $"'{entrypoint.Name}' projects a module-level '{declared}', which is already " +
                        $"declared by {owner}. Rename the class.", resolution.Location);
                else taken[declared] = $"'{entrypoint.Name}'";
            }
        }
    }

    private static IEnumerable<string> Declares (Entrypoint entrypoint)
    {
        yield return entrypoint.Name;
        if (entrypoint.Kind != "DurableObject") yield break;
        yield return Rules.Adapter(Rules.NamespaceType(entrypoint.Name));
        yield return Rules.Adapter(Rules.StubType(entrypoint.Name));
    }

    private static IEnumerable<Defect> LogSinkDefect (ImmutableArray<LocationInfo> uses, bool imported)
    {
        if (imported || uses.Length == 0) yield break;
        yield return new Defect("CFW015", "Log sink import is missing",
            "This app uses Bootsharp.Cloudflare's logging, whose entries are written by a JavaScript " +
            "handler the worker runtime binds to the guest's ILogSink import. Declare " +
            "[assembly: Import(typeof(Bootsharp.Cloudflare.Logging.ILogSink))]; without it the sink " +
            "does not exist and every log call fails at runtime.",
            uses[0]);
    }

    /// <summary>
    /// The base types are generic over the app's env (and, for a worker, its response record), so
    /// the walk matches on the unbound name — <see cref="INamedTypeSymbol.Name"/> carries no arity
    /// and never on a constructed type the app happened to close.
    /// </summary>
    private static string? BaseKind (INamedTypeSymbol symbol)
    {
        for (var type = symbol.BaseType; type is not null; type = type.BaseType)
            if (Rules.Kind(type.ContainingNamespace.ToDisplayString(), type.Name) is { } kind)
                return kind;
        return null;
    }

    private static (string? Kind, bool Await) ReturnShape (ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { Name: "Task" } task
            && task.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks")
            return task.Arity == 1 ? (ValueKind(task.TypeArguments[0]), true) : ("void", true);
        if (type.SpecialType == SpecialType.System_Void) return ("void", false);
        return (ValueKind(type), false);
    }

    private static string? ValueKind (ITypeSymbol type) => Rules.ValueKind(Space(type), type.Name);

    private static string? ParameterKind (ITypeSymbol type) =>
        Rules.ParameterKind(Space(type), type.Name, type.NullableAnnotation == NullableAnnotation.Annotated);

    /// <summary>Declaring namespace of a type, empty for the global one, as the rules spell it.</summary>
    private static string Space (ITypeSymbol type) =>
        type.ContainingNamespace is null or { IsGlobalNamespace: true }
            ? "" : type.ContainingNamespace.ToDisplayString();

    private static string FullName (INamedTypeSymbol type) =>
        Space(type) is { Length: > 0 } space ? $"{space}.{type.Name}" : type.Name;

    /// <summary>
    /// C# source for a defaulted parameter's value. Only the shapes <see cref="ParameterKind"/>
    /// admits reach here, all of which format; a null literal is the one Roslyn declines to render,
    /// and <c>null</c> is exactly what it means.
    /// </summary>
    private static string Literal (IParameterSymbol parameter) =>
        SymbolDisplay.FormatPrimitive(parameter.ExplicitDefaultValue!, true, false) ?? "null";
}
