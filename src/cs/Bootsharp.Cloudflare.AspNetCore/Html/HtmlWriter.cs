using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Bootsharp.Cloudflare.AspNetCore.Html;

/// <summary>
/// The sink a template renders into.
/// </summary>
/// <remarks>
/// <para>
/// A template is a method that takes one of these and writes; it never builds and returns a string.
/// That is the whole of the "streaming-shaped output" the design asks for: today the only
/// implementation buffers (<see cref="StringHtmlWriter"/>), because a worker response body is
/// buffered until milestone 0b lands a live <c>ReadableStream</c> handle. When it does, a
/// writer that pushes each chunk across the interop boundary is a second subclass and every
/// template written against this class starts streaming without a source change.
/// </para>
/// <para>
/// The sinks below are the encoding contexts, one method each. Which one a hole in a template gets
/// is not the author's decision — <see cref="HtmlContextScanner"/> reads it off the surrounding
/// markup, at compile time when the generator is present and at render time otherwise. The author's
/// only lever is <see cref="HtmlString"/>, which is the one way to write markup verbatim and is
/// therefore the one thing to grep for in a security review. That is the inverse of the
/// interpolated-string SSR this replaces, where every hole was raw until someone remembered to
/// wrap it.
/// </para>
/// </remarks>
public abstract class HtmlWriter
{
    /// <summary>Writes text that is already markup, without encoding it.</summary>
    /// <remarks>The one primitive a subclass has to implement. Everything else on this class
    /// encodes into it.</remarks>
    public abstract void WriteLiteral (string value);

    /// <summary>Writes a span of text that is already markup, without encoding it.</summary>
    /// <remarks>Overridable so that an encoder can hand over slices of its input rather than
    /// allocating an encoded copy of every value it writes.</remarks>
    public virtual void WriteLiteral (ReadOnlySpan<char> value) => WriteLiteral(value.ToString());

    /// <summary>Writes a pre-rendered fragment verbatim.</summary>
    public void WriteHtml (HtmlString value) => WriteLiteral(value.Value);

    /// <summary>Writes a value as element content.</summary>
    public void WriteText (string? value) => HtmlEncoding.Encode(this, value);

    /// <inheritdoc cref="WriteText(string?)"/>
    public void WriteText (HtmlString value) => WriteHtml(value);

    /// <inheritdoc cref="WriteText(string?)"/>
    /// <remarks>Formatting is invariant, never the ambient culture: the same model has to render the
    /// same bytes on every isolate, and a worker has no user locale to speak of — the sample's apps
    /// publish with <c>InvariantGlobalization</c> anyway.</remarks>
    public void WriteText<T> (T value, string? format = null) => HtmlEncoding.Encode(this, Format(value, format));

    /// <summary>Writes a value as a quoted attribute value.</summary>
    public void WriteAttribute (string? value) => HtmlEncoding.Encode(this, value);

    /// <inheritdoc cref="WriteAttribute(string?)"/>
    public void WriteAttribute (HtmlString value) => WriteHtml(value);

    /// <inheritdoc cref="WriteAttribute(string?)"/>
    public void WriteAttribute<T> (T value, string? format = null) => HtmlEncoding.Encode(this, Format(value, format));

    /// <summary>Writes a value as the URL of a URL-valued attribute.</summary>
    /// <remarks>Refuses the scheme rather than the characters: a <c>javascript:</c> URL survives any
    /// amount of attribute encoding, because the payload never has to leave the attribute.</remarks>
    public void WriteUrl (string? value) => HtmlEncoding.Url(this, value);

    /// <inheritdoc cref="WriteUrl(string?)"/>
    public void WriteUrl<T> (T value, string? format = null) => HtmlEncoding.Url(this, Format(value, format));

    /// <summary>Renders an interpolated string as a template into this writer.</summary>
    /// <remarks>The method has an empty body on purpose: the handler is constructed with this writer
    /// and has already written everything by the time the call is made. When the template lives in a
    /// method marked <see cref="HtmlTemplateAttribute"/>, the generator replaces the calls to that
    /// method with straight-line writes and this path is never entered at all.</remarks>
    public void Write ([InterpolatedStringHandlerArgument("")] ref HtmlTemplateHandler template) { }

    private static string? Format<T> (T value, string? format)
    {
        if (value is null) return null;
        if (value is string text) return text;
        if (value is IFormattable formattable) return formattable.ToString(format, CultureInfo.InvariantCulture);
        return value.ToString();
    }
}

/// <summary>A writer that buffers into a <see cref="StringBuilder"/>.</summary>
/// <remarks>The only implementation while response bodies are buffered. It is also what makes a
/// template unit-testable without an HTTP context: render into one and assert on
/// <see cref="ToString"/>.</remarks>
public sealed class StringHtmlWriter : HtmlWriter
{
    private readonly StringBuilder builder;

    public StringHtmlWriter () : this(new StringBuilder()) { }
    public StringHtmlWriter (StringBuilder builder) => this.builder = builder;

    public override void WriteLiteral (string value) => builder.Append(value);
    public override void WriteLiteral (ReadOnlySpan<char> value) => builder.Append(value);

    /// <summary>The markup written so far.</summary>
    public override string ToString () => builder.ToString();
}

/// <summary>A template: everything it writes, it writes into the given writer.</summary>
public delegate void HtmlBody (HtmlWriter html);

/// <summary>
/// Marks a method as an HTML template, so that the generator compiles it.
/// </summary>
/// <remarks>
/// <para>
/// The method has to be static, return <c>void</c>, take a <see cref="HtmlWriter"/> parameter, and
/// have a body that is exactly one <c>writer.Write($"…")</c> call on a literal interpolated string.
/// In that shape the generator can read the markup at compile time: it runs
/// <see cref="HtmlContextScanner"/> over the literal segments, picks each hole's sink from the
/// context it lands in, and emits an interceptor that replaces every call to the method with the
/// resulting straight-line writes. No scanning, no handler, no per-render decisions.
/// </para>
/// <para>
/// Nothing depends on the attribute for correctness. A template the generator declines — the shape
/// is different, a hole reads a private member, the call site is in another assembly — renders
/// through <see cref="HtmlTemplateHandler"/> instead, which scans the same literals with the same
/// scanner and reaches the same bytes. The attribute buys speed and compile-time diagnostics, never
/// safety.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class HtmlTemplateAttribute : Attribute;
