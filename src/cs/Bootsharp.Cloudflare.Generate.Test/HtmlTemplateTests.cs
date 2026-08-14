using Microsoft.CodeAnalysis;
using Xunit;

namespace Bootsharp.Cloudflare.Generate.Tests;

/// <summary>
/// The default rendering tier: a template is plain C#, its holes are encoded for the
/// markup around them, and the generator only makes that free.
/// </summary>
public class HtmlTemplateTests
{
    /// <summary>Wraps a one-line template and a model in an app the harness can render.</summary>
    private static string App (string template, string model = "\"<b>&\"") => $$$""""
        using Bootsharp.Cloudflare.AspNetCore.Html;

        public static class Page
        {
            [HtmlTemplate]
            public static void Render (HtmlWriter html, string value) => html.Write($"""{{{template}}}""");
        }

        public static class Runner
        {
            public static string Render ()
            {
                var writer = new StringHtmlWriter();
                Page.Render(writer, {{{model}}});
                return writer.ToString();
            }
        }
        """";

    [Fact]
    public void EncodesElementContent ()
    {
        Assert.Equal("<p>&lt;b&gt;&amp;</p>", HtmlTemplateHarness.Both(App("<p>{value}</p>")));
    }

    [Fact]
    public void EncodesQuotedAttributeValue ()
    {
        Assert.Equal("<p class=\"&lt;b&gt;&amp;\">x</p>",
            HtmlTemplateHarness.Both(App("<p class=\"{value}\">x</p>")));
    }

    [Fact]
    public void EncodesQuoteCharacters ()
    {
        Assert.Equal("<p class=\"&quot;&#39;\">x</p>",
            HtmlTemplateHarness.Both(App("<p class=\"{value}\">x</p>", "\"\\\"'\"")));
    }

    /// <summary>
    /// The one context where encoding is not the whole answer: a <c>javascript:</c> URL survives it.
    /// </summary>
    [Fact]
    public void RefusesUnsafeUrlScheme ()
    {
        Assert.Equal("<a href=\"about:invalid\">x</a>",
            HtmlTemplateHarness.Both(App("<a href=\"{value}\">x</a>", "\"javascript:alert(1)\"")));
    }

    [Fact]
    public void KeepsOrdinaryUrls ()
    {
        Assert.Equal("<a href=\"/api/echo?text=a&amp;times=3\">x</a>",
            HtmlTemplateHarness.Both(App("<a href=\"{value}\">x</a>", "\"/api/echo?text=a&times=3\"")));
    }

    /// <summary>Leading whitespace is stripped by browsers before the scheme is read.</summary>
    [Fact]
    public void RefusesUrlSchemeBehindWhitespace ()
    {
        Assert.Equal("<a href=\"about:invalid\">x</a>",
            HtmlTemplateHarness.Both(App("<a href=\"{value}\">x</a>", "\" \\njavascript:alert(1)\"")));
    }

    [Fact]
    public void WritesHtmlStringVerbatim ()
    {
        Assert.Equal("<p><b>bold</b></p>",
            HtmlTemplateHarness.Both(App("<p>{HtmlString.Raw(value)}</p>", "\"<b>bold</b>\"")));
    }

    /// <summary>The composition seam: a fragment built by one template is a hole in another.</summary>
    [Fact]
    public void ComposesFragments ()
    {
        var app = """"
            using Bootsharp.Cloudflare.AspNetCore.Html;

            public static class Page
            {
                [HtmlTemplate]
                public static void Render (HtmlWriter html, string value) =>
                    html.Write($"<main>{Notice(value)}</main>");

                public static HtmlString Notice (string? text) => string.IsNullOrEmpty(text)
                    ? HtmlString.Empty
                    : HtmlString.From($"""<p class="flash">{text}</p>""");
            }

            public static class Runner
            {
                public static string Render ()
                {
                    var writer = new StringHtmlWriter();
                    Page.Render(writer, "a & b");
                    return writer.ToString();
                }
            }
            """";
        Assert.Equal("<main><p class=\"flash\">a &amp; b</p></main>", HtmlTemplateHarness.Both(app));
    }

