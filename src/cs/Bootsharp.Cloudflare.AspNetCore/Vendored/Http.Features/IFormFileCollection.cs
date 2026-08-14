// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Features/src/IFormFileCollection.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// Represents the collection of files sent with the HttpRequest.
/// </summary>
public interface IFormFileCollection : IReadOnlyList<IFormFile>
{
    /// <summary>
    /// Gets the first file with the specified name.
    /// </summary>
    /// <param name="name">The name of the file to get.</param>
    /// <returns>
    ///	The requested file, or null if it is not present.
    /// </returns>
    IFormFile? this[string name] { get; }

    /// <summary>
    /// Gets the first file with the specified name.
    /// </summary>
    /// <param name="name">The name of the file to get.</param>
    /// <returns>
    ///	The requested file, or null if it is not present.
    /// </returns>
    IFormFile? GetFile(string name);

    /// <summary>
    ///     Gets an <see cref="IReadOnlyList{T}" /> containing the files of the
    ///     <see cref="IFormFileCollection" /> with the specified name.
    /// </summary>
    /// <param name="name">The name of the files to get.</param>
    /// <returns>
    ///     An <see cref="IReadOnlyList{T}" /> containing the files of the object
    ///     that implements <see cref="IFormFileCollection" />.
    /// </returns>
    IReadOnlyList<IFormFile> GetFiles(string name);
}
