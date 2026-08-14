using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Maps route handlers onto an <see cref="IEndpointRouteBuilder"/>.
/// </summary>
/// <remarks>
/// <para>
/// These are the call sites <c>Bootsharp.Cloudflare.Generate</c> intercepts. Each one
/// takes the handler as a bare <see cref="Delegate"/> exactly as ASP.NET Core does, so user code
/// ports unchanged; the generator replaces the call with one that binds the handler's parameters at
/// compile time and forwards to <see cref="RouteHandlerServices"/>.
/// </para>
/// <para>
/// Reaching a body here therefore means the call was <b>not</b> intercepted, and there is no
/// <c>RequestDelegateFactory</c> to fall back to — that is the point: ASP.NET Core's own factory is
/// mechanically impossible under NativeAOT (eight <c>Expression.Compile</c> sites, 26 reflection
/// roots), which is why every unsupported signature is a build error from the generator rather than
/// a runtime surprise. The throw exists for the one case a build error cannot cover: a project that
/// references this package without the generator.
/// </para>
/// </remarks>
public static class EndpointRouteBuilderExtensions
{
    /// <summary>Maps a <c>GET</c> handler.</summary>
    public static RouteHandlerBuilder MapGet (this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern, Delegate handler) =>
        throw NotIntercepted(nameof(MapGet), pattern);

    /// <summary>Maps a <c>POST</c> handler.</summary>
    public static RouteHandlerBuilder MapPost (this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern, Delegate handler) =>
        throw NotIntercepted(nameof(MapPost), pattern);

    /// <summary>Maps a <c>PUT</c> handler.</summary>
    public static RouteHandlerBuilder MapPut (this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern, Delegate handler) =>
        throw NotIntercepted(nameof(MapPut), pattern);

    /// <summary>Maps a <c>DELETE</c> handler.</summary>
    public static RouteHandlerBuilder MapDelete (this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern, Delegate handler) =>
        throw NotIntercepted(nameof(MapDelete), pattern);

    /// <summary>Maps a <c>PATCH</c> handler.</summary>
    public static RouteHandlerBuilder MapPatch (this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern, Delegate handler) =>
        throw NotIntercepted(nameof(MapPatch), pattern);

    /// <summary>Maps a handler for the listed methods.</summary>
    public static RouteHandlerBuilder MapMethods (this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern, IEnumerable<string> httpMethods, Delegate handler) =>
        throw NotIntercepted(nameof(MapMethods), pattern);

    /// <summary>Maps a handler for every method.</summary>
    public static RouteHandlerBuilder Map (this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern, Delegate handler) =>
        throw NotIntercepted(nameof(Map), pattern);

    private static InvalidOperationException NotIntercepted (string method, string pattern) => new(
        $"{method}(\"{pattern}\", …) was not intercepted by Bootsharp.Cloudflare.Generate. Route " +
        "handlers are bound at compile time on this platform — there is no reflection-based binder " +
        "to fall back to. Reference the Bootsharp.Cloudflare package (which carries the generator) " +
        "and make sure the pattern is a compile-time constant and the handler a lambda, local " +
        "function or method group.");
}

