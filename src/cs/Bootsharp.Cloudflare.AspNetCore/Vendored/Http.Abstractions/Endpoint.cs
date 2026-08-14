// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Abstractions/src/Routing/Endpoint.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// Represents a logical endpoint in an application.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public class Endpoint
{
    /// <summary>
    /// Creates a new instance of <see cref="Endpoint"/>.
    /// </summary>
    /// <param name="requestDelegate">The delegate used to process requests for the endpoint.</param>
    /// <param name="metadata">
    /// The endpoint <see cref="EndpointMetadataCollection"/>. May be null.
    /// </param>
    /// <param name="displayName">
    /// The informational display name of the endpoint. May be null.
    /// </param>
    public Endpoint(
        RequestDelegate? requestDelegate,
        EndpointMetadataCollection? metadata,
        string? displayName)
    {
        // All are allowed to be null
        RequestDelegate = requestDelegate;
        Metadata = metadata ?? EndpointMetadataCollection.Empty;
        DisplayName = displayName;
    }

    /// <summary>
    /// Gets the informational display name of this endpoint.
    /// </summary>
    public string? DisplayName { get; }

    /// <summary>
    /// Gets the collection of metadata associated with this endpoint.
    /// </summary>
    public EndpointMetadataCollection Metadata { get; }

    /// <summary>
    /// Gets the delegate used to process requests for the endpoint.
    /// </summary>
    public RequestDelegate? RequestDelegate { get; }

    /// <summary>
    /// Returns a string representation of the endpoint.
    /// </summary>
    public override string? ToString() => DisplayName ?? base.ToString();
}
