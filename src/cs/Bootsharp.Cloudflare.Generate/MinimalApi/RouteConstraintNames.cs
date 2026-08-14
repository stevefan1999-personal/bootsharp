namespace Bootsharp.Cloudflare.Generate.MinimalApi;

/// <summary>
/// The inline constraints the runtime can resolve, checked while the app compiles.
/// </summary>
/// <remarks>
/// The set restates <c>Bootsharp.Cloudflare.AspNetCore</c>'s <c>RouteConstraints.Resolve</c>, which
/// is a closed switch for the reasons that file gives — no <c>ParameterPolicyActivator</c>, no
/// <c>RouteOptions.ConstraintMap</c>, so no reflection. Restating it here moves the failure from
/// "the endpoint table could not be built when the worker booted" to "this pattern does not
/// compile", which is the trade makes for the whole pattern.
/// </remarks>
internal static class RouteConstraintNames
{
    private static readonly Dictionary<string, int[]> supported = new(StringComparer.Ordinal) {
        ["int"] = [0],
        ["long"] = [0],
        ["bool"] = [0],
        ["guid"] = [0],
        ["decimal"] = [0],
        ["double"] = [0],
        ["float"] = [0],
        ["datetime"] = [0],
        ["alpha"] = [0],
        ["file"] = [0],
        ["nonfile"] = [0],
        ["length"] = [1, 2],
        ["minlength"] = [1],
        ["maxlength"] = [1],
        ["min"] = [1],
        ["max"] = [1],
        ["range"] = [2]
    };

    /// <summary>Why a constraint token cannot be used, or null when it can.</summary>
    public static string? Resolve (string token)
    {
        var open = token.IndexOf('(');
        var name = open < 0 ? token : token.Substring(0, open);
        if (name == "regex")
            return "roots System.Text.RegularExpressions, which this package leaves out of the " +
                   "NativeAOT graph. Validate the value inside the handler, or narrow the route with a " +
                   "literal segment.";
        if (!supported.TryGetValue(name, out var arities))
            return $"is not one of the constraints this package resolves. Supported: {string.Join(", ", supported.Keys)}.";
        if (open < 0) return arities.Contains(0) ? null : $"takes {string.Join(" or ", arities)} argument(s), written as '{name}(…)'.";
        if (!token.EndsWith(")")) return "is missing its closing parenthesis.";
        var inner = token.Substring(open + 1, token.Length - open - 2);
        var count = inner.Length == 0 ? 0 : inner.Split(',').Length;
        return arities.Contains(count)
            ? null
            : $"was given {count} argument(s); '{name}' takes {string.Join(" or ", arities)}.";
    }
}
