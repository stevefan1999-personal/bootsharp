using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Conventions that name and annotate an endpoint after it was mapped.
/// </summary>
/// <remarks>
/// The subset of upstream's <c>RoutingEndpointConventionBuilderExtensions</c> whose implementation
/// is a metadata append — every one of them is <c>builder.Add(b =&gt; b.Metadata.Add(…))</c> spelled
/// once, so reproducing them costs nothing and spares user code from writing the convention out by
/// hand. Deliberately absent: <c>ExcludeFromDescription</c> and the <c>Produces*</c> family, which
/// exist to describe an endpoint to an OpenAPI document generator this package does not ship
/// metadata nothing reads is metadata NativeAOT carries for no one.
/// </remarks>
public static class RoutingEndpointConventionBuilderExtensions
{
    /// <summary>Adds metadata items to the endpoint.</summary>
    public static TBuilder WithMetadata<TBuilder> (this TBuilder builder, params object[] items)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(items);
        builder.Add(endpoint =>
        {
            foreach (var item in items) endpoint.Metadata.Add(item);
        });
        return builder;
    }

    /// <summary>Names the endpoint, for logs and diagnostics.</summary>
    /// <remarks>Upstream's name is also what <c>LinkGenerator</c> resolves <c>*AtRoute</c> results
    /// against; there is no link generator here, so the name is diagnostic only.</remarks>
    public static TBuilder WithName<TBuilder> (this TBuilder builder, string endpointName)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(endpointName);
        return builder.WithMetadata(new EndpointNameMetadata(endpointName), new RouteNameMetadata(endpointName));
    }

    /// <summary>Sets the endpoint's display name.</summary>
    public static TBuilder WithDisplayName<TBuilder> (this TBuilder builder, string displayName)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(displayName);
        builder.Add(endpoint => endpoint.DisplayName = displayName);
        return builder;
    }

    /// <summary>Sets the endpoint's display name from the one it already had.</summary>
    public static TBuilder WithDisplayName<TBuilder> (this TBuilder builder, Func<EndpointBuilder, string> func)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(func);
        builder.Add(endpoint => endpoint.DisplayName = func(endpoint));
        return builder;
    }
}
