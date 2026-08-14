// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Abstractions/src/Extensions/UseExtensions.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Http;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Extension methods for adding middleware.
/// </summary>
public static class UseExtensions
{
    /// <summary>
    /// Adds a middleware delegate defined in-line to the application's request pipeline.
    /// If you aren't calling the next function, use <see cref="RunExtensions.Run(IApplicationBuilder, RequestDelegate)"/> instead.
    /// <para>
    /// Prefer using <see cref="Use(IApplicationBuilder, Func{HttpContext, RequestDelegate, Task})"/> for better performance as shown below:
    /// <code>
    /// app.Use((context, next) =>
    /// {
    ///     return next(context);
    /// });
    /// </code>
    /// </para>
    /// </summary>
    /// <param name="app">The <see cref="IApplicationBuilder"/> instance.</param>
    /// <param name="middleware">A function that handles the request and calls the given next function.</param>
    /// <returns>The <see cref="IApplicationBuilder"/> instance.</returns>
    public static IApplicationBuilder Use(this IApplicationBuilder app, Func<HttpContext, Func<Task>, Task> middleware)
    {
        return app.Use(next =>
        {
            return context =>
            {
                Func<Task> simpleNext = () => next(context);
                return middleware(context, simpleNext);
            };
        });
    }

    /// <summary>
    /// Adds a middleware delegate defined in-line to the application's request pipeline.
    /// If you aren't calling the next function, use <see cref="RunExtensions.Run(IApplicationBuilder, RequestDelegate)"/> instead.
    /// </summary>
    /// <param name="app">The <see cref="IApplicationBuilder"/> instance.</param>
    /// <param name="middleware">A function that handles the request and calls the given next function.</param>
    /// <returns>The <see cref="IApplicationBuilder"/> instance.</returns>
    public static IApplicationBuilder Use(this IApplicationBuilder app, Func<HttpContext, RequestDelegate, Task> middleware)
    {
        return app.Use(next => context => middleware(context, next));
    }
}
