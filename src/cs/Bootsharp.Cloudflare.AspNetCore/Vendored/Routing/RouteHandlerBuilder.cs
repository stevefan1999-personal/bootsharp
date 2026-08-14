// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/Builder/RouteHandlerBuilder.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Builds conventions that will be used for customization of MapAction <see cref="EndpointBuilder"/> instances.
/// </summary>
public sealed class RouteHandlerBuilder : IEndpointConventionBuilder
{
    private readonly IEnumerable<IEndpointConventionBuilder>? _endpointConventionBuilders;
    private readonly ICollection<Action<EndpointBuilder>>? _conventions;
    private readonly ICollection<Action<EndpointBuilder>>? _finallyConventions;

    internal RouteHandlerBuilder(ICollection<Action<EndpointBuilder>> conventions, ICollection<Action<EndpointBuilder>> finallyConventions)
    {
        _conventions = conventions;
        _finallyConventions = finallyConventions;
    }

    /// <summary>
    /// Instantiates a new <see cref="RouteHandlerBuilder" /> given multiple
    /// <see cref="IEndpointConventionBuilder" /> instances.
    /// </summary>
    /// <param name="endpointConventionBuilders">A sequence of <see cref="IEndpointConventionBuilder" /> instances.</param>
    public RouteHandlerBuilder(IEnumerable<IEndpointConventionBuilder> endpointConventionBuilders)
    {
        _endpointConventionBuilders = endpointConventionBuilders;
    }

    /// <summary>
    /// Adds the specified convention to the builder. Conventions are used to customize <see cref="EndpointBuilder"/> instances.
    /// </summary>
    /// <param name="convention">The convention to add to the builder.</param>
    public void Add(Action<EndpointBuilder> convention)
    {
        if (_conventions is not null)
        {
            _conventions.Add(convention);
        }
        else
        {
            foreach (var endpointConventionBuilder in _endpointConventionBuilders!)
            {
                endpointConventionBuilder.Add(convention);
            }
        }
    }

    /// <inheritdoc />
    public void Finally(Action<EndpointBuilder> finalConvention)
    {
        if (_finallyConventions is not null)
        {
            _finallyConventions.Add(finalConvention);
        }
        else
        {
            foreach (var endpointConventionBuilder in _endpointConventionBuilders!)
            {
                endpointConventionBuilder.Finally(finalConvention);
            }
        }
    }
}
