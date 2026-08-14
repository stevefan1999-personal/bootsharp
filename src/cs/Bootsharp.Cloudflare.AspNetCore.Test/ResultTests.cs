using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Bootsharp.Cloudflare.AspNetCore.Tests;

/// <summary>
/// the curated <see cref="TypedResults"/> set. Each case pins the three things a
/// result is — the status it writes, the headers it sets and the bytes it produces — because the
/// façade is original code standing in for an assembly this package cannot take.
/// </summary>
public class ResultStatusTests
{
    [Fact]
    public async Task EmptyBodiedResultsWriteTheirStatusAndNothingElse ()
    {
        Assert.Equal(new Answer(200, "{}", ""), await Worker.Execute(TypedResults.Ok()));
        Assert.Equal(new Answer(204, "{}", ""), await Worker.Execute(TypedResults.NoContent()));
        Assert.Equal(new Answer(400, "{}", ""), await Worker.Execute(TypedResults.BadRequest()));
        Assert.Equal(new Answer(404, "{}", ""), await Worker.Execute(TypedResults.NotFound()));
        Assert.Equal(new Answer(409, "{}", ""), await Worker.Execute(TypedResults.Conflict()));
        Assert.Equal(new Answer(422, "{}", ""), await Worker.Execute(TypedResults.UnprocessableEntity()));
        Assert.Equal(new Answer(418, "{}", ""), await Worker.Execute(TypedResults.StatusCode(418)));
    }

    /// <summary>The status each result advertises is the status it writes.</summary>
    [Fact]
    public async Task AdvertisedStatusMatchesTheWrittenOne ()
    {
        foreach (var result in new IStatusCodeHttpResult[] {
                     TypedResults.Ok(), TypedResults.Ok(new Todo(1, "x")), TypedResults.NoContent(),
                     TypedResults.BadRequest(), TypedResults.BadRequest(new Todo(1, "x")),
                     TypedResults.NotFound(), TypedResults.NotFound(new Todo(1, "x")),
                     TypedResults.Conflict(), TypedResults.UnprocessableEntity(),
                     TypedResults.Created("/a"), TypedResults.Created("/a", new Todo(1, "x")),
                     TypedResults.Accepted("/a"), TypedResults.StatusCode(503)
                 })
            Assert.Equal(result.StatusCode, (await Worker.Execute((IResult)result)).Status);
    }

    [Fact]
    public async Task CreatedAndAcceptedCarryTheirLocation ()
    {
        Assert.Equal("/todos/1", (await Worker.Execute(TypedResults.Created("/todos/1"))).Header("location"));
        Assert.Equal("/todos/1", (await Worker.Execute(TypedResults.Created("/todos/1", new Todo(1, "x")))).Header("location"));
        Assert.Equal("/jobs/1", (await Worker.Execute(TypedResults.Accepted("/jobs/1"))).Header("location"));
        // A null location is an omitted header, not an empty one.
        Assert.Null((await Worker.Execute(TypedResults.Created((string?)null))).Header("location"));
    }

    [Fact]
    public void ValueCarryingResultsExposeTheValueTheyWillWrite ()
    {
        var todo = new Todo(1, "x");
        Assert.Same(todo, ((IValueHttpResult<Todo>)TypedResults.Ok(todo)).Value);
        Assert.Same(todo, ((IValueHttpResult<Todo>)TypedResults.NotFound(todo)).Value);
        Assert.Same(todo, ((IValueHttpResult<Todo>)TypedResults.BadRequest(todo)).Value);
        Assert.Same(todo, ((IValueHttpResult<Todo>)TypedResults.Created("/a", todo)).Value);
    }
}

/// <summary>
/// The JSON half. Every value-carrying result funnels through one writer, so the "which metadata?"
/// question is answered once — and an unregistered type says so instead of falling through to a
/// reflective resolver that is not there.
/// </summary>
public class ResultJsonTests
{
    [Fact]
    public async Task ValueIsSerializedThroughSourceGeneratedMetadata ()
    {
        var answer = await Worker.Execute(TypedResults.Ok(new Todo(1, "x")));
        Assert.Equal(200, answer.Status);
        Assert.Equal("application/json; charset=utf-8", answer.Header("content-type"));
        Assert.Equal("""{"id":1,"title":"x"}""", answer.Body);
    }

