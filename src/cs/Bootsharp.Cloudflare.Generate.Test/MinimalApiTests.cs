using Xunit;

namespace Bootsharp.Cloudflare.Generate.Tests;

/// <summary>App halves the minimal-API driver tests map handlers onto.</summary>
internal static class MinimalApiSources
{
    /// <summary>An app that registers one service and maps whatever the case supplies.</summary>
    public static string App (params string[] maps) => $$"""
        using System;
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using System.Text.Json.Serialization;
        using Microsoft.AspNetCore.Builder;
        using Microsoft.AspNetCore.Http;
        using Microsoft.AspNetCore.Mvc;
        using Microsoft.Extensions.DependencyInjection;

        public sealed record Todo(int Id, string Title);

        public enum Sort { Asc, Desc }

        // Carries only the attribute the generator reads: a real context is emitted by the
        // System.Text.Json generator, which this compilation does not run.
        [JsonSerializable(typeof(Todo))]
        public partial class AppJsonContext { }

        public interface IClock { DateTime Now { get; } }

        public sealed class Clock : IClock { public DateTime Now => DateTime.UtcNow; }

        public static class Api
        {
            public static WebApplication Build()
            {
                var builder = WebApplication.CreateSlimBuilder();
                builder.Services.AddSingleton<IClock>(new Clock());
                var app = builder.Build();
                {{string.Join("\n        ", maps)}}
                app.Run();
                return app;
            }
        }

        // Runs a request through the built app, so a driver test can assert on what the intercepted
        // call site actually did rather than only on the text it emitted.
        public sealed class FakeRequest : Bootsharp.Cloudflare.IJsRequest
        {
            public FakeRequest(string method, string url, string body)
            {
                Method = method;
                Url = url;
                Text_ = body;
            }

            public string Method { get; }
            public string Url { get; }
            public string HeadersJson => "{}";
            public string? CfJson => null;
            private string Text_ { get; }
            public Task<string> Text() => Task.FromResult(Text_);
        }

        public static class Runner
        {
            private static WebApplication? app;

            public static async Task<string> Send(string method, string url, string body)
            {
                app ??= Api.Build();
                var response = await app.InvokeAsync(new FakeRequest(method, url, body));
                return response.Status + "|" + response.Body;
            }
        }
        """;
}

/// <summary>
/// every <c>Map*</c> call site is replaced by an interceptor that binds the handler
/// when the app compiles. These drive the generator over real call sites and compile what it
/// emitted against the real package — the only check that proves the emitted shape and the runtime
/// seam still agree.
/// </summary>
public class MinimalApiTests
{
    [Fact]
    public void CallSiteIsInterceptedAndForwardedToTheRuntimeSeam ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App("""app.MapGet("/health", () => "ok");"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Equal(["MapGet0"], run.Interceptors);
        Assert.Equal(1, run.InterceptsLocationCount);
        Assert.Contains("global::Microsoft.AspNetCore.Builder.RouteHandlerServices.Map(", run.Generated);
        Assert.Contains("GetVerb", run.Generated);
    }

    /// <summary>
    /// The non-async fast path: a handler with nothing to await gets a plain <c>Task</c>-returning
    /// local function, as upstream's generator emits for the same shape.
    /// </summary>
    [Fact]
    public void HandlerWithNothingToAwaitGetsASynchronousRequestHandler ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App("""app.MapGet("/health", () => "ok");"""));
        Assert.Contains("Task RequestHandler(HttpContext httpContext)", run.Generated);
        Assert.DoesNotContain("async Task RequestHandler(HttpContext httpContext)", run.Generated);
        // The filtered path always awaits the filter chain, so it is always async.
        Assert.Contains("async Task RequestHandlerFiltered(HttpContext httpContext)", run.Generated);
    }

    [Fact]
    public void HandlerReadingABodyGetsAnAsynchronousRequestHandler ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App("""app.MapPost("/todos", (Todo todo) => todo.Id);"""));
        Assert.Empty(run.DefectIds);
        Assert.Contains("async Task RequestHandler(HttpContext httpContext)", run.Generated);
        Assert.Contains("TryResolveBodyAsync<global::Todo>(", run.Generated);
    }

    /// <summary>Call sites that would emit the same body share one interceptor.</summary>
    [Fact]
    public void CallSitesWithTheSameShapeShareOneInterceptor ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """app.MapGet("/one", (int id) => $"{id}");""",
            """app.MapGet("/two", (int id) => $"{id}!");"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Single(run.Interceptors);
        Assert.Equal(2, run.InterceptsLocationCount);
    }

