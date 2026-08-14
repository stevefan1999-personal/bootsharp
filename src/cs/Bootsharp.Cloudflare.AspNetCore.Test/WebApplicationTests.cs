using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Bootsharp.Cloudflare.AspNetCore.Tests;

/// <summary>
/// The terminal middleware, reproduced from the ~50 lines of real logic inside upstream's
/// <c>EndpointRoutingMiddleware</c>/<c>EndpointMiddleware</c> pair: match, then invoke.
/// </summary>
public class DispatchTests
{
    [Fact]
    public async Task MatchedEndpointAnswers ()
    {
        var app = Worker.App(static app => app.Says("/health", "ok", "GET"));
        Assert.Equal(new Answer(200, "{}", "ok"), await app.Send("GET", "https://w.dev/health"));
    }

    [Fact]
    public async Task UnclaimedPathIsANotFoundWithNoBody ()
    {
        var app = Worker.App(static app => app.Says("/health", "ok", "GET"));
        var answer = await app.Send("GET", "https://w.dev/nope");
        Assert.Equal(404, answer.Status);
        Assert.Equal("", answer.Body);
        Assert.Null(answer.Header("allow"));
    }

    /// <summary>A path claimed only by other methods answers 405 and says which they are.</summary>
    [Fact]
    public async Task MethodMismatchIsA405ListingTheAllowedMethods ()
    {
        var app = Worker.App(static app => {
            app.Says("/todos", "list", "GET");
            app.Says("/todos", "made", "POST");
        });
        var answer = await app.Send("DELETE", "https://w.dev/todos");
        Assert.Equal(405, answer.Status);
        Assert.Equal("GET, POST", answer.Header("allow"));
    }

    [Fact]
    public async Task RouteValuesAndTheMatchedEndpointReachTheHandler ()
    {
        var app = Worker.App(static app => RouteHandlerServices.Map(app, "/todos/{id:int}", static context => {
            var endpoint = context.Features.Get<IEndpointFeature>()!.Endpoint;
            return context.Response.WriteAsync($"{context.Request.RouteValues["id"]}@{endpoint!.DisplayName}");
        }, ["GET"]));
        Assert.Equal("42@HTTP: GET /todos/{id:int}", (await app.Send("GET", "https://w.dev/todos/42")).Body);
    }

    /// <summary>Response headers and status a handler set are what the snapshot carries.</summary>
    [Fact]
    public async Task WhatTheHandlerSetIsWhatCrossesTheBoundary ()
    {
        var app = Worker.App(static app => RouteHandlerServices.Map(app, "/x", static context => {
            context.Response.StatusCode = 201;
            context.Response.Headers["x-trace"] = "abc";
            context.Response.ContentType = "text/plain";
            return context.Response.WriteAsync("made");
        }, null));
        var answer = await app.Send("PUT", "https://w.dev/x");
        Assert.Equal(201, answer.Status);
        Assert.Equal("abc", answer.Header("x-trace"));
        Assert.Equal("text/plain", answer.Header("content-type"));
        Assert.Equal("made", answer.Body);
    }

    /// <summary>
    /// An exception escaping the pipeline becomes an opaque 500. Nothing is read off the request to
    /// build it: the request handle may be exactly what failed.
    /// </summary>
    [Fact]
    public async Task UnhandledExceptionIsAnOpaque500 ()
    {
        var app = Worker.App(static app => RouteHandlerServices.Map(app, "/boom",
            static _ => throw new InvalidOperationException("secret detail"), null));
        var answer = await app.Send("GET", "https://w.dev/boom");
        Assert.Equal(500, answer.Status);
        Assert.Equal("Internal Server Error", answer.Body);
        Assert.Equal("text/plain; charset=utf-8", answer.Header("content-type"));
        Assert.DoesNotContain("secret detail", answer.Body);
    }

    /// <summary>A malformed URL fails the same way, rather than escaping into the JS side.</summary>
    [Fact]
    public async Task FailureBuildingTheContextIsAlsoA500 ()
    {
        var app = Worker.App(static app => app.Says("/a", "ok"));
        Assert.Equal(500, (await app.Send("GET", "not a url")).Status);
    }

