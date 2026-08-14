// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/EndpointNameAttribute.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Routing;

/// <summary>
/// Specifies the endpoint name in <see cref="Endpoint.Metadata"/>.
/// </summary>
/// <remarks>
/// Endpoint names must be unique within an application, and can be used to unambiguously
/// identify a desired endpoint for URI generation using <see cref="Microsoft.AspNetCore.Routing.LinkGenerator"/>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Delegate, Inherited = false, AllowMultiple = false)]
public sealed class EndpointNameAttribute : Attribute, IEndpointNameMetadata
{
    /// <summary>
    /// Initializes an instance of the EndpointNameAttribute.
    /// </summary>
    /// <param name="endpointName">The endpoint name.</param>
    public EndpointNameAttribute(string endpointName)
    {
        ArgumentNullException.ThrowIfNull(endpointName);

        EndpointName = endpointName;
    }

    /// <inheritdoc />
    public string EndpointName { get; }
}