    [Fact]
    public void CallSitesWithDifferentShapesGetTheirOwnInterceptors ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """app.MapGet("/one", (int id) => $"{id}");""",
            """app.MapPost("/two", (Todo todo) => todo.Title);"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal(["MapGet0", "MapPost1"], run.Interceptors);
    }

    /// <summary>
    /// A name the pattern declares binds from the route; the same name absent from it binds from the
    /// query string. Upstream defers that choice to a runtime lookup against the parsed pattern.
    /// </summary>
    [Fact]
    public void ParameterNamedInThePatternBindsFromTheRouteAndTheRestFromTheQuery ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """app.MapGet("/todos/{id}", (int id, string? filter) => $"{id}{filter}");"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains("""httpContext.Request.RouteValues["id"]""", run.Generated);
        Assert.Contains("""httpContext.Request.Query["filter"]""", run.Generated);
        Assert.Contains("global::System.Int32.TryParse(id_text", run.Generated);
    }

    [Fact]
    public void ExplicitSourcesBindFromWhereTheyName ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """app.MapGet("/x/{slug}", ([FromRoute(Name = "slug")] string s, [FromQuery(Name = "q")] string? term, [FromHeader(Name = "X-Trace")] string? trace) => s + term + trace);"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains("""httpContext.Request.RouteValues["slug"]""", run.Generated);
        Assert.Contains("""httpContext.Request.Query["q"]""", run.Generated);
        Assert.Contains("""httpContext.Request.Headers["X-Trace"]""", run.Generated);
    }

    /// <summary>
    /// The service-or-body question answers at compile time: a type the app registers is
    /// a service, an unregistered one on a body-carrying method is the body.
    /// </summary>
    [Fact]
    public void RegisteredTypeBindsAsAServiceAndAnUnregisteredOneAsTheBody ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """app.MapPost("/todos", (Todo todo, IClock clock) => todo.Id);"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains("GetRequiredService<global::IClock>()", run.Generated);
        Assert.Contains("TryResolveBodyAsync<global::Todo>(", run.Generated);
    }

    /// <summary>The RDG's rule: an endpoint that accepts a bodyless method infers no body.</summary>
    [Fact]
    public void ComplexParameterOnAGetIsRefusedRatherThanInferredAsABody ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """app.MapGet("/todos", (Todo todo) => todo.Id);"""));
        Assert.Equal(["CFW026"], run.DefectIds);
        Assert.Contains("[FromServices]", run.DefectReport);
        Assert.Empty(run.Generated);
    }

    [Fact]
    public void ExplicitBodyOnAGetIsBoundWithAWarning ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """app.MapGet("/todos", ([FromBody] Todo todo) => todo.Id);"""));
        Assert.Equal(["CFW027"], run.DefectIds);
        Assert.Contains("TryResolveBodyAsync<global::Todo>(", run.Generated);
    }

    [Fact]
    public void SpecialTypesArePassedStraightThrough ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """app.MapGet("/ctx", (HttpContext context, HttpRequest request, HttpResponse response, CancellationToken token, System.Security.Claims.ClaimsPrincipal user) => "ok");"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains("handler(httpContext, httpContext.Request, httpContext.Response, httpContext.RequestAborted, EmptyPrincipal)", run.Generated);
    }

    [Fact]
    public void EnumsAndArraysBindFromTheQueryString ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """app.MapGet("/todos", (Sort sort, int[] ids, string[]? tags) => $"{sort}{ids.Length}");"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains("global::System.Enum.TryParse<global::Sort>(sort_text, true, out var sort_parsed)", run.Generated);
        Assert.Contains("var ids_values = new global::System.Int32[ids_raw.Count];", run.Generated);
        Assert.Contains("tags_local = tags_raw.ToArray()!;", run.Generated);
    }

    /// <summary>
    /// Compile-time precedence: the table the matcher sorts by is emitted, not recomputed at
    /// startup. The values are the ones <c>RoutePrecedence.ComputeInbound</c> assigns — a literal
    /// segment beats a constrained parameter, which beats an unconstrained one.
    /// </summary>
    [Fact]
    public void PrecedenceOfEveryPatternIsComputedWhenTheAppCompiles ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """app.MapGet("/api/health", () => "ok");""",
            """app.MapGet("/api/{id:int}", (int id) => $"{id}");""",
            """app.MapGet("/api/x/{rest}", (string rest) => rest);""",
            """app.MapGet("/files/{*path}", (string path) => path);"""));
        Assert.Empty(run.DefectIds);
        Assert.Contains("\"/api/health\" => 1.1m,", run.Generated);
        Assert.Contains("\"/api/{id:int}\" => 1.2m,", run.Generated);
        Assert.Contains("\"/api/x/{rest}\" => 1.13m,", run.Generated);
        Assert.Contains("\"/files/{*path}\" => 1.5m,", run.Generated);
        Assert.Contains("Precedence(pattern));", run.Generated);
    }

    [Fact]
    public void JsonReturningHandlerResolvesTypedMetadataWhenTheEndpointIsBuilt ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """app.MapGet("/todo", () => new Todo(1, "x"));"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains("var response_JsonTypeInfo = jsonOptions.GetTypeInfo<global::Todo>();", run.Generated);
        Assert.DoesNotContain("GetTypeInfo(typeof(", run.Generated);
    }

    [Fact]
    public void JsonTypeNoContextDeclaresIsReported ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """app.MapGet("/clock", () => new Clock());"""));
        Assert.Equal(["CFW029"], run.DefectIds);
        Assert.Contains("JsonSerializable", run.DefectReport);
    }

    [Fact]
    public void IResultReturningHandlerIsExecutedRatherThanSerialized ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """app.MapGet("/todo", () => TypedResults.Ok());"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains("return result.ExecuteAsync(httpContext);", run.Generated);
    }

    [Fact]
    public void MapMethodsPassesItsVerbsThroughAndKeepsBodyInferenceOff ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """app.MapMethods("/todos", new[] { "GET", "POST" }, (Todo todo) => todo.Id);"""));
        Assert.Equal(["CFW026"], run.DefectIds);
    }

    [Fact]
    public void MapMethodsForwardsTheCallSitesOwnVerbs ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """app.MapMethods("/todos", new[] { "POST", "PUT" }, (Todo todo) => todo.Id);"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains("IEnumerable<string> httpMethods,", run.Generated);
        Assert.Contains("                httpMethods,", run.Generated);
    }

    [Fact]
    public void NonConstantPatternIsRefused ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """var route = string.Concat("/a", "/b"); app.MapGet(route, () => "ok");"""));
        Assert.Equal(["CFW021"], run.DefectIds);
        Assert.Empty(run.Generated);
    }

    [Fact]
    public void MalformedPatternIsABuildError ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App("""app.MapGet("/a/{id", (string id) => id);"""));
        Assert.Equal(["CFW022"], run.DefectIds);
        Assert.Contains("no matching '}'", run.DefectReport);
    }

    [Fact]
    public void UnsupportedConstraintIsABuildErrorNamingWhy ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """app.MapGet("/a/{id:regex(^\\d+$)}", (string id) => id);"""));
        Assert.Equal(["CFW023"], run.DefectIds);
        Assert.Contains("System.Text.RegularExpressions", run.DefectReport);
    }

    [Fact]
    public void RouteParameterThePatternDoesNotDeclareIsABuildError ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """app.MapGet("/a/{id}", ([FromRoute(Name = "identifier")] string id) => id);"""));
        Assert.Equal(["CFW024"], run.DefectIds);
        Assert.Contains("the pattern does not declare", run.DefectReport);
    }

    [Fact]
    public void TwoEndpointsOnTheSameMethodAndPatternAreReported ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """app.MapGet("/a", () => "one");""",
            """app.MapGet("/a", () => "two");"""));
        Assert.Equal(["CFW025"], run.DefectIds);
        Assert.Contains("unreachable", run.DefectReport);
    }

    [Fact]
    public void HandlerThatIsNotALambdaOrMethodGroupIsRefused ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """Delegate handler = () => "ok"; app.MapGet("/a", handler);"""));
        Assert.Equal(["CFW026"], run.DefectIds);
        Assert.Empty(run.Generated);
    }

    [Fact]
    public void ObjectReturningHandlerIsRefusedBecauseItHasNoJsonMetadata ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App("""app.MapGet("/a", () => (object)1);"""));
        Assert.Equal(["CFW028"], run.DefectIds);
        Assert.Contains("TypedResults", run.DefectReport);
    }

    [Fact]
    public void FormBindingIsRefusedWithTheReason ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """app.MapPost("/a", ([FromForm] string name) => name);"""));
        Assert.Equal(["CFW026"], run.DefectIds);
        Assert.Contains("multipart", run.DefectReport);
    }

    /// <summary>
    /// The emitted code never names the machinery forbids, and never asks the
    /// serializer for metadata by <c>Type</c> — which is the call that falls through to the
    /// reflective resolver.
    /// </summary>
    [Fact]
    public void EmittedCodeStaysOutsideTheForbiddenSet ()
    {
        var run = MinimalApiHarness.Run(MinimalApiSources.App(
            """app.MapPost("/todos/{id:int}", (int id, Todo todo, IClock clock, CancellationToken token) => new Todo(id, todo.Title));"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        // 'RequestDelegateFactory.' rather than the bare name: the emitted file declares upstream's
        // own 'RequestDelegateFactoryFunc' using-alias, which is a Func<> and reaches no metadata.
        foreach (var forbidden in new[] {
                     "RequestDelegateFactory.", "DfaMatcher", "ILEmitTrie", "DefaultJsonTypeInfoResolver",
                     "ParameterPolicyActivator", "GetTypeInfo(typeof(", "GetParameters()", "PropertyAsParameterInfo"
                 })
            Assert.DoesNotContain(forbidden, run.Generated);
    }
}

