// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Abstractions/src/IApplicationBuilder.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Defines a class that provides the mechanisms to configure an application's request pipeline.
/// </summary>
public interface IApplicationBuilder
{
    /// <summary>
    /// Gets or sets the <see cref="IServiceProvider"/> that provides access to the application's service container.
    /// </summary>
    IServiceProvider ApplicationServices { get; set; }

    /// <summary>
    /// Gets the set of HTTP features the application's server provides.
    /// </summary>
    /// <remarks>
    /// An empty collection is returned if a server wasn't specified for the application builder.
    /// </remarks>
    IFeatureCollection ServerFeatures { get; }

    /// <summary>
    /// Gets a key/value collection that can be used to share data between middleware.
    /// </summary>
    IDictionary<string, object?> Properties { get; }

    /// <summary>
    /// Adds a middleware delegate to the application's request pipeline.
    /// </summary>
    /// <param name="middleware">The middleware delegate.</param>
    /// <returns>The <see cref="IApplicationBuilder"/>.</returns>
    IApplicationBuilder Use(Func<RequestDelegate, RequestDelegate> middleware);

    /// <summary>
    /// Creates a new <see cref="IApplicationBuilder"/> that shares the <see cref="Properties"/> of this
    /// <see cref="IApplicationBuilder"/>.
    /// </summary>
    /// <returns>The new <see cref="IApplicationBuilder"/>.</returns>
    IApplicationBuilder New();

    /// <summary>
    /// Builds the delegate used by this application to process HTTP requests.
    /// </summary>
    /// <returns>The request handling delegate.</returns>
    RequestDelegate Build();
}