    [Fact]
    public void FormatsInvariantly ()
    {
        var app = """
            using Bootsharp.Cloudflare.AspNetCore.Html;

            public static class Page
            {
                [HtmlTemplate]
                public static void Render (HtmlWriter html, decimal value) => html.Write($"<p>{value:0.00}</p>");
            }

            public static class Runner
            {
                public static string Render ()
                {
                    var writer = new StringHtmlWriter();
                    Page.Render(writer, 1.5m);
                    return writer.ToString();
                }
            }
            """;
        Assert.Equal("<p>1.50</p>", HtmlTemplateHarness.Both(app));
    }

    /// <summary>A <c>&lt;style&gt;</c> block full of braces is what the sample's page mostly is.</summary>
    [Fact]
    public void KeepsRawTextElementsIntact ()
    {
        var app = """"
            using Bootsharp.Cloudflare.AspNetCore.Html;

            public static class Page
            {
                [HtmlTemplate]
                public static void Render (HtmlWriter html, string value) => html.Write($$"""
                    <style>body { color: red; }</style>
                    <p>{{value}}</p>
                    """);
            }

            public static class Runner
            {
                public static string Render ()
                {
                    var writer = new StringHtmlWriter();
                    Page.Render(writer, "&");
                    return writer.ToString();
                }
            }
            """";
        Assert.Equal("<style>body { color: red; }</style>\n<p>&amp;</p>", HtmlTemplateHarness.Both(app));
    }

    [Fact]
    public void RefusesValueInsideScript ()
    {
        var run = HtmlTemplateHarness.Run(App("<script>var x = {value};</script>"));
        Assert.Equal(["CFW031"], run.DefectIds);
        Assert.Equal([DiagnosticSeverity.Error], run.Severities);
        Assert.Contains("whose content is not markup", run.DefectReport);
    }

    [Fact]
    public void RefusesValueInUnquotedAttribute ()
    {
        var run = HtmlTemplateHarness.Run(App("<p class={value}>x</p>"));
        Assert.Equal(["CFW031"], run.DefectIds);
        Assert.Contains("unquoted attribute value", run.DefectReport);
    }

    [Fact]
    public void RefusesValueInEventHandlerAttribute ()
    {
        var run = HtmlTemplateHarness.Run(App("<button onclick=\"{value}\">x</button>"));
        Assert.Equal(["CFW031"], run.DefectIds);
        Assert.Contains("whose value is JavaScript", run.DefectReport);
    }

    [Fact]
    public void RefusesValueInAttributeNamePosition ()
    {
        var run = HtmlTemplateHarness.Run(App("<p {value}=\"x\">y</p>"));
        Assert.Equal(["CFW031"], run.DefectIds);
        Assert.Contains("attribute name position", run.DefectReport);
    }

    /// <summary>The same refusal the generator makes, on a template it never saw.</summary>
    [Fact]
    public void RuntimeHandlerRefusesTheSamePositions ()
    {
        var app = """
            using System;
            using Bootsharp.Cloudflare.AspNetCore.Html;

            public static class Page
            {
                public static void Render (HtmlWriter html, string value) =>
                    html.Write($"<script>var x = {value};</script>");
            }

            public static class Runner
            {
                public static string Render ()
                {
                    var writer = new StringHtmlWriter();
                    try { Page.Render(writer, "1"); return "no throw"; }
                    catch (InvalidOperationException error) { return error.Message; }
                }
            }
            """;
        Assert.Contains("whose content is not markup", HtmlTemplateHarness.Rendered(app));
    }

