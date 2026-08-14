// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/Constraints/CompositeRouteConstraint.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: see this file's entry in Vendored/manifest.json (rendered in Vendored/README.md).
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#if !BSCF_LEAN
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing.Matching;
#else
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
#endif
namespace Microsoft.AspNetCore.Routing.Constraints;

#if !BSCF_LEAN
/// <summary>
/// Constrains a route by several child constraints.
/// </summary>
public class CompositeRouteConstraint : IRouteConstraint, IParameterLiteralNodeMatchingPolicy
#else
public class CompositeRouteConstraint : IRouteConstraint
#endif
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CompositeRouteConstraint" /> class.
    /// </summary>
    /// <param name="constraints">The child constraints that must match for this constraint to match.</param>
    public CompositeRouteConstraint(IEnumerable<IRouteConstraint> constraints)
    {
        ArgumentNullException.ThrowIfNull(constraints);

        Constraints = constraints;
    }

    /// <summary>
    /// Gets the child constraints that must match for this constraint to match.
    /// </summary>
    public IEnumerable<IRouteConstraint> Constraints { get; private set; }

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

        foreach (var constraint in Constraints)
        {
#if !BSCF_LEAN
            if (!constraint.Match(httpContext, route, routeKey, values, routeDirection))
#else
            if (!constraint.Match(routeKey, values))
#endif
            {
                return false;
            }
        }

        return true;
    }

#if !BSCF_LEAN
    bool IParameterLiteralNodeMatchingPolicy.MatchesLiteral(string parameterName, string literal)
    {
        foreach (var constraint in Constraints)
        {
            if (constraint is IParameterLiteralNodeMatchingPolicy literalConstraint && !literalConstraint.MatchesLiteral(parameterName, literal))
            {
                return false;
            }
        }

        return true;
    }
#endif
}
