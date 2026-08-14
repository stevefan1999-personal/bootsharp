// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Features/src/SameSiteMode.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// Used to set the SameSite field on response cookies to indicate if those cookies should be included by the client on future "same-site" or "cross-site" requests.
/// RFC Draft: <see href="https://tools.ietf.org/html/draft-ietf-httpbis-rfc6265bis-03#section-4.1.1"/>
/// </summary>
// This mirrors Microsoft.Net.Http.Headers.SameSiteMode
public enum SameSiteMode
{
    /// <summary>No SameSite field will be set, the client should follow its default cookie policy.</summary>
    Unspecified = -1,
    /// <summary>Indicates the client should disable same-site restrictions.</summary>
    None = 0,
    /// <summary>Indicates the client should send the cookie with "same-site" requests, and with "cross-site" top-level navigations.</summary>
    Lax,
    /// <summary>Indicates the client should only send the cookie with "same-site" requests.</summary>
    Strict
}
