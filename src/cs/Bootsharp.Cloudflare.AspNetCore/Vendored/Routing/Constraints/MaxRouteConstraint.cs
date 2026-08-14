// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/Constraints/MaxRouteConstraint.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: see this file's entry in Vendored/manifest.json (rendered in Vendored/README.md).
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
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
/// Constrains a route parameter to be an integer with a maximum value.
/// </summary>
public class MaxRouteConstraint : IRouteConstraint, IParameterLiteralNodeMatchingPolicy, ICachableParameterPolicy
#else
public class MaxRouteConstraint : IRouteConstraint
#endif
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MaxRouteConstraint" /> class.
    /// </summary>
    /// <param name="max">The maximum value allowed for the route parameter.</param>
    public MaxRouteConstraint(long max)
    {
        Max = max;
    }

    /// <summary>
    /// Gets the maximum allowed value of the route parameter.
    /// </summary>
    public long Max { get; private set; }

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
#if !BSCF_LEAN
        ArgumentNullException.ThrowIfNull(routeKey);
#endif
        ArgumentNullException.ThrowIfNull(values);

        if (values.TryGetValue(routeKey, out var value) && value != null)
        {
            var valueString = Convert.ToString(value, CultureInfo.InvariantCulture);
            return CheckConstraintCore(valueString);
        }

        return false;
    }

    private bool CheckConstraintCore(string? valueString)
    {
        if (long.TryParse(valueString, NumberStyles.Integer, CultureInfo.InvariantCulture, out var longValue))
        {
            return longValue <= Max;
        }
        return false;
    }

#if !BSCF_LEAN
    bool IParameterLiteralNodeMatchingPolicy.MatchesLiteral(string parameterName, string literal)
    {
        return CheckConstraintCore(literal);
    }
#endif
}
