using System.Text;
using Microsoft.AspNetCore.Razor.Language.CodeGeneration;
using Microsoft.AspNetCore.Razor.Language.Intermediate;

namespace Bootsharp.Cloudflare.Razor;

/// <summary>
/// Seam three: what markup and holes turn into.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RuntimeNodeWriter"/> already emits the right <em>shape</em> — a straight run of calls
/// on a receiver — and the names it calls are settable properties, so literal markup and element
/// content need only be pointed at <c>HtmlWriter.WriteLiteral</c> and <c>HtmlWriter.WriteText</c>.
/// Everything inherited keeps the compiler's own <c>#line</c> mapping back into the page.
/// </para>
/// <para>
/// Attributes are the part that has to be rewritten. MVC's protocol is a six-argument
/// <c>BeginWriteAttribute</c> / <c>WriteAttributeValue</c> / <c>EndWriteAttribute</c> sequence whose
/// purpose is to buffer a whole attribute so it can be dropped when its only value is null or false.
/// Flattening it into literals is what makes the emitted program straight-line and allocation-free,
/// and it is what lets the encoding sink be chosen <em>here</em>, at compile time, from the attribute
/// name: <c>WriteUrl</c> for a URL-valued attribute, <c>WriteAttribute</c> otherwise. That compile-time
/// choice is why this tier never needs the runtime <c>HtmlContextScanner</c> — the Razor parser has
/// already decided whether a hole is element content or an attribute value, and it is cheaper to ask
/// it than to rescan the markup.
/// </para>
/// <para>
/// The behavioural difference the flattening buys is documented rather than hidden:
/// <c>title="@maybe"</c> with a null value emits <c>title=""</c> here where MVC would omit the
/// attribute entirely. Nothing else about the output differs.
/// </para>
/// </remarks>
internal sealed class CshtmlNodeWriter : RuntimeNodeWriter
{
    private const string writer = CshtmlClassifierPass.WriterParameter;
    private const string textSink = writer + ".WriteText";
    private const string attributeSink = writer + ".WriteAttribute";
    private const string urlSink = writer + ".WriteUrl";

    /// <summary>Attributes whose value is a URL, and therefore need the scheme refused rather than
    /// the characters escaped: a <c>javascript:</c> payload survives any amount of attribute
    /// encoding, because it never has to leave the attribute.</summary>
    private static readonly HashSet<string> urlAttributes = new(StringComparer.OrdinalIgnoreCase) {
        "href", "src", "action", "formaction", "cite", "poster", "data", "manifest", "ping",
        "background", "longdesc", "profile", "usemap", "codebase", "srcset", "icon"
    };

    /// <summary>The sink holes are written through: element content, unless an attribute is open.</summary>
    private string sink = textSink;

    public CshtmlNodeWriter ()
    {
        WriteHtmlContentMethod = writer + ".WriteLiteral";
        WriteCSharpExpressionMethod = textSink;
    }

    public override void WriteHtmlAttribute (CodeRenderingContext context, HtmlAttributeIntermediateNode node)
    {
        sink = urlAttributes.Contains(node.AttributeName ?? "") ? urlSink : attributeSink;
        Literal(context, node.Prefix);
        context.RenderChildren(node);
        Literal(context, node.Suffix);
        sink = textSink;
    }

    public override void WriteHtmlAttributeValue (CodeRenderingContext context, HtmlAttributeValueIntermediateNode node)
    {
        Literal(context, node.Prefix + string.Concat(node.Children.OfType<IntermediateToken>()
            .Where(token => token.IsHtml).Select(token => token.Content)));
    }

    public override void WriteCSharpExpressionAttributeValue (CodeRenderingContext context,
        CSharpExpressionAttributeValueIntermediateNode node)
    {
        Literal(context, node.Prefix);
        var mapped = Pragma(context, node);
        context.CodeWriter.Write(sink).Write("(");
        foreach (var token in node.Children.OfType<IntermediateToken>().Where(token => token.IsCSharp))
            context.CodeWriter.Write(token.Content);
        context.CodeWriter.WriteLine(");");
        if (mapped) context.CodeWriter.WriteLine("#line default").WriteLine("#line hidden");
    }

    /// <summary>Maps an interpolated attribute value back to the line it came from. The inherited
    /// members get this from the compiler's own pragma helpers, which are internal, so a hand-written
    /// override has to emit it or the mapping for attribute holes goes coarse — and an attribute hole
    /// is exactly where a type error tends to land.</summary>
    private static bool Pragma (CodeRenderingContext context, IntermediateNode node)
    {
        if (node.Source is not { FilePath: not null } source) return false;
        context.CodeWriter.WriteLine()
            .Write("#line ").Write((source.LineIndex + 1).ToString())
            .Write(" \"").Write(source.FilePath.Replace("\\", "\\\\")).WriteLine("\"");
        return true;
    }

    private static void Literal (CodeRenderingContext context, string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        context.CodeWriter.Write(writer).Write(".WriteLiteral(").Write(Quote(text!)).WriteLine(");");
    }

    private static string Quote (string text)
    {
        var quoted = new StringBuilder("\"");
        foreach (var character in text)
            quoted.Append(character switch {
                '\\' => "\\\\", '"' => "\\\"", '\r' => "\\r", '\n' => "\\n", '\t' => "\\t",
                _ => character < ' ' ? $"\\u{(int)character:x4}" : character.ToString()
            });
        return quoted.Append('"').ToString();
    }
}
