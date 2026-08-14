// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Abstractions/src/EndpointFilterFactoryContext.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// Represents the information accessible via the route handler filter
/// API when the user is constructing a new route handler.
/// </summary>
public sealed class EndpointFilterFactoryContext
{
    /// <summary>
    /// The <see cref="MethodInfo"/> associated with the current route handler, <see cref="RequestDelegate"/> or MVC action.
    /// </summary>
    /// <remarks>
    /// In the future this could support more endpoint types.
    /// </remarks>
    public required MethodInfo MethodInfo { get; init; }

    /// <summary>
    /// Gets the <see cref="IServiceProvider"/> instance used to access application services.
    /// </summary>
    public IServiceProvider ApplicationServices { get; init; } = EmptyServiceProvider.Instance;

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static EmptyServiceProvider Instance { get; } = new EmptyServiceProvider();
        public object? GetService(Type serviceType) => null;
    }
}
