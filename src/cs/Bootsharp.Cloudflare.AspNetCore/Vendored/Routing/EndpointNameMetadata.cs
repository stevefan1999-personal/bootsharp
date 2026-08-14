// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/EndpointNameMetadata.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Shared;

namespace Microsoft.AspNetCore.Routing;

/// <summary>
/// Specifies an endpoint name in <see cref="Endpoint.Metadata"/>.
/// </summary>
/// <remarks>
/// Endpoint names must be unique within an application, and can be used to unambiguously
/// identify a desired endpoint for URI generation using <see cref="LinkGenerator"/>.
/// </remarks>
[DebuggerDisplay("{ToString(),nq}")]
public class EndpointNameMetadata : IEndpointNameMetadata
{
    /// <summary>
    /// Creates a new instance of <see cref="EndpointNameMetadata"/> with the provided endpoint name.
    /// </summary>
    /// <param name="endpointName">The endpoint name.</param>
    public EndpointNameMetadata(string endpointName)
    {
        ArgumentNullException.ThrowIfNull(endpointName);

        EndpointName = endpointName;
    }

    /// <summary>
    /// Gets the endpoint name.
    /// </summary>
    public string EndpointName { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return DebuggerHelpers.GetDebugText(nameof(EndpointName), EndpointName);
    }
}
