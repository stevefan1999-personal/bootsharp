// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/Patterns/RoutePatternPartKind.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: see this file's entry in Vendored/manifest.json (rendered in Vendored/README.md).
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Routing.Patterns;

/// <summary>
/// Defines the kinds of <see cref="RoutePatternPart"/> instances.
/// </summary>
#if !BSCF_LEAN
public enum RoutePatternPartKind
#else
public enum RoutePatternPartKind
#endif
{
    /// <summary>
    /// The <see cref="RoutePatternPartKind"/> of a <see cref="RoutePatternLiteralPart"/>.
    /// </summary>
    Literal,

    /// <summary>
    /// The <see cref="RoutePatternPartKind"/> of a <see cref="RoutePatternParameterPart"/>.
    /// </summary>
    Parameter,

    /// <summary>
    /// The <see cref="RoutePatternPartKind"/> of a <see cref="RoutePatternSeparatorPart"/>.
    /// </summary>
    Separator,
}
