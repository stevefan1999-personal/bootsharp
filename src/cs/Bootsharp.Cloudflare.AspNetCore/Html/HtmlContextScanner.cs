using System.Text;

namespace Bootsharp.Cloudflare.AspNetCore.Html;

/// <summary>What an interpolation hole is written as, decided by where in the markup it sits.</summary>
internal enum HtmlHoleKind
{
    /// <summary>Element content: encoded as text.</summary>
    Text,
    /// <summary>A quoted attribute value: encoded as an attribute value.</summary>
    Attribute,
    /// <summary>A quoted attribute value of a URL-valued attribute: scheme-checked, then encoded.</summary>
    Url,
    /// <summary>Nowhere a value can be written safely; <see cref="HtmlHole.Refusal"/> says why.</summary>
    Refused
}

/// <summary>Where one interpolation hole landed.</summary>
internal readonly struct HtmlHole (HtmlHoleKind kind, string? attribute, string? refusal)
{
    /// <summary>How the value has to be written.</summary>
    public HtmlHoleKind Kind { get; } = kind;

    /// <summary>Lowercased attribute name for <see cref="HtmlHoleKind.Attribute"/> and
    /// <see cref="HtmlHoleKind.Url"/>; null otherwise.</summary>
    public string? Attribute { get; } = attribute;

    /// <summary>Why the position is refused, phrased as the tail of "a value written …"; null
    /// unless the kind is <see cref="HtmlHoleKind.Refused"/>.</summary>
    public string? Refusal { get; } = refusal;
}

/// <summary>
/// Tracks where in an HTML document the next character would land, so that an interpolation hole
/// can be encoded for the context it actually sits in rather than for the one context the author
/// remembered.
/// </summary>
/// <remarks>
/// <para>
/// The single source of truth for context-aware encoding, and deliberately the only file this
/// package shares by source with <c>Bootsharp.Cloudflare.Generate</c> (linked, not referenced —
/// the generator is a netstandard2.0 analyzer). The generator runs it over a template's literal
/// segments at compile time and bakes the answer into straight-line writes; the runtime
/// <see cref="HtmlTemplateHandler"/> runs the same instance over the same literals when no
/// generator did. Two implementations of "what does <c>&lt;a href="</c> mean" would be two
/// implementations of the security property.
/// </para>
/// <para>
/// It is a tokenizer, not a parser: it never builds a tree, never matches an open tag to a close
/// tag, and cares about exactly the transitions that change how a value must be encoded. Anything
/// it cannot classify with certainty it refuses rather than guessing — an unquoted attribute value
/// is a refusal, not an attribute.
/// </para>
/// </remarks>
internal sealed class HtmlContextScanner
{
    private enum State
    {
        Text, TagOpen, EndTagOpen, TagName, BeforeAttributeName, AttributeName, AfterAttributeName,
        BeforeAttributeValue, AttributeValueDouble, AttributeValueSingle, AttributeValueUnquoted,
        AfterAttributeValueQuoted, Declaration, Comment, RawText
    }

    /// <summary>
    /// Elements whose content the parser does not read as markup, so a <c>&lt;</c> in them starts no
    /// tag and the scanner has to run to the matching close tag to get out.
    /// </summary>
    /// <remarks>They split in two, and the split is what decides whether a value can be written in
    /// one. In <c>&lt;script&gt;</c> and <c>&lt;style&gt;</c> (raw text) character references are not
    /// decoded either, so encoding a value changes nothing about what runs — those are refused. In
    /// <c>&lt;title&gt;</c> and <c>&lt;textarea&gt;</c> (escapable raw text) references <em>are</em>
    /// decoded, so encoding <c>&lt;</c> is exactly what stops a value from closing the element, and
    /// ordinary text encoding is both safe and necessary. A page title is dynamic often enough that
    /// refusing it would be the wrong answer.</remarks>
    private static readonly string[] rawTextElements = ["script", "style", "textarea", "title"];

    private static readonly string[] escapableRawTextElements = ["textarea", "title"];

