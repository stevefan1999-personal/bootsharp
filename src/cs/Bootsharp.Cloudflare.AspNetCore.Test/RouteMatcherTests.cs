using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Bootsharp.Cloudflare.AspNetCore.Tests;

/// <summary>
/// the 6,682-line DFA is replaced by a precedence-sorted linear scan. These pin the
/// two properties that replacement has to preserve — that the order is upstream's order, and that a
/// path claimed only by other methods is 405 rather than 404.
/// </summary>
public class RouteMatcherTests
{
    private static string? Matched (WorkerRouteMatcher matcher, string path, string method = "GET") =>
        matcher.Find(new PathString(path), method).Endpoint?.DisplayName;

    [Fact]
    public void LiteralSegmentBeatsAParameterWhicheverWayRoundTheyAreRegistered ()
    {
        Assert.Equal("/api/health", Matched(Worker.Matcher(
            Worker.Endpoint("/api/{id}"), Worker.Endpoint("/api/health")), "/api/health"));
        Assert.Equal("/api/health", Matched(Worker.Matcher(
            Worker.Endpoint("/api/health"), Worker.Endpoint("/api/{id}")), "/api/health"));
    }

    /// <summary>A constrained parameter is more specific than an unconstrained one.</summary>
    [Fact]
    public void ConstrainedParameterBeatsAnUnconstrainedOne ()
    {
        var matcher = Worker.Matcher(Worker.Endpoint("/api/{id}"), Worker.Endpoint("/api/{id:int}"));
        Assert.Equal("/api/{id:int}", Matched(matcher, "/api/7"));
        // The constraint is a filter, not only a tiebreaker: a non-integer falls through to the
        // unconstrained candidate rather than failing.
        Assert.Equal("/api/{id}", Matched(matcher, "/api/seven"));
    }

    [Fact]
    public void CatchAllIsTheLastResortEvenWhenRegisteredFirst ()
    {
        var matcher = Worker.Matcher(Worker.Endpoint("/files/{*path}"), Worker.Endpoint("/files/readme"));
        Assert.Equal("/files/readme", Matched(matcher, "/files/readme"));
        Assert.Equal("/files/{*path}", Matched(matcher, "/files/a/b/c.txt"));
    }

    [Fact]
    public void CatchAllCapturesEverySegmentAfterItsPrefix ()
    {
        var match = Worker.Matcher(Worker.Endpoint("/files/{*path}")).Find(new PathString("/files/a/b/c.txt"), "GET");
        Assert.Equal("a/b/c.txt", match.RouteValues["path"]);
    }

    [Fact]
    public void PathNoEndpointClaimsIsAMissWithNoAllowedMethods ()
    {
        var match = Worker.Matcher(Worker.Endpoint("/a", ["GET"])).Find(new PathString("/b"), "GET");
        Assert.Null(match.Endpoint);
        Assert.Null(match.AllowedMethods);
        Assert.Empty(match.RouteValues);
    }

    /// <summary>The 404-versus-405 distinction: a claimed path answers with what it does accept.</summary>
    [Fact]
    public void PathClaimedOnlyByOtherMethodsReportsThoseMethods ()
    {
        var match = Worker.Matcher(Worker.Endpoint("/a", ["GET"]), Worker.Endpoint("/a", ["PUT"]))
            .Find(new PathString("/a"), "POST");
        Assert.Null(match.Endpoint);
        Assert.Equal(["GET", "PUT"], match.AllowedMethods!);
    }

    [Fact]
    public void RepeatedMethodsAreReportedOnce ()
    {
        var match = Worker.Matcher(Worker.Endpoint("/a", ["GET"]), Worker.Endpoint("/a/{x?}", ["GET"]))
            .Find(new PathString("/a"), "DELETE");
        Assert.Equal(["GET"], match.AllowedMethods!);
    }

    /// <summary>HEAD is answered by the GET endpoint, as it is in ASP.NET Core.</summary>
    [Fact]
    public void HeadIsServedByTheGetEndpoint ()
    {
        var matcher = Worker.Matcher(Worker.Endpoint("/a", ["GET"]));
        Assert.Equal("/a", Matched(matcher, "/a", "HEAD"));
        // Only that direction: a GET request is not served by a HEAD endpoint.
        Assert.Null(Matched(Worker.Matcher(Worker.Endpoint("/a", ["HEAD"])), "/a"));
    }

    [Fact]
    public void EndpointWithNoMethodMetadataAcceptsEveryMethod ()
    {
        var matcher = Worker.Matcher(Worker.Endpoint("/any"));
        foreach (var method in new[] { "GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS" })
            Assert.Equal("/any", Matched(matcher, "/any", method));
    }

    [Fact]
    public void MethodComparisonIsCaseInsensitive () =>
        Assert.Equal("/a", Matched(Worker.Matcher(Worker.Endpoint("/a", ["GET"])), "/a", "get"));

    /// <summary>A default the pattern declares reaches the route values when the path omits it.</summary>
    [Fact]
    public void PatternDefaultIsSuppliedWhenThePathOmitsTheSegment ()
    {
        var matcher = Worker.Matcher(Worker.Endpoint("/page/{n=7}"));
        Assert.Equal("7", matcher.Find(new PathString("/page"), "GET").RouteValues["n"]);
        Assert.Equal("3", matcher.Find(new PathString("/page/3"), "GET").RouteValues["n"]);
    }

    [Fact]
    public void OptionalParameterMatchesWithAndWithoutItsSegment ()
    {
        var matcher = Worker.Matcher(Worker.Endpoint("/opt/{tag?}"));
        Assert.Equal("/opt/{tag?}", Matched(matcher, "/opt"));
        Assert.Equal("x", matcher.Find(new PathString("/opt/x"), "GET").RouteValues["tag"]);
    }