    [Fact]
    public async Task ErrorStatusesStillCarryTheirBody ()
    {
        Assert.Equal("""{"id":2,"title":"gone"}""", (await Worker.Execute(TypedResults.NotFound(new Todo(2, "gone")))).Body);
        Assert.Equal("""{"id":3,"title":"bad"}""", (await Worker.Execute(TypedResults.BadRequest(new Todo(3, "bad")))).Body);
        Assert.Equal("""{"id":4,"title":"new"}""", (await Worker.Execute(TypedResults.Created("/a", new Todo(4, "new")))).Body);
    }

    /// <summary>A null value sets the content type and writes nothing, rather than writing "null".</summary>
    [Fact]
    public async Task NullValueWritesNoBody ()
    {
        var answer = await Worker.Execute(TypedResults.Ok<Todo>(null));
        Assert.Equal("application/json; charset=utf-8", answer.Header("content-type"));
        Assert.Equal("", answer.Body);
    }

    [Fact]
    public async Task ExplicitMetadataAndStatusAreHonoured ()
    {
        var answer = await Worker.Execute(TypedResults.Json(new Todo(1, "x"), TestJson.Default.Todo,
            contentType: "application/vnd.todo+json", statusCode: 201));
        Assert.Equal(201, answer.Status);
        Assert.Equal("application/vnd.todo+json", answer.Header("content-type"));
        Assert.Equal("""{"id":1,"title":"x"}""", answer.Body);
    }

    /// <summary>
    /// The failure ASP.NET Core reports as an opaque first-request 500: here it names the type and
    /// the attribute to add, because there is no reflective fallback that could have saved it.
    /// </summary>
    [Fact]
    public async Task UnregisteredTypeSaysWhatIsMissing ()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Worker.Execute(TypedResults.Ok(new Uri("https://w.dev"))));
        Assert.Contains("JsonSerializable", error.Message);
        Assert.Contains("Uri", error.Message);
    }

    /// <summary>
    /// A hand-built provider that forgot the options is told so, rather than throwing a
    /// NullReferenceException from inside the writer.
    /// </summary>
    [Fact]
    public async Task ProviderWithoutJsonOptionsSaysSo ()
    {
        using var context = Worker.Context(services: new ServiceCollection().BuildServiceProvider());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => TypedResults.Ok(new Todo(1, "x")).ExecuteAsync(context));
        Assert.Contains("JsonOptions is not registered", error.Message);
    }
}

/// <summary>Content, text and redirect: the results whose whole behaviour is headers.</summary>
public class ResultContentTests
{
    [Fact]
    public async Task ContentWithoutATypeIsPlainTextInUtf8 ()
    {
        var answer = await Worker.Execute(TypedResults.Content("hello"));
        Assert.Equal(200, answer.Status);
        Assert.Equal("text/plain; charset=utf-8", answer.Header("content-type"));
        Assert.Equal("hello", answer.Body);
    }

    [Fact]
    public async Task TextDefaultsToPlainTextWhateverElseIsPassed () =>
        Assert.Equal("text/plain; charset=utf-8", (await Worker.Execute(TypedResults.Text("hello"))).Header("content-type"));

    [Fact]
    public async Task CharsetIsAppendedOnceAndOnlyWhenAbsent ()
    {
        Assert.Equal("application/xml; charset=utf-8",
            (await Worker.Execute(TypedResults.Content("<x/>", "application/xml"))).Header("content-type"));
        Assert.Equal("application/xml; charset=utf-16",
            (await Worker.Execute(TypedResults.Content("<x/>", "application/xml; charset=utf-16", Encoding.Unicode)))
            .Header("content-type"));
    }

    [Fact]
    public async Task ContentHonoursAnExplicitStatus () =>
        Assert.Equal(503, (await Worker.Execute(TypedResults.Content("down", statusCode: 503))).Status);

    [Fact]
    public async Task NullContentWritesNoBodyButStillTypesTheResponse ()
    {
        var answer = await Worker.Execute(TypedResults.Content(null));
        Assert.Equal("", answer.Body);
        Assert.Equal("text/plain; charset=utf-8", answer.Header("content-type"));
    }