/// <summary>
/// The intercepted call sites, run. Everything else in this file asserts on emitted text; these
/// compile that text into an assembly, load it, and put requests through the app it builds — which
/// is the only check that the interceptor actually replaced the call rather than merely compiling
/// beside it.
/// </summary>
public class MinimalApiBehaviourTests
{
    private const string routes = """
        app.MapGet("/api/health", () => "ok");
        app.MapGet("/api/todos/{id:int}", (int id) => $"todo {id}");
        app.MapGet("/api/search", (string q, int page = 1) => $"{q}:{page}");
        app.MapPost("/api/echo", (HttpContext context, IClock clock) => context.Request.Method);
        app.MapGet("/api/files/{*path}", (string path) => path);
        """;

    private static string Send (string method, string url, string body = "") =>
        MinimalApiHarness.Send(MinimalApiSources.App(routes.Split('\n')), method, url, body);

    [Fact]
    public void LiteralRouteAnswers () => Assert.Equal("200|ok", Send("GET", "https://w.dev/api/health"));

    [Fact]
    public void RouteValueIsParsedIntoTheHandlersParameter () =>
        Assert.Equal("200|todo 42", Send("GET", "https://w.dev/api/todos/42"));

    /// <summary>The constraint is enforced, so a non-integer is a miss rather than a 400.</summary>
    [Fact]
    public void RouteValueThatFailsItsConstraintDoesNotMatch () =>
        Assert.Equal("404|", Send("GET", "https://w.dev/api/todos/abc"));

