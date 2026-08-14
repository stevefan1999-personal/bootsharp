// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Abstractions/src/Routing/IRouteValuesFeature.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Http.Features;

/// <summary>
/// A feature interface for routing values. Use <see cref="HttpContext.Features"/>
/// to access the values associated with the current request.
/// </summary>
public interface IRouteValuesFeature
{
    /// <summary>
    /// Gets or sets the <see cref="RouteValueDictionary"/> associated with the current
    /// request.
    /// </summary>
    RouteValueDictionary RouteValues { get; set; }
}