    /// <summary>The four redirects, by the two flags that choose between them.</summary>
    [Theory]
    [InlineData(false, false, 302)]
    [InlineData(true, false, 301)]
    [InlineData(false, true, 307)]
    [InlineData(true, true, 308)]
    public async Task RedirectPicksItsStatusFromPermanenceAndMethodPreservation (bool permanent, bool preserveMethod, int expected)
    {
        var answer = await Worker.Execute(TypedResults.Redirect("/there", permanent, preserveMethod));
        Assert.Equal(expected, answer.Status);
        Assert.Equal("/there", answer.Header("location"));
    }

    /// <summary>
    /// 303 See Other: the Post/Redirect/Get answer, and the one status the four-way matrix cannot
    /// produce — which is why this package adds it beyond upstream's surface.
    /// </summary>
    [Fact]
    public async Task RedirectSeeOtherIsThreeOhThree ()
    {
        var answer = await Worker.Execute(TypedResults.RedirectSeeOther("/there"));
        Assert.Equal(303, answer.Status);
        Assert.Equal("/there", answer.Header("location"));
        Assert.Equal(303, TypedResults.RedirectSeeOther("/there").StatusCode);
    }

    /// <summary>
    /// The result that writes nothing at all: whatever the handler already set survives it, which is
    /// what makes it usable after a middleware has composed the response by hand.
    /// </summary>
    [Fact]
    public async Task EmptyLeavesTheResponseExactlyAsItFoundIt ()
    {
        using var context = Worker.Context();
        context.Response.StatusCode = 418;
        await context.Response.WriteAsync("brewed");
        await TypedResults.Empty.ExecuteAsync(context);
        Assert.Equal(418, context.Response.StatusCode);
        Assert.Equal("brewed", Worker.Body(context));
    }
}

/// <summary>
/// RFC 7807. The body is written with <c>Utf8JsonWriter</c> rather than the serializer, because
/// <see cref="ProblemDetails.Extensions"/> is a bag of arbitrary values that source-generated
/// metadata cannot describe.
/// </summary>
public class ProblemResultTests
{
    [Fact]
    public async Task ProblemDefaultsTo500WithTheStandardTitle ()
    {
        var answer = await Worker.Execute(TypedResults.Problem());
        Assert.Equal(500, answer.Status);
        Assert.Equal("application/problem+json; charset=utf-8", answer.Header("content-type"));
        Assert.Equal("""{"title":"An error occurred while processing your request.","status":500}""", answer.Body);
    }

    [Fact]
    public async Task ProblemWritesOnlyTheMembersItWasGiven ()
    {
        var answer = await Worker.Execute(TypedResults.Problem(
            detail: "nope", instance: "/a/1", statusCode: 404, title: "Missing", type: "https://x/404"));
        Assert.Equal(404, answer.Status);
        Assert.Equal(
            """{"type":"https://x/404","title":"Missing","status":404,"detail":"nope","instance":"/a/1"}""",
            answer.Body);
    }

    /// <summary>An unspecified title comes from the status, as ASP.NET Core's does.</summary>
    [Fact]
    public async Task TitleIsDerivedFromTheStatusWhenAbsent ()
    {
        Assert.Contains("\"title\":\"Not Found\"", (await Worker.Execute(TypedResults.Problem(statusCode: 404))).Body);
        Assert.Contains("\"title\":\"Conflict\"", (await Worker.Execute(TypedResults.Problem(statusCode: 409))).Body);
        Assert.Contains("\"title\":\"Bad Request\"", (await Worker.Execute(TypedResults.Problem(statusCode: 400))).Body);
    }

    [Fact]
    public async Task ProblemDetailsCanBeSuppliedWhole ()
    {
        var details = new ProblemDetails { Status = 402, Title = "Pay up" };
        var result = TypedResults.Problem(details);
        Assert.Same(details, result.ProblemDetails);
        Assert.Equal(402, (await Worker.Execute(result)).Status);
    }

    [Fact]
    public async Task ValidationProblemIsA400ListingItsErrors ()
    {
        var answer = await Worker.Execute(TypedResults.ValidationProblem(new Dictionary<string, string[]> {
            ["name"] = ["required"],
            ["age"] = ["too small", "not a number"]
        }));
        Assert.Equal(400, answer.Status);
        Assert.Equal("application/problem+json; charset=utf-8", answer.Header("content-type"));
        Assert.Equal(
            """{"title":"One or more validation errors occurred.","status":400,"errors":{"name":["required"],"age":["too small","not a number"]}}""",
            answer.Body);
    }

