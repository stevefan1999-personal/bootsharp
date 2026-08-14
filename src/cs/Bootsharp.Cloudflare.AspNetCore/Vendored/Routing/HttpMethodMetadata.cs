// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/HttpMethodMetadata.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Linq;
using Microsoft.AspNetCore.Shared;
using static Microsoft.AspNetCore.Http.HttpMethods;

namespace Microsoft.AspNetCore.Routing;

/// <summary>
/// Represents HTTP method metadata used during routing.
/// </summary>
[DebuggerDisplay("{ToString(),nq}")]
public sealed class HttpMethodMetadata : IHttpMethodMetadata
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HttpMethodMetadata" /> class.
    /// </summary>
    /// <param name="httpMethods">
    /// The HTTP methods used during routing.
    /// An empty collection means any HTTP method will be accepted.
    /// </param>
    public HttpMethodMetadata(IEnumerable<string> httpMethods)
        : this(httpMethods, acceptCorsPreflight: false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpMethodMetadata" /> class.
    /// </summary>
    /// <param name="httpMethods">
    /// The HTTP methods used during routing.
    /// An empty collection means any HTTP method will be accepted.
    /// </param>
    /// <param name="acceptCorsPreflight">A value indicating whether routing accepts CORS preflight requests.</param>
    public HttpMethodMetadata(IEnumerable<string> httpMethods, bool acceptCorsPreflight)
    {
        ArgumentNullException.ThrowIfNull(httpMethods);

        HttpMethods = httpMethods.Select(GetCanonicalizedValue).ToArray();
        AcceptCorsPreflight = acceptCorsPreflight;
    }

    /// <summary>
    /// Returns a value indicating whether the associated endpoint should accept CORS preflight requests.
    /// </summary>
    public bool AcceptCorsPreflight { get; set; }

    /// <summary>
    /// Returns a read-only collection of HTTP methods used during routing.
    /// An empty collection means any HTTP method will be accepted.
    /// </summary>
    public IReadOnlyList<string> HttpMethods { get; }

    /// <inheritdoc/>
    public override string ToString()
    {
        return DebuggerHelpers.GetDebugText(nameof(HttpMethods), HttpMethods, "Cors", AcceptCorsPreflight);
    }
}
