using System.Collections.Immutable;
using Bootsharp.Cloudflare.Projection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Bootsharp.Cloudflare.Generate.MinimalApi;

/// <summary>
/// Binds <c>app.MapGet(…)</c> and its siblings at compile time.
/// </summary>
/// <remarks>
/// <para>
/// The second generator of this assembly, beside the entrypoint one, and the reason
/// <c>Bootsharp.Cloudflare.AspNetCore</c>'s <c>Map*</c> methods have bodies that only throw: every
/// call site is replaced by an interceptor that reads the request, converts each argument and
/// invokes the handler through a delegate whose type is known when the app compiles. ASP.NET Core's
/// own <c>RequestDelegateFactory</c> does that work at startup with <c>Expression.Compile</c> and
/// 26 reflection roots, which NativeAOT cannot host at all, and Microsoft's
/// Request Delegate Generator cannot be borrowed — its interceptor predicate requires the call to
/// resolve into an assembly literally named <c>Microsoft.AspNetCore.Routing</c>, and the SDK only
/// enables it behind a <c>FrameworkReference</c> this package deliberately does not take.
/// </para>
/// <para>
/// Three things are decided here that upstream defers to startup, because deciding them here is the
/// whole point: the route pattern is parsed (so a bad pattern is a build error and the precedence
/// the matcher sorts by is a constant), the service-or-body ambiguity is answered from the DI
/// registrations the compilation contains rather than from <c>IServiceProviderIsService</c>, and
/// JSON metadata coverage is checked against the app's own <c>JsonSerializerContext</c>.
/// </para>
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class MinimalApiGenerator : IIncrementalGenerator
{
    private const string serializableAttribute = "System.Text.Json.Serialization.JsonSerializableAttribute";

    private static readonly string[] registrationMethods = [
        "AddSingleton", "AddScoped", "AddTransient",
        "AddKeyedSingleton", "AddKeyedScoped", "AddKeyedTransient",
        "TryAddSingleton", "TryAddScoped", "TryAddTransient"
    ];

    public void Initialize (IncrementalGeneratorInitializationContext context)
    {
        var endpoints = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => EndpointResolver.IsCandidate(node),
                static (ctx, _) => EndpointResolver.Resolve(ctx))
            .Where(static candidate => candidate is not null)
            .Select(static (candidate, _) => candidate!)
            .Collect();

        // The compile-time stand-in for IServiceProviderIsService: what the app registers is what a
        // complex parameter can be resolved as. Registrations the generator cannot see (a helper in
        // another assembly, a conditional registration) simply do not answer, and the parameter
        // falls to the body — which is why [FromServices] exists as the escape hatch.
        var services = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => IsRegistration(node),
                static (ctx, _) => Registered(ctx))
            .Where(static type => type is not null)
            .Select(static (type, _) => type!)
            .Collect();

        var serialized = context.SyntaxProvider
            .ForAttributeWithMetadataName(serializableAttribute,
                static (node, _) => node is ClassDeclarationSyntax or RecordDeclarationSyntax,
                static (ctx, _) => Serializable(ctx))
            .Collect();

        context.RegisterSourceOutput(endpoints.Combine(services).Combine(serialized),
            static (spc, data) => Execute(spc, data.Left.Left, data.Left.Right, data.Right));
    }

    private static bool IsRegistration (SyntaxNode node) =>
        node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member }
        && registrationMethods.Contains(member.Name.Identifier.ValueText);

    /// <summary>The service type one registration call registers, or null when it is not one.</summary>
    private static string? Registered (GeneratorSyntaxContext ctx)
    {
        if (ctx.SemanticModel.GetOperation(ctx.Node) is not IInvocationOperation operation) return null;
        var method = operation.TargetMethod;
        if (method.ContainingNamespace?.ToDisplayString() != "Microsoft.Extensions.DependencyInjection") return null;
        if (method.TypeArguments.Length > 0) return EndpointResolver.Display(method.TypeArguments[0]);
        // The non-generic overloads take the service type as a typeof argument.
        foreach (var argument in operation.Arguments)
            if (argument.Value is ITypeOfOperation typeOf) return EndpointResolver.Display(typeOf.TypeOperand);
        return null;
    }

    /// <summary>Types one <c>JsonSerializerContext</c> declares metadata for.</summary>
    private static EquatableArray<string> Serializable (GeneratorAttributeSyntaxContext ctx)
    {
        var types = new List<string>();
        foreach (var attribute in ctx.TargetSymbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != serializableAttribute) continue;
            if (attribute.ConstructorArguments.FirstOrDefault().Value is ITypeSymbol type)
                types.Add(EndpointResolver.Display(type));
        }
        return new(types);
    }

    private static void Execute (SourceProductionContext spc, ImmutableArray<EndpointCandidate> candidates,
        ImmutableArray<string> services, ImmutableArray<EquatableArray<string>> serialized)
    {
        var defects = candidates.SelectMany(static candidate => candidate.Defects.Items).ToList();
        var registered = new HashSet<string>(services, StringComparer.Ordinal);
        var known = new HashSet<string>(serialized.SelectMany(static array => array.Items), StringComparer.Ordinal);
        var resolved = candidates
            .Where(static candidate => candidate.Model is not null && candidate.Interception is not null)
            .Select(candidate => Resolve(candidate, registered, defects))
            .Where(static candidate => candidate.Model is not null)
            .ToArray();
        defects.AddRange(Ambiguities(resolved));
        defects.AddRange(resolved.SelectMany(candidate => Unserializable(candidate, known)));
        foreach (var defect in defects)
            spc.ReportDiagnostic(Diagnostic.Create(
                new DiagnosticDescriptor(defect.Id, defect.Title, "{0}", Rules.Library, defect.Severity, true),
                defect.Location?.ToLocation() ?? Location.None,
                defect.Message));
        if (resolved.Length == 0) return;
        var groups = resolved
            .GroupBy(static candidate => candidate.Model!.SignatureKey, StringComparer.Ordinal)
            .Select(static (group, index) => new EndpointGroup(
                [.. group.Select(static candidate => candidate.Model!)], index,
                [.. group.Select(static candidate => candidate.Interception!)]))
            .ToArray();
        spc.AddSource("BootsharpMinimalApi.g.cs", EndpointEmitter.Emit(groups));
    }

    /// <summary>
    /// Answers the one binding question that needs the whole compilation: whether a complex
    /// parameter is a registered service, the request body, or neither.
    /// </summary>
    /// <remarks>
    /// Upstream asks <c>IServiceProviderIsService</c> the same question while the app starts. Asking
    /// the registrations instead means the answer is available to the emitted code — no runtime
    /// branch, no <c>ResolveJsonBodyOrService</c> closure — and that "neither" is a build error
    /// rather than a 400 on the first request that reached the endpoint.
    /// </remarks>
    private static EndpointCandidate Resolve (EndpointCandidate candidate, HashSet<string> registered, List<Defect> defects)
    {
        var model = candidate.Model!;
        if (!model.Parameters.Items.Any(static p => p.Source is BindingSource.ServiceOrBody or BindingSource.ServiceOnly))
            return candidate;
        var parameters = new List<EndpointParameter>();
        foreach (var parameter in model.Parameters.Items)
        {
            if (parameter.Source is not (BindingSource.ServiceOrBody or BindingSource.ServiceOnly))
            {
                parameters.Add(parameter);
                continue;
            }
            if (registered.Contains(parameter.Type)) parameters.Add(parameter with { Source = BindingSource.Service });
            else if (parameter.Source == BindingSource.ServiceOrBody)
                parameters.Add(parameter with { Source = BindingSource.JsonBody });
            else
            {
                defects.Add(new Defect("CFW026", "Unsupported route handler parameter",
                    $"Parameter '{parameter.Name}' of type '{parameter.Type}' has no binding source: it is " +
                    "not a route parameter of the pattern, it cannot be parsed from a string, this endpoint's " +
                    "methods carry no body, and no registration in this project makes it a service. Mark it " +
                    "[FromServices] if it is registered elsewhere, or [FromBody] if it really is the request body.",
                    parameter.At ?? candidate.Location));
                return candidate with { Model = null };
            }
        }
        return candidate with { Model = model with { Parameters = new(parameters) } };
    }

    /// <summary>
    /// Two endpoints that accept the same method on the same pattern have the same precedence, so
    /// which one answers is registration order — an ambiguity ASP.NET Core reports as an exception
    /// on the request that hits it. Reporting it here costs nothing and is one build earlier.
    /// </summary>
    private static IEnumerable<Defect> Ambiguities (IReadOnlyList<EndpointCandidate> candidates)
    {
        var seen = new Dictionary<string, EndpointCandidate>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            var model = candidate.Model!;
            // A pattern whose methods are only known at runtime cannot be compared with certainty.
            if (model.VerbsFromArgument) continue;
            var verbs = model.Verbs.Length == 0 ? ["*"] : model.Verbs.Items;
            foreach (var verb in verbs)
            {
                var key = $"{verb} {model.Pattern}";
                if (seen.ContainsKey(key))
                    yield return new Defect("CFW025", "Ambiguous route",
                        $"'{verb} {model.Pattern}' is mapped more than once. Both endpoints have the same " +
                        "precedence, so the first one registered answers every request and the other is " +
                        "unreachable.", candidate.Location, DiagnosticSeverity.Warning);
                else seen[key] = candidate;
            }
        }
    }

    /// <summary>
    /// Types the emitted code will ask <c>JsonOptions</c> for, that no context in this compilation
    /// declares.
    /// </summary>
    /// <remarks>A warning rather than an error: the app may register a context declared in another
    /// assembly, which this generator cannot see. When it really is missing, the emitted code fails
    /// while the endpoint table is built, naming the type and the attribute to add.</remarks>
    private static IEnumerable<Defect> Unserializable (EndpointCandidate candidate, HashSet<string> known)
    {
        var model = candidate.Model!;
        var types = model.Parameters.Items
            .Where(static parameter => parameter.Source == BindingSource.JsonBody)
            .Select(static parameter => parameter.Type)
            .Concat(model.Response is { Kind: ResponseKind.Json, Primitive: false }
                ? [model.Response.ValueType] : []);
        foreach (var type in types.Select(Bare).Distinct())
        {
            if (known.Contains(type)) continue;
            yield return new Defect("CFW029", "JSON metadata is missing for a route handler type",
                $"'{type}' crosses this endpoint as JSON, and no JsonSerializerContext in this project " +
                $"declares it. Add [JsonSerializable(typeof({type.Split('.').Last()}))] to a context and " +
                "register it with builder.Services + JsonOptions.AddContext: serialization here resolves " +
                "through source-generated metadata only, with no reflective fallback to cover the gap.",
                candidate.Location, DiagnosticSeverity.Warning);
        }
    }

    private static string Bare (string type) => type.EndsWith("?") ? type.Substring(0, type.Length - 1) : type;
}