    [Fact]
    public void QueryStringBindsWithItsDefault () =>
        Assert.Equal("200|cats:1", Send("GET", "https://w.dev/api/search?q=cats"));

    [Fact]
    public void QueryStringBindsTheValueThatIsThere () =>
        Assert.Equal("200|cats:3", Send("GET", "https://w.dev/api/search?q=cats&page=3"));

    /// <summary>The single 400 gate: a required parameter with nothing to bind never runs the handler.</summary>
    [Fact]
    public void MissingRequiredQueryParameterIsABadRequest () =>
        Assert.Equal("400|", Send("GET", "https://w.dev/api/search"));

    [Fact]
    public void UnparsableQueryParameterIsABadRequest () =>
        Assert.Equal("400|", Send("GET", "https://w.dev/api/search?q=cats&page=x"));

    [Fact]
    public void CatchAllSpansSegments () =>
        Assert.Equal("200|a/b/c.txt", Send("GET", "https://w.dev/api/files/a/b/c.txt"));

    [Fact]
    public void MethodMismatchIsNotFoundOrNotAllowedRatherThanTheWrongHandler () =>
        Assert.Equal("405|", Send("POST", "https://w.dev/api/health"));

    /// <summary>
    /// A pattern's own default reaches the handler: the matcher supplies it as a route value, and
    /// the parameter binds from there rather than from any C# default.
    /// </summary>
    [Fact]
    public void RouteParameterDefaultDeclaredInThePatternIsBound ()
    {
        var source = MinimalApiSources.App("""app.MapGet("/page/{n=7}", (int n) => $"page {n}");""");
        Assert.Equal("200|page 7", MinimalApiHarness.Send(source, "GET", "https://w.dev/page"));
        Assert.Equal("200|page 3", MinimalApiHarness.Send(source, "GET", "https://w.dev/page/3"));
    }

