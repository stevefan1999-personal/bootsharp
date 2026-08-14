// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Features/src/ISession.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// Stores user data while the user browses a web application. Session state uses a store maintained by the application
/// to persist data across requests from a client. The session data is backed by a cache and considered ephemeral data.
/// </summary>
public interface ISession
{
    /// <summary>
    /// Indicates whether the current session loaded successfully. Accessing this property before the session is loaded will cause it to be loaded inline.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// A unique identifier for the current session. This is not the same as the session cookie
    /// since the cookie lifetime may not be the same as the session entry lifetime in the data store.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Enumerates all the keys, if any.
    /// </summary>
    IEnumerable<string> Keys { get; }

    /// <summary>
    /// Load the session from the data store. This may throw if the data store is unavailable.
    /// </summary>
    /// <returns></returns>
    Task LoadAsync(CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// Store the session in the data store. This may throw if the data store is unavailable.
    /// </summary>
    /// <returns></returns>
    Task CommitAsync(CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// Retrieve the value of the given key, if present.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    /// <returns>The retrieved value.</returns>
    bool TryGetValue(string key, [NotNullWhen(true)] out byte[]? value);

    /// <summary>
    /// Set the given key and value in the current session. This will throw if the session
    /// was not established prior to sending the response.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    void Set(string key, byte[] value);

    /// <summary>
    /// Remove the given key from the session if present.
    /// </summary>
    /// <param name="key"></param>
    void Remove(string key);

    /// <summary>
    /// Remove all entries from the current session, if any.
    /// The session cookie is not removed.
    /// </summary>
    void Clear();
}
