// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/Constraints/GuidRouteConstraint.cs
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
/// Constrains a route parameter to represent only <see cref="Guid"/> values.
/// Matches values specified in any of the five formats "N", "D", "B", "P", or "X",
/// supported by Guid.ToString(string) and Guid.ToString(String, IFormatProvider) methods.
/// </summary>
public class GuidRouteConstraint : IRouteConstraint, IParameterLiteralNodeMatchingPolicy, ICachableParameterPolicy
#else
public class GuidRouteConstraint : IRouteConstraint
#endif
{
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

        if (values.TryGetValue(routeKey, out var value) && value != null)
        {
            if (value is Guid)
            {
                return true;
            }

            var valueString = Convert.ToString(value, CultureInfo.InvariantCulture);
            return CheckConstraintCore(valueString);
        }

        return false;
    }

    private static bool CheckConstraintCore(string? valueString)
    {
        return Guid.TryParse(valueString, out _);
    }

#if !BSCF_LEAN
    bool IParameterLiteralNodeMatchingPolicy.MatchesLiteral(string parameterName, string literal)
    {
        return CheckConstraintCore(literal);
    }
#endif
}
