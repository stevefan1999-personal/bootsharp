using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.Routing.Template;

namespace Bootsharp.Cloudflare.AspNetCore;

/// <summary>
/// Matches a request path against the endpoint table, in precedence order.
/// </summary>
/// <remarks>
/// <para>
/// ASP.NET Core's matcher is a 6,682-line DFA built for hundred-route tables with hierarchical
/// policy jump tables, and one of its jump-table shapes is IL-emitted — already dead under
/// NativeAOT. A worker has 5-40 routes, so the table is sorted once at build time
/// by <see cref="RoutePrecedence.ComputeInbound(Microsoft.AspNetCore.Routing.Patterns.RoutePattern)"/>
/// — the vendored, unmodified upstream computation, so <c>/api/health</c> beats <c>/api/{id}</c>
/// exactly as it does in ASP.NET Core — and scanned linearly.
/// </para>
/// <para>
/// The per-candidate work is upstream's <c>RoutePatternMatcher</c>, which is what buys
/// <c>{id}</c>, <c>{id?}</c>, <c>{id=default}</c> and <c>{*rest}</c>; the old shim compared segment
/// counts and so 404'd on <c>/app/a/b</c> for the pattern <c>/app/{rest}</c>.
/// </para>
/// <para>
/// Method mismatch is distinguished from path mismatch, because they are different answers: a path
/// no route claims is 404, a path claimed only by other methods is 405 with an <c>Allow</c> header.
/// </para>
/// </remarks>
public sealed class WorkerRouteMatcher
{
    private readonly Candidate[] candidates;

    private readonly record struct Candidate (
        RouteEndpoint Endpoint,
        RoutePatternMatcher Matcher,
        string[]? Methods,
        RouteValueDictionary Defaults,
        (string Key, IRouteConstraint Constraint, bool Optional)[] Constraints);

    /// <summary>Sorts the endpoint table into match order.</summary>
    public WorkerRouteMatcher (IReadOnlyList<Endpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        candidates = endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => new Candidate(
                endpoint,
                new RoutePatternMatcher(endpoint.RoutePattern, new RouteValueDictionary(endpoint.RoutePattern.Defaults)),
                endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods as string[]
                ?? endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.ToArray(),
                new RouteValueDictionary(endpoint.RoutePattern.Defaults),
                ResolveConstraints(endpoint.RoutePattern)))
            // Order first (an explicit user override), then precedence, then registration order
            // upstream's EndpointComparer ordering, minus the metadata-based tiebreakers that only
            // matter to policies this package does not have.
            .OrderBy(candidate => candidate.Endpoint.Order)
            .ThenBy(candidate => Precedence(candidate.Endpoint))
            .ToArray();
    }

    /// <summary>Inbound precedence of an endpoint: the generator's value when it has one.</summary>
    /// <remarks>Both branches are the same computation — see
    /// <see cref="Microsoft.AspNetCore.Builder.RoutePrecedenceMetadata"/> — so a table mixing
    /// generated and hand-registered endpoints is ordered consistently.</remarks>
    private static decimal Precedence (RouteEndpoint endpoint) =>
        endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Builder.RoutePrecedenceMetadata>()?.Value
        ?? RoutePrecedence.ComputeInbound(endpoint.RoutePattern);

    /// <summary>The outcome of matching one request against the table.</summary>
    /// <param name="Endpoint">The matched endpoint, or null when nothing matched the path.</param>
    /// <param name="RouteValues">Values captured from the path, empty when nothing matched.</param>
    /// <param name="AllowedMethods">Methods other candidates on this path accept, when the path
    /// matched but the method did not; null otherwise.</param>
    public readonly record struct Match (
        RouteEndpoint? Endpoint,
        RouteValueDictionary RouteValues,
        string[]? AllowedMethods);

    /// <summary>Finds the endpoint for a request.</summary>
    public Match Find (PathString path, string method)
    {
        List<string>? allowed = null;
        foreach (var candidate in candidates)
        {
            var values = new RouteValueDictionary();
            if (!candidate.Matcher.TryMatch(path, values)) continue;
            if (!Satisfies(candidate.Constraints, values)) continue;
            if (candidate.Methods is null || candidate.Methods.Length == 0 || Accepts(candidate.Methods, method))
            {
                // Defaults the pattern declares but the path did not supply, e.g. {page=1}.
                foreach (var (key, value) in candidate.Defaults)
                    if (!values.ContainsKey(key)) values[key] = value;
                return new Match(candidate.Endpoint, values, null);
            }
            (allowed ??= []).AddRange(candidate.Methods);
        }
        return new Match(null, [], allowed?.Distinct().ToArray());
    }

    /// <summary>
    /// Resolves the pattern's inline constraints once, when the table is built.
    /// </summary>
    /// <remarks>An unknown constraint is therefore a startup failure rather than a 404 on the one
    /// route that used it — the same "fail where the mistake is" trade the compile-time pattern
    /// parsing makes.</remarks>
    private static (string, IRouteConstraint, bool)[] ResolveConstraints (RoutePattern pattern)
    {
        List<(string, IRouteConstraint, bool)>? resolved = null;
        foreach (var parameter in pattern.Parameters)
            foreach (var policy in parameter.ParameterPolicies)
            {
                var constraint = policy.ParameterPolicy as IRouteConstraint
                    ?? (policy.Content is { } content ? RouteConstraints.Resolve(content) : null);
                if (constraint is null) continue;
                (resolved ??= []).Add((parameter.Name, constraint, parameter.IsOptional || parameter.Default is not null));
            }
        return resolved?.ToArray() ?? [];
    }

    // An absent optional parameter has nothing to constrain: {id:int?} must match a path that omits
    // id, which is upstream's OptionalRouteConstraint wrapper expressed as a check rather than a
    // second constraint object.
    private static bool Satisfies ((string Key, IRouteConstraint Constraint, bool Optional)[] constraints, RouteValueDictionary values)
    {
        foreach (var (key, constraint, optional) in constraints)
        {
            if (optional && (!values.TryGetValue(key, out var value) || value is null)) continue;
            if (!constraint.Match(key, values)) return false;
        }
        return true;
    }

    // HEAD is answered by the GET endpoint, as it is in ASP.NET Core: the response body is dropped
    // on the way out rather than a second endpoint being registered.
    private static bool Accepts (string[] methods, string method)
    {
        foreach (var candidate in methods)
            if (HttpMethods.Equals(candidate, method) ||
                (HttpMethods.IsHead(method) && HttpMethods.IsGet(candidate)))
                return true;
        return false;
    }
}
