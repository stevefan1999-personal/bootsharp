// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Abstractions/src/Metadata/IAcceptsMetadata.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Http.Metadata;

/// <summary>
/// Interface for accepting request media types.
/// </summary>
public interface IAcceptsMetadata
{
    /// <summary>
    /// Gets a list of the allowed request content types.
    /// If the incoming request contains a <c>Content-Type</c> and the content type is not
    /// one of these values, the request will be rejected with a 415 response. If the
    /// incoming request does not contain a <c>Content-Type</c> header, the content type
    /// check will be bypassed.
    /// </summary>
    IReadOnlyList<string> ContentTypes { get; }

    /// <summary>
    /// Gets the type being read from the request. 
    /// </summary>
    Type? RequestType { get; }

    /// <summary>
    /// Gets a value that determines if the request body is optional.
    /// </summary>
    bool IsOptional { get; }
}
