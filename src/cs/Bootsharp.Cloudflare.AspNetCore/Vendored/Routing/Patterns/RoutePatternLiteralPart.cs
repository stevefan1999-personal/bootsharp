// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/Patterns/RoutePatternLiteralPart.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: see this file's entry in Vendored/manifest.json (rendered in Vendored/README.md).
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

namespace Microsoft.AspNetCore.Routing.Patterns;

/// <summary>
/// Represents a literal text part of a route pattern. Instances of <see cref="RoutePatternLiteralPart"/>
/// are immutable.
/// </summary>
[DebuggerDisplay("{DebuggerToString()}")]
#if !BSCF_LEAN
public sealed class RoutePatternLiteralPart : RoutePatternPart
#else
public sealed class RoutePatternLiteralPart : RoutePatternPart
#endif
{
    internal RoutePatternLiteralPart(string content)
        : base(RoutePatternPartKind.Literal)
    {
        Debug.Assert(!string.IsNullOrEmpty(content));
        Content = content;
    }

    /// <summary>
    /// Gets the text content.
    /// </summary>
    public string Content { get; }

    internal override string DebuggerToString()
    {
        return Content;
    }
}
