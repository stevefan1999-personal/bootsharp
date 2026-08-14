// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/RouteNameMetadata.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.AspNetCore.Shared;

namespace Microsoft.AspNetCore.Routing;

/// <summary>
/// Metadata used during link generation to find the associated endpoint using route name.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public sealed class RouteNameMetadata : IRouteNameMetadata
{
    /// <summary>
    /// Creates a new instance of <see cref="RouteNameMetadata"/> with the provided route name.
    /// </summary>
    /// <param name="routeName">The route name. Can be <see langword="null"/>.</param>
    public RouteNameMetadata(string? routeName)
    {
        RouteName = routeName;
    }

    /// <summary>
    /// Gets the route name. Can be <see langword="null"/>.
    /// </summary>
    public string? RouteName { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return DebuggerHelpers.GetDebugText(nameof(RouteName), RouteName);
    }
}
