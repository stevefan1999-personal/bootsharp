// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Features/src/IFormFile.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// Represents a file sent with the HttpRequest.
/// </summary>
public interface IFormFile
{
    /// <summary>
    /// Gets the raw Content-Type header of the uploaded file.
    /// </summary>
    string ContentType { get; }

    /// <summary>
    /// Gets the raw Content-Disposition header of the uploaded file.
    /// </summary>
    string ContentDisposition { get; }

    /// <summary>
    /// Gets the header dictionary of the uploaded file.
    /// </summary>
    IHeaderDictionary Headers { get; }

    /// <summary>
    /// Gets the file length in bytes.
    /// </summary>
    long Length { get; }

    /// <summary>
    /// Gets the form field name from the Content-Disposition header.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the file name from the Content-Disposition header.
    /// </summary>
    /// <remarks>
    /// Do not use the <see cref="FileName"/> property of <see cref="IFormFile"/> other than for display and logging.
    /// When displaying or logging, HTML encode the file name. A cyberattacker can provide a malicious filename, including full paths or relative paths.
    /// <para>
    /// You can use the following code to remove the path from the file name:
    /// </para>
    /// <code>
    /// string untrustedFileName = Path.GetFileName(formFile.FileName);
    /// </code>
    /// </remarks>
    string FileName { get; }

    /// <summary>
    /// Opens the request stream for reading the uploaded file.
    /// </summary>
    Stream OpenReadStream();

    /// <summary>
    /// Copies the contents of the uploaded file to the <paramref name="target"/> stream.
    /// </summary>
    /// <param name="target">The stream to copy the file contents to.</param>
    void CopyTo(Stream target);

    /// <summary>
    /// Asynchronously copies the contents of the uploaded file to the <paramref name="target"/> stream.
    /// </summary>
    /// <param name="target">The stream to copy the file contents to.</param>
    /// <param name="cancellationToken"></param>
    Task CopyToAsync(Stream target, CancellationToken cancellationToken = default(CancellationToken));
}
