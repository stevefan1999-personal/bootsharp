using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Bootsharp.Cloudflare.Generate.SignalR;

/// <summary>
/// The hub-dispatch component of the one analyzer assembly: for every <c>Hub</c> subclass
/// it emits a name→slot table, static parameter-type arrays feeding <c>IInvocationBinder</c>, and a
/// switch of direct, unboxed calls.
/// </summary>
/// <remarks>
/// <para>Upstream has no generator at all — <c>grep -rl IIncrementalGenerator src/SignalR/server</c>
/// is empty — and its "trim/AOT-compatible" dispatch path is literally
/// <c>_executor = methodInfo.Invoke</c> (<c>ObjectMethodExecutor.cs:76</c>), reached after a
/// <c>GetMethods()</c> + <c>GetInterfaceMap</c> discovery walk that needs
/// <c>[DynamicallyAccessedMembers]</c> on every hub. Emitting the table instead is strictly smaller,
/// strictly faster, and removes the trimmer root.</para>
/// <para> rule holds here as everywhere: total dispatch or a diagnostic. Nothing is
/// emitted for a shape that could not be dispatched correctly.</para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class HubGenerator : IIncrementalGenerator
{
    public void Initialize (IncrementalGeneratorInitializationContext context)
    {
        var hubs = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
                static (ctx, _) => Resolve(ctx))
            .Where(static h => h is not null)
            .Select(static (h, _) => h!)
            .Collect();

        // Payload types the app already declares on a source-generated JSON context. Collected by
        // attribute rather than by walking every type, so the pipeline stays incremental.
        var serializable = context.SyntaxProvider
            .ForAttributeWithMetadataName(HubRules.SerializableAttribute,
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, _) => Declared(ctx))
            .Collect();

        context.RegisterSourceOutput(hubs.Combine(serializable),
            static (spc, data) => Execute(spc, data.Left, data.Right));
    }

    private static HubModel? Resolve (GeneratorSyntaxContext ctx)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)ctx.Node) is not INamedTypeSymbol symbol) return null;
        if (symbol.IsAbstract || !Derives(symbol, HubRules.HubType)) return null;
        var methods = new List<HubMethodModel>();
        var defects = new List<HubDefect>();
        var taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Every public instance method the hub adds on top of the base, base classes included: a
        // hub may split its surface across an intermediate class, and upstream's
        // HubReflectionHelper.GetHubMethods walks the same chain.
        foreach (var method in Methods(symbol))
            Project(method, methods, defects, taken);
        return new HubModel(
            symbol.Name,
            symbol.ContainingNamespace.IsGlobalNamespace ? "" : symbol.ContainingNamespace.ToDisplayString(),
            symbol.InstanceConstructors.Any(static c =>
                c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public),
            new(methods),
            new(defects),
            LocationInfo.From(symbol));
    }

    private static IEnumerable<IMethodSymbol> Methods (INamedTypeSymbol hub)
    {
        for (var type = hub; type is not null; type = type.BaseType)
        {
            if (FullName(type) == HubRules.HubType) yield break;
            foreach (var method in type.GetMembers().OfType<IMethodSymbol>())
                if (method.MethodKind == MethodKind.Ordinary
                    && method.DeclaredAccessibility == Accessibility.Public
                    && !method.IsStatic
                    && method.AssociatedSymbol is null
                    && method.OverriddenMethod is null)
                    yield return method;
        }
    }

    private static void Project (IMethodSymbol method, List<HubMethodModel> methods,
        List<HubDefect> defects, Dictionary<string, string> taken)
    {
        var name = WireName(method);
        // Upstream refuses generic hub methods outright (DefaultHubDispatcher.cs:881-884) because it
        // cannot pick a type argument from a JSON payload; a generator cannot either.
        if (method.IsGenericMethod)
        {
            defects.Add(new HubDefect("CFW042", "Generic hub method",
                $"'{method.Name}' is generic. A hub method's arguments arrive as JSON, so there is " +
                "nothing to infer a type argument from — upstream rejects these at discovery too.",
                LocationInfo.From(method)));
            return;
        }
        // Overloads are the case where a silent choice would be worst: the name→slot table is
        // case-insensitive (upstream's StringComparer.OrdinalIgnoreCase), so two overloads are one
        // wire name and one of them would simply never run.
        if (taken.TryGetValue(name, out var owner))
        {
            defects.Add(new HubDefect("CFW041", "Duplicate hub method name",
                $"'{method.Name}' and '{owner}' both map to the wire name '{name}', which hub " +
                "dispatch resolves case-insensitively. Rename one, or give it a [HubMethodName].",
                LocationInfo.From(method)));
            return;
        }
        var parameters = new List<HubParameter>();
        var refused = false;
        foreach (var parameter in method.Parameters)
        {
            if (parameter.RefKind != RefKind.None)
            {
                defects.Add(new HubDefect("CFW043", "Unsupported hub method parameter",
                    $"Parameter '{parameter.Name}' of '{method.Name}' is passed by " +
                    $"'{parameter.RefKind.ToString().ToLowerInvariant()}'. Hub arguments are " +
                    "deserialized from JSON and can only be passed by value.",
                    LocationInfo.From(parameter)));
                refused = true;
                continue;
            }
            parameters.Add(new HubParameter(parameter.Name, Qualified(parameter.Type), PayloadName(parameter.Type)));
        }
        var (returned, await, payload) = ReturnShape(method.ReturnType);
        if (returned is null)
        {
            defects.Add(new HubDefect("CFW044", "Unsupported hub method return type",
                $"'{method.Name}' returns '{method.ReturnType.ToDisplayString()}'. Supported: void, " +
                "Task, ValueTask, and any serializable value optionally wrapped in Task<> or " +
                "ValueTask<>. Streaming returns (IAsyncEnumerable<T>, ChannelReader<T>) are their " +
                "own package.",
                LocationInfo.From(method)));
            refused = true;
        }
        if (refused) return;
        taken[name] = method.Name;
        methods.Add(new HubMethodModel(method.Name, name, await, returned! != "void", payload, new(parameters)));
    }

    /// <summary>Wire name: <c>[HubMethodName]</c> when present, the method name otherwise.</summary>
    private static string WireName (IMethodSymbol method)
    {
        foreach (var attribute in method.GetAttributes())
            if (attribute.AttributeClass is { } type && FullName(type) == HubRules.MethodNameAttribute
                && attribute.ConstructorArguments.Length == 1
                && attribute.ConstructorArguments[0].Value is string name)
                return name;
        return method.Name;
    }

    /// <returns>Encoded return shape, whether it must be awaited, and the payload type to cover.</returns>
    private static (string? Kind, bool Await, string? Payload) ReturnShape (ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { Name: "Task" or "ValueTask" } awaitable
            && awaitable.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks")
            return awaitable.Arity == 1
                ? ("value", true, PayloadName(awaitable.TypeArguments[0]))
                : ("void", true, null);
        if (type.SpecialType == SpecialType.System_Void) return ("void", false, null);
        // Streaming shapes are refused by name rather than admitted as an opaque payload: returning
        // an IAsyncEnumerable<T> from a hub is a request for server→client streaming, and silently
        // serializing the enumerable object would be the worst possible answer.
        if (type.Name is "IAsyncEnumerable" or "ChannelReader") return (null, false, null);
        return ("value", false, PayloadName(type));
    }

    private static void Execute (SourceProductionContext spc,
        ImmutableArray<HubModel> hubs, ImmutableArray<ImmutableArray<string>> serializable)
    {
        var covered = new HashSet<string>(serializable.SelectMany(static s => s), StringComparer.Ordinal);
        foreach (var hub in hubs.OrderBy(static h => h.Name, StringComparer.Ordinal))
        {
            foreach (var defect in hub.Defects.Items.Concat(Uncovered(hub, covered)).Concat(Constructible(hub)))
                spc.ReportDiagnostic(Diagnostic.Create(
                    new DiagnosticDescriptor(defect.Id, defect.Title, "{0}", HubRules.Library, defect.Severity, true),
                    defect.Location?.ToLocation() ?? Location.None, defect.Message));
            if (hub.Defects.Length == 0 && hub.HasDefaultConstructor)
                spc.AddSource($"{HubRules.DispatcherName(hub.Name)}.g.cs", HubEmitter.Emit(hub));
        }
    }

    /// <summary>
    /// Payload types no source-generated JSON context declares. Reflection-based serialization is off
    /// for a trimmed app, so an undeclared type does not degrade — it throws
    /// <c>NotSupportedException</c> the first time a client sends it.
    /// </summary>
    /// <remarks>
    /// Reported rather than emitted because Roslyn generators cannot chain: a
    /// <c>[JsonSerializable]</c> partial this generator wrote would never be seen by the JSON
    /// generator in the same compilation, so it would compile to nothing and fail identically at
    /// runtime, with the failure now looking like the library's fault.
    /// </remarks>
    private static IEnumerable<HubDefect> Uncovered (HubModel hub, HashSet<string> covered)
    {
        // One report per undeclared type, not one per mention: a Send(Message)/Message round trip
        // names the same type twice and the app has one declaration to add either way.
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in hub.Methods.Items)
        foreach (var payload in Payloads(method))
        {
            if (payload is null || HubRules.Primitives.Contains(payload) || covered.Contains(payload)) continue;
            if (!reported.Add(payload)) continue;
            yield return new HubDefect("CFW040", "Hub payload type has no JSON context",
                $"'{method.CsName}' passes '{payload}', which no [JsonSerializable] declares. Add " +
                $"[JsonSerializable(typeof({payload}))] to a partial JsonSerializerContext of this app " +
                $"and set HubOptions.PayloadTypeInfoResolver to it — reflection-based serialization is " +
                "off under NativeAOT, so an undeclared payload throws at the first client message.",
                hub.Location, DiagnosticSeverity.Warning);
        }
    }

    private static IEnumerable<string?> Payloads (HubMethodModel method) =>
        method.Parameters.Items.Select(static p => p.PayloadName).Concat([method.PayloadName]);

    private static IEnumerable<HubDefect> Constructible (HubModel hub)
    {
        if (hub.HasDefaultConstructor || hub.Defects.Length > 0) yield break;
        yield return new HubDefect("CFW045", "Hub has no parameterless constructor",
            $"'{hub.Name}' cannot be constructed by generated dispatch. Give it a public " +
            "parameterless constructor, or resolve its dependencies from a static composition root " +
            "— there is no hub activator and no per-connection service scope on this host.",
            hub.Location);
    }

    /// <summary>Types one <c>[JsonSerializable]</c>-carrying context declares.</summary>
    private static ImmutableArray<string> Declared (GeneratorAttributeSyntaxContext ctx) =>
        [..ctx.Attributes
            .Select(static a => a.ConstructorArguments.Length > 0 ? a.ConstructorArguments[0].Value : null)
            .OfType<INamedTypeSymbol>()
            .Select(PayloadName)];

    private static bool Derives (INamedTypeSymbol symbol, string baseName)
    {
        for (var type = symbol.BaseType; type is not null; type = type.BaseType)
            if (FullName(type) == baseName)
                return true;
        return false;
    }

    /// <summary>Fully qualified name for emission, arity and nullable annotation included.</summary>
    private static string Qualified (ITypeSymbol type) =>
        type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    /// <summary>
    /// Name a JSON context declares the type under: without nullable annotation (a context declares
    /// the underlying type) and without the <c>global::</c> prefix, so the two sides compare.
    /// </summary>
    private static string PayloadName (ITypeSymbol type)
    {
        var underlying = type is INamedTypeSymbol { Name: "Nullable", Arity: 1 } nullable
            ? nullable.TypeArguments[0] : type;
        return underlying.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            .Replace("global::", "").TrimEnd('?');
    }

    private static string FullName (INamedTypeSymbol type) =>
        type.ContainingNamespace is null or { IsGlobalNamespace: true }
            ? type.Name : $"{type.ContainingNamespace.ToDisplayString()}.{type.Name}";
}
