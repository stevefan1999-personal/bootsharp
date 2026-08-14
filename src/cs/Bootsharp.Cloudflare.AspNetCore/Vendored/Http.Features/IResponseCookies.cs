// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Features/src/IResponseCookies.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// A wrapper for the response Set-Cookie header.
/// </summary>
public interface IResponseCookies
{
    /// <summary>
    /// Add a new cookie and value.
    /// </summary>
    /// <param name="key">Name of the new cookie.</param>
    /// <param name="value">Value of the new cookie.</param>
    void Append(string key, string value);

    /// <summary>
    /// Add a new cookie.
    /// </summary>
    /// <param name="key">Name of the new cookie.</param>
    /// <param name="value">Value of the new cookie.</param>
    /// <param name="options"><see cref="CookieOptions"/> included in the new cookie setting.</param>
    void Append(string key, string value, CookieOptions options);

    /// <summary>
    /// Add elements of specified collection as cookies.
    /// </summary>
    /// <param name="keyValuePairs">Key value pair collections whose elements will be added as cookies.</param>
    /// <param name="options"><see cref="CookieOptions"/> included in new cookie settings.</param>
    void Append(ReadOnlySpan<KeyValuePair<string, string>> keyValuePairs, CookieOptions options)
    {
        foreach (var keyValuePair in keyValuePairs)
        {
            // codeql[SM02373] - This default interface method only forwards the caller's CookieOptions to the concrete Append; it applies no cookie policy itself.
            Append(keyValuePair.Key, keyValuePair.Value, options);
        }
    }

    /// <summary>
    /// Sets an expired cookie.
    /// </summary>
    /// <param name="key">Name of the cookie to expire.</param>
    void Delete(string key);

    /// <summary>
    /// Sets an expired cookie.
    /// </summary>
    /// <param name="key">Name of the cookie to expire.</param>
    /// <param name="options">
    /// <see cref="CookieOptions"/> used to discriminate the particular cookie to expire. The
    /// <see cref="CookieOptions.Domain"/> and <see cref="CookieOptions.Path"/> values are especially important.
    /// </param>
    void Delete(string key, CookieOptions options);
}
