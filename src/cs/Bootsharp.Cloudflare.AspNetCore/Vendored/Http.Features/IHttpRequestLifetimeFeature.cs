// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Features/src/IHttpRequestLifetimeFeature.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Http.Features;

/// <summary>
/// Provides access to the HTTP request lifetime operations.
/// </summary>
public interface IHttpRequestLifetimeFeature
{
    /// <summary>
    /// A <see cref="CancellationToken"/> that fires if the request is aborted and
    /// the application should cease processing. The token will not fire if the request
    /// completes successfully.
    /// </summary>
    CancellationToken RequestAborted { get; set; }

    /// <summary>
    /// Forcefully aborts the request if it has not already completed. This will result in
    /// RequestAborted being triggered.
    /// </summary>
    void Abort();
}
