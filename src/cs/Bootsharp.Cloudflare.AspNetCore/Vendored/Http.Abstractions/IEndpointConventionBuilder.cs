// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Abstractions/src/Extensions/IEndpointConventionBuilder.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Builds conventions that will be used for customization of <see cref="EndpointBuilder"/> instances.
/// </summary>
/// <remarks>
/// This interface is used at application startup to customize endpoints for the application.
/// </remarks>
public interface IEndpointConventionBuilder
{
    /// <summary>
    /// Adds the specified convention to the builder. Conventions are used to customize <see cref="EndpointBuilder"/> instances.
    /// </summary>
    /// <param name="convention">The convention to add to the builder.</param>
    void Add(Action<EndpointBuilder> convention);

    /// <summary>
    /// Registers the specified convention for execution after conventions registered
    /// via <see cref="Add(Action{EndpointBuilder})"/>
    /// </summary>
    /// <param name="finallyConvention">The convention to add to the builder.</param>
    void Finally(Action<EndpointBuilder> finallyConvention) => throw new NotImplementedException();
}
