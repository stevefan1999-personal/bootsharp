// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Abstractions/src/EndpointFilterDelegate.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// A delegate that is applied as a filter on a route handler.
/// </summary>
/// <param name="context">The <see cref="EndpointFilterInvocationContext"/> associated with the current request.</param>
/// <returns>
/// A <see cref="ValueTask"/> result of calling the handler and applying any modifications made by filters in the pipeline.
/// </returns>
public delegate ValueTask<object?> EndpointFilterDelegate(EndpointFilterInvocationContext context);
