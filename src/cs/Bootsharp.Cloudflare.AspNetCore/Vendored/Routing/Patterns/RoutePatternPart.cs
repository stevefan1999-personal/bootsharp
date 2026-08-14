// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/Patterns/RoutePatternPart.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: see this file's entry in Vendored/manifest.json (rendered in Vendored/README.md).
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Routing.Patterns;

/// <summary>
/// Represents a part of a route pattern.
/// </summary>
#if !BSCF_LEAN
public abstract class RoutePatternPart
#else
public abstract class RoutePatternPart
#endif
{
    // This class is **not** an extensibility point - every part of the routing system
    // needs to be aware of what kind of parts we support.
    //
    // It is abstract so we can add semantics later inside the library.
    private protected RoutePatternPart(RoutePatternPartKind partKind)
    {
        PartKind = partKind;
    }

    /// <summary>
    /// Gets the <see cref="RoutePatternPartKind"/> of this part.
    /// </summary>
    public RoutePatternPartKind PartKind { get; }

    /// <summary>
    /// Returns <c>true</c> if this part is literal text. Otherwise returns <c>false</c>.
    /// </summary>
    public bool IsLiteral => PartKind == RoutePatternPartKind.Literal;

    /// <summary>
    /// Returns <c>true</c> if this part is a route parameter. Otherwise returns <c>false</c>.
    /// </summary>
    public bool IsParameter => PartKind == RoutePatternPartKind.Parameter;

    /// <summary>
    /// Returns <c>true</c> if this part is an optional separator. Otherwise returns <c>false</c>.
    /// </summary>
    public bool IsSeparator => PartKind == RoutePatternPartKind.Separator;

    internal abstract string DebuggerToString();
}
