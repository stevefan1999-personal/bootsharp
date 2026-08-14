// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/Constraints/OptionalRouteConstraint.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: see this file's entry in Vendored/manifest.json (rendered in Vendored/README.md).
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if !BSCF_LEAN
using Microsoft.AspNetCore.Http;
#else
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
#endif

namespace Microsoft.AspNetCore.Routing.Constraints;

#if !BSCF_LEAN
/// <summary>
/// Defines a constraint on an optional parameter. If the parameter is present, then it is constrained by InnerConstraint.
/// </summary>
public class OptionalRouteConstraint : IRouteConstraint
#else
public class OptionalRouteConstraint : IRouteConstraint
#endif
{
    /// <summary>
    /// Creates a new <see cref="OptionalRouteConstraint"/> instance given the <paramref name="innerConstraint"/>.
    /// </summary>
    /// <param name="innerConstraint"></param>
    public OptionalRouteConstraint(IRouteConstraint innerConstraint)
    {
        ArgumentNullException.ThrowIfNull(innerConstraint);

        InnerConstraint = innerConstraint;
    }

    /// <summary>
    /// Gets the <see cref="IRouteConstraint"/> associated with the optional parameter.
    /// </summary>
    public IRouteConstraint InnerConstraint { get; }

    /// <inheritdoc />
    public bool Match(
#if !BSCF_LEAN
        HttpContext? httpContext,
        IRouter? route,
        string routeKey,
        RouteValueDictionary values,
        RouteDirection routeDirection)
#else
        string routeKey,
        RouteValueDictionary values)
#endif
    {
        ArgumentNullException.ThrowIfNull(routeKey);
        ArgumentNullException.ThrowIfNull(values);

        if (values.TryGetValue(routeKey, out _))
        {
            return InnerConstraint.Match(
#if !BSCF_LEAN
                httpContext,
                route,
#endif
                routeKey,
#if !BSCF_LEAN
                values,
                routeDirection);
#else
                values);
#endif
        }

        return true;
    }
}
