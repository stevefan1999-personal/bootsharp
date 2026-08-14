// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Http.Abstractions/src/Resources.resx
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: see this file's entry in Vendored/manifest.json (rendered in Vendored/README.md).
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.

// Rendered from the .resx by Vendored/vendor.ts, standing in for the source-generating MSBuild
// task upstream runs. One literal property and one Format overload per entry, as upstream emits.

using System.Globalization;

namespace Microsoft.AspNetCore.Http.Abstractions;

internal static class Resources
{
    internal static string Name1 => "this is my long string";

    internal static string FormatName1 (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "this is my long string", args);

    internal static string Bitmap1 => "[base64 mime encoded serialized .NET Framework object]";

    internal static string FormatBitmap1 (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "[base64 mime encoded serialized .NET Framework object]", args);

    internal static string Icon1 => "[base64 mime encoded string representing a byte array form of the .NET Framework object]";

    internal static string FormatIcon1 (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "[base64 mime encoded string representing a byte array form of the .NET Framework object]", args);

    internal static string Exception_UseMiddlewareIServiceProviderNotAvailable => "'{0}' is not available.";

    internal static string FormatException_UseMiddlewareIServiceProviderNotAvailable (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "'{0}' is not available.", args);

    internal static string Exception_UseMiddlewareNoInvokeMethod => "No public '{0}' or '{1}' method found for middleware of type '{2}'.";

    internal static string FormatException_UseMiddlewareNoInvokeMethod (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "No public '{0}' or '{1}' method found for middleware of type '{2}'.", args);

    internal static string Exception_UseMiddlewareNonTaskReturnType => "'{0}' or '{1}' does not return an object of type '{2}'.";

    internal static string FormatException_UseMiddlewareNonTaskReturnType (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "'{0}' or '{1}' does not return an object of type '{2}'.", args);

    internal static string Exception_UseMiddlewareNoParameters => "The '{0}' or '{1}' method's first argument must be of type '{2}'.";

    internal static string FormatException_UseMiddlewareNoParameters (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The '{0}' or '{1}' method's first argument must be of type '{2}'.", args);

    internal static string Exception_UseMiddleMutlipleInvokes => "Multiple public '{0}' or '{1}' methods are available.";

    internal static string FormatException_UseMiddleMutlipleInvokes (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "Multiple public '{0}' or '{1}' methods are available.", args);

    internal static string Exception_PathMustStartWithSlashOrBackslash => "The path in '{0}' must start with '/' or '\\'.";

    internal static string FormatException_PathMustStartWithSlashOrBackslash (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The path in '{0}' must start with '/' or '\\'.", args);

    internal static string Exception_InvokeMiddlewareNoService => "Unable to resolve service for type '{0}' while attempting to Invoke middleware '{1}'.";

    internal static string FormatException_InvokeMiddlewareNoService (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "Unable to resolve service for type '{0}' while attempting to Invoke middleware '{1}'.", args);

    internal static string Exception_InvokeDoesNotSupportRefOrOutParams => "The '{0}' method must not have ref or out parameters.";

    internal static string FormatException_InvokeDoesNotSupportRefOrOutParams (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The '{0}' method must not have ref or out parameters.", args);

    internal static string Exception_PortMustBeGreaterThanZero => "The value must be greater than zero.";

    internal static string FormatException_PortMustBeGreaterThanZero (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The value must be greater than zero.", args);

    internal static string Exception_UseMiddlewareNoMiddlewareFactory => "No service for type '{0}' has been registered.";

    internal static string FormatException_UseMiddlewareNoMiddlewareFactory (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "No service for type '{0}' has been registered.", args);

    internal static string Exception_UseMiddlewareUnableToCreateMiddleware => "'{0}' failed to create middleware of type '{1}'.";

    internal static string FormatException_UseMiddlewareUnableToCreateMiddleware (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "'{0}' failed to create middleware of type '{1}'.", args);

    internal static string Exception_UseMiddlewareExplicitArgumentsNotSupported => "Types that implement '{0}' do not support explicit arguments.";

    internal static string FormatException_UseMiddlewareExplicitArgumentsNotSupported (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "Types that implement '{0}' do not support explicit arguments.", args);

    internal static string ArgumentCannotBeNullOrEmpty => "Argument cannot be null or empty.";

    internal static string FormatArgumentCannotBeNullOrEmpty (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "Argument cannot be null or empty.", args);

    internal static string RouteValueDictionary_DuplicateKey => "An element with the key '{0}' already exists in the {1}.";

    internal static string FormatRouteValueDictionary_DuplicateKey (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "An element with the key '{0}' already exists in the {1}.", args);

    internal static string RouteValueDictionary_DuplicatePropertyName => "The type '{0}' defines properties '{1}' and '{2}' which differ only by casing. This is not supported by {3} which uses case-insensitive comparisons.";

    internal static string FormatRouteValueDictionary_DuplicatePropertyName (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The type '{0}' defines properties '{1}' and '{2}' which differ only by casing. This is not supported by {3} which uses case-insensitive comparisons.", args);

    internal static string Exception_KeyedServicesNotSupported => "This service provider doesn't support keyed services.";

    internal static string FormatException_KeyedServicesNotSupported (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "This service provider doesn't support keyed services.", args);
}
