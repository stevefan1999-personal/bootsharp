// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/RouteValueEqualityComparer.cs
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: see this file's entry in Vendored/manifest.json (rendered in Vendored/README.md).
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;

namespace Microsoft.AspNetCore.Routing;

/// <summary>
/// An <see cref="IEqualityComparer{Object}"/> implementation that compares objects as-if
/// they were route value strings.
/// </summary>
/// <remarks>
/// Values that are are not strings are converted to strings using
/// <c>Convert.ToString(x, CultureInfo.InvariantCulture)</c>. <c>null</c> values are converted
/// to the empty string.
///
/// strings are compared using <see cref="StringComparison.OrdinalIgnoreCase"/>.
/// </remarks>
#if !BSCF_LEAN
public class RouteValueEqualityComparer : IEqualityComparer<object?>
#else
public class RouteValueEqualityComparer : IEqualityComparer<object?>
#endif
{
    /// <summary>
    /// A default instance of the <see cref="RouteValueEqualityComparer"/>.
    /// </summary>
    public static readonly RouteValueEqualityComparer Default = new RouteValueEqualityComparer();

    /// <inheritdoc />
    public new bool Equals(object? x, object? y)
    {
        var stringX = x as string ?? Convert.ToString(x, CultureInfo.InvariantCulture);
        var stringY = y as string ?? Convert.ToString(y, CultureInfo.InvariantCulture);

        if (string.IsNullOrEmpty(stringX) && string.IsNullOrEmpty(stringY))
        {
            return true;
        }
        else
        {
            return string.Equals(stringX, stringY, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <inheritdoc />
    public int GetHashCode(object obj)
    {
        var stringObj = obj as string ?? Convert.ToString(obj, CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(stringObj))
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(string.Empty);
        }
        else
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(stringObj);
        }
    }
}