    /// <summary>
    /// An absent optional parameter has nothing to constrain — upstream's
    /// <c>OptionalRouteConstraint</c> wrapper, expressed here as a check rather than a second
    /// constraint object.
    /// </summary>
    [Fact]
    public void OptionalConstrainedParameterMatchesWhenAbsentAndIsEnforcedWhenPresent ()
    {
        var matcher = Worker.Matcher(Worker.Endpoint("/opt/{id:int?}"));
        Assert.Equal("/opt/{id:int?}", Matched(matcher, "/opt"));
        Assert.Equal("/opt/{id:int?}", Matched(matcher, "/opt/12"));
        Assert.Null(Matched(matcher, "/opt/twelve"));
    }

    [Fact]
    public void ConstraintFailureIsAMissRatherThanAMatch ()
    {
        var match = Worker.Matcher(Worker.Endpoint("/api/{id:int}", ["GET"])).Find(new PathString("/api/abc"), "GET");
        Assert.Null(match.Endpoint);
        // Not 405 either: the path never matched, so no method is offered.
        Assert.Null(match.AllowedMethods);
    }

    [Fact]
    public void EveryConstraintTheResolverSupportsIsEnforcedByTheMatcher ()
    {
        Assert.Equal("/x/{v:long}", Matched(Worker.Matcher(Worker.Endpoint("/x/{v:long}")), "/x/9000000000"));
        Assert.Equal("/x/{v:bool}", Matched(Worker.Matcher(Worker.Endpoint("/x/{v:bool}")), "/x/true"));
        Assert.Equal("/x/{v:alpha}", Matched(Worker.Matcher(Worker.Endpoint("/x/{v:alpha}")), "/x/abc"));
        Assert.Null(Matched(Worker.Matcher(Worker.Endpoint("/x/{v:alpha}")), "/x/a1"));
        Assert.Equal("/x/{v:range(1,10)}", Matched(Worker.Matcher(Worker.Endpoint("/x/{v:range(1,10)}")), "/x/5"));
        Assert.Null(Matched(Worker.Matcher(Worker.Endpoint("/x/{v:range(1,10)}")), "/x/50"));
        Assert.Equal("/x/{v:minlength(3)}", Matched(Worker.Matcher(Worker.Endpoint("/x/{v:minlength(3)}")), "/x/abc"));
        Assert.Null(Matched(Worker.Matcher(Worker.Endpoint("/x/{v:minlength(3)}")), "/x/ab"));
    }

    /// <summary>
    /// The generator's precomputed value is what orders the table. Stating one that
    /// disagrees with the pattern is the only way to prove the metadata is read rather than the
    /// pattern re-measured.
    /// </summary>
    [Fact]
    public void PrecedenceMetadataIsWhatSortsTheTable ()
    {
        var matcher = Worker.Matcher(
            Worker.Endpoint("/a/b", precedence: 9m),
            Worker.Endpoint("/a/{x}", precedence: 1m));
        Assert.Equal("/a/{x}", Matched(matcher, "/a/b"));
    }

    /// <summary>A table mixing generated and hand-registered endpoints is one order.</summary>
    [Fact]
    public void EndpointWithoutMetadataIsOrderedByTheSameComputation ()
    {
        var matcher = Worker.Matcher(
            Worker.Endpoint("/a/{x}"),
            Worker.Endpoint("/a/b", precedence: 1.1m));
        Assert.Equal("/a/b", Matched(matcher, "/a/b"));
    }

    /// <summary>Order is the user's explicit override and outranks precedence, as upstream's does.</summary>
    [Fact]
    public void ExplicitOrderOutranksPrecedence ()
    {
        var matcher = Worker.Matcher(
            Worker.Endpoint("/a/{x}", order: -1),
            Worker.Endpoint("/a/b"));
        Assert.Equal("/a/{x}", Matched(matcher, "/a/b"));
    }

    [Fact]
    public void EndpointsThatAreNotRouteEndpointsAreIgnored ()
    {
        var matcher = new WorkerRouteMatcher(
            [new Endpoint(static _ => Task.CompletedTask, EndpointMetadataCollection.Empty, "plain")]);
        Assert.Null(Matched(matcher, "/anything"));
    }

    [Fact]
    public void EmptyTableMatchesNothingRatherThanThrowing () =>
        Assert.Null(Matched(Worker.Matcher(), "/a"));

    [Fact]
    public void NullEndpointListIsRejected () =>
        Assert.Throws<ArgumentNullException>(static () => new WorkerRouteMatcher(null!));

    /// <summary>
    /// An unknown constraint fails when the table is built, not on the request that first reaches
    /// the route — the "fail where the mistake is" trade the compile-time parsing makes.
    /// </summary>
    [Fact]
    public void UnknownConstraintFailsWhenTheTableIsBuilt ()
    {
        var endpoint = Worker.Endpoint("/x/{v:nope}");
        var error = Assert.ThrowsAny<Exception>(() => Worker.Matcher(endpoint));
        Assert.Contains("not a known route constraint", error.Message);
    }

    [Fact]
    public void RegexConstraintFailsNamingTheDependencyItWouldRoot ()
    {
        var endpoint = Worker.Endpoint(@"/x/{v:regex(^\d+$)}");
        var error = Assert.ThrowsAny<Exception>(() => Worker.Matcher(endpoint));
        Assert.Contains("System.Text.RegularExpressions", error.Message);
    }
}
