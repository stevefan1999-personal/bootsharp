// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/IRouteNameMetadata.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Routing;

/// <summary>
/// Represents metadata used during link generation to find
/// the associated endpoint using route name.
/// </summary>
public interface IRouteNameMetadata
{
    /// <summary>
    /// Gets the route name. Can be <see langword="null"/>.
    /// </summary>
    string? RouteName { get; }
}
