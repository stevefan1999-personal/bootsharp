using Xunit;

namespace Bootsharp.Cloudflare.Generate.Tests;

/// <summary>
/// The pre-ordered route table: the generator computes
/// <c>RoutePrecedence.ComputeInbound</c> at build time and emits it, so the matcher sorts by a
/// number rather than re-deriving one from a pattern it only sees at startup.
/// </summary>
public class MinimalApiPrecedenceTests
{
    private static string Table (params string[] patterns) =>
        MinimalApiHarness.Run(BindingSources.App(
            [.. patterns.Select(static pattern => $"app.MapGet(\"{pattern}\", () => \"{pattern}\");")])).Generated;

    /// <summary>
    /// Upstream's digits: a literal segment scores 1, a constrained parameter 2, an unconstrained
    /// one 3, a catch-all 5 — read one decimal place per segment.
    /// </summary>
    [Theory]
    [InlineData("/api/health", "1.1m")]
    [InlineData("/api/{id:int}", "1.2m")]
    [InlineData("/api/{id}", "1.3m")]
    [InlineData("/files/{*path}", "1.5m")]
    [InlineData("/a/b/c", "1.11m")]
    [InlineData("/a/{b}/c", "1.31m")]
    public void PrecedenceOfEachShapeIsTheValueUpstreamAssigns (string pattern, string expected) =>
        Assert.Contains($"\"{pattern}\" => {expected},", Table(pattern));