    [Fact]
    public async Task EventWithoutARequestIsRejected ()
    {
        var app = Worker.App(static app => app.Says("/a", "ok"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => app.InvokeAsync(null!));
    }
}

/// <summary>The middleware pipeline: ordered, sealed once serving starts, and non-branching.</summary>
public class PipelineTests
{
    [Fact]
    public async Task MiddlewareRunsOutsideInAndCanShortCircuit ()
    {
        var order = new List<string>();
        var app = Worker.App(app => {
            app.Use(next => async context => { order.Add("first"); await next(context); order.Add("first done"); });
            app.Use(next => async context => { order.Add("second"); await next(context); });
            app.Says("/a", "handler");
        });
        Assert.Equal("handler", (await app.Send("GET", "https://w.dev/a")).Body);
        Assert.Equal(["first", "second", "first done"], order);
    }

    [Fact]
    public async Task MiddlewareCanAnswerWithoutReachingTheEndpoint ()
    {
        var app = Worker.App(static app => {
            app.Use(static _ => static context => {
                context.Response.StatusCode = 401;
                return Task.CompletedTask;
            });
            app.Says("/a", "handler");
        });
        var answer = await app.Send("GET", "https://w.dev/a");
        Assert.Equal(401, answer.Status);
        Assert.Equal("", answer.Body);
    }

    /// <summary>
    /// <c>Run()</c> does not block here — it seals the pipeline, which is the same thing the line at
    /// the end of a Program.cs means. Adding middleware afterwards is the mistake it can catch.
    /// </summary>
    [Fact]
    public void PipelineIsSealedOnceServingStarts ()
    {
        var app = Worker.App(static app => app.Says("/a", "ok"));
        app.Run();
        Assert.Contains("after the application has started serving",
            Assert.Throws<InvalidOperationException>(() => { app.Use(static next => next); }).Message);
    }

    [Fact]
    public void NullMiddlewareIsRejected ()
    {
        var app = Worker.App(static _ => { });
        Assert.Throws<ArgumentNullException>(() => { app.Use(null!); });
    }

    /// <summary>Branching is not supported, because nothing here composes a nested pipeline.</summary>
    [Fact]
    public void BranchingAndReplacingServicesAreRefused ()
    {
        var app = Worker.App(static _ => { });
        var builder = (IApplicationBuilder)app;
        Assert.Throws<NotSupportedException>(() => { builder.New(); });
        Assert.Throws<NotSupportedException>(() => { builder.ApplicationServices = null!; });
        Assert.Same(app.Services, builder.ApplicationServices);
        Assert.NotNull(builder.ServerFeatures);
        Assert.NotNull(builder.Properties);
    }
}

/// <summary>
/// one DI scope per top-level event, disposed in the same <c>finally</c> that disposes
/// the context — so the.NET scope can never outlive the JS handles the invocation imported.
/// </summary>
public class RequestScopeTests
{
    private sealed class Sequence { public int Next; }

    private sealed class Scoped (Sequence sequence)
    {
        public int Id { get; } = ++sequence.Next;
    }

    private sealed class Tracked (Sequence sequence) : IDisposable
    {
        public int Id { get; } = ++sequence.Next;
        public bool Disposed { get; private set; }
        public void Dispose () => Disposed = true;
    }

    [Fact]
    public async Task OneScopeSpansTheEventAndANewOneOpensForTheNext ()
    {
        var app = Worker.App(
            static app => RouteHandlerServices.Map(app, "/scope", static context => {
                var first = context.RequestServices.GetRequiredService<Scoped>();
                var second = context.RequestServices.GetRequiredService<Scoped>();
                return context.Response.WriteAsync($"{first.Id}:{second.Id}");
            }, null),
            static services => services.AddSingleton<Sequence>().AddScoped<Scoped>());
        // Stable within one event…
        Assert.Equal("1:1", (await app.Send("GET", "https://w.dev/scope")).Body);
        // …and never shared with the next.
        Assert.Equal("2:2", (await app.Send("GET", "https://w.dev/scope")).Body);
    }

    [Fact]
    public async Task ScopedDisposablesAreReleasedWhenTheEventEnds ()
    {
        Tracked? resolved = null;
        var app = Worker.App(
            app => RouteHandlerServices.Map(app, "/scope", context => {
                resolved = context.RequestServices.GetRequiredService<Tracked>();
                return Task.CompletedTask;
            }, null),
            static services => services.AddSingleton<Sequence>().AddScoped<Tracked>());
        await app.Send("GET", "https://w.dev/scope");
        Assert.NotNull(resolved);
        Assert.True(resolved.Disposed);
    }

