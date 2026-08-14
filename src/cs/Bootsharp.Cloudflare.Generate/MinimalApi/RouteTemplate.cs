using System.Collections.Generic;
using System.Linq;
using Bootsharp.Cloudflare.Projection;

namespace Bootsharp.Cloudflare.Generate.MinimalApi;

/// <summary>One parameter of a route pattern, as the pattern text declares it.</summary>
/// <param name="CatchAll"><c>{*rest}</c> or <c>{**rest}</c>.</param>
/// <param name="Optional"><c>{id?}</c>.</param>
/// <param name="Default">Text after <c>=</c>, or null.</param>
/// <param name="Policies">Inline constraint tokens, in declaration order.</param>
internal sealed record RouteParameter(
    string Name,
    bool CatchAll,
    bool Optional,
    string? Default,
    EquatableArray<string> Policies);

/// <summary>A route pattern parsed at compile time.</summary>
/// <param name="Precedence">The value <c>RoutePrecedence.ComputeInbound</c> would compute for it.</param>
internal sealed record RouteTemplate(
    EquatableArray<RouteParameter> Parameters,
    decimal Precedence);

/// <summary>
/// Parses route patterns at generation time — the deliberate divergence of
/// </summary>
/// <remarks>
/// <para>
/// ASP.NET Core's Request Delegate Generator never looks inside the pattern: it emits
/// <c>ResolveFromRouteOrQuery(name, routeParameterNames)</c> and lets <c>RoutePatternFactory.Parse</c>
/// decide at startup, so a typo in <c>{id}</c> is a 404 (or a silently query-bound parameter) at
/// runtime. Parsing here turns that into a build error, lets the binding ladder pick route vs query
/// with certainty, and yields the inbound precedence the matcher would otherwise recompute.
/// </para>
/// <para>
/// This mirrors the grammar and the validation rules of upstream's
/// <c>src/Http/Routing/src/Patterns/RoutePatternParser.cs</c> and
/// <c>RouteParameterParser.cs</c> rather than vendoring them: those types build a
/// <c>RoutePattern</c> through <c>RoutePatternFactory</c>, which needs <c>RouteValueDictionary</c>,
/// <c>IParameterPolicy</c> and the constraint types, and they are written against APIs
/// (<c>SearchValues&lt;char&gt;</c>, <c>ArgumentNullException.ThrowIfNull</c>) that a netstandard2.0
/// analyzer does not have. The runtime still parses the same text with the real parser, so this one
/// only has to agree — which is why anything it is unsure of becomes a diagnostic rather than a
/// guess.
/// </para>
/// </remarks>
internal static class RouteTemplateParser
{
    private const string invalidNameChars = "/{}?*";

