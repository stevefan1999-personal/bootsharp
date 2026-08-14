// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Abstractions/src/RequestDelegate.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// A function that can process an HTTP request.
/// </summary>
/// <param name="context">The <see cref="HttpContext"/> for the request.</param>
/// <returns>A task that represents the completion of request processing.</returns>
public delegate Task RequestDelegate(HttpContext context);