    /// <summary>
    /// The context dies with the event: a handler that squirrels away the reference finds it inert,
    /// which is the point — nothing may still be holding a workerd handle after the event returns.
    /// </summary>
    [Fact]
    public async Task ContextIsDeadOnceTheEventReturns ()
    {
        HttpContext? escaped = null;
        var app = Worker.App(app => RouteHandlerServices.Map(app, "/leak", context => {
            escaped = context;
            return Task.CompletedTask;
        }, null));
        await app.Send("GET", "https://w.dev/leak");
        Assert.NotNull(escaped);
        Assert.Throws<ObjectDisposedException>(() => { _ = escaped.Request; });
    }
}

/// <summary>The body is buffered by the invocation, and only when the method could carry one.</summary>
public class RequestBodyTests
{
    private sealed class CountingRequest (string method, string url, string body) : IJsRequest
    {
        public int Reads { get; private set; }
        public string Method => method;
        public string Url => url;
        public string HeadersJson => "{}";
        public string? CfJson => null;
        public Task<string> Text () { Reads++; return Task.FromResult(body); }
    }

    private static async Task<(string Body, int Reads)> Send (string method)
    {
        var app = Worker.App(static app => RouteHandlerServices.Map(app, "/echo",
            static context => context.Response.WriteAsync(new StreamReader(context.Request.Body).ReadToEnd()), null));
        var request = new CountingRequest(method, "https://w.dev/echo", "payload");
        var response = await app.InvokeAsync(request);
        return (response.Body, request.Reads);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public async Task BodyCarryingMethodsAreReadOnce (string method) =>
        Assert.Equal(("payload", 1), await Send(method));

    /// <summary>A bodyless method never asks the JS side for text it cannot have.</summary>
    /// <remarks>DELETE and TRACE are in the list because the context declares them bodyless, and the
    /// invocation used to disagree — buffering a DELETE body over interop that
    /// <see cref="IHttpRequestBodyDetectionFeature.CanHaveBody"/> then said was unreadable. One
    /// predicate, <see cref="WorkerHttpContext.CanHaveBody"/>, answers for both now.</remarks>
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("DELETE")]
    [InlineData("TRACE")]
    public async Task BodylessMethodsAreNotReadAtAll (string method) =>
        Assert.Equal(("", 0), await Send(method));

    /// <summary>The invocation and the context answer "does this carry a body" identically.</summary>
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("DELETE")]
    [InlineData("TRACE")]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    public async Task BufferingAgreesWithBodyDetection (string method)
    {
        var reads = (await Send(method)).Reads;
        using var context = Worker.Context(method);
        var declared = context.Features.Get<IHttpRequestBodyDetectionFeature>()!.CanHaveBody;
        Assert.Equal(declared, reads == 1);
    }
}

/// <summary>
/// There is no socket, so "has started" can only become true when the snapshot is taken — which is
/// what makes the <c>OnStarting</c> ordering ASP.NET Core promises observable here.
/// </summary>
public class ResponseCallbackTests
{
    [Fact]
    public async Task StartingCallbacksRunInRegistrationOrderAndCompletedInReverse ()
    {
        var order = new List<string>();
        var app = Worker.App(app => RouteHandlerServices.Map(app, "/x", context => {
            context.Response.OnStarting(_ => { order.Add("starting 1"); return Task.CompletedTask; }, order);
            context.Response.OnStarting(_ => { order.Add("starting 2"); return Task.CompletedTask; }, order);
            context.Response.OnCompleted(_ => { order.Add("completed 1"); return Task.CompletedTask; }, order);
            context.Response.OnCompleted(_ => { order.Add("completed 2"); return Task.CompletedTask; }, order);
            return Task.CompletedTask;
        }, null));
        await app.Send("GET", "https://w.dev/x");
        Assert.Equal(["starting 1", "starting 2", "completed 2", "completed 1"], order);
    }

    /// <summary>A starting callback can still change the response: nothing has been transmitted.</summary>
    [Fact]
    public async Task StartingCallbacksCanStillChangeTheResponse ()
    {
        var app = Worker.App(static app => RouteHandlerServices.Map(app, "/x", static context => {
            context.Response.OnStarting(static state => {
                ((HttpContext)state).Response.Headers["x-late"] = "yes";
                return Task.CompletedTask;
            }, context);
            return Task.CompletedTask;
        }, null));
        Assert.Equal("yes", (await app.Send("GET", "https://w.dev/x")).Header("x-late"));
    }
}

