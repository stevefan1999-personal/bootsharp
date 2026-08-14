using System.Diagnostics.CodeAnalysis;

namespace Microsoft.AspNetCore.Routing.Patterns;

/// <summary>
/// Restores the one <see cref="RoutePatternFactory"/> entry point the vendored selection drops.
/// </summary>
/// <remarks>
/// The vendored file is taken at Blazor's <c>COMPONENTS</c> seam, which excludes every <c>Parse</c>
/// overload — Blazor calls the internal parser directly. Of those overloads only the
/// <c>string</c>-shaped one is AOT-clean upstream (the <c>object defaults</c> ones are the seven
/// <c>[RequiresUnreferencedCode]</c> sites catalogues), so it is the only one that
/// comes back, and it comes back with upstream's exact signature: whatever builds endpoints — our
/// own <c>Map*</c> here, the interceptors the generator emits in the app — needs a public way to
/// turn a pattern string into a <see cref="RoutePattern"/>.
/// </remarks>
public static partial class RoutePatternFactory
{
    /// <summary>Parses a route pattern into a <see cref="RoutePattern"/>.</summary>
    /// <param name="pattern">The route pattern, e.g. <c>api/todos/{id:int}</c>.</param>
    /// <exception cref="RoutePatternException">The pattern is not valid.</exception>
    public static RoutePattern Parse ([StringSyntax("Route")] string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return RoutePatternParser.Parse(pattern);
    }
}