    /// <summary>Every distinctly-shaped call site contributes its pattern to the switch.</summary>
    [Fact]
    public void EachEmittedShapeContributesItsPattern ()
    {
        var generated = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a", () => "a");""",
            """app.MapGet("/b/{id:int}", (int id) => $"{id}");""",
            """app.MapPost("/c", (Todo todo) => todo.Title);""")).Generated;
        Assert.Contains("private static decimal? Precedence(string pattern) => pattern switch", generated);
        Assert.Contains("\"/a\" => 1m,", generated);
        Assert.Contains("\"/b/{id:int}\" => 1.2m,", generated);
        Assert.Contains("\"/c\" => 1m,", generated);
    }

    /// <summary>
    /// Every mapped pattern reaches the table, including the ones whose call sites share an
    /// interceptor with another.
    /// </summary>
    /// <remarks>
    /// The regression this pins: the table was built from one model per interceptor group, and
    /// grouping deliberately excludes the pattern — so three same-shaped <c>MapGet</c>s emitted one
    /// arm and the other two fell through to <c>_ => null</c>. Ordering was unaffected (the fallback
    /// is the same computation), but pre-ordered table held for one pattern per shape
    /// rather than for the app. The <c>_ => null</c> arm still exists, for an endpoint registered
    /// without the generator.
    /// </remarks>
    [Fact]
    public void EveryPatternReachesTheTableEvenWhenCallSitesShareAnInterceptor ()
    {
        var generated = Table("/a", "/b", "/c");
        Assert.Equal(3, generated.Split('\n').Count(static line => line.TrimStart().StartsWith("\"/")));
        Assert.Contains("\"/a\" => 1m,", generated);
        Assert.Contains("\"/b\" => 1m,", generated);
        Assert.Contains("\"/c\" => 1m,", generated);
        Assert.Contains("_ => null", generated);
    }

    /// <summary>An optional segment is not counted, so it scores as the segment before it.</summary>
    [Fact]
    public void OptionalSegmentDoesNotAddADigit () =>
        Assert.Contains("\"/a/{id?}\" => 1.3m,", Table("/a/{id?}"));

    /// <summary>The value the generator computed is what reaches the endpoint.</summary>
    [Fact]
    public void ComputedValueIsHandedToTheRegistrationSeam () =>
        Assert.Contains("Precedence(pattern));", Table("/a"));

    /// <summary>
    /// What the ordering buys, run: a literal beats a parameter, a constrained parameter beats an
    /// unconstrained one, and a catch-all is the last resort — whatever order they were mapped in.
    /// </summary>
    [Fact]
    public void MoreSpecificRoutesWinWhateverOrderTheyWereMappedIn ()
    {
        var source = BindingSources.App(
            """app.MapGet("/api/{*rest}", (string rest) => "catch-all");""",
            """app.MapGet("/api/{id}", (string id) => "parameter");""",
            """app.MapGet("/api/{id:int}", (int id) => "constrained");""",
            """app.MapGet("/api/health", () => "literal");""");
        Assert.Equal("200|literal", MinimalApiHarness.Send(source, "GET", "https://w.dev/api/health"));
        Assert.Equal("200|constrained", MinimalApiHarness.Send(source, "GET", "https://w.dev/api/7"));
        Assert.Equal("200|parameter", MinimalApiHarness.Send(source, "GET", "https://w.dev/api/seven"));
        Assert.Equal("200|catch-all", MinimalApiHarness.Send(source, "GET", "https://w.dev/api/a/b/c"));
    }

    /// <summary>
    /// A route whose constraint the path fails is a miss, not a bad request: the endpoint never
    /// matched, so no parameter of its was ever bound.
    /// </summary>
    [Fact]
    public void ConstraintFailureFallsThroughRatherThanFailingTheRequest ()
    {
        var source = BindingSources.App("""app.MapGet("/api/{id:int}", (int id) => $"{id}");""");
        Assert.Equal("200|7", MinimalApiHarness.Send(source, "GET", "https://w.dev/api/7"));
        Assert.Equal("404|", MinimalApiHarness.Send(source, "GET", "https://w.dev/api/seven"));
    }

    /// <summary>Every constraint the runtime resolver knows is accepted by the compile-time parser.</summary>
    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("bool")]
    [InlineData("guid")]
    [InlineData("decimal")]
    [InlineData("double")]
    [InlineData("float")]
    [InlineData("datetime")]
    [InlineData("alpha")]
    [InlineData("file")]
    [InlineData("nonfile")]
    [InlineData("length(3)")]
    [InlineData("minlength(3)")]
    [InlineData("maxlength(3)")]
    [InlineData("min(3)")]
    [InlineData("max(3)")]
    [InlineData("range(1,3)")]
    public void SupportedConstraintsAreAcceptedWhenTheAppCompiles (string constraint)
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            $$"""app.MapGet("/a/{v:{{constraint}}}", ([FromRoute] string v) => v);"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
    }
}

/// <summary>
/// Interceptor grouping: call sites that would emit the same
/// body share one interceptor carrying N <c>[InterceptsLocation]</c> attributes. What counts as
/// "the same body" is the whole binding shape, not the pattern.
/// </summary>
public class MinimalApiGroupingTests
{
    [Fact]
    public void SameShapeUnderDifferentPatternsSharesOneInterceptor ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/one", (int id) => $"{id}");""",
            """app.MapGet("/two", (int id) => $"{id}");""",
            """app.MapGet("/three", (int id) => $"{id}");"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Single(run.Interceptors);
        Assert.Equal(3, run.InterceptsLocationCount);
    }

    /// <summary>
    /// The verb is part of the shape: the interceptor is named for the method it replaces and
    /// hoists that method's verb array.
    /// </summary>
    [Fact]
    public void DifferentVerbsDoNotShareAnInterceptor ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a", (string q) => q);""",
            """app.MapDelete("/b", (string q) => q);"""));
        Assert.Equal(["MapGet0", "MapDelete1"], run.Interceptors);
        Assert.Equal(2, run.InterceptsLocationCount);
    }

    /// <summary>A different binding source is a different body, even at the same arity and type.</summary>
    [Fact]
    public void SameSignatureBoundFromDifferentSourcesDoesNotShare ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a/{id}", (string id) => id);""",
            """app.MapGet("/b", (string id) => id);"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal(2, run.Interceptors.Length);
    }

    /// <summary>And so is a different default, because the cast has to reproduce it.</summary>
    [Fact]
    public void SameSignatureWithDifferentDefaultsDoesNotShare ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a", (int page = 1) => page);""",
            """app.MapGet("/b", (int page = 2) => page);"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal(2, run.Interceptors.Length);
    }

    /// <summary>
    /// <c>MapMethods</c> passes its verbs through at runtime, so two call sites with the same
    /// binding shape share an interceptor even when their verb lists differ — the list is an
    /// argument, not part of the emitted body.
    /// </summary>
    [Fact]
    public void MapMethodsSharesAcrossDifferentVerbLists ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            """app.MapMethods("/a", new[] { "POST" }, (Todo todo) => todo.Id);""",
            """app.MapMethods("/b", new[] { "PUT", "PATCH" }, (Todo todo) => todo.Id);"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Single(run.Interceptors);
        Assert.Equal(2, run.InterceptsLocationCount);
    }

    /// <summary>
    /// Every grouped call site is still its own endpoint: sharing an interceptor must not mean
    /// sharing a route.
    /// </summary>
    [Fact]
    public void GroupedCallSitesStillRegisterSeparateEndpoints ()
    {
        var source = BindingSources.App(
            """app.MapGet("/one", (string q) => "one " + q);""",
            """app.MapGet("/two", (string q) => "two " + q);""");
        Assert.Single(MinimalApiHarness.Run(source).Interceptors);
        Assert.Equal("200|one x", MinimalApiHarness.Send(source, "GET", "https://w.dev/one?q=x"));
        Assert.Equal("200|two x", MinimalApiHarness.Send(source, "GET", "https://w.dev/two?q=x"));
    }

    /// <summary>A verb array is hoisted once per verb, not once per call site.</summary>
    [Fact]
    public void VerbArraysAreHoistedOncePerVerb ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a", () => "a");""",
            """app.MapGet("/b", (int id) => $"{id}");""",
            """app.MapPost("/c", (Todo todo) => todo.Id);"""));
        Assert.Empty(run.DefectIds);
        var lines = run.Generated.Split('\n');
        Assert.Equal(1, lines.Count(static line => line.Contains("GetVerb = new[]")));
        Assert.Equal(1, lines.Count(static line => line.Contains("PostVerb = new[]")));
    }
}
