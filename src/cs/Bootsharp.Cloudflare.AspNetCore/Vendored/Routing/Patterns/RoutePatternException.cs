// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/Patterns/RoutePatternException.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: see this file's entry in Vendored/manifest.json (rendered in Vendored/README.md).
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;

namespace Microsoft.AspNetCore.Routing.Patterns;

#if !BSCF_LEAN
/// <summary>
/// An exception that is thrown for error constructing a <see cref="RoutePattern"/>.
/// </summary>
[Serializable]
public sealed class RoutePatternException : Exception
#else
public sealed class RoutePatternException : Exception
#endif
{
    [Obsolete]
    private RoutePatternException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        Pattern = (string)info.GetValue(nameof(Pattern), typeof(string))!;
    }

    /// <summary>
    /// Creates a new instance of <see cref="RoutePatternException"/>.
    /// </summary>
    /// <param name="pattern">The route pattern as raw text.</param>
    /// <param name="message">The exception message.</param>
    public RoutePatternException([StringSyntax("Route")] string pattern, string message)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(message);

        Pattern = pattern;
    }

    /// <summary>
    /// Gets the route pattern associated with this exception.
    /// </summary>
    public string Pattern { get; }

    /// <summary>
    /// Populates a <see cref="SerializationInfo"/> with the data needed to serialize the target object.
    /// </summary>
    /// <param name="info">The <see cref="SerializationInfo"/> to populate with data.</param>
    /// <param name="context">The destination (<see cref="StreamingContext" />) for this serialization.</param>
    [Obsolete("This API supports obsolete formatter-based serialization. It should not be called or extended by application code.", DiagnosticId = "SYSLIB0051", UrlFormat = "https://aka.ms/dotnet-warnings/{0}")]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        info.AddValue(nameof(Pattern), Pattern);
        base.GetObjectData(info, context);
    }
}
