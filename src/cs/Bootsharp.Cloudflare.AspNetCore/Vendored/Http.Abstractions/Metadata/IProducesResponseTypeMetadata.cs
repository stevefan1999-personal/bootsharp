// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Abstractions/src/Metadata/IProducesResponseTypeMetadata.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Http.Metadata;

/// <summary>
/// Defines a contract for outline the response type returned from an endpoint.
/// </summary>
public interface IProducesResponseTypeMetadata
{
    /// <summary>
    /// Gets the optimistic return type of the action.
    /// </summary>
    Type? Type { get; }

    /// <summary>
    /// Gets the HTTP status code of the response.
    /// </summary>
    int StatusCode { get; }

    /// <summary>
    /// Gets the description of the response.
    /// </summary>
    string? Description => null;

    /// <summary>
    /// Gets the content types supported by the metadata.
    /// </summary>
    IEnumerable<string> ContentTypes { get; }
}
