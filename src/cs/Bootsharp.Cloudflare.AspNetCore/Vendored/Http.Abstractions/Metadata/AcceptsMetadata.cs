// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Abstractions/src/Metadata/AcceptsMetadata.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.AspNetCore.Shared;

namespace Microsoft.AspNetCore.Http.Metadata;

/// <summary>
/// Metadata that specifies the supported request content types.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public sealed class AcceptsMetadata : IAcceptsMetadata
{
    /// <summary>
    /// Creates a new instance of <see cref="AcceptsMetadata"/> with a type.
    /// </summary>
    /// <param name="contentTypes">Content types that are accepted by endpoint.</param>
    /// <param name="type">The type being read from the request.</param>
    /// <param name="isOptional">Whether the request body is optional.</param>
    public AcceptsMetadata(string[] contentTypes, Type? type = null, bool isOptional = false)
    {
        ArgumentNullException.ThrowIfNull(contentTypes);

        RequestType = type;
        ContentTypes = contentTypes;
        IsOptional = isOptional;
    }

    /// <summary>
    /// Gets the supported request content types.
    /// </summary>
    public IReadOnlyList<string> ContentTypes { get; }

    /// <summary>
    /// Gets the type being read from the request.
    /// </summary>
    public Type? RequestType { get; }

    /// <summary>
    /// Gets a value that determines if the request body is optional.
    /// </summary>
    public bool IsOptional { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return DebuggerHelpers.GetDebugText(nameof(ContentTypes), ContentTypes, nameof(RequestType), RequestType, nameof(IsOptional), IsOptional, includeNullValues: false, prefix: "Accepts");
    }
}
