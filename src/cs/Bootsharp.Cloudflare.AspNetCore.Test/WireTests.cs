using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Bootsharp.Cloudflare.AspNetCore.Tests;

/// <summary>
/// The flat JSON header object that crosses the interop boundary. The old shim built it by string
/// concatenation and escaped nothing, so a value containing a quote produced JSON
/// the JavaScript side could not parse — which is why the round trip, not the spelling, is what
/// these assert.
/// </summary>
public class HeaderJsonTests
{
    [Fact]
    public void AbsentHeadersAreAnEmptyDictionaryRatherThanNull ()
    {
        Assert.Empty(HeaderJson.Parse(null));
        Assert.Empty(HeaderJson.Parse(""));
        Assert.Empty(HeaderJson.Parse("{}"));
    }

    [Fact]
    public void EachHeaderArrivesUnderItsOwnName ()
    {
        var headers = HeaderJson.Parse("""{"content-type":"application/json","x-trace":"abc"}""");
        Assert.Equal("application/json", headers["content-type"]);
        Assert.Equal("abc", headers["X-TRACE"]);
        Assert.Equal(2, headers.Count);
    }

    /// <summary>A document that is not an object is not a header set; it is not an error either.</summary>
    [Fact]
    public void NonObjectDocumentsYieldNoHeaders ()
    {
        Assert.Empty(HeaderJson.Parse("[]"));
        Assert.Empty(HeaderJson.Parse("\"x\""));
        Assert.Empty(HeaderJson.Parse("7"));
    }

    /// <summary>Only string values are headers; anything else is a bug on the JavaScript side.</summary>
    [Fact]
    public void NonStringValuesAreSkipped ()
    {
        var headers = HeaderJson.Parse("""{"a":"1","b":2,"c":null,"d":["x"]}""");
        Assert.Equal("1", headers["a"]);
        Assert.Single(headers);
    }

    /// <summary>
    /// Malformed JSON propagates: it is a bug on the JavaScript side, not user input, and swallowing
    /// it into an empty header set would hide it behind a request that merely behaves oddly.
    /// </summary>
    [Fact]
    public void MalformedDocumentsPropagate () =>
        Assert.ThrowsAny<JsonException>(static () => { HeaderJson.Parse("{not json"); });

    [Fact]
    public void EmptyDictionaryRendersAsAnEmptyObject () =>
        Assert.Equal("{}", HeaderJson.Render(new HeaderDictionary()));

    /// <summary>The bug the string-concatenating shim had: a value with a quote in it.</summary>
    [Fact]
    public void ValuesNeedingEscapesSurviveTheRoundTrip ()
    {
        var headers = new HeaderDictionary {
            ["x-quote"] = "he said \"hi\"",
            ["x-slash"] = @"a\b",
            ["x-newline"] = "a\nb",
            ["x-unicode"] = "naïve ☃"
        };
        var parsed = HeaderJson.Parse(HeaderJson.Render(headers));
        Assert.Equal("he said \"hi\"", parsed["x-quote"]);
        Assert.Equal(@"a\b", parsed["x-slash"]);
        Assert.Equal("a\nb", parsed["x-newline"]);
        Assert.Equal("naïve ☃", parsed["x-unicode"]);
    }

    /// <summary>Multi-valued headers are comma-joined, which is correct for all but Set-Cookie.</summary>
    [Fact]
    public void RepeatedValuesAreCommaJoined ()
    {
        var headers = new HeaderDictionary { ["accept"] = new StringValues(["text/html", "application/json"]) };
        Assert.Equal("""{"accept":"text/html,application/json"}""", HeaderJson.Render(headers));
    }

    /// <summary>A header with no values is not a header with an empty value.</summary>
    [Fact]
    public void ValuelessHeadersAreOmitted ()
    {
        var headers = new HeaderDictionary { ["x-empty"] = StringValues.Empty, ["x-real"] = "1" };
        Assert.Equal("""{"x-real":"1"}""", HeaderJson.Render(headers));
    }
}

/// <summary>
/// Query parsing, done here because <c>QueryHelpers</c> lives in
/// <c>Microsoft.AspNetCore.WebUtilities</c> — a package this layer does not carry.
/// </summary>
public class QueryStringParserTests
{
    [Fact]
    public void AbsentQueryIsTheEmptyCollection ()
    {
        Assert.Same(QueryCollection.Empty, QueryStringParser.Parse(null));
        Assert.Same(QueryCollection.Empty, QueryStringParser.Parse(""));
        Assert.Same(QueryCollection.Empty, QueryStringParser.Parse("?"));
    }

    [Fact]
    public void PairsAreParsedWithOrWithoutTheLeadingQuestionMark ()
    {
        foreach (var query in new[] { "?a=1&b=2", "a=1&b=2" })
        {
            var parsed = QueryStringParser.Parse(query);
            Assert.Equal("1", parsed["a"]);
            Assert.Equal("2", parsed["b"]);
        }
    }

    /// <summary>Repeated keys accumulate, which is what an array-typed handler parameter binds from.</summary>
    [Fact]
    public void RepeatedKeysAccumulateIntoOneValueSet ()
    {
        var parsed = QueryStringParser.Parse("?id=1&id=2&id=3");
        Assert.Equal(3, parsed["id"].Count);
        Assert.Equal("1,2,3", parsed["id"].ToString());
    }

    [Fact]
    public void KeysAreMatchedCaseInsensitively ()
    {
        var parsed = QueryStringParser.Parse("?Page=2");
        Assert.Equal("2", parsed["page"]);
        Assert.True(parsed.ContainsKey("PAGE"));
    }

    /// <summary>
    /// A query string is <c>application/x-www-form-urlencoded</c>, where <c>+</c> means space
    /// something <see cref="Uri.UnescapeDataString"/> does not know, so the two steps are separate.
    /// </summary>
    [Fact]
    public void ValuesArePercentAndPlusDecoded ()
    {
        Assert.Equal("hello world", QueryStringParser.Parse("?q=hello+world")["q"]);
        Assert.Equal("hello world", QueryStringParser.Parse("?q=hello%20world")["q"]);
        Assert.Equal("a&b=c", QueryStringParser.Parse("?q=a%26b%3Dc")["q"]);
        Assert.Equal("naïve", QueryStringParser.Parse("?q=na%C3%AFve")["q"]);
    }

    [Fact]
    public void KeysAreDecodedToo () =>
        Assert.Equal("1", QueryStringParser.Parse("?a%20b=1")["a b"]);

    [Fact]
    public void KeyWithoutAValueIsPresentAndEmpty ()
    {
        var parsed = QueryStringParser.Parse("?flag&q=1");
        Assert.True(parsed.ContainsKey("flag"));
        Assert.Equal("", parsed["flag"]);
        Assert.Equal("1", parsed["q"]);
    }

    [Fact]
    public void EmptySegmentsAreSkipped ()
    {
        var parsed = QueryStringParser.Parse("?&a=1&&b=2&");
        Assert.Equal(2, parsed.Count);
        Assert.Equal("1", parsed["a"]);
    }

    [Fact]
    public void ValueContainingAnEqualsSignKeepsIt () =>
        Assert.Equal("a=b", QueryStringParser.Parse("?q=a=b")["q"]);

    [Fact]
    public void AbsentKeyReadsAsEmptyRatherThanThrowing () =>
        Assert.Equal(StringValues.Empty, QueryStringParser.Parse("?a=1")["missing"]);
}
