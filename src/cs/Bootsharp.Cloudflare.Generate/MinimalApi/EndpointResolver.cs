using Bootsharp.Cloudflare.Projection;
using Microsoft.AspNetCore.Analyzers.Infrastructure;
using Microsoft.AspNetCore.App.Analyzers.Infrastructure;
using Microsoft.AspNetCore.Analyzers.RouteEmbeddedLanguage.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Bootsharp.Cloudflare.Generate.MinimalApi;

/// <summary>
/// Resolves a <c>Map*</c> call site into the model its interceptor is emitted from: the route
/// pattern parsed, every handler parameter placed on the binding ladder, the return shape
/// classified, and anything refused reported at the call site that wrote it.
/// </summary>
internal static class EndpointResolver
{
    /// <summary>Assembly whose <c>Map*</c> overloads are ours to intercept.</summary>
    private const string package = "Bootsharp.Cloudflare.AspNetCore";

    private const string extensions = "Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions";

    /// <summary>Verbs whose requests never carry a body, so a complex parameter is not one.</summary>
    /// <remarks>Upstream's rule, and it is a rule about the endpoint rather than the request: if any
    /// accepted method is bodyless, inference from the body is off for all of them.</remarks>
    private static readonly string[] bodylessVerbs = ["GET", "HEAD", "DELETE", "OPTIONS"];

    private static readonly Dictionary<string, string> verbsByMethod = new(StringComparer.Ordinal) {
        ["MapGet"] = "GET",
        ["MapPost"] = "POST",
        ["MapPut"] = "PUT",
        ["MapDelete"] = "DELETE",
        ["MapPatch"] = "PATCH"
    };

    /// <summary>Whether a node is worth asking the semantic model about.</summary>
    public static bool IsCandidate (SyntaxNode node) =>
        node is InvocationExpressionSyntax invocation
        && Name(invocation.Expression) is { } name
        && (verbsByMethod.ContainsKey(name) || name is "Map" or "MapMethods");

    private static string? Name (ExpressionSyntax expression) => expression switch {
        MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
        IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
        _ => null
    };

    /// <summary>Resolves a candidate call site, or returns null when it is not one of ours.</summary>
    public static EndpointCandidate? Resolve (GeneratorSyntaxContext ctx)
    {
        var syntax = (InvocationExpressionSyntax)ctx.Node;
        if (ctx.SemanticModel.GetOperation(syntax) is not IInvocationOperation operation) return null;
        var method = operation.TargetMethod;
        if (method.ContainingType?.ToDisplayString() != extensions) return null;
        // Identity, not name: an app is free to write its own EndpointRouteBuilderExtensions, and
        // intercepting that would replace someone else's implementation with ours.
        if (method.ContainingAssembly?.Name != package) return null;
        var location = LocationInfo.From(syntax);
        var defects = new List<Defect>();
        var model = Resolve(ctx, operation, method.Name, location, defects);
        var interception = Intercept(ctx, syntax);
        if (interception is null && !Refused(defects))
            defects.Add(new Defect("CFW020", "Route handler cannot be intercepted",
                $"'{method.Name}' at this call site has no interceptable location, so the compile-time " +
                "binding of  cannot replace it. Call it directly rather than through a " +
                "delegate, a dynamic invocation or a generated expression.", location));
        return new EndpointCandidate(Refused(defects) ? null : model, new(defects), location, interception);
    }

    private static InterceptedLocation? Intercept (GeneratorSyntaxContext ctx, InvocationExpressionSyntax syntax)
    {
#pragma warning disable RSEXPERIMENTAL002 // Roslyn's interceptable-location API, which the RDG uses too.
        var location = ctx.SemanticModel.GetInterceptableLocation(syntax);
        return location is null ? null : new InterceptedLocation(location.Version, location.Data, location.GetDisplayLocation());
#pragma warning restore RSEXPERIMENTAL002
    }

