using System.Runtime.CompilerServices;

namespace Bootsharp.Cloudflare.AspNetCore.Html;

/// <summary>
/// Markup that is already encoded, and is therefore written verbatim.
/// </summary>
/// <remarks>
/// The only escape hatch out of automatic encoding, and named so that reviewing every place a
/// template trusts a value is one grep for <c>HtmlString</c>. There is no implicit conversion from
/// <see cref="string"/>: turning text into markup is always a decision someone writes down.
/// </remarks>
public readonly struct HtmlString (string? value) : IEquatable<HtmlString>
{
    /// <summary>Renders as nothing.</summary>
    public static HtmlString Empty => default;

    /// <summary>The markup.</summary>
    public string Value { get; } = value ?? "";

    /// <summary>Takes the string as markup, encoding nothing.</summary>
    /// <remarks>Every use is a claim that the value cannot carry attacker-controlled markup.</remarks>
    public static HtmlString Raw (string? html) => new(html);

    /// <summary>Renders a template into a fragment.</summary>
    /// <remarks>The composition seam: a fragment built here can be a hole in another template and is
    /// written verbatim there, because it was already encoded here. Use it for the conditional
    /// pieces a single interpolated string cannot express — an optional banner, a row of a list.</remarks>
    public static HtmlString From (HtmlTemplateHandler template) => template.ToHtmlString();

    /// <summary>Renders a body into a fragment.</summary>
    public static HtmlString From (HtmlBody body)
    {
        var writer = new StringHtmlWriter();
        body(writer);
        return new(writer.ToString());
    }

    public bool Equals (HtmlString other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override bool Equals (object? obj) => obj is HtmlString other && Equals(other);
    public override int GetHashCode () => Value.GetHashCode();
    public override string ToString () => Value;

    public static bool operator == (HtmlString left, HtmlString right) => left.Equals(right);
    public static bool operator != (HtmlString left, HtmlString right) => !left.Equals(right);
}

/// <summary>
/// Renders an interpolated string as HTML, deciding each hole's encoding from the markup around it.
/// </summary>
/// <remarks>
/// <para>
/// The fallback half of the two-tier writer, and the half that makes the tier split safe: a
/// template renders correctly with no generator involved at all, so the generator is an optimizer
/// rather than a load-bearing part of the security story. It costs one pass over the literal
/// segments per render — the segments are compile-time constants, so the pass is a state machine
/// over characters with no allocation beyond the encoded values themselves.
/// </para>
/// <para>
/// A hole that lands somewhere no value can be written safely — inside <c>&lt;script&gt;</c>, in an
/// unquoted attribute value, in an attribute name — throws. That is the same answer the generator
/// gives as a build error; this one just arrives later, on a template the generator never saw.
/// </para>
/// </remarks>
[InterpolatedStringHandler]
public ref struct HtmlTemplateHandler
{
    private readonly HtmlWriter writer;
    private readonly StringHtmlWriter? owned;
    private readonly HtmlContextScanner scanner;

    /// <summary>Renders into the writer the interpolated string is written against.</summary>
    public HtmlTemplateHandler (int literalLength, int formattedCount, HtmlWriter writer)
    {
        this.writer = writer;
        scanner = new HtmlContextScanner();
    }

    /// <summary>Renders into a fragment of its own, for <see cref="HtmlString.From(HtmlTemplateHandler)"/>.</summary>
    public HtmlTemplateHandler (int literalLength, int formattedCount)
    {
        owned = new StringHtmlWriter();
        writer = owned;
        scanner = new HtmlContextScanner();
    }

    public void AppendLiteral (string value)
    {
        scanner.Advance(value);
        writer.WriteLiteral(value);
    }

    public void AppendFormatted (string? value) => Write(value, null);
    public void AppendFormatted (HtmlString value) => Verbatim(value);
    public void AppendFormatted<T> (T value) => Write(value, null);
    public void AppendFormatted<T> (T value, string? format) => Write(value, format);

    /// <summary>Alignment is not markup and cannot be padded into it meaningfully.</summary>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public void AppendFormatted<T> (T value, int alignment, string? format = null) =>
        throw new InvalidOperationException(
            "An HTML template hole cannot take an alignment: padding a value with spaces changes " +
            "the markup around it, not the layout. Format the value before interpolating it.");

    internal HtmlString ToHtmlString () => new(owned?.ToString());

    private void Write<T> (T value, string? format)
    {
        // HtmlString reaching the generic overload (through a nullable or a generic caller) still has
        // to be written verbatim, or a composed fragment would arrive double-encoded.
        if (value is HtmlString html) { Verbatim(html); return; }
        var hole = Classify();
        switch (hole.Kind)
        {
            case HtmlHoleKind.Text: writer.WriteText(value, format); break;
            case HtmlHoleKind.Attribute: writer.WriteAttribute(value, format); break;
            case HtmlHoleKind.Url: writer.WriteUrl(value, format); break;
        }
    }

    private void Verbatim (HtmlString value)
    {
        Classify();
        writer.WriteHtml(value);
    }

    private HtmlHole Classify ()
    {
        var hole = scanner.Classify();
        if (hole.Kind == HtmlHoleKind.Refused)
            throw new InvalidOperationException($"An HTML template writes a value {hole.Refusal}.");
        return hole;
    }
}