    [Fact]
    public void ValidationProblemKeepsItsErrorsAddressable ()
    {
        var errors = new Dictionary<string, string[]> { ["name"] = ["required"] };
        Assert.Same(errors, TypedResults.ValidationProblem(errors).Errors);
        Assert.Throws<ArgumentNullException>(static () => { TypedResults.ValidationProblem(null!); });
    }
}

/// <summary>
/// The two results that exist so that code written against them fails at the call rather than
/// compiling against an API that was never there.
/// </summary>
public class StubbedResultTests
{
    [Fact]
    public void StreamNamesTheMilestoneAndTheWorkaround ()
    {
        var error = Assert.Throws<PlatformNotSupportedException>(
            static () => { TypedResults.Stream(new MemoryStream()); });
        Assert.Contains("milestone 0b", error.Message);
        Assert.Contains("TypedResults.Content", error.Message);
    }

    [Fact]
    public void ServerSentEventsNameTheSerializationGateToo ()
    {
        var error = Assert.Throws<PlatformNotSupportedException>(
            static () => { TypedResults.ServerSentEvents(Empty()); });
        Assert.Contains("milestone 0b", error.Message);
        Assert.Contains("serialization gate", error.Message);
    }

#pragma warning disable CS1998 // No await: the point is that the call throws before enumeration.
    private static async IAsyncEnumerable<int> Empty () { yield break; }
#pragma warning restore CS1998
}

/// <summary>
/// The untyped façade, for handlers whose branches return different results. It has to agree with
/// <see cref="TypedResults"/> member for member, or the two would be different APIs wearing one
/// name.
/// </summary>
public class ResultsFacadeTests
{
    [Fact]
    public async Task EveryFacadeMemberProducesTheSameAnswerAsItsTypedCounterpart ()
    {
        await Agree(TypedResults.Ok(), Results.Ok());
        await Agree(TypedResults.Ok(new Todo(1, "x")), Results.Ok(new Todo(1, "x")));
        await Agree(TypedResults.Created("/a"), Results.Created("/a"));
        await Agree(TypedResults.Created("/a", new Todo(1, "x")), Results.Created("/a", new Todo(1, "x")));
        await Agree(TypedResults.Accepted("/a"), Results.Accepted("/a"));
        await Agree(TypedResults.NoContent(), Results.NoContent());
        await Agree(TypedResults.BadRequest(), Results.BadRequest());
        await Agree(TypedResults.BadRequest(new Todo(1, "x")), Results.BadRequest(new Todo(1, "x")));
        await Agree(TypedResults.NotFound(), Results.NotFound());
        await Agree(TypedResults.NotFound(new Todo(1, "x")), Results.NotFound(new Todo(1, "x")));
        await Agree(TypedResults.Conflict(), Results.Conflict());
        await Agree(TypedResults.UnprocessableEntity(), Results.UnprocessableEntity());
        await Agree(TypedResults.StatusCode(503), Results.StatusCode(503));
        await Agree(TypedResults.Empty, Results.Empty);
        await Agree(TypedResults.Content("x"), Results.Content("x"));
        await Agree(TypedResults.Text("x"), Results.Text("x"));
        await Agree(TypedResults.Json(new Todo(1, "x"), TestJson.Default.Todo),
            Results.Json(new Todo(1, "x"), TestJson.Default.Todo));
        await Agree(TypedResults.Redirect("/a", true), Results.Redirect("/a", true));
        await Agree(TypedResults.RedirectSeeOther("/a"), Results.RedirectSeeOther("/a"));
        await Agree(TypedResults.Problem(detail: "d"), Results.Problem(detail: "d"));
        await Agree(TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["a"] = ["b"] }),
            Results.ValidationProblem(new Dictionary<string, string[]> { ["a"] = ["b"] }));
    }

    private static async Task Agree (IResult typed, IResult untyped) =>
        Assert.Equal(await Worker.Execute(typed), await Worker.Execute(untyped));
}