    private static EndpointModel? Resolve (GeneratorSyntaxContext ctx, IInvocationOperation operation,
        string mapMethod, LocationInfo? location, List<Defect> defects)
    {
        if (Argument(operation, "pattern") is not { } patternArgument) return null;
        if (patternArgument.Value.ConstantValue is not { HasValue: true, Value: string pattern })
        {
            defects.Add(new Defect("CFW021", "Route pattern is not a compile-time constant",
                $"'{mapMethod}' needs a literal route pattern: the pattern is parsed while the app is " +
                "compiled, which is what turns a malformed route into a build error and " +
                "lets parameters bind without reflection. Inline the pattern or make it a const.",
                LocationInfo.From(patternArgument.Value.Syntax) ?? location));
            return null;
        }
        if (!RouteTemplateParser.TryParse(pattern, out var template, out var error))
        {
            defects.Add(new Defect("CFW022", "Invalid route pattern",
                $"'{pattern}' is not a valid route pattern. {error}",
                LocationInfo.From(patternArgument.Value.Syntax) ?? location));
            return null;
        }
        foreach (var parameter in template.Parameters.Items)
            foreach (var policy in parameter.Policies.Items)
                if (RouteConstraintNames.Resolve(policy) is { } reason)
                    defects.Add(new Defect("CFW023", "Unsupported route constraint",
                        $"The constraint '{policy}' on route parameter '{parameter.Name}' {reason}",
                        LocationInfo.From(patternArgument.Value.Syntax) ?? location));
        if (Argument(operation, "handler") is not { } handlerArgument) return null;
        if (Handler(handlerArgument.Value) is not { } handler)
        {
            defects.Add(new Defect("CFW026", "Route handler is not a lambda or method group",
                $"'{mapMethod}' needs a handler whose signature is known when the app is compiled: a " +
                "lambda, a local function or a method group. A value of a delegate type cannot be bound " +
                "here, and there is no reflection-based binder to fall back to on this platform.",
                LocationInfo.From(handlerArgument.Value.Syntax) ?? location));
            return null;
        }
        var verbs = Verbs(operation, mapMethod);
        var allowsBody = mapMethod == "MapMethods"
            ? verbs.Length > 0 && !verbs.Any(static verb => bodylessVerbs.Contains(verb))
            : !bodylessVerbs.Contains(verbsByMethod.TryGetValue(mapMethod, out var verb) ? verb : "");
        var wellKnown = WellKnownTypes.GetOrCreate(ctx.SemanticModel.Compilation);
        var parameters = new List<EndpointParameter>();
        foreach (var symbol in handler.Parameters)
            if (Bind(symbol, template, allowsBody, wellKnown, defects, location) is { } bound) parameters.Add(bound);
        var response = Respond(handler.ReturnType, defects, location);
        if (response is null || Refused(defects)) return null;
        return new EndpointModel(
            mapMethod,
            new(mapMethod == "MapMethods" ? [] : verbs),
            mapMethod == "MapMethods",
            pattern,
            new(parameters),
            response,
            HandlerType(handler),
            template.Precedence,
            new(template.Parameters.Items.Select(static p => p.Name).ToArray()));
    }

    /// <summary>Accepted verbs, as far as the call site states them.</summary>
    /// <remarks><c>MapMethods</c> passes its list through at runtime, but the compile-time value is
    /// still needed: whether a complex parameter is a body depends on it. A list the generator
    /// cannot read is treated as bodyless, which downgrades to "say which one you meant" rather than
    /// to a wrong guess.</remarks>
    private static string[] Verbs (IInvocationOperation operation, string mapMethod)
    {
        if (verbsByMethod.TryGetValue(mapMethod, out var verb)) return [verb];
        if (mapMethod != "MapMethods") return [];
        if (Argument(operation, "httpMethods")?.Value is not { } value) return [];
        var elements = Unwrap(value) switch {
            IArrayCreationOperation { Initializer: { } initializer } => initializer.ElementValues,
            ICollectionExpressionOperation collection => collection.Elements,
            _ => default
        };
        if (elements.IsDefaultOrEmpty) return [];
        var verbs = new List<string>();
        foreach (var element in elements)
            if (Unwrap(element).ConstantValue is { HasValue: true, Value: string text }) verbs.Add(text.ToUpperInvariant());
            else return [];
        return [.. verbs];
    }

    private static IOperation Unwrap (IOperation operation) =>
        operation is IConversionOperation conversion ? Unwrap(conversion.Operand) : operation;

