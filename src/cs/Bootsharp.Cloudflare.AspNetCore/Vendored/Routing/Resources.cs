// <vendored/>
// Vendored from dotnet/aspnetcore. Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT license — see THIRD-PARTY-NOTICES.md beside this directory.
//   upstream: src/Http/Routing/src/Resources.resx
//   commit:   80de79ab881ab6b3899146db6c6fb8f8d339fd94
//   local changes: see this file's entry in Vendored/manifest.json (rendered in Vendored/README.md).
// Do not edit by hand: change manifest.json and re-run `deno run -A Vendored/vendor.ts`.

// Rendered from the .resx by Vendored/vendor.ts, standing in for the source-generating MSBuild
// task upstream runs. One literal property and one Format overload per entry, as upstream emits.

using System.Globalization;

namespace Microsoft.AspNetCore.Routing;

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

    internal static string ArgumentMustBeGreaterThanOrEqualTo => "Value must be greater than or equal to {0}.";

    internal static string FormatArgumentMustBeGreaterThanOrEqualTo (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "Value must be greater than or equal to {0}.", args);

    internal static string RangeConstraint_MinShouldBeLessThanOrEqualToMax => "The value for argument '{0}' should be less than or equal to the value for the argument '{1}'.";

    internal static string FormatRangeConstraint_MinShouldBeLessThanOrEqualToMax (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The value for argument '{0}' should be less than or equal to the value for the argument '{1}'.", args);

    internal static string PropertyOfTypeCannotBeNull => "The '{0}' property of '{1}' must not be null.";

    internal static string FormatPropertyOfTypeCannotBeNull (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The '{0}' property of '{1}' must not be null.", args);

    internal static string NamedRoutes_AmbiguousRoutesFound => "The supplied route name '{0}' is ambiguous and matched more than one route.";

    internal static string FormatNamedRoutes_AmbiguousRoutesFound (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The supplied route name '{0}' is ambiguous and matched more than one route.", args);

    internal static string DefaultHandler_MustBeSet => "A default handler must be set on the {0}.";

    internal static string FormatDefaultHandler_MustBeSet (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "A default handler must be set on the {0}.", args);

    internal static string DefaultInlineConstraintResolver_AmbiguousCtors => "The constructor to use for activating the constraint type '{0}' is ambiguous. Multiple constructors were found with the following number of parameters: {1}.";

    internal static string FormatDefaultInlineConstraintResolver_AmbiguousCtors (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The constructor to use for activating the constraint type '{0}' is ambiguous. Multiple constructors were found with the following number of parameters: {1}.", args);

    internal static string DefaultInlineConstraintResolver_CouldNotFindCtor => "Could not find a constructor for constraint type '{0}' with the following number of parameters: {1}.";

    internal static string FormatDefaultInlineConstraintResolver_CouldNotFindCtor (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "Could not find a constructor for constraint type '{0}' with the following number of parameters: {1}.", args);

    internal static string DefaultInlineConstraintResolver_TypeNotConstraint => "The constraint type '{0}' which is mapped to constraint key '{1}' must implement the '{2}' interface.";

    internal static string FormatDefaultInlineConstraintResolver_TypeNotConstraint (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The constraint type '{0}' which is mapped to constraint key '{1}' must implement the '{2}' interface.", args);

    internal static string TemplateRoute_CannotHaveCatchAllInMultiSegment => "A path segment that contains more than one section, such as a literal section or a parameter, cannot contain a catch-all parameter.";

    internal static string FormatTemplateRoute_CannotHaveCatchAllInMultiSegment (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "A path segment that contains more than one section, such as a literal section or a parameter, cannot contain a catch-all parameter.", args);

    internal static string TemplateRoute_CannotHaveDefaultValueSpecifiedInlineAndExplicitly => "The route parameter '{0}' has both an inline default value and an explicit default value specified. A route parameter cannot contain an inline default value when a default value is specified explicitly. Consider removing one of them.";

    internal static string FormatTemplateRoute_CannotHaveDefaultValueSpecifiedInlineAndExplicitly (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The route parameter '{0}' has both an inline default value and an explicit default value specified. A route parameter cannot contain an inline default value when a default value is specified explicitly. Consider removing one of them.", args);

    internal static string TemplateRoute_CannotHaveConsecutiveParameters => "A path segment cannot contain two consecutive parameters. They must be separated by a '/' or by a literal string.";

    internal static string FormatTemplateRoute_CannotHaveConsecutiveParameters (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "A path segment cannot contain two consecutive parameters. They must be separated by a '/' or by a literal string.", args);

    internal static string TemplateRoute_CannotHaveConsecutiveSeparators => "The route template separator character '/' cannot appear consecutively. It must be separated by either a parameter or a literal value.";

    internal static string FormatTemplateRoute_CannotHaveConsecutiveSeparators (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The route template separator character '/' cannot appear consecutively. It must be separated by either a parameter or a literal value.", args);

    internal static string TemplateRoute_CatchAllCannotBeOptional => "A catch-all parameter cannot be marked optional.";

    internal static string FormatTemplateRoute_CatchAllCannotBeOptional (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "A catch-all parameter cannot be marked optional.", args);

    internal static string TemplateRoute_OptionalCannotHaveDefaultValue => "An optional parameter cannot have default value.";

    internal static string FormatTemplateRoute_OptionalCannotHaveDefaultValue (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "An optional parameter cannot have default value.", args);

    internal static string TemplateRoute_CatchAllMustBeLast => "A catch-all parameter can only appear as the last segment of the route template.";

    internal static string FormatTemplateRoute_CatchAllMustBeLast (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "A catch-all parameter can only appear as the last segment of the route template.", args);

    internal static string TemplateRoute_InvalidLiteral => "The literal section '{0}' is invalid. Literal sections cannot contain the '?' character.";

    internal static string FormatTemplateRoute_InvalidLiteral (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The literal section '{0}' is invalid. Literal sections cannot contain the '?' character.", args);

    internal static string TemplateRoute_InvalidParameterName => "The route parameter name '{0}' is invalid. Route parameter names must be non-empty and cannot contain these characters: '{{', '}}', '/'. The '?' character marks a parameter as optional, and can occur only at the end of the parameter. The '*' character marks a parameter as catch-all, and can occur only at the start of the parameter.";

    internal static string FormatTemplateRoute_InvalidParameterName (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The route parameter name '{0}' is invalid. Route parameter names must be non-empty and cannot contain these characters: '{{', '}}', '/'. The '?' character marks a parameter as optional, and can occur only at the end of the parameter. The '*' character marks a parameter as catch-all, and can occur only at the start of the parameter.", args);

    internal static string TemplateRoute_InvalidRouteTemplate => "The route template cannot start with a '~' character unless followed by a '/'.";

    internal static string FormatTemplateRoute_InvalidRouteTemplate (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The route template cannot start with a '~' character unless followed by a '/'.", args);

    internal static string TemplateRoute_MismatchedParameter => "There is an incomplete parameter in the route template. Check that each '{' character has a matching '}' character.";

    internal static string FormatTemplateRoute_MismatchedParameter (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "There is an incomplete parameter in the route template. Check that each '{' character has a matching '}' character.", args);

    internal static string TemplateRoute_RepeatedParameter => "The route parameter name '{0}' appears more than one time in the route template.";

    internal static string FormatTemplateRoute_RepeatedParameter (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The route parameter name '{0}' appears more than one time in the route template.", args);

    internal static string RouteConstraintBuilder_ValidationMustBeStringOrCustomConstraint => "The constraint entry '{0}' - '{1}' on the route '{2}' must have a string value or be of a type which implements '{3}'.";

    internal static string FormatRouteConstraintBuilder_ValidationMustBeStringOrCustomConstraint (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The constraint entry '{0}' - '{1}' on the route '{2}' must have a string value or be of a type which implements '{3}'.", args);

    internal static string RouteConstraintBuilder_CouldNotResolveConstraint => "The constraint entry '{0}' - '{1}' on the route '{2}' could not be resolved by the constraint resolver of type '{3}'.";

    internal static string FormatRouteConstraintBuilder_CouldNotResolveConstraint (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The constraint entry '{0}' - '{1}' on the route '{2}' could not be resolved by the constraint resolver of type '{3}'.", args);

    internal static string TemplateRoute_UnescapedBrace => "In a route parameter, '{' and '}' must be escaped with '{{' and '}}'.";

    internal static string FormatTemplateRoute_UnescapedBrace (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "In a route parameter, '{' and '}' must be escaped with '{{' and '}}'.", args);

    internal static string TemplateRoute_OptionalParameterCanbBePrecededByPeriod => "In the segment '{0}', the optional parameter '{1}' is preceded by an invalid segment '{2}'. Only a period (.) can precede an optional parameter.";

    internal static string FormatTemplateRoute_OptionalParameterCanbBePrecededByPeriod (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "In the segment '{0}', the optional parameter '{1}' is preceded by an invalid segment '{2}'. Only a period (.) can precede an optional parameter.", args);

    internal static string TemplateRoute_OptionalParameterHasTobeTheLast => "An optional parameter must be at the end of the segment. In the segment '{0}', optional parameter '{1}' is followed by '{2}'.";

    internal static string FormatTemplateRoute_OptionalParameterHasTobeTheLast (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "An optional parameter must be at the end of the segment. In the segment '{0}', optional parameter '{1}' is followed by '{2}'.", args);

    internal static string AttributeRoute_DifferentLinkGenerationEntries_SameName => "Two or more routes named '{0}' have different templates.";

    internal static string FormatAttributeRoute_DifferentLinkGenerationEntries_SameName (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "Two or more routes named '{0}' have different templates.", args);

    internal static string UnableToFindServices => "Unable to find the required services. Please add all the required services by calling '{0}.{1}' inside the call to '{2}' in the application startup code.";

    internal static string FormatUnableToFindServices (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "Unable to find the required services. Please add all the required services by calling '{0}.{1}' inside the call to '{2}' in the application startup code.", args);

    internal static string TemplateRoute_Exception => "An error occurred while creating the route with name '{0}' and template '{1}'.";

    internal static string FormatTemplateRoute_Exception (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "An error occurred while creating the route with name '{0}' and template '{1}'.", args);

    internal static string AmbiguousEndpoints => "The request matched multiple endpoints. Matches: {0}{0}{1}";

    internal static string FormatAmbiguousEndpoints (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The request matched multiple endpoints. Matches: {0}{0}{1}", args);

    internal static string RoutePatternBuilder_CollectionCannotBeEmpty => "The collection cannot be empty.";

    internal static string FormatRoutePatternBuilder_CollectionCannotBeEmpty (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The collection cannot be empty.", args);

    internal static string ConstraintMustBeStringOrConstraint => "The constraint entry '{0}' - '{1}' must have a string value or be of a type which implements '{2}'.";

    internal static string FormatConstraintMustBeStringOrConstraint (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The constraint entry '{0}' - '{1}' must have a string value or be of a type which implements '{2}'.", args);

    internal static string RoutePattern_InvalidConstraintReference => "Invalid constraint '{0}'. A constraint must be of type 'string' or '{1}'.";

    internal static string FormatRoutePattern_InvalidConstraintReference (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "Invalid constraint '{0}'. A constraint must be of type 'string' or '{1}'.", args);

    internal static string RoutePattern_InvalidParameterConstraintReference => "Invalid constraint '{0}' for parameter '{1}'. A constraint must be of type 'string', '{2}', or '{3}'.";

    internal static string FormatRoutePattern_InvalidParameterConstraintReference (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "Invalid constraint '{0}' for parameter '{1}'. A constraint must be of type 'string', '{2}', or '{3}'.", args);

    internal static string RoutePattern_ConstraintReferenceNotFound => "The constraint reference '{0}' could not be resolved to a type. Register the constraint type with '{1}.{2}'.";

    internal static string FormatRoutePattern_ConstraintReferenceNotFound (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The constraint reference '{0}' could not be resolved to a type. Register the constraint type with '{1}.{2}'.", args);

    internal static string RoutePattern_InvalidStringConstraintReference => "Invalid constraint type '{0}' registered as '{1}'. A constraint  type must either implement '{2}', or inherit from '{3}'.";

    internal static string FormatRoutePattern_InvalidStringConstraintReference (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "Invalid constraint type '{0}' registered as '{1}'. A constraint  type must either implement '{2}', or inherit from '{3}'.", args);

    internal static string DuplicateEndpointNameEntry => "Endpoints with endpoint name '{0}':";

    internal static string FormatDuplicateEndpointNameEntry (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "Endpoints with endpoint name '{0}':", args);

    internal static string DuplicateEndpointNameHeader => "The following endpoints with a duplicate endpoint name were found.";

    internal static string FormatDuplicateEndpointNameHeader (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "The following endpoints with a duplicate endpoint name were found.", args);

    internal static string FormatterMapping_MediaTypeInvalid => "No media type found for format '{0}'.";

    internal static string FormatFormatterMapping_MediaTypeInvalid (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "No media type found for format '{0}'.", args);

    internal static string MapGroup_ChangingRoutePatternUnsupported => "MapGroup does not support mutating RouteEndpointBuilder.RoutePattern from '{0}' to '{1}' via conventions.";

    internal static string FormatMapGroup_ChangingRoutePatternUnsupported (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "MapGroup does not support mutating RouteEndpointBuilder.RoutePattern from '{0}' to '{1}' via conventions.", args);

    internal static string MapGroup_CustomEndpointUnsupported => "MapGroup does not support custom Endpoint type '{0}'. Only RouteEndpoints can be grouped.";

    internal static string FormatMapGroup_CustomEndpointUnsupported (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "MapGroup does not support custom Endpoint type '{0}'. Only RouteEndpoints can be grouped.", args);

    internal static string MapGroup_RepeatedDictionaryEntry => "MapGroup cannot build a pattern for '{0}' because the 'RoutePattern.{1}' dictionary key '{2}' has multiple values.";

    internal static string FormatMapGroup_RepeatedDictionaryEntry (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "MapGroup cannot build a pattern for '{0}' because the 'RoutePattern.{1}' dictionary key '{2}' has multiple values.", args);

    internal static string RouteEndpointDataSource_ConventionsCannotBeModifiedAfterBuild => "Conventions cannot be added after building the endpoint.";

    internal static string FormatRouteEndpointDataSource_ConventionsCannotBeModifiedAfterBuild (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "Conventions cannot be added after building the endpoint.", args);

    internal static string RouteEndpointDataSource_RequestDelegateCannotBeCalledBeforeBuild => "This RequestDelegate cannot be called before the final endpoint is built.";

    internal static string FormatRouteEndpointDataSource_RequestDelegateCannotBeCalledBeforeBuild (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "This RequestDelegate cannot be called before the final endpoint is built.", args);

    internal static string RegexRouteContraint_NotConfigured => "A route parameter uses the regex constraint, which isn't registered. If this application was configured using CreateSlimBuilder(...) or AddRoutingCore(...) then this constraint is not registered by default. To use the regex constraint, configure route options at app startup: services.Configure<RouteOptions>(options => options.SetParameterPolicy<RegexInlineRouteConstraint>(\"regex\"));";

    internal static string FormatRegexRouteContraint_NotConfigured (params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, "A route parameter uses the regex constraint, which isn't registered. If this application was configured using CreateSlimBuilder(...) or AddRoutingCore(...) then this constraint is not registered by default. To use the regex constraint, configure route options at app startup: services.Configure<RouteOptions>(options => options.SetParameterPolicy<RegexInlineRouteConstraint>(\"regex\"));", args);
}
