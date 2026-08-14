using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Routing;

/// <summary>
/// A source of <see cref="Endpoint"/> instances.
/// </summary>
/// <remarks>
/// Reimplemented rather than vendored, because upstream's version is two things: a list of
/// endpoints, and the machinery for a table that changes while the app runs — an
/// <see cref="Microsoft.Extensions.Primitives.IChangeToken"/> per source, and
/// <c>GetGroupedEndpoints</c>, which composes route-group prefixes through the
/// <c>RoutePatternFactory.Combine</c> overload the vendored selection excludes. A worker's endpoint
/// table is fixed the moment <c>app.Run()</c> is reached and route groups are not in the v1 surface,
/// so both are dropped instead of being carried as members that could never do anything.
/// </remarks>
public abstract class EndpointDataSource
{
    /// <summary>The endpoints this source contributes.</summary>
    public abstract IReadOnlyList<Endpoint> Endpoints { get; }
}

/// <summary>An <see cref="EndpointDataSource"/> built from endpoints added one call at a time.</summary>
public sealed class WorkerEndpointDataSource : EndpointDataSource
{
    private readonly List<Endpoint> endpoints = [];
    private readonly List<Func<Endpoint>> deferred = [];

    public override IReadOnlyList<Endpoint> Endpoints
    {
        get
        {
            // Conventions (.WithName(), .Produces(), filters) are applied to a builder after Map*
            // returns, so an endpoint cannot be built at registration time. Building on first read
            // — which is when the app is asked to route — is the same "build once, at the end"
            // point upstream reaches through its change-token dance.
            if (deferred.Count > 0)
            {
                foreach (var build in deferred) endpoints.Add(build());
                deferred.Clear();
            }
            return endpoints;
        }
    }

    /// <summary>Registers an endpoint to be built when the table is first read.</summary>
    public void Add (Func<Endpoint> build)
    {
        ArgumentNullException.ThrowIfNull(build);
        if (endpoints.Count > 0)
            throw new InvalidOperationException("Endpoints cannot be added after the endpoint table has been read.");
        deferred.Add(build);
    }
}

/// <summary>Defines a contract for a route builder in an application.</summary>
/// <remarks>
/// Upstream's third member, <c>CreateApplicationBuilder()</c>, exists so a branch of the middleware
/// pipeline can be built per endpoint; nothing in this package branches the pipeline, so it is not
/// reproduced.
/// </remarks>
public interface IEndpointRouteBuilder
{
    /// <summary>Services available to endpoints built here.</summary>
    IServiceProvider ServiceProvider { get; }

    /// <summary>The endpoint sources this builder contributes to.</summary>
    ICollection<EndpointDataSource> DataSources { get; }
}
