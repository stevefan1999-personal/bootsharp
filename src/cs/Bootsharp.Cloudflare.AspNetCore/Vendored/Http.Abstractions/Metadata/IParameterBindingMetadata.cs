// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Abstractions/src/Metadata/IParameterBindingMetadata.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: none — copied verbatim.
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;

namespace Microsoft.AspNetCore.Http.Metadata;

/// <summary>
/// Exposes metadata about the parameter binding details associated with a parameter
/// in the endpoints handler.
/// </summary>
/// <remarks>
/// This metadata is injected by the RequestDelegateFactory and RequestDelegateGenerator components
/// and is primarily intended for consumption by the EndpointMetadataApiDescriptionProvider in
/// ApiExplorer.
/// </remarks>
public interface IParameterBindingMetadata
{
    /// <summary>
    /// The name of the parameter.
    /// </summary>
	string Name { get; }

    /// <summary>
    /// <see langword="true "/> is the parameter is associated with a type that implements <see cref="IParsable{TSelf}" /> or exposes a <c>TryParse</c> method.
    /// </summary>
	bool HasTryParse { get; }

    /// <summary>
    /// <see langword="true"/> if the parameter is associated with a type that implements a <c>BindAsync</c> method.
    /// </summary>
	bool HasBindAsync { get; }

    /// <summary>
    /// The <see cref="ParameterInfo"/> associated with the parameter.
    /// </summary>
    ParameterInfo ParameterInfo { get; }

    /// <summary>
    /// <see langword="true"/> if the parameter is optional.
    /// </summary>
    bool IsOptional { get; }
}
