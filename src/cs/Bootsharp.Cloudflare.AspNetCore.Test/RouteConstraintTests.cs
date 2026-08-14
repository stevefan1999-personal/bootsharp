using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Bootsharp.Cloudflare.AspNetCore.Tests;

/// <summary>
/// the closed <c>switch</c> that stands in for
/// <c>DefaultInlineConstraintResolver</c> → <c>ParameterPolicyActivator</c>, whose only defence
/// against trimming is a <c>[DynamicallyAccessedMembers]</c> annotation on a constraint map.
/// </summary>
public class RouteConstraintResolverTests
{
    private static bool Matches (string token, string value) =>
        RouteConstraints.Resolve(token).Match("v", new RouteValueDictionary { ["v"] = value });

    [Theory]
    [InlineData("int", typeof(IntRouteConstraint))]
    [InlineData("long", typeof(LongRouteConstraint))]
    [InlineData("bool", typeof(BoolRouteConstraint))]
    [InlineData("guid", typeof(GuidRouteConstraint))]
    [InlineData("decimal", typeof(DecimalRouteConstraint))]
    [InlineData("double", typeof(DoubleRouteConstraint))]
    [InlineData("float", typeof(FloatRouteConstraint))]
    [InlineData("datetime", typeof(DateTimeRouteConstraint))]
    [InlineData("alpha", typeof(AlphaRouteConstraint))]
    [InlineData("file", typeof(FileNameRouteConstraint))]
    [InlineData("nonfile", typeof(NonFileNameRouteConstraint))]
    [InlineData("length(3)", typeof(LengthRouteConstraint))]
    [InlineData("length(1,3)", typeof(LengthRouteConstraint))]
    [InlineData("minlength(3)", typeof(MinLengthRouteConstraint))]
    [InlineData("maxlength(3)", typeof(MaxLengthRouteConstraint))]
    [InlineData("min(3)", typeof(MinRouteConstraint))]
    [InlineData("max(3)", typeof(MaxRouteConstraint))]
    [InlineData("range(1,3)", typeof(RangeRouteConstraint))]
    public void EverySupportedTokenResolvesToItsConstraint (string token, Type expected) =>
        Assert.IsType(expected, RouteConstraints.Resolve(token));

    [Fact]
    public void TypeConstraintsAcceptTheirTypeAndRejectTheRest ()
    {
        Assert.True(Matches("int", "42"));
        Assert.False(Matches("int", "4.2"));
        Assert.True(Matches("long", "9000000000"));
        Assert.True(Matches("bool", "TRUE"));
        Assert.False(Matches("bool", "yes"));
        Assert.True(Matches("guid", Guid.NewGuid().ToString()));
        Assert.False(Matches("guid", "not-a-guid"));
        Assert.True(Matches("decimal", "4.2"));
        Assert.True(Matches("double", "4.2"));
        Assert.True(Matches("float", "4.2"));
        Assert.True(Matches("datetime", "2026-08-14"));
        Assert.False(Matches("datetime", "yesterday"));
    }

    [Fact]
    public void FileConstraintsSplitOnTheExtension ()
    {
        Assert.True(Matches("file", "readme.txt"));
        Assert.False(Matches("file", "readme"));
        Assert.True(Matches("nonfile", "readme"));
        Assert.False(Matches("nonfile", "readme.txt"));
    }

    [Fact]
    public void LengthAndRangeConstraintsUseTheirArguments ()
    {
        Assert.True(Matches("length(3)", "abc"));
        Assert.False(Matches("length(3)", "ab"));
        Assert.True(Matches("length(1,3)", "ab"));
        Assert.False(Matches("length(1,3)", "abcd"));
        Assert.True(Matches("minlength(3)", "abcd"));
        Assert.False(Matches("minlength(3)", "ab"));
        Assert.True(Matches("maxlength(3)", "ab"));
        Assert.False(Matches("maxlength(3)", "abcd"));
        Assert.True(Matches("min(3)", "4"));
        Assert.False(Matches("min(3)", "2"));
        Assert.True(Matches("max(3)", "2"));
        Assert.False(Matches("max(3)", "4"));
        Assert.True(Matches("range(1,3)", "2"));
        Assert.False(Matches("range(1,3)", "4"));
    }

    /// <summary>
    /// The one constraint the set is closed against by name: taking upstream's would root
    /// <c>System.Text.RegularExpressions</c>, and the message says exactly that.
    /// </summary>
    [Fact]
    public void RegexIsRefusedByName ()
    {
        var error = Assert.Throws<RoutePatternException>(static () => { RouteConstraints.Resolve(@"regex(^\d+$)"); });
        Assert.Contains("System.Text.RegularExpressions", error.Message);
    }

    [Fact]
    public void UnknownTokensListWhatIsSupported ()
    {
        var error = Assert.Throws<RoutePatternException>(static () => { RouteConstraints.Resolve("weekday"); });
        Assert.Contains("'weekday' is not a known route constraint", error.Message);
        Assert.Contains("int, long, bool, guid", error.Message);
    }

    [Fact]
    public void MalformedArgumentListsAreReportedRatherThanGuessed ()
    {
        Assert.Contains("closing parenthesis",
            Assert.Throws<RoutePatternException>(static () => { RouteConstraints.Resolve("range(1,3"); }).Message);
        Assert.Contains("needs 2 argument",
            Assert.Throws<RoutePatternException>(static () => { RouteConstraints.Resolve("range(1)"); }).Message);
        Assert.Contains("whole numbers",
            Assert.Throws<RoutePatternException>(static () => { RouteConstraints.Resolve("min(x)"); }).Message);
        Assert.Contains("needs 1 argument",
            Assert.Throws<RoutePatternException>(static () => { RouteConstraints.Resolve("minlength()"); }).Message);
    }

    [Fact]
    public void NullTokenIsRejected () =>
        Assert.Throws<ArgumentNullException>(static () => { RouteConstraints.Resolve(null!); });
}

/// <summary>
/// Reimplemented rather than vendored, because upstream's derives from
/// <c>RegexRouteConstraint</c> with the pattern <c>^[a-z]*$</c> — the one constraint that least
/// needs a regex engine. These pin that the substitute means the same thing.
/// </summary>
public class AlphaRouteConstraintTests
{
    private static bool Matches (string? value) =>
        new AlphaRouteConstraint().Match("v", new RouteValueDictionary { ["v"] = value });

    [Fact]
    public void AsciiLettersMatchInEitherCase ()
    {
        Assert.True(Matches("abc"));
        Assert.True(Matches("ABC"));
        Assert.True(Matches("aBc"));
    }

    /// <summary>Upstream's <c>*</c> quantifier matches the empty string; so does this.</summary>
    [Fact]
    public void EmptyStringMatches () => Assert.True(Matches(""));

    [Fact]
    public void AnythingThatIsNotAnAsciiLetterDoesNot ()
    {
        Assert.False(Matches("a1"));
        Assert.False(Matches("a-b"));
        Assert.False(Matches("naïve"));
        Assert.False(Matches(" "));
    }

    [Fact]
    public void AbsentOrNullValuesDoNotMatch ()
    {
        Assert.False(Matches(null));
        Assert.False(new AlphaRouteConstraint().Match("v", []));
    }

    [Fact]
    public void NullArgumentsAreRejected ()
    {
        Assert.Throws<ArgumentNullException>(static () => new AlphaRouteConstraint().Match(null!, []));
        Assert.Throws<ArgumentNullException>(static () => new AlphaRouteConstraint().Match("v", null!));
    }
}
