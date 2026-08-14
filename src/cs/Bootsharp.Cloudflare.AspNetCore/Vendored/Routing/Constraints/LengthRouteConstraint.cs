// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/Constraints/LengthRouteConstraint.cs
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
/// Constrains a route parameter to be a string of a given length or within a given range of lengths.
/// </summary>
public class LengthRouteConstraint : IRouteConstraint, IParameterLiteralNodeMatchingPolicy, ICachableParameterPolicy
#else
public class LengthRouteConstraint : IRouteConstraint
#endif
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LengthRouteConstraint" /> class that constrains
    /// a route parameter to be a string of a given length.
    /// </summary>
    /// <param name="length">The length of the route parameter.</param>
    public LengthRouteConstraint(int length)
    {
        if (length < 0)
        {
            var errorMessage = Resources.FormatArgumentMustBeGreaterThanOrEqualTo(0);
            throw new ArgumentOutOfRangeException(nameof(length), length, errorMessage);
        }

        MinLength = MaxLength = length;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LengthRouteConstraint" /> class that constrains
    /// a route parameter to be a string of a given length.
    /// </summary>
    /// <param name="minLength">The minimum length allowed for the route parameter.</param>
    /// <param name="maxLength">The maximum length allowed for the route parameter.</param>
    public LengthRouteConstraint(int minLength, int maxLength)
    {
        if (minLength < 0)
        {
            var errorMessage = Resources.FormatArgumentMustBeGreaterThanOrEqualTo(0);
            throw new ArgumentOutOfRangeException(nameof(minLength), minLength, errorMessage);
        }

        if (maxLength < 0)
        {
            var errorMessage = Resources.FormatArgumentMustBeGreaterThanOrEqualTo(0);
            throw new ArgumentOutOfRangeException(nameof(maxLength), maxLength, errorMessage);
        }

        if (minLength > maxLength)
        {
            var errorMessage =
                Resources.FormatRangeConstraint_MinShouldBeLessThanOrEqualToMax("minLength", "maxLength");
            throw new ArgumentOutOfRangeException(nameof(minLength), minLength, errorMessage);
        }

        MinLength = minLength;
        MaxLength = maxLength;
    }

    /// <summary>
    /// Gets the minimum length allowed for the route parameter.
    /// </summary>
    public int MinLength { get; }

    /// <summary>
    /// Gets the maximum length allowed for the route parameter.
    /// </summary>
    public int MaxLength { get; }

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
            var valueString = Convert.ToString(value, CultureInfo.InvariantCulture)!;
            return CheckConstraintCore(valueString);
        }

        return false;
    }

    private bool CheckConstraintCore(string valueString)
    {
        var length = valueString.Length;
        return length >= MinLength && length <= MaxLength;
    }

#if !BSCF_LEAN
    bool IParameterLiteralNodeMatchingPolicy.MatchesLiteral(string parameterName, string literal)
    {
        return CheckConstraintCore(literal);
    }
#endif
}
