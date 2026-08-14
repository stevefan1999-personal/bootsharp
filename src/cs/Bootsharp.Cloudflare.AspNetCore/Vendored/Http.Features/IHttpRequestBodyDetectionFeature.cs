// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Features/src/IHttpRequestBodyDetectionFeature.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Http.Features;

/// <summary>
/// Used to indicate if the request can have a body.
/// </summary>
public interface IHttpRequestBodyDetectionFeature
{
    /// <summary>
    /// Indicates if the request can have a body.
    /// </summary>
    /// <remarks>
    /// This returns true when:
    /// <list type="bullet">
    /// <item><description>
    /// It's an HTTP/1.x request with a non-zero Content-Length or a 'Transfer-Encoding: chunked' header.
    /// </description></item>
    /// <item><description>
    /// It's an HTTP/2 request that did not set the END_STREAM flag on the initial headers frame.
    /// </description></item>
    /// <item><description>
    /// It's an HTTP/3 request that did not set the END_STREAM flag on the initial headers frame.
    /// </description></item>
    /// </list>
    /// The final request body length may still be zero for the chunked or HTTP/2 scenarios.
    /// <para>
    /// This returns false when:
    /// <list type="bullet">
    /// <item><description>
    /// It's an HTTP/1.x request with no Content-Length or 'Transfer-Encoding: chunked' header, or the Content-Length is 0.
    /// </description></item>
    /// <item><description>
    /// It's an HTTP/1.x request with Connection: Upgrade (e.g. WebSockets). There is no HTTP request body for these requests and
    /// no data should be received until after the upgrade.
    /// </description></item>
    /// <item><description>
    /// It's an HTTP/2 request that set END_STREAM on the initial headers frame.
    /// </description></item>
    /// <item><description>
    /// It's an HTTP/3 request that set END_STREAM on the initial headers frame.
    /// </description></item>
    /// </list>
    /// </para>
    /// When false, the request body should never return data.
    /// </remarks>
    bool CanHaveBody { get; }
}
