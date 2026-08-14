using Microsoft.AspNetCore.Builder;

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// Registers endpoint filters on a mapped endpoint.
/// </summary>
/// <remarks>
/// <para>
/// A filter reaches the request because the generated request delegate is built <b>after</b> the
/// endpoint's conventions have run: <c>RouteHandlerServices.Register</c> walks
/// <see cref="Microsoft.AspNetCore.Builder.EndpointBuilder.FilterFactories"/> when it binds, so a
/// filter added here — after the <c>Map*</c> call that returned the builder — is inside the delegate
/// the endpoint ends up carrying.
/// </para>
/// <para>
/// Upstream's generic <c>AddEndpointFilter&lt;TFilterType&gt;()</c> is deliberately absent: it
/// constructs the filter through <c>ActivatorUtilities</c>, which picks a constructor by reflection.
/// Register the filter yourself and pass the instance, or pass a factory — both are one line, and
/// neither roots a constructor NativeAOT would have to keep.
/// </para>
/// </remarks>
public static class EndpointFilterExtensions
{
    /// <summary>Adds a filter instance to the endpoint.</summary>
    public static TBuilder AddEndpointFilter<TBuilder> (this TBuilder builder, IEndpointFilter filter)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(filter);
        return builder.AddEndpointFilterFactory((_, next) => invocation => filter.InvokeAsync(invocation, next));
    }

    /// <summary>Adds a filter written as a delegate.</summary>
    public static TBuilder AddEndpointFilter<TBuilder> (this TBuilder builder,
        Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> routeHandlerFilter)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(routeHandlerFilter);
        return builder.AddEndpointFilterFactory((_, next) => invocation => routeHandlerFilter(invocation, next));
    }

    /// <summary>Adds a factory that builds the filter once, when the endpoint is built.</summary>
    /// <remarks>The rung the other two stand on, and the one to use when the filter needs to know
    /// the handler's shape: the factory sees the
    /// <see cref="EndpointFilterFactoryContext"/> and may return <paramref name="filterFactory"/>'s
    /// <c>next</c> unchanged to opt out for this endpoint.</remarks>
    public static TBuilder AddEndpointFilterFactory<TBuilder> (this TBuilder builder,
        Func<EndpointFilterFactoryContext, EndpointFilterDelegate, EndpointFilterDelegate> filterFactory)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(filterFactory);
        builder.Add(endpoint => endpoint.FilterFactories.Add(filterFactory));
        return builder;
    }
}