    /// <summary>An optional route parameter absent from the path is not a failed check.</summary>
    [Fact]
    public void OptionalRouteParameterIsAbsentRatherThanABadRequest ()
    {
        var source = MinimalApiSources.App("""app.MapGet("/opt/{tag?}", (string? tag) => "tag " + (tag ?? "none"));""");
        Assert.Equal("200|tag none", MinimalApiHarness.Send(source, "GET", "https://w.dev/opt"));
        Assert.Equal("200|tag x", MinimalApiHarness.Send(source, "GET", "https://w.dev/opt/x"));
    }

    [Fact]
    public void SpecialTypesArriveFromTheRequestBeingHandled () =>
        Assert.Equal("200|POST", Send("POST", "https://w.dev/api/echo", "\"x\""));
}

/// <summary>
/// The two shapes that are easy to emit and hard to be sure of: a route with no method constraint,
/// and the filtered path — which only runs when something registered a filter factory.
/// </summary>
public class MinimalApiFilterAndMapTests
{
    [Fact]
    public void MapWithoutAVerbAnswersEveryMethod ()
    {
        var source = MinimalApiSources.App("""app.Map("/any", (HttpContext c) => c.Request.Method);""");
        var run = MinimalApiHarness.Run(source);
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        // No verb array is hoisted and none is passed: the endpoint constrains no method.
        Assert.DoesNotContain("Verb = new[]", run.Generated);
        Assert.Equal("200|GET", MinimalApiHarness.Send(source, "GET", "https://w.dev/any"));
        Assert.Equal("200|DELETE", MinimalApiHarness.Send(source, "DELETE", "https://w.dev/any"));
    }

    /// <summary>
    /// A filter registered after <c>Map*</c> returns is still part of the delegate the endpoint
    /// carries — which is why the request delegate is built when the endpoint is, not when the call
    /// site ran. Registered through the convention builder because this package ships no
    /// <c>AddEndpointFilter</c> sugar yet.
    /// </summary>
    [Fact]
    public void FilterRegisteredAfterTheCallSiteWrapsTheHandler ()
    {
        var source = MinimalApiSources.App("""
            app.MapGet("/filtered", (string name) => $"hello {name}")
               .Add(b => b.FilterFactories.Add((_, next) => async ic =>
                   {
                       var inner = await next(ic);
                       return inner is string text ? text.ToUpperInvariant() : inner;
                   }));
            """.Split('\n'));
        var run = MinimalApiHarness.Run(source);
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Equal("200|HELLO WORLD", MinimalApiHarness.Send(source, "GET", "https://w.dev/filtered?name=world"));
    }

    /// <summary>The filtered path shares the parameter binding, including its 400 gate.</summary>
    [Fact]
    public void FilteredPathStillGatesOnAFailedParameterCheck ()
    {
        var source = MinimalApiSources.App("""
            app.MapGet("/filtered", (int id) => $"got {id}")
               .Add(b => b.FilterFactories.Add((_, next) => ic => next(ic)));
            """.Split('\n'));
        Assert.Equal("400|", MinimalApiHarness.Send(source, "GET", "https://w.dev/filtered?id=nope"));
    }
}