    private static IArgumentOperation? Argument (IInvocationOperation operation, string name) =>
        operation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == name);

    private static IMethodSymbol? Handler (IOperation value) => Unwrap(value) switch {
        IDelegateCreationOperation creation => creation.Target switch {
            IAnonymousFunctionOperation lambda => lambda.Symbol,
            IMethodReferenceOperation reference => reference.Method,
            _ => null
        },
        _ => null
    };

    /// <summary>The delegate shape the emitted <c>Cast</c> pins the handler to.</summary>
    /// <remarks>Default values are part of it, and load-bearing: a lambda with an optional parameter
    /// has no <c>Func&lt;&gt;</c> natural type, so the compiler synthesises an anonymous delegate type
    /// for it. Writing the same defaults here makes the cast's lambda unify with the handler's
    /// synthesised type instead of failing at startup with an InvalidCastException.</remarks>
    private static string HandlerType (IMethodSymbol handler)
    {
        var parameters = handler.Parameters.Select(static (parameter, index) =>
            $"{Display(parameter.Type)} arg{index}" +
            (parameter.HasExplicitDefaultValue ? $" = {parameter.GetDefaultValueString()}" : ""));
        return $"{Display(handler.ReturnType)} ({string.Join(", ", parameters)})";
    }

    private static EndpointParameter? Bind (IParameterSymbol symbol, RouteTemplate template, bool allowsBody,
        WellKnownTypes wellKnown, List<Defect> defects, LocationInfo? location)
    {
        var at = LocationInfo.From(symbol) ?? location;
        if (symbol.RefKind != RefKind.None)
        {
            defects.Add(new Defect("CFW026", "Unsupported route handler parameter",
                $"Parameter '{symbol.Name}' is passed by reference, which a bound handler cannot be.", at));
            return null;
        }
        if (Special(symbol.Type) is { } special)
            return new EndpointParameter(symbol.Name, Display(symbol.Type), Display(symbol.Type),
                special, symbol.Name, true, null, ParseKind.String, false, false);
        var attribute = Attributed(symbol);
        var name = attribute.Name ?? symbol.Name;
        var optional = symbol.HasExplicitDefaultValue || symbol.Type.NullableAnnotation == NullableAnnotation.Annotated
            || symbol.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        var defaultExpression = symbol.HasExplicitDefaultValue ? symbol.GetDefaultValueString() : null;
        if (attribute.Form)
        {
            defects.Add(new Defect("CFW026", "Unsupported binding source",
                $"Parameter '{symbol.Name}' binds from a form. A worker request arrives as a buffered " +
                "body with no multipart or urlencoded reader in this package, so form " +
                "binding would have to be invented rather than bound. Read the body as JSON instead.", at));
            return null;
        }
        if (attribute.Source == BindingSource.Service)
            return Bound(symbol, BindingSource.Service, symbol.Name, optional, at);
        if (attribute.Source == BindingSource.JsonBody)
            return Body(symbol, allowsBody, optional, defects, at, explicitly: true);
        if (attribute.Source == BindingSource.Route
            && !template.Parameters.Items.Any(parameter =>
                string.Equals(parameter.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            defects.Add(new Defect("CFW024", "Route parameter is not in the pattern",
                $"Parameter '{symbol.Name}' binds from route value '{name}', which the pattern does not " +
                "declare. ASP.NET Core discovers this when the app starts; here the pattern is parsed " +
                "while it compiles, so it is a build error instead.", at));
            return null;
        }
        var isArray = symbol.Type is IArrayTypeSymbol;
        var element = symbol.Type is IArrayTypeSymbol array ? array.ElementType : symbol.Type;
        var value = element.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            ? ((INamedTypeSymbol)element).TypeArguments[0]
            : element;
        var parsable = ParsabilityHelper.GetParsability(value, wellKnown, out var method) == Parsability.Parsable;
        var source = attribute.Source ?? Infer(symbol, template, parsable, isArray, allowsBody);
        if (source is BindingSource.ServiceOrBody or BindingSource.ServiceOnly or BindingSource.Service)
            return Bound(symbol, source.Value, symbol.Name, optional, at);
        if (source is null)
        {
            defects.Add(new Defect("CFW026", "Unsupported route handler parameter",
                $"Parameter '{symbol.Name}' of type '{symbol.Type.ToDisplayString()}' has no binding source " +
                "this package can infer: it is not a route parameter of the pattern, it is not parsable from " +
                "a string, and its method never carries a body. Mark it [FromServices] if it is a service, " +
                "[FromBody] if it really is the request body, or give it a type with a TryParse.", at));
            return null;
        }
        if (!parsable)
        {
            defects.Add(new Defect("CFW026", "Unsupported route handler parameter",
                $"Parameter '{symbol.Name}' binds from the {Describe(source.Value)}, which is text, but " +
                $"'{value.ToDisplayString()}' cannot be parsed from a string — it has no TryParse, does not " +
                "implement IParsable<T> and is not an enum.", at));
            return null;
        }
        if (isArray && source == BindingSource.Route)
        {
            defects.Add(new Defect("CFW026", "Unsupported route handler parameter",
                $"Parameter '{symbol.Name}' is an array bound from the route, but a route value is a single " +
                "string. Bind arrays from the query string or a header.", at));
            return null;
        }
        return new EndpointParameter(
            symbol.Name,
            Display(symbol.Type),
            Display(value),
            source.Value,
            name,
            optional,
            defaultExpression,
            Parse(method),
            isArray,
            element.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T);
    }

    /// <summary>A parameter bound whole, without text parsing: a service, or the body.</summary>
    private static EndpointParameter Bound (IParameterSymbol symbol, BindingSource source, string name,
        bool optional, LocationInfo? at) =>
        new(symbol.Name, Display(symbol.Type), Display(symbol.Type), source, name, optional, null,
            ParseKind.String, false, false, at);

    private static EndpointParameter? Body (IParameterSymbol symbol, bool allowsBody, bool optional,
        List<Defect> defects, LocationInfo? at, bool explicitly)
    {
        if (!allowsBody && explicitly)
            // A warning rather than an error: the shape is bindable and the app may know something the
            // generator does not (a proxy that rewrites the method), but the request this endpoint sees
            // will not carry a body, so binding it can only ever 400.
            defects.Add(new Defect("CFW027", "Body binding on a method that carries no body",
                $"Parameter '{symbol.Name}' binds from the request body, but this endpoint only accepts " +
                "methods whose requests have no body (GET, HEAD, DELETE, OPTIONS), so the body is always " +
                "absent and the request is answered with 400.", at, DiagnosticSeverity.Warning));
        return new EndpointParameter(symbol.Name, Display(symbol.Type), Display(symbol.Type),
            BindingSource.JsonBody, symbol.Name, optional, null, ParseKind.String, false, false);
    }

    /// <summary>
    /// The implicit half of the ladder: a name the pattern declares binds from the route, anything
    /// parsable binds from the query, and a complex type is a service or the body.
    /// </summary>
    private static BindingSource? Infer (IParameterSymbol symbol, RouteTemplate template,
        bool parsable, bool isArray, bool allowsBody)
    {
        if (!isArray && template.Parameters.Items.Any(parameter =>
                string.Equals(parameter.Name, symbol.Name, StringComparison.OrdinalIgnoreCase)))
            return BindingSource.Route;
        if (parsable || symbol.Type.SpecialType == SpecialType.System_String) return BindingSource.Query;
        if (isArray) return null;
        return allowsBody ? BindingSource.ServiceOrBody : BindingSource.ServiceOnly;
    }

    private static string Describe (BindingSource source) => source switch {
        BindingSource.Route => "route",
        BindingSource.Query => "query string",
        BindingSource.Header => "headers",
        _ => source.ToString()
    };

    private static ParseKind Parse (ParsabilityMethod? method) => method switch {
        ParsabilityMethod.Enum => ParseKind.Enum,
        ParsabilityMethod.Uri => ParseKind.Uri,
        ParsabilityMethod.IParsable => ParseKind.IParsable,
        ParsabilityMethod.TryParseWithFormatProvider => ParseKind.TryParseWithFormat,
        ParsabilityMethod.TryParse => ParseKind.TryParse,
        _ => ParseKind.String
    };

    private static BindingSource? Special (ITypeSymbol type) => type.ToDisplayString() switch {
        "Microsoft.AspNetCore.Http.HttpContext" => BindingSource.HttpContext,
        "Microsoft.AspNetCore.Http.HttpRequest" => BindingSource.HttpRequest,
        "Microsoft.AspNetCore.Http.HttpResponse" => BindingSource.HttpResponse,
        "System.Threading.CancellationToken" => BindingSource.CancellationToken,
        "System.Security.Claims.ClaimsPrincipal" => BindingSource.ClaimsPrincipal,
        _ => null
    };

    /// <summary>The binding source a <c>[From…]</c> attribute states, with the name it overrides.</summary>
    private static (BindingSource? Source, string? Name, bool Form) Attributed (IParameterSymbol symbol)
    {
        foreach (var data in symbol.GetAttributes())
        {
            var name = data.AttributeClass?.ToDisplayString();
            var source = name switch {
                "Microsoft.AspNetCore.Mvc.FromRouteAttribute" => BindingSource.Route,
                "Microsoft.AspNetCore.Mvc.FromQueryAttribute" => BindingSource.Query,
                "Microsoft.AspNetCore.Mvc.FromHeaderAttribute" => BindingSource.Header,
                "Microsoft.AspNetCore.Mvc.FromBodyAttribute" => BindingSource.JsonBody,
                "Microsoft.AspNetCore.Mvc.FromServicesAttribute" => BindingSource.Service,
                _ => (BindingSource?)null
            };
            if (name == "Microsoft.AspNetCore.Mvc.FromFormAttribute") return (null, null, true);
            if (source is null) continue;
            var named = data.NamedArguments.FirstOrDefault(argument => argument.Key == "Name").Value.Value as string;
            return (source, string.IsNullOrEmpty(named) ? null : named, false);
        }
        return (null, null, false);
    }

    /// <summary>Whether anything reported stops the call site from being intercepted.</summary>
    /// <remarks>Warnings do not: the shape they describe still binds, and the emission is what makes
    /// the warning's subject reachable at all.</remarks>
    private static bool Refused (List<Defect> defects) =>
        defects.Any(static defect => defect.Severity == DiagnosticSeverity.Error);

    private static EndpointResponse? Respond (ITypeSymbol type, List<Defect> defects, LocationInfo? location)
    {
        var awaited = false;
        var returned = type;
        if (type is INamedTypeSymbol named && named.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks"
            && named.Name is "Task" or "ValueTask")
        {
            awaited = true;
            returned = named.TypeArguments.Length == 1 ? named.TypeArguments[0] : null!;
        }
        if (returned is null || returned.SpecialType == SpecialType.System_Void)
            return new EndpointResponse(awaited, ResponseKind.Void, "");
        if (returned.SpecialType == SpecialType.System_String)
            return new EndpointResponse(awaited, ResponseKind.String, Display(returned));
        if (Implements(returned, "Microsoft.AspNetCore.Http.IResult"))
            return new EndpointResponse(awaited, ResponseKind.Result, Display(returned));
        if (returned.SpecialType == SpecialType.System_Object)
        {
            defects.Add(new Defect("CFW028", "Unsupported route handler return type",
                "A handler returning 'object' cannot be written: this package serializes through " +
                "source-generated JSON metadata resolved for a known type, and there is no reflective " +
                "resolver behind it. Return IResult (TypedResults.Ok(value) keeps the type), a string, " +
                "or the value's own type.", location));
            return null;
        }
        return new EndpointResponse(awaited, ResponseKind.Json, Display(returned),
            returned.SpecialType != SpecialType.None || returned.TypeKind == TypeKind.Enum);
    }

    private static bool Implements (ITypeSymbol type, string interfaceName) =>
        type.ToDisplayString() == interfaceName
        || type.AllInterfaces.Any(candidate => candidate.ToDisplayString() == interfaceName);

    /// <summary>Fully qualified rendering, nullable annotations included, as the emitter needs it.</summary>
    /// <remarks><c>void</c> is spelled as the keyword rather than qualified: the display format
    /// renders it <c>global::System.Void</c>, and C# forbids naming that type (CS0673), so a
    /// synchronously void handler would otherwise emit a delegate signature that cannot compile.</remarks>
    public static string Display (ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_Void ? "void" : type.ToDisplayString(displayFormat);

    // Keywords are dropped in favour of the qualified names (global::System.String, not string) for
    // the reason the RDG's own format drops them: the emitted text is read next to upstream's
    // baselines, and a qualified name cannot be shadowed by a user type that happens to share it.
    private static readonly SymbolDisplayFormat displayFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .WithMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
            | SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers);
}
