// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Abstractions/src/BadHttpRequestException.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// Represents an HTTP request error
/// </summary>
public class BadHttpRequestException : IOException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BadHttpRequestException"/> class.
    /// </summary>
    /// <param name="message">The message to associate with this exception.</param>
    /// <param name="statusCode">The HTTP status code to associate with this exception.</param>
    public BadHttpRequestException(string message, int statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BadHttpRequestException"/> class with the <see cref="StatusCode"/> set to 400 Bad Request.
    /// </summary>
    /// <param name="message">The message to associate with this exception</param>
    public BadHttpRequestException(string message)
        : base(message)
    {
        StatusCode = StatusCodes.Status400BadRequest;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BadHttpRequestException"/> class.
    /// </summary>
    /// <param name="message">The message to associate with this exception.</param>
    /// <param name="statusCode">The HTTP status code to associate with this exception.</param>
    /// <param name="innerException">The inner exception to associate with this exception</param>
    public BadHttpRequestException(string message, int statusCode, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BadHttpRequestException"/> class with the <see cref="StatusCode"/> set to 400 Bad Request.
    /// </summary>
    /// <param name="message">The message to associate with this exception</param>
    /// <param name="innerException">The inner exception to associate with this exception</param>
    public BadHttpRequestException(string message, Exception innerException)
        : base(message, innerException)
    {
        StatusCode = StatusCodes.Status400BadRequest;
    }

    /// <summary>
    /// Gets the HTTP status code for this exception.
    /// </summary>
    public int StatusCode { get; }
}