    [Fact]
    public void DeclinesTemplateWhoseBodyIsNotOneWrite ()
    {
        var app = """
            using Bootsharp.Cloudflare.AspNetCore.Html;

            public static class Page
            {
                [HtmlTemplate]
                public static void Render (HtmlWriter html, string value)
                {
                    html.Write($"<p>{value}</p>");
                    html.Write($"<p>{value}</p>");
                }
            }

            public static class Runner
            {
                public static string Render ()
                {
                    var writer = new StringHtmlWriter();
                    Page.Render(writer, "&");
                    return writer.ToString();
                }
            }
            """;
        var run = HtmlTemplateHarness.Run(app);
        Assert.Equal(["CFW032"], run.DefectIds);
        Assert.Equal([DiagnosticSeverity.Warning], run.Severities);
        Assert.False(run.Intercepted);
        // Declining costs speed, never behaviour.
        Assert.Equal("<p>&amp;</p><p>&amp;</p>", HtmlTemplateHarness.Rendered(app));
    }

    /// <summary>An interceptor lives in its own namespace, so it cannot read the template's privates.</summary>
    [Fact]
    public void DeclinesTemplateReadingAPrivateMember ()
    {
        var app = """
            using Bootsharp.Cloudflare.AspNetCore.Html;

            public static class Page
            {
                private static string Secret => "s";

                [HtmlTemplate]
                public static void Render (HtmlWriter html, string value) => html.Write($"<p>{Secret}</p>");
            }

            public static class Runner
            {
                public static string Render ()
                {
                    var writer = new StringHtmlWriter();
                    Page.Render(writer, "");
                    return writer.ToString();
                }
            }
            """;
        var run = HtmlTemplateHarness.Run(app);
        Assert.Equal(["CFW032"], run.DefectIds);
        Assert.Contains("Page.Secret", run.DefectReport);
        Assert.Equal("<p>s</p>", HtmlTemplateHarness.Rendered(app));
    }

    [Fact]
    public void RefusesNonStaticTemplate ()
    {
        var app = """
            using Bootsharp.Cloudflare.AspNetCore.Html;

            public class Page
            {
                [HtmlTemplate]
                public void Render (HtmlWriter html) => html.Write($"<p>x</p>");
            }
            """;
        var run = HtmlTemplateHarness.Run(app);
        Assert.Equal(["CFW030"], run.DefectIds);
        Assert.Equal([DiagnosticSeverity.Error], run.Severities);
    }

    /// <summary>The literals around a hole are one write, not one per line of a raw string literal.</summary>
    [Fact]
    public void CollapsesAdjacentLiterals ()
    {
        var app = """"
            using Bootsharp.Cloudflare.AspNetCore.Html;

            public static class Page
            {
                [HtmlTemplate]
                public static void Render (HtmlWriter html, string value) => html.Write($"""
                    <p>a</p>
                    <p>{value}</p>
                    <p>b</p>
                    """);
            }

            public static class Runner
            {
                public static string Render ()
                {
                    var writer = new StringHtmlWriter();
                    Page.Render(writer, "&");
                    return writer.ToString();
                }
            }
            """";
        var run = HtmlTemplateHarness.Run(app);
        Assert.True(run.Intercepted, run.DefectReport);
        Assert.Equal([
            "html.WriteLiteral(\"<p>a</p>\\n<p>\");",
            "html.WriteText(value);",
            "html.WriteLiteral(\"</p>\\n<p>b</p>\");"
        ], run.Writes);
    }

    /// <summary>A template nobody calls needs no interceptor, and that is not a defect.</summary>
    [Fact]
    public void EmitsNothingForAnUncalledTemplate ()
    {
        var app = """
            using Bootsharp.Cloudflare.AspNetCore.Html;

            public static class Page
            {
                [HtmlTemplate]
                public static void Render (HtmlWriter html, string value) => html.Write($"<p>{value}</p>");
            }
            """;
        var run = HtmlTemplateHarness.Run(app);
        Assert.Empty(run.Defects);
        Assert.False(run.Intercepted);
    }
}