    /// <summary>
    /// Attributes whose value is a URL.
    /// </summary>
    /// <remarks>Attribute-encoding alone does not make <c>href="javascript:…"</c> safe — the payload
    /// never leaves the attribute, so no amount of quoting changes what the browser does with it.
    /// These are the attributes where the value's scheme is checked as well.</remarks>
    private static readonly string[] urlAttributes = [
        "action", "background", "cite", "data", "formaction", "href", "longdesc", "manifest",
        "poster", "profile", "src", "usemap", "xlink:href"
    ];

    private readonly StringBuilder name = new();
    private readonly StringBuilder attribute = new();
    private readonly StringBuilder pending = new();
    private State state = State.Text;
    private bool closing;
    private int declaration;
    private int dashes;
    private string rawText = "";

    /// <summary>Feeds one literal segment of the template.</summary>
    public void Advance (string text)
    {
        for (var index = 0; index < text.Length; index++)
            Step(text[index]);
    }

    /// <summary>How a value written at the current position has to be encoded.</summary>
    public HtmlHole Classify () => state switch {
        State.Text => new(HtmlHoleKind.Text, null, null),
        State.AttributeValueDouble or State.AttributeValueSingle => InAttribute(),
        State.RawText => Array.IndexOf(escapableRawTextElements, rawText) >= 0
            ? new(HtmlHoleKind.Text, null, null)
            : Refuse($"inside <{rawText}>, whose content is not markup: " +
                     "HTML encoding cannot make a value safe there"),
        State.Comment => Refuse("inside an HTML comment"),
        State.Declaration => Refuse("inside a declaration such as <!DOCTYPE>"),
        // BeforeAttributeValue is the hole that is the whole value of an unquoted attribute, which
        // is the common way to write one: class={value} rather than class="{value}".
        State.AttributeValueUnquoted or State.BeforeAttributeValue => Refuse(
            "in an unquoted attribute value, where whitespace in the value would start another " +
            "attribute — quote it with \" or '"),
        State.TagOpen or State.EndTagOpen or State.TagName => Refuse("in an element name"),
        _ => Refuse("in an attribute name position, where a value could introduce an attribute " +
                    "the template never wrote")
    };

    private HtmlHole InAttribute ()
    {
        var named = attribute.ToString().ToLowerInvariant();
        // An event handler's value is JavaScript, so it is the attribute equivalent of <script>.
        if (named.StartsWith("on", StringComparison.Ordinal) && named.Length > 2)
            return Refuse($"in the '{named}' event handler attribute, whose value is JavaScript");
        return new(Array.IndexOf(urlAttributes, named) >= 0 ? HtmlHoleKind.Url : HtmlHoleKind.Attribute, named, null);
    }

    private static HtmlHole Refuse (string reason) => new(HtmlHoleKind.Refused, null, reason);

    private void Step (char character)
    {
        switch (state)
        {
            case State.Text: StepText(character); break;
            case State.TagOpen: StepTagOpen(character); break;
            case State.EndTagOpen: StepEndTagOpen(character); break;
            case State.TagName: StepTagName(character); break;
            case State.BeforeAttributeName: StepBeforeAttributeName(character); break;
            case State.AttributeName: StepAttributeName(character); break;
            case State.AfterAttributeName: StepAfterAttributeName(character); break;
            case State.BeforeAttributeValue: StepBeforeAttributeValue(character); break;
            case State.AttributeValueDouble: StepQuotedValue(character, '"'); break;
            case State.AttributeValueSingle: StepQuotedValue(character, '\''); break;
            case State.AttributeValueUnquoted: StepUnquotedValue(character); break;
            case State.AfterAttributeValueQuoted: StepBeforeAttributeName(character); break;
            case State.Declaration: StepDeclaration(character); break;
            case State.Comment: StepComment(character); break;
            case State.RawText: StepRawText(character); break;
        }
    }

    private void StepText (char character)
    {
        if (character != '<') return;
        state = State.TagOpen;
        closing = false;
        name.Clear();
    }

