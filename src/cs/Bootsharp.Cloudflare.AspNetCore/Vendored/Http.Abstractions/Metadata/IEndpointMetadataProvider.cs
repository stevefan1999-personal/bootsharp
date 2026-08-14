// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Abstractions/src/Metadata/IEndpointMetadataProvider.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using Microsoft.AspNetCore.Builder;

namespace Microsoft.AspNetCore.Http.Metadata;

/// <summary>
/// Indicates that a type provides a static method that provides <see cref="Endpoint"/> metadata when declared as a parameter type or the
/// returned type of an <see cref="Endpoint"/> route handler delegate.
/// </summary>
public interface IEndpointMetadataProvider
{
    /// <summary>
    /// Populates metadata for the related <see cref="Endpoint"/> and <see cref="MethodInfo"/>.
    /// </summary>
    /// <remarks>
    /// This method is called by RequestDelegateFactory when creating a <see cref="RequestDelegate"/> and by MVC when creating endpoints for controller actions.
    /// This is called for each parameter and return type of the route handler or action with a declared type implementing this interface.
    /// Add or remove objects on the <see cref="EndpointBuilder.Metadata"/> property of the <paramref name="builder"/> to modify the <see cref="Endpoint.Metadata"/> being built.
    /// </remarks>
    /// <param name="method">The <see cref="MethodInfo"/> of the route handler delegate or MVC Action of the endpoint being created.</param>
    /// <param name="builder">The <see cref="EndpointBuilder"/> used to construct the endpoint for the given <paramref name="method"/>.</param>
    static abstract void PopulateMetadata(MethodInfo method, EndpointBuilder builder);
}