/// <summary>
/// The seam generated interceptors call once they have bound a handler's parameters.
/// </summary>
/// <remarks>
/// Named after upstream's <c>RouteHandlerServices</c>, which carries the same
/// <c>[EditorBrowsable(Never)]</c> "intended to be consumed from the generator only" contract. The
/// signature differs in one deliberate way: the pattern arrives already parsed, because this
/// package parses route patterns at compile time rather than deferring to a runtime
/// <c>RouteParameterNames</c> check.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class RouteHandlerServices
{
    /// <summary>Registers an endpoint whose handler is already a <see cref="RequestDelegate"/>.</summary>
    /// <param name="endpoints">The builder the endpoint belongs to.</param>
    /// <param name="pattern">The route pattern, parsed here so that the generator can hand over
    /// the original text and diagnostics stay attributable to the user's source.</param>
    /// <param name="requestDelegate">The bound handler.</param>
    /// <param name="httpMethods">Accepted methods, or null for all of them.</param>
    /// <param name="displayName">Diagnostic name for the endpoint.</param>
    public static RouteHandlerBuilder Map (
        IEndpointRouteBuilder endpoints,
        string pattern,
        RequestDelegate requestDelegate,
        IEnumerable<string>? httpMethods,
        string? displayName = null)
    {
        ArgumentNullException.ThrowIfNull(requestDelegate);
        return Register(endpoints, pattern, httpMethods, null, _ => requestDelegate, null, displayName);
    }

    /// <summary>
    /// Registers an endpoint the generator bound: the request delegate is built from the handler
    /// once the endpoint is, so that filters registered after the <c>Map*</c> call are visible to it.
    /// </summary>
    /// <remarks>
    /// The parameter list mirrors upstream's generator seam — handler, methods, metadata populator,
    /// delegate factory — with three differences that follow from this package's decisions. There is
    /// no <c>RequestDelegateFactoryOptions</c>/<c>RequestDelegateMetadataResult</c> pair, because
    /// both exist to carry state between <c>RequestDelegateFactory</c> and the endpoint builder and
    /// there is no factory here; the <see cref="EndpointBuilder"/> is passed directly instead. No
    /// <see cref="System.Reflection.MethodInfo"/> is taken, because upstream's is used only to
    /// rebuild <c>ParameterInfo</c> metadata the generator already resolved at compile time
    ///. And <paramref name="precedence"/> arrives precomputed, which is
    /// the compile-time route parsing of reaching the matcher.
    /// </remarks>
    /// <param name="endpoints">The builder the endpoint belongs to.</param>
    /// <param name="pattern">The route pattern, as written at the call site.</param>
    /// <param name="handler">The user's handler, passed to <paramref name="createRequestDelegate"/>.</param>
    /// <param name="httpMethods">Accepted methods, or null for all of them.</param>
    /// <param name="populateMetadata">Adds inferred metadata; runs before the user's conventions.</param>
    /// <param name="createRequestDelegate">Binds the handler once conventions have been applied.</param>
    /// <param name="precedence">Inbound precedence computed at build time from the same rules
    /// <c>RoutePrecedence.ComputeInbound</c> applies, or null to have the matcher compute it.</param>
    public static RouteHandlerBuilder Map (
        IEndpointRouteBuilder endpoints,
        string pattern,
        Delegate handler,
        IEnumerable<string>? httpMethods,
        Action<EndpointBuilder> populateMetadata,
        Func<Delegate, EndpointBuilder, RequestDelegate> createRequestDelegate,
        decimal? precedence)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(populateMetadata);
        ArgumentNullException.ThrowIfNull(createRequestDelegate);
        return Register(endpoints, pattern, httpMethods, populateMetadata,
            builder => createRequestDelegate(handler, builder), precedence, null);
    }

    private static RouteHandlerBuilder Register (
        IEndpointRouteBuilder endpoints,
        string pattern,
        IEnumerable<string>? httpMethods,
        Action<EndpointBuilder>? populateMetadata,
        Func<EndpointBuilder, RequestDelegate> createRequestDelegate,
        decimal? precedence,
        string? displayName)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(pattern);
        var source = endpoints.DataSources.OfType<WorkerEndpointDataSource>().FirstOrDefault()
            ?? throw new InvalidOperationException("The endpoint builder has no worker endpoint source.");
        var conventions = new List<Action<EndpointBuilder>>();
        var finallyConventions = new List<Action<EndpointBuilder>>();
        var methods = httpMethods?.ToArray();
        source.Add(() =>
        {
            var builder = new RouteEndpointBuilder(Unbound, RoutePatternFactory.Parse(pattern), order: 0)
            {
                DisplayName = displayName ?? (methods is null ? pattern : $"HTTP: {string.Join(", ", methods)} {pattern}"),
                ApplicationServices = endpoints.ServiceProvider,
            };
            if (methods is not null) builder.Metadata.Add(new HttpMethodMetadata(methods));
            if (precedence is { } value) builder.Metadata.Add(new RoutePrecedenceMetadata(value));
            populateMetadata?.Invoke(builder);
            foreach (var convention in conventions) convention(builder);
            foreach (var convention in finallyConventions) convention(builder);
            // Last, so that a filter registered by a convention above is part of the delegate the
            // endpoint ends up carrying — the reason the delegate is built here and not at Map time.
            builder.RequestDelegate = createRequestDelegate(builder);
            return builder.Build();
        });
        return new RouteHandlerBuilder([new ConventionCollector(conventions, finallyConventions)]);
    }

    // RouteEndpointBuilder demands a delegate at construction and the real one is not bound until
    // the conventions have run; this stands in for the window between the two and never runs.
    private static Task Unbound (HttpContext context) =>
        throw new InvalidOperationException("The endpoint's handler was never bound.");

    private sealed class ConventionCollector (
        ICollection<Action<EndpointBuilder>> conventions,
        ICollection<Action<EndpointBuilder>> finallyConventions) : IEndpointConventionBuilder
    {
        public void Add (Action<EndpointBuilder> convention) => conventions.Add(convention);
        public void Finally (Action<EndpointBuilder> finalConvention) => finallyConventions.Add(finalConvention);
    }
}

/// <summary>
/// The inbound precedence of an endpoint's pattern, computed when the app was compiled.
/// </summary>
/// <remarks>
/// ASP.NET Core computes precedence while building its matcher, from a pattern it only sees at
/// startup. This package parses patterns at compile time, so the value is known then
/// and travels with the endpoint instead — the matcher sorts by it rather than deriving it. An
/// endpoint registered without the generator carries no such metadata and is ordered by the same
/// computation upstream uses, so the two orders are one order.
/// </remarks>
/// <param name="Value">The <c>RoutePrecedence.ComputeInbound</c> value for the endpoint's pattern.</param>
public sealed record RoutePrecedenceMetadata (decimal Value);
