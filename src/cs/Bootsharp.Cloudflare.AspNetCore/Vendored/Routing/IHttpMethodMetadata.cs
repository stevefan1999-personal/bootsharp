// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/IHttpMethodMetadata.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Routing;

/// <summary>
/// Represents HTTP method metadata used during routing.
/// </summary>
public interface IHttpMethodMetadata
{
    /// <summary>
    /// Returns a value indicating whether the associated endpoint should accept CORS preflight requests.
    /// </summary>
    bool AcceptCorsPreflight
    {
        get => false;
        set => throw new NotImplementedException();
    }

    /// <summary>
    /// Returns a read-only collection of HTTP methods used during routing.
    /// An empty collection means any HTTP method will be accepted.
    /// </summary>
    IReadOnlyList<string> HttpMethods { get; }
}