/// <summary>
/// The endpoint table is fixed the moment the app is asked to route: conventions run before it is
/// read, and nothing may be added after.
/// </summary>
public class EndpointRegistrationTests
{
    [Fact]
    public void ConventionsRegisteredAfterMapAreAppliedBeforeTheEndpointIsBuilt ()
    {
        var app = Worker.App(static app => {
            var route = app.Says("/a", "ok", "GET");
            route.Add(static builder => builder.DisplayName = "named late");
            route.Finally(static builder => builder.DisplayName += " and finally");
        });
        var endpoint = Assert.Single(app.DataSources.Single().Endpoints);
        Assert.Equal("named late and finally", endpoint.DisplayName);
    }

    [Fact]
    public void DisplayNameCarriesTheMethodsWhenThereAreAny ()
    {
        var app = Worker.App(static app => {
            app.Says("/a", "ok", "GET", "POST");
            app.Says("/b", "ok");
        });
        Assert.Equal(["HTTP: GET, POST /a", "/b"],
            app.DataSources.Single().Endpoints.Select(static endpoint => endpoint.DisplayName)!);
    }

    [Fact]
    public void EndpointsCannotBeAddedOnceTheTableHasBeenRead ()
    {
        var app = Worker.App(static app => app.Says("/a", "ok"));
        app.Run();
        Assert.Contains("after the endpoint table has been read",
            Assert.Throws<InvalidOperationException>(() => { app.Says("/b", "ok"); }).Message);
    }

    [Fact]
    public void TheTableIsBuiltOnceHoweverOftenItIsRead ()
    {
        var app = Worker.App(static app => app.Says("/a", "ok"));
        var source = app.DataSources.Single();
        Assert.Same(source.Endpoints[0], source.Endpoints[0]);
        Assert.Single(source.Endpoints);
    }

    [Fact]
    public void RegistrationArgumentsAreChecked ()
    {
        var app = Worker.App(static _ => { });
        Assert.Throws<ArgumentNullException>(() => { RouteHandlerServices.Map(app, "/a", (RequestDelegate)null!, null); });
        Assert.Throws<ArgumentNullException>(() => { RouteHandlerServices.Map(app, null!, static _ => Task.CompletedTask, null); });
        Assert.Throws<ArgumentNullException>(() => { RouteHandlerServices.Map(null!, "/a", static _ => Task.CompletedTask, null); });
    }

    /// <summary>
    /// The generator's seam: the request delegate is built when the endpoint is, so a filter or a
    /// convention registered after the call site is inside it.
    /// </summary>
    [Fact]
    public async Task GeneratorSeamDefersBuildingTheRequestDelegate ()
    {
        var built = 0;
        var app = Worker.App(app => {
            var route = RouteHandlerServices.Map(app, "/a", static () => "ignored", ["GET"],
                static builder => builder.Metadata.Add("populated"),
                (_, builder) => {
                    built++;
                    var seen = builder.Metadata.OfType<string>().ToArray();
                    return context => context.Response.WriteAsync(string.Join(",", seen));
                },
                precedence: 1.1m);
            route.Add(static builder => builder.Metadata.Add("convention"));
        });
        // Nothing is built until the table is read.
        Assert.Equal(0, built);
        Assert.Equal("populated,convention", (await app.Send("GET", "https://w.dev/a")).Body);
        Assert.Equal(1, built);
    }

    [Fact]
    public void GeneratorSeamAttachesThePrecomputedPrecedence ()
    {
        var app = Worker.App(static app => RouteHandlerServices.Map(app, "/a", static () => "x", null,
            static _ => { }, static (_, _) => static context => context.Response.WriteAsync("x"),
            precedence: 1.25m));
        var endpoint = Assert.Single(app.DataSources.Single().Endpoints);
        Assert.Equal(1.25m, endpoint.Metadata.GetMetadata<RoutePrecedenceMetadata>()!.Value);
    }

    [Fact]
    public void GeneratorSeamArgumentsAreChecked ()
    {
        var app = Worker.App(static _ => { });
        Assert.Throws<ArgumentNullException>(() => {
            RouteHandlerServices.Map(app, "/a", (Delegate)null!, null, static _ => { },
                static (_, _) => static _ => Task.CompletedTask, null);
        });
        Assert.Throws<ArgumentNullException>(() => {
            RouteHandlerServices.Map(app, "/a", static () => "x", null, null!,
                static (_, _) => static _ => Task.CompletedTask, null);
        });
        Assert.Throws<ArgumentNullException>(() => {
            RouteHandlerServices.Map(app, "/a", static () => "x", null, static _ => { }, null!, null);
        });
    }
}
