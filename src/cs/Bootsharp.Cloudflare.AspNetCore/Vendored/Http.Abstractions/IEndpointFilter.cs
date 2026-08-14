// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Abstractions/src/IEndpointFilter.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// Provides an interface for implementing a filter targeting a route handler.
/// </summary>
public interface IEndpointFilter
{
    /// <summary>
    /// Implements the core logic associated with the filter given a <see cref="EndpointFilterInvocationContext"/>
    /// and the next filter to call in the pipeline.
    /// </summary>
    /// <param name="context">The <see cref="EndpointFilterInvocationContext"/> associated with the current request/response.</param>
    /// <param name="next">The next filter in the pipeline.</param>
    /// <returns>An awaitable result of calling the handler and apply
    /// any modifications made by filters in the pipeline.</returns>
    ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next);
}
