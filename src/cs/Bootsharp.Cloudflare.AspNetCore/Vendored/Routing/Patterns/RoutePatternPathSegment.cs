// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/Patterns/RoutePatternPathSegment.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: see this file's entry in Vendored/manifest.json (rendered in Vendored/README.md).
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Linq;

namespace Microsoft.AspNetCore.Routing.Patterns;

/// <summary>
/// Represents a path segment in a route pattern. Instances of <see cref="RoutePatternPathSegment"/> are
/// immutable.
/// </summary>
/// <remarks>
/// Route patterns are made up of URL path segments, delimited by <c>/</c>. A
/// <see cref="RoutePatternPathSegment"/> contains a group of
/// <see cref="RoutePatternPart"/> that represent the structure of a segment
/// in a route pattern.
/// </remarks>
[DebuggerDisplay("{DebuggerToString()}")]
#if !BSCF_LEAN
public sealed class RoutePatternPathSegment
#else
public sealed class RoutePatternPathSegment
#endif
{
    internal RoutePatternPathSegment(IReadOnlyList<RoutePatternPart> parts)
    {
        Parts = parts;
    }

    /// <summary>
    /// Returns <c>true</c> if the segment contains a single part;
    /// otherwise returns <c>false</c>.
    /// </summary>
    public bool IsSimple => Parts.Count == 1;

    /// <summary>
    /// Gets the list of parts in this segment.
    /// </summary>
    public IReadOnlyList<RoutePatternPart> Parts { get; }

    internal string DebuggerToString()
    {
        return DebuggerToString(Parts);
    }

    internal static string DebuggerToString(IReadOnlyList<RoutePatternPart> parts)
    {
        return string.Join(string.Empty, parts.Select(p => p.DebuggerToString()));
    }
}
