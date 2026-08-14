// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Features/src/IHttpRequestIdentifierFeature.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Http.Features;

/// <summary>
/// Feature to uniquely identify a request.
/// </summary>
public interface IHttpRequestIdentifierFeature
{
    /// <summary>
    /// Gets or sets a value to uniquely identify a request.
    /// This can be used for logging and diagnostics.
    /// </summary>
    string TraceIdentifier { get; set; }
}
