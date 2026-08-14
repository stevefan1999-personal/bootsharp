// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Abstractions/src/ProblemDetails/HttpValidationProblemDetails.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// A <see cref="ProblemDetails"/> for validation errors.
/// </summary>
public class HttpValidationProblemDetails : ProblemDetails
{
    /// <summary>
    /// Initializes a new instance of <see cref="HttpValidationProblemDetails"/>.
    /// </summary>
    public HttpValidationProblemDetails()
        : this(new Dictionary<string, string[]>(StringComparer.Ordinal))
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="HttpValidationProblemDetails"/> using the specified <paramref name="errors"/>.
    /// </summary>
    /// <param name="errors">The validation errors.</param>
    public HttpValidationProblemDetails(IDictionary<string, string[]> errors)
        : this((IEnumerable<KeyValuePair<string, string[]>>)errors)
    {
    }

    /// <summary>
    /// Initializes a new instance of <see cref="HttpValidationProblemDetails"/> using the specified <paramref name="errors"/>.
    /// </summary>
    /// <param name="errors">The validation errors.</param>
    public HttpValidationProblemDetails(IEnumerable<KeyValuePair<string, string[]>> errors)
        : this(new Dictionary<string, string[]>(errors ?? throw new ArgumentNullException(nameof(errors)), StringComparer.Ordinal))
    {
    }

    private HttpValidationProblemDetails(Dictionary<string, string[]> errors)
    {
        Title = "One or more validation errors occurred.";
        Errors = errors;
    }

    /// <summary>
    /// Gets the validation errors associated with this instance of <see cref="HttpValidationProblemDetails"/>.
    /// </summary>
    [JsonPropertyName("errors")]
    public IDictionary<string, string[]> Errors { get; set; }
}
