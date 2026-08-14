using Microsoft.CodeAnalysis;

namespace Bootsharp.Cloudflare.Razor;

/// <summary>
/// The diagnostics this generator reports, allocated the <c>CFW060-CFW069</c> block.
/// </summary>
/// <remarks>
/// <para>
/// Ids are allocated in per-front-end blocks because a user suppresses, escalates or documents a
/// diagnostic by its <em>id</em>: two generators sharing a number means one project's
/// <c>NoWarn</c> silences another's. The block table lives in
/// <c>Bootsharp.Cloudflare.Generate.Test/DiagnosticIdTests</c>, which is also the allocation record;
/// this block is reserved there even though the scan cannot reach this assembly.
/// </para>
/// <para>
/// All three are errors and none is configurable. That is deliberate: every construct
/// <see cref="Refused"/> names parses without complaint and compiles to something plausible and
/// wrong — <c>@inject IFoo Foo</c> becomes <c>html.WriteText(inject)</c> followed by the literal text
/// <c>" IFoo Foo"</c> — so a warning would leave the page rendering, silently, with the wrong
/// output. Refusing by name at the page's own line is the only safe reading.
/// </para>
/// </remarks>
internal static class CshtmlDiagnostics
{
    private const string category = "Bootsharp";

    /// <summary>A construct that parses as Razor but has no meaning on this tier.</summary>
    public static readonly DiagnosticDescriptor Refused = new("CFW060",
        "Unsupported Razor construct", "{0}", category, DiagnosticSeverity.Error, true,
        "The .cshtml tier compiles Razor syntax against Bootsharp's HtmlWriter, not against the " +
        "ASP.NET Core MVC runtime, which does not exist for this target. Constructs that need that " +
        "runtime are refused by name rather than mis-compiled.");

    /// <summary>An error the Razor compiler itself raised while parsing or lowering the page.</summary>
    public static readonly DiagnosticDescriptor Invalid = new("CFW061",
        "Razor compilation failed", "{0}", category, DiagnosticSeverity.Error, true,
        "The page could not be compiled. The message and location come from the Razor compiler.");

    /// <summary>Two declarations that would land on the same generated name.</summary>
    public static readonly DiagnosticDescriptor Collision = new("CFW062",
        "Generated page member collides", "{0}", category, DiagnosticSeverity.Error, true,
        "A page compiles to a static partial class with one Render method whose parameters come " +
        "from the page's @model and @param directives. A name declared twice would surface as a C# " +
        "error inside generated code, naming a file the author did not write.");
}
