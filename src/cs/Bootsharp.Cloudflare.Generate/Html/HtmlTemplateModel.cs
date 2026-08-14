using Bootsharp.Cloudflare.AspNetCore.Html;
using Bootsharp.Cloudflare.Generate.MinimalApi;
using Bootsharp.Cloudflare.Projection;

namespace Bootsharp.Cloudflare.Generate.Html;

/// <summary>One piece of a compiled template: either literal markup or one interpolation hole.</summary>
/// <param name="Literal">The markup, when this is a literal segment.</param>
/// <param name="Expression">The hole's C# expression, re-emitted verbatim into the interceptor.</param>
/// <param name="Kind">The sink the hole's context resolved to.</param>
/// <param name="Verbatim">Whether the hole's static type is <c>HtmlString</c>, so it is written
/// without encoding whatever its context is.</param>
/// <param name="Format">The hole's format specifier, or null.</param>
internal sealed record HtmlSegment(
    string? Literal,
    string? Expression,
    HtmlHoleKind Kind,
    bool Verbatim,
    string? Format) : IEquatable<HtmlSegment>;

/// <summary>One parameter of a template method, reproduced on the interceptor.</summary>
/// <remarks>The names have to match the template's: the hole expressions are re-emitted verbatim and
/// they read the parameters by name.</remarks>
internal sealed record HtmlParameter(string Name, string Type) : IEquatable<HtmlParameter>;

/// <summary>
/// A method marked <c>[HtmlTemplate]</c>, compiled: the markup split into literals and classified
/// holes, plus everything needed to emit an interceptor with the same signature.
/// </summary>
/// <param name="Usings">The using directives of the template's own file, re-emitted so that a hole
/// expression resolves the same names in the generated file that it resolved in the source one.</param>
internal sealed record HtmlTemplateModel(
    string Method,
    string ContainingType,
    EquatableArray<HtmlParameter> Parameters,
    EquatableArray<HtmlSegment> Segments,
    EquatableArray<string> Usings)
{
    /// <summary>Identity of the template a call site is intercepted for.</summary>
    public string Key => ContainingType + "." + Method;
}

/// <summary>What one <c>[HtmlTemplate]</c> method resolved to: a model, or the defects that stopped it.</summary>
/// <remarks>Declining is safe here in a way it is not for the <c>Map*</c> generator: an
/// un-intercepted template still renders, through the runtime handler, with the same encoding
/// decisions. So a shape this generator cannot compile is a warning about lost speed, not an error
/// about lost behaviour — the errors are reserved for holes that no encoder can make safe, which
/// would throw at render time anyway.</remarks>
internal sealed record HtmlTemplateCandidate(
    HtmlTemplateModel? Model,
    EquatableArray<Defect> Defects);

/// <summary>A call site of a template method, with the location an interceptor would replace.</summary>
internal sealed record HtmlCallSite(string Key, InterceptedLocation Location);
