// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Features/src/IHeaderDictionary.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// Represents HttpRequest and HttpResponse headers
/// </summary>
public partial interface IHeaderDictionary : IDictionary<string, StringValues>
{
    /// <summary>
    /// IHeaderDictionary has a different indexer contract than IDictionary, where it will return StringValues.Empty for missing entries.
    /// </summary>
    /// <param name="key"></param>
    /// <returns>The stored value, or StringValues.Empty if the key is not present.</returns>
    new StringValues this[string key] { get; set; }

    /// <summary>
    /// Strongly typed access to the Content-Length header. Implementations must keep this in sync with the string representation.
    /// </summary>
    long? ContentLength { get; set; }
}