    private void StepTagOpen (char character)
    {
        if (character == '!') { state = State.Declaration; declaration = 0; }
        else if (character == '/') state = State.EndTagOpen;
        else if (char.IsLetter(character)) { state = State.TagName; name.Append(character); }
        // A '<' that starts nothing is text again — "a < b" is not a tag.
        else state = State.Text;
    }

    private void StepEndTagOpen (char character)
    {
        if (char.IsLetter(character)) { state = State.TagName; closing = true; name.Append(character); }
        else state = State.Text;
    }

    private void StepTagName (char character)
    {
        if (character == '>') CloseTag();
        else if (character == '/') state = State.BeforeAttributeName;
        else if (char.IsWhiteSpace(character)) state = State.BeforeAttributeName;
        else name.Append(character);
    }

    private void StepBeforeAttributeName (char character)
    {
        if (character == '>') CloseTag();
        else if (character == '/' || char.IsWhiteSpace(character)) state = State.BeforeAttributeName;
        else { state = State.AttributeName; attribute.Clear(); attribute.Append(character); }
    }

    private void StepAttributeName (char character)
    {
        if (character == '>') CloseTag();
        else if (character == '=') state = State.BeforeAttributeValue;
        else if (character == '/') state = State.BeforeAttributeName;
        else if (char.IsWhiteSpace(character)) state = State.AfterAttributeName;
        else attribute.Append(character);
    }

    private void StepAfterAttributeName (char character)
    {
        if (character == '=') state = State.BeforeAttributeValue;
        else if (!char.IsWhiteSpace(character)) StepBeforeAttributeName(character);
    }

    private void StepBeforeAttributeValue (char character)
    {
        if (char.IsWhiteSpace(character)) return;
        if (character == '"') state = State.AttributeValueDouble;
        else if (character == '\'') state = State.AttributeValueSingle;
        else if (character == '>') CloseTag();
        else state = State.AttributeValueUnquoted;
    }

    private void StepQuotedValue (char character, char quote)
    {
        if (character == quote) state = State.AfterAttributeValueQuoted;
    }

    private void StepUnquotedValue (char character)
    {
        if (character == '>') CloseTag();
        else if (char.IsWhiteSpace(character)) state = State.BeforeAttributeName;
    }

    private void StepDeclaration (char character)
    {
        // Two dashes immediately after "<!" is a comment; anything else is a declaration that ends
        // at the first '>' (a DOCTYPE, which the sample's very first line is).
        if (declaration < 2 && character == '-')
        {
            if (++declaration == 2) { state = State.Comment; dashes = 0; }
        }
        else if (character == '>') state = State.Text;
        else declaration = 2;
    }

    private void StepComment (char character)
    {
        if (character == '-') dashes++;
        else if (character == '>' && dashes >= 2) { state = State.Text; dashes = 0; }
        else dashes = 0;
    }

    private void StepRawText (char character)
    {
        if (character == '<')
        {
            pending.Clear();
            pending.Append(character);
            return;
        }
        if (pending.Length == 0) return;
        pending.Append(character);
        var candidate = pending.ToString();
        var closer = "</" + rawText;
        if (candidate.Length >= closer.Length)
        {
            if (string.Equals(candidate, closer, StringComparison.OrdinalIgnoreCase))
            {
                // Hand the rest of the close tag back to the ordinary tag states, so attributes on a
                // malformed closer cannot leave the scanner stuck in raw text.
                state = State.TagName;
                closing = true;
                name.Clear();
                name.Append(rawText);
            }
            pending.Clear();
        }
        else if (!closer.StartsWith(candidate, StringComparison.OrdinalIgnoreCase)) pending.Clear();
    }

    private void CloseTag ()
    {
        var tag = name.ToString().ToLowerInvariant();
        name.Clear();
        attribute.Clear();
        if (!closing && Array.IndexOf(rawTextElements, tag) >= 0)
        {
            state = State.RawText;
            rawText = tag;
            pending.Clear();
        }
        else state = State.Text;
        closing = false;
    }
}