    /// <summary>Parses a pattern, or explains what is wrong with it.</summary>
    public static bool TryParse (string pattern, out RouteTemplate template, out string error)
    {
        template = new RouteTemplate(EquatableArray<RouteParameter>.Empty, 0m);
        error = "";
        var text = TrimPrefix(pattern);
        var segments = new List<List<Part>>();
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] == '/')
            {
                error = "The route pattern cannot contain two consecutive '/' characters.";
                return false;
            }
            if (!TryParseSegment(text, ref index, out var parts, out error)) return false;
            segments.Add(parts);
            // A trailing separator ends the pattern without opening another segment, as upstream's
            // outer loop does: "/api/" is one segment, "/api//x" is the error above.
            if (index < text.Length && text[index] == '/') index++;
        }
        if (!Validate(segments, out error)) return false;
        var parameters = segments
            .SelectMany(static parts => parts)
            .OfType<ParameterPart>()
            .Select(static part => part.Parameter)
            .ToArray();
        template = new RouteTemplate(new(parameters), Precedence(segments));
        return true;
    }

    /// <summary>
    /// Inbound precedence, digit for digit as <c>RoutePrecedence.ComputeInboundPrecedenceDigit</c>
    /// assigns them: literal 1, multi-part 2, parameter 3 (2 when constrained), catch-all 5 (4 when
    /// constrained), combined one decimal place per segment. The required-value branch upstream has
    /// is unreachable here — required values come from route groups and MVC conventions, neither of
    /// which this package has.
    /// </summary>
    private static decimal Precedence (List<List<Part>> segments)
    {
        var precedence = 0m;
        for (var index = 0; index < segments.Count; index++)
        {
            var parts = segments[index];
            var digit = parts.Count > 1 ? 2 : parts[0] switch {
                LiteralPart => 1,
                ParameterPart { Parameter: { CatchAll: true, Policies.Length: > 0 } } => 4,
                ParameterPart { Parameter.CatchAll: true } => 5,
                ParameterPart { Parameter.Policies.Length: > 0 } => 2,
                _ => 3
            };
            precedence += digit / Pow10(index);
        }
        return precedence;
    }

    private static decimal Pow10 (int power)
    {
        var value = 1m;
        for (var index = 0; index < power; index++) value *= 10m;
        return value;
    }

    private static bool TryParseSegment (string text, ref int index, out List<Part> parts, out string error)
    {
        parts = [];
        error = "";
        while (index < text.Length && text[index] != '/')
            if (text[index] == '{' && index + 1 < text.Length && text[index + 1] == '{')
            {
                if (!TryParseLiteral(text, ref index, parts, out error)) return false;
            }
            else if (text[index] == '{')
            {
                if (!TryParseParameter(text, ref index, parts, out error)) return false;
            }
            else if (!TryParseLiteral(text, ref index, parts, out error)) return false;
        if (parts.Count != 0) return true;
        error = "The route pattern cannot contain an empty segment.";
        return false;
    }

    private static bool TryParseLiteral (string text, ref int index, List<Part> parts, out string error)
    {
        error = "";
        var literal = "";
        while (index < text.Length && text[index] != '/')
        {
            var current = text[index];
            if (current == '{' || current == '}')
            {
                // Braces only appear in a literal doubled, and a doubled brace stands for one.
                if (index + 1 < text.Length && text[index + 1] == current)
                {
                    literal += current;
                    index += 2;
                    continue;
                }
                if (current == '}')
                {
                    error = "The route pattern has an unescaped '}' outside a parameter; write '}}' for a literal one.";
                    return false;
                }
                break;
            }
            if (current == '?')
            {
                error = "The route pattern has a '?' in a literal segment, which is not allowed.";
                return false;
            }
            literal += current;
            index++;
        }
        if (literal.Length > 0) parts.Add(new LiteralPart(literal));
        return true;
    }

    private static bool TryParseParameter (string text, ref int index, List<Part> parts, out string error)
    {
        error = "";
        var start = ++index;
        var depth = 0;
        while (index < text.Length)
        {
            var current = text[index];
            if (current == '{' && index + 1 < text.Length && text[index + 1] == '{') index += 2;
            else if (current == '}' && index + 1 < text.Length && text[index + 1] == '}') index += 2;
            else if (current == '(') { depth++; index++; }
            else if (current == ')') { depth--; index++; }
            // A '}' inside a constraint's parentheses belongs to the constraint, e.g. {p:regex(\d{3})}.
            else if (current == '}' && depth <= 0) break;
            else index++;
        }
        if (index >= text.Length)
        {
            error = "The route pattern has a '{' with no matching '}'.";
            return false;
        }
        var inner = text.Substring(start, index - start);
        index++;
        if (!TryParseParameterBody(inner, out var parameter, out error)) return false;
        parts.Add(new ParameterPart(parameter));
        return true;
    }

    /// <summary>
    /// Splits <c>**name:policy(arg):policy=default</c> the way upstream's
    /// <c>RouteParameterParser</c> does: catch-all marker, then name up to the first ':' or '='
    /// that is not the first character, then policies, then the default value.
    /// </summary>
    private static bool TryParseParameterBody (string text, out RouteParameter parameter, out string error)
    {
        parameter = new RouteParameter("", false, false, null, EquatableArray<string>.Empty);
        error = "";
        if (text.Length == 0)
        {
            error = "The route pattern has a parameter with no name.";
            return false;
        }
        var start = 0;
        var end = text.Length - 1;
        var catchAll = false;
        if (text.StartsWith("**")) { catchAll = true; start = 2; }
        else if (text[0] == '*') { catchAll = true; start = 1; }
        var optional = text[end] == '?';
        if (optional) end--;
        var index = start;
        var name = "";
        while (index <= end)
        {
            var current = text[index];
            if ((current == ':' || current == '=') && index != start)
            {
                name = text.Substring(start, index - start);
                break;
            }
            if (index == end) name = text.Substring(start, index - start + 1);
            index++;
        }
        var policies = new List<string>();
        while (index <= end && text[index] == ':')
        {
            var policyStart = ++index;
            var depth = 0;
            while (index <= end && (depth > 0 || (text[index] != ':' && text[index] != '=')))
            {
                if (text[index] == '(') depth++;
                else if (text[index] == ')') depth--;
                index++;
            }
            policies.Add(text.Substring(policyStart, index - policyStart));
        }
        string? defaultValue = null;
        if (index <= end && text[index] == '=') defaultValue = text.Substring(index + 1, end - index);
        if (name.Length == 0)
        {
            error = "The route pattern has a parameter with no name.";
            return false;
        }
        if (name.IndexOfAny(invalidNameChars.ToCharArray()) >= 0)
        {
            error = $"The route parameter name '{name}' contains one of the invalid characters '{invalidNameChars}'.";
            return false;
        }
        if (catchAll && optional)
        {
            error = $"The catch-all parameter '{name}' cannot be marked optional.";
            return false;
        }
        parameter = new RouteParameter(name, catchAll, optional, defaultValue, new(policies));
        return true;
    }

    /// <summary>
    /// The whole-pattern rules upstream checks in <c>IsSegmentValid</c>/<c>IsAllValid</c>: a
    /// catch-all is last and alone, an optional parameter is last and separated from what precedes
    /// it by a '.', parameters are not adjacent, and names are unique.
    /// </summary>
    private static bool Validate (List<List<Part>> segments, out string error)
    {
        error = "";
        var names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        for (var s = 0; s < segments.Count; s++)
        {
            var parts = segments[s];
            for (var p = 0; p < parts.Count; p++)
            {
                if (parts[p] is not ParameterPart { Parameter: var parameter }) continue;
                if (!names.Add(parameter.Name))
                {
                    error = $"The route parameter name '{parameter.Name}' appears more than once.";
                    return false;
                }
                if (p > 0 && parts[p - 1] is ParameterPart)
                {
                    error = $"The route parameter '{parameter.Name}' immediately follows another parameter; " +
                            "a literal has to separate them.";
                    return false;
                }
                if (parameter.CatchAll && (s != segments.Count - 1 || parts.Count != 1))
                {
                    error = $"The catch-all parameter '{parameter.Name}' has to be the last segment of the pattern " +
                            "and the only thing in it.";
                    return false;
                }
                if (!parameter.Optional) continue;
                if (s != segments.Count - 1 || p != parts.Count - 1)
                {
                    error = $"The optional parameter '{parameter.Name}' has to be the last part of the pattern.";
                    return false;
                }
                if (p > 0 && parts[p - 1] is not LiteralPart { Text: "." })
                {
                    error = $"The optional parameter '{parameter.Name}' can only be preceded by a '.' in its segment.";
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>Drops the leading <c>~/</c> or <c>/</c> a pattern may be written with.</summary>
    private static string TrimPrefix (string pattern)
    {
        if (pattern.StartsWith("~/")) return pattern.Substring(2);
        if (pattern.StartsWith("/")) return pattern.Substring(1);
        return pattern;
    }

    private abstract record Part;
    private sealed record LiteralPart(string Text) : Part;
    private sealed record ParameterPart(RouteParameter Parameter) : Part;
}
