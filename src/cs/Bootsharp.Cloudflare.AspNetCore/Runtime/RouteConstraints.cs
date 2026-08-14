using System.Globalization;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Bootsharp.Cloudflare.AspNetCore;

/// <summary>
/// Turns an inline constraint token — the <c>int</c> in <c>{id:int}</c> — into the constraint that
/// enforces it.
/// </summary>
/// <remarks>
/// <para>
/// ASP.NET Core does this through <c>DefaultInlineConstraintResolver</c> →
/// <c>ParameterPolicyActivator.CreateParameterPolicy</c>, which calls <c>Type.GetConstructors()</c>
/// and picks one by arity against <c>RouteOptions.ConstraintMap</c> — a reflective path whose only
/// defence against trimming is a <c>[DynamicallyAccessedMembers]</c> annotation on the map
///. Notably it is the reason Blazor could not take the <c>Constraints</c> folder
/// without also taking three files of activation machinery.
/// </para>
/// <para>
/// A <c>switch</c> is the whole of what that machinery achieves for a closed set of constraints, so
/// that is what this is. The set is closed on purpose: an unknown token is an error naming the
/// supported ones, raised when the endpoint table is built rather than on the request that first
/// hits the route.
/// </para>
/// </remarks>
public static class RouteConstraints
{
    /// <summary>Resolves one inline constraint token.</summary>
    /// <param name="token">The token as written in the pattern, e.g. <c>int</c> or <c>range(1,10)</c>.</param>
    /// <exception cref="RoutePatternException">The token is unknown or malformed.</exception>
    public static IRouteConstraint Resolve (string token)
    {
        ArgumentNullException.ThrowIfNull(token);
        var open = token.IndexOf('(');
        var name = open < 0 ? token : token[..open];
        var arguments = open < 0 ? [] : ParseArguments(token, open);
        return name switch
        {
            "int" => new IntRouteConstraint(),
            "long" => new LongRouteConstraint(),
            "bool" => new BoolRouteConstraint(),
            "guid" => new GuidRouteConstraint(),
            "decimal" => new DecimalRouteConstraint(),
            "double" => new DoubleRouteConstraint(),
            "float" => new FloatRouteConstraint(),
            "datetime" => new DateTimeRouteConstraint(),
            "alpha" => new AlphaRouteConstraint(),
            "file" => new FileNameRouteConstraint(),
            "nonfile" => new NonFileNameRouteConstraint(),
            "length" => arguments.Length == 1
                ? new LengthRouteConstraint(Count(name, arguments[0]))
                : new LengthRouteConstraint(Count(name, Argument(name, arguments, 0)), Count(name, Argument(name, arguments, 1))),
            "minlength" => new MinLengthRouteConstraint(Count(name, Argument(name, arguments, 0))),
            "maxlength" => new MaxLengthRouteConstraint(Count(name, Argument(name, arguments, 0))),
            "min" => new MinRouteConstraint(Number(name, Argument(name, arguments, 0))),
            "max" => new MaxRouteConstraint(Number(name, Argument(name, arguments, 0))),
            "range" => new RangeRouteConstraint(Number(name, Argument(name, arguments, 0)), Number(name, Argument(name, arguments, 1))),
            "regex" => throw Unsupported(
                "regex route constraints root System.Text.RegularExpressions, which is a large " +
                "NativeAOT dependency; they are a separate opt-in layer"),
            _ => throw Unsupported($"'{name}' is not a known route constraint. Supported: int, long, " +
                                   "bool, guid, decimal, double, float, datetime, alpha, file, nonfile, " +
                                   "length, minlength, maxlength, min, max, range"),
        };
    }

    private static string[] ParseArguments (string token, int open)
    {
        if (!token.EndsWith(')'))
            throw Unsupported($"the constraint '{token}' is missing its closing parenthesis");
        var inner = token[(open + 1)..^1];
        return inner.Length == 0 ? [] : inner.Split(',');
    }

    private static string Argument (string name, string[] arguments, int index) =>
        index < arguments.Length
            ? arguments[index]
            : throw Unsupported($"the '{name}' constraint needs {index + 1} argument(s)");

    private static long Number (string name, string argument) =>
        long.TryParse(argument.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw Unsupported($"the '{name}' constraint takes whole numbers; got '{argument}'");

    // The length constraints count characters and take int; the value constraints compare numbers
    // and take long. Both go through the same parse so the diagnostic is worded once.
    private static int Count (string name, string argument) => checked((int)Number(name, argument));

    private static RoutePatternException Unsupported (string reason) => new(reason, reason);
}

/// <summary>Constrains a route parameter to letters only.</summary>
/// <remarks>
/// Reimplemented rather than vendored: upstream's <c>AlphaRouteConstraint</c> derives from
/// <c>RegexRouteConstraint</c> with the pattern <c>^[a-z]*$</c>, and taking it would pull
/// <c>System.Text.RegularExpressions</c> into the graph for the one constraint that least needs it
///. Case-insensitive ASCII letters, matching upstream's <c>IgnoreCase</c> option.
/// </remarks>
public sealed class AlphaRouteConstraint : IRouteConstraint
{
    /// <inheritdoc/>
    public bool Match (string routeKey, RouteValueDictionary values)
    {
        ArgumentNullException.ThrowIfNull(routeKey);
        ArgumentNullException.ThrowIfNull(values);
        if (!values.TryGetValue(routeKey, out var value) || value is null) return false;
        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        if (text is null) return false;
        foreach (var character in text)
            if (!char.IsAsciiLetter(character))
                return false;
        return true;
    }
}
