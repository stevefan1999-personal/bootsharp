// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/Patterns/RoutePatternParameterPolicyReference.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: see this file's entry in Vendored/manifest.json (rendered in Vendored/README.md).
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

namespace Microsoft.AspNetCore.Routing.Patterns;

/// <summary>
/// The parsed representation of a policy in a <see cref="RoutePattern"/> parameter. Instances
/// of <see cref="RoutePatternParameterPolicyReference"/> are immutable.
/// </summary>
[DebuggerDisplay("{DebuggerToString()}")]
#if !BSCF_LEAN
public sealed class RoutePatternParameterPolicyReference
#else
public sealed class RoutePatternParameterPolicyReference
#endif
{
    internal RoutePatternParameterPolicyReference(string content)
    {
        Content = content;
    }

    internal RoutePatternParameterPolicyReference(IParameterPolicy parameterPolicy)
    {
        ParameterPolicy = parameterPolicy;
    }

    /// <summary>
    /// Gets the constraint text.
    /// </summary>
    public string? Content { get; }

    /// <summary>
    /// Gets a pre-existing <see cref="IParameterPolicy"/> that was used to construct this reference.
    /// </summary>
    public IParameterPolicy? ParameterPolicy { get; }

    private string? DebuggerToString()
    {
        return Content;
    }
}
