// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/Constraints/FileNameRouteConstraint.cs
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
/// Constrains a route parameter to represent only file name values. Does not validate that
/// the route value contains valid file system characters, or that the value represents
/// an actual file on disk.
/// </summary>
/// <remarks>
/// <para>
/// This constraint can be used to disambiguate requests for static files versus dynamic
/// content served from the application.
/// </para>
/// <para>
/// This constraint determines whether a route value represents a file name by examining
/// the last URL Path segment of the value (delimited by <c>/</c>). The last segment
/// must contain the dot (<c>.</c>) character followed by one or more non-(<c>.</c>) characters.
/// </para>
/// <para>
/// If the route value does not contain a <c>/</c> then the entire value will be interpreted
/// as the last segment.
/// </para>
/// <para>
/// The <see cref="FileNameRouteConstraint"/> does not attempt to validate that the value contains
/// a legal file name for the current operating system.
/// </para>
/// <para>
/// The <see cref="FileNameRouteConstraint"/> does not attempt to validate that the value represents
/// an actual file on disk.
/// </para>
/// <para>
/// <list type="bullet">
///     <listheader>
///         <term>Examples of route values that will be matched as file names</term>
///         <description>description</description>
///     </listheader>
///     <item>
///         <term><c>/a/b/c.txt</c></term>
///         <description>Final segment contains a <c>.</c> followed by other characters.</description>
///     </item>
///     <item>
///         <term><c>/hello.world.txt</c></term>
///         <description>Final segment contains a <c>.</c> followed by other characters.</description>
///     </item>
///     <item>
///         <term><c>hello.world.txt</c></term>
///         <description>Final segment contains a <c>.</c> followed by other characters.</description>
///     </item>
///     <item>
///         <term><c>.gitignore</c></term>
///         <description>Final segment contains a <c>.</c> followed by other characters.</description>
///     </item>
/// </list>
/// <list type="bullet">
///     <listheader>
///         <term>Examples of route values that will be rejected as non-file-names</term>
///         <description>description</description>
///     </listheader>
///     <item>
///         <term><c>/a/b/c</c></term>
///         <description>Final segment does not contain a <c>.</c>.</description>
///     </item>
///     <item>
///         <term><c>/a/b.d/c</c></term>
///         <description>Final segment does not contain a <c>.</c>.</description>
///     </item>
///     <item>
///         <term><c>/a/b.d/c/</c></term>
///         <description>Final segment is empty.</description>
///     </item>
///     <item>
///         <term><c></c></term>
///         <description>Value is empty</description>
///     </item>
/// </list>
/// </para>
/// </remarks>
public class FileNameRouteConstraint : IRouteConstraint, IParameterLiteralNodeMatchingPolicy, ICachableParameterPolicy
#else
public class FileNameRouteConstraint : IRouteConstraint
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

        if (values.TryGetValue(routeKey, out var obj) && obj != null)
        {
            var value = Convert.ToString(obj, CultureInfo.InvariantCulture);
            return IsFileName(value);
        }

        // No value or null value.
        return false;
    }

    // This is used both here and in NonFileNameRouteConstraint
    // Any changes to this logic need to update the docs in those places.
    internal static bool IsFileName(ReadOnlySpan<char> value)
    {
        if (value.Length == 0)
        {
            // Not a file name because empty.
            return false;
        }

        var lastSlashIndex = value.LastIndexOf('/');
        if (lastSlashIndex >= 0)
        {
            value = value.Slice(lastSlashIndex + 1);
        }

        var dotIndex = value.IndexOf('.');
        if (dotIndex == -1)
        {
            // No dot.
            return false;
        }

        for (var i = dotIndex + 1; i < value.Length; i++)
        {
            if (value[i] != '.')
            {
                return true;
            }
        }

        return false;
    }

#if !BSCF_LEAN
    bool IParameterLiteralNodeMatchingPolicy.MatchesLiteral(string parameterName, string literal)
    {
        return IsFileName(literal);
    }
#endif
}
