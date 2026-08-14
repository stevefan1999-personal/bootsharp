using Xunit;

namespace Bootsharp.Cloudflare.Generate.Tests;

/// <summary>
/// An app carrying one of everything the binding ladder can classify: a registered service, a
/// keyed one, a type with a bare <c>TryParse</c>, a type that implements <c>IParsable</c>, and a
/// payload a generated JSON context covers.
/// </summary>
internal static class BindingSources
{
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

        [JsonSerializable(typeof(Todo))]
        public partial class AppJsonContext { }

        public interface IClock { DateTime Now { get; } }

        public sealed class Clock : IClock { public DateTime Now => DateTime.UtcNow; }

        public interface ICache { string Get(string key); }

        public sealed class Cache : ICache { public string Get(string key) => key; }

        // Nothing registers this one: it is the "neither a service nor bindable" case.
        public interface IUnregistered { }

        // Parsable by a bare TryParse, the rung below IParsable on upstream's ladder.
        public readonly struct Slug
        {
            public string Value { get; init; }
            public static bool TryParse(string? text, out Slug value)
            {
                value = new Slug { Value = text ?? "" };
                return !string.IsNullOrEmpty(text);
            }
            public override string ToString() => Value;
        }

        // Parsable through IParsable<T>, which is the rung the primitives are on too.
        public readonly struct Ticket : IParsable<Ticket>
        {
            public int Number { get; init; }
            public static Ticket Parse(string s, IFormatProvider? provider) =>
                TryParse(s, provider, out var value) ? value : throw new FormatException();
            public static bool TryParse(string? s, IFormatProvider? provider, out Ticket result)
            {
                result = default;
                if (!int.TryParse(s, out var number)) return false;
                result = new Ticket { Number = number };
                return true;
            }
            public override string ToString() => Number.ToString();
        }

        public static class Api
        {
            public static WebApplication Build()
            {
                var builder = WebApplication.CreateSlimBuilder();
                builder.Services.AddSingleton<IClock>(new Clock());
                builder.Services.AddKeyedSingleton<ICache, Cache>("main");
                var app = builder.Build();
                {{string.Join("\n        ", maps)}}
                app.Run();
                return app;
            }
        }

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
            public string HeadersJson => "{\"x-trace\":\"abc\",\"x-tag\":\"a,b\",\"x-count\":\"7\"}";
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
/// the RDG's binding ladder is the spec. One case per rung, asserting the expression
/// the interceptor reads the value from and the parse call it puts it through — because the rung a
/// parameter lands on is the whole of what the generator decides for it.
/// </summary>
public class MinimalApiRouteAndQueryBindingTests
{
    [Fact]
    public void RouteValueIsReadFromTheRouteValuesAndParsedForItsType ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/todos/{id:int}/{slug}", (int id, string slug) => $"{id}{slug}");"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains("""var id_text = (string?)httpContext.Request.RouteValues["id"];""", run.Generated);
        Assert.Contains("""var slug_text = (string?)httpContext.Request.RouteValues["slug"];""", run.Generated);
        Assert.Contains("Source = Route)", run.Generated);
    }

    /// <summary>Route matching is case-insensitive, so the name a parameter claims is too.</summary>
    [Fact]
    public void RouteParameterMatchesThePatternRegardlessOfCase ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App("""app.MapGet("/todos/{Id}", (int id) => $"{id}");"""));
        Assert.Empty(run.DefectIds);
        Assert.Contains("""httpContext.Request.RouteValues["id"]""", run.Generated);
    }

    [Fact]
    public void NameThePatternDoesNotDeclareBindsFromTheQuery ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App("""app.MapGet("/todos", (string q) => q);"""));
        Assert.Empty(run.DefectIds);
        Assert.Contains("""var q_text = (string?)httpContext.Request.Query["q"];""", run.Generated);
        Assert.Contains("Source = Query)", run.Generated);
    }

    /// <summary>A required string with nothing to bind is the single 400 gate, not an empty string.</summary>
    [Fact]
    public void RequiredStringGatesOnBeingAbsent ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App("""app.MapGet("/a", (string q) => q);"""));
        Assert.Contains("if (string.IsNullOrEmpty(q_text))", run.Generated);
        Assert.Contains("    wasParamCheckFailure = true;", run.Generated);
        Assert.Contains("if (wasParamCheckFailure)", run.Generated);
    }

    [Fact]
    public void OptionalStringDoesNotGateAndTakesItsDefault ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App("""app.MapGet("/a", (string q = "all") => q);"""));
        Assert.Empty(run.DefectIds);
        Assert.Contains("""global::System.String q_local = q_text ?? "all";""", run.Generated);
        Assert.DoesNotContain("if (string.IsNullOrEmpty(q_text))", run.Generated);
    }

    [Fact]
    public void NullableAnnotationAloneMakesAParameterOptional ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App("""app.MapGet("/a", (int? page) => $"{page}");"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains("IsOptional = True", run.Generated);
        Assert.DoesNotContain("else\n    wasParamCheckFailure = true;", run.Generated.Replace("\r", ""));
    }

    /// <summary>Each rung of the parse ladder emits the call that rung is named for.</summary>
    [Fact]
    public void EveryParseRungEmitsItsOwnCall ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a", (int i, Guid g, DateTime d, Sort s, Uri u, Slug slug, Ticket t) => "ok");"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains("global::System.Enum.TryParse<global::Sort>(s_text, true, out var s_parsed)", run.Generated);
        Assert.Contains("global::System.Uri.TryCreate(u_text, global::System.UriKind.RelativeOrAbsolute, out var u_parsed)", run.Generated);
        // A bare TryParse takes no format provider; everything else on the ladder takes the invariant one.
        Assert.Contains("global::Slug.TryParse(slug_text, out var slug_parsed)", run.Generated);
        Assert.Contains("global::Ticket.TryParse(t_text, global::System.Globalization.CultureInfo.InvariantCulture, out var t_parsed)", run.Generated);
        Assert.Contains("global::System.Guid.TryParse(g_text, global::System.Globalization.CultureInfo.InvariantCulture, out var g_parsed)", run.Generated);
        Assert.Contains("global::System.DateTime.TryParse(d_text, global::System.Globalization.CultureInfo.InvariantCulture, out var d_parsed)", run.Generated);
    }

    /// <summary>
    /// A route value is one string, so an array can only come from a repeatable source. Upstream
    /// discovers this at startup; here it is a build error naming the sources that do work.
    /// </summary>
    [Fact]
    public void ArrayFromTheRouteIsRefusedAndFromTheQueryIsNot ()
    {
        var refused = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a/{ids}", ([FromRoute] int[] ids) => ids.Length);"""));
        Assert.Equal(["CFW026"], refused.DefectIds);
        Assert.Contains("Bind arrays from the query string or a header", refused.DefectReport);
        var bound = MinimalApiHarness.Run(BindingSources.App("""app.MapGet("/a", (int[] ids) => ids.Length);"""));
        Assert.Empty(bound.DefectIds);
        Assert.Contains("""var ids_raw = httpContext.Request.Query["ids"];""", bound.Generated);
    }

    /// <summary>An absent optional array is an empty array, not a failed check.</summary>
    [Fact]
    public void OptionalArrayIsEmptyRatherThanMissing ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App("""app.MapGet("/a", (int[]? ids) => ids?.Length ?? 0);"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains("global::System.Array.Empty<global::System.Int32>();", run.Generated);
    }
}

/// <summary>Headers: the source with the same shape as the query and a different accessor.</summary>
public class MinimalApiHeaderBindingTests
{
    [Fact]
    public void HeaderBindsScalarsArraysAndParsedValues ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a", ([FromHeader(Name = "X-Trace")] string trace, [FromHeader(Name = "X-Tag")] string[] tags, [FromHeader(Name = "X-Count")] int count) => trace);"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains("""var trace_text = (string?)httpContext.Request.Headers["X-Trace"];""", run.Generated);
        Assert.Contains("""var tags_raw = httpContext.Request.Headers["X-Tag"];""", run.Generated);
        Assert.Contains("""var count_text = (string?)httpContext.Request.Headers["X-Count"];""", run.Generated);
    }

    /// <summary>Without a name the header is the parameter's own name, as upstream's is.</summary>
    [Fact]
    public void HeaderNameDefaultsToTheParameterName ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App("""app.MapGet("/a", ([FromHeader] string? trace) => trace);"""));
        Assert.Empty(run.DefectIds);
        Assert.Contains("""httpContext.Request.Headers["trace"]""", run.Generated);
    }

    [Fact]
    public void HeaderValuesReachTheHandler () =>
        Assert.Equal("200|abc", MinimalApiHarness.Send(
            BindingSources.App("""app.MapGet("/a", ([FromHeader(Name = "X-Trace")] string trace) => trace);"""),
            "GET", "https://w.dev/a"));
}

/// <summary>
/// The service-or-body question answers from the registrations the generator can see,
/// standing in for upstream's startup <c>IServiceProviderIsService</c> call.
/// </summary>
public class MinimalApiServiceBindingTests
{
    [Fact]
    public void RegisteredTypeResolvesAsARequiredService ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App("""app.MapGet("/a", (IClock clock) => "ok");"""));
        Assert.Empty(run.DefectIds);
        Assert.Contains("httpContext.RequestServices.GetRequiredService<global::IClock>();", run.Generated);
    }

    /// <summary>A keyed registration still says "this type is a service".</summary>
    [Fact]
    public void KeyedRegistrationCountsAsARegistration ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App("""app.MapGet("/a", (ICache cache) => "ok");"""));
        Assert.Empty(run.DefectIds);
        Assert.Contains("GetRequiredService<global::ICache>();", run.Generated);
    }

    /// <summary>A nullable service is optional, so it is asked for rather than demanded.</summary>
    [Fact]
    public void NullableServiceUsesTheNonThrowingResolution ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a", ([FromServices] IUnregistered? maybe) => "ok");"""));
        Assert.Empty(run.DefectIds);
        // The nullable annotation is dropped from the type argument: it decides the resolution, it
        // is not part of the type being resolved.
        Assert.Contains("httpContext.RequestServices.GetService<global::IUnregistered>();", run.Generated);
    }

    /// <summary>
    /// The escape hatch: a type no registration in this compilation mentions is still a service when
    /// the call site says so.
    /// </summary>
    [Fact]
    public void FromServicesOverridesWhatTheRegistrationsSay ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            """app.MapPost("/a", ([FromServices] IUnregistered service, Todo todo) => "ok");"""));
        Assert.Empty(run.DefectIds);
        Assert.Contains("GetRequiredService<global::IUnregistered>();", run.Generated);
        Assert.Contains("TryResolveBodyAsync<global::Todo>(", run.Generated);
    }

    /// <summary>
    /// Unregistered, on a bodyless method, with no attribute: neither answer is available, so the
    /// refusal names both escape hatches rather than guessing.
    /// </summary>
    [Fact]
    public void UnregisteredTypeOnABodylessMethodIsRefusedNamingBothHatches ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App("""app.MapGet("/a", (IUnregistered service) => "ok");"""));
        Assert.Equal(["CFW026"], run.DefectIds);
        Assert.Contains("[FromServices]", run.DefectReport);
        Assert.Contains("[FromBody]", run.DefectReport);
        Assert.Empty(run.Generated);
    }

    /// <summary>The same type on a body-carrying method is the body — upstream's rule.</summary>
    [Fact]
    public void UnregisteredTypeOnABodyCarryingMethodIsTheBody ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App("""app.MapPut("/a", (Todo todo) => todo.Id);"""));
        Assert.Empty(run.DefectIds);
        Assert.Contains("TryResolveBodyAsync<global::Todo>(httpContext, todo_JsonTypeInfo, false);", run.Generated);
    }

    /// <summary>A nullable body is allowed to be absent; a required one is a failed check.</summary>
    [Fact]
    public void NullableBodyIsOptional ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App("""app.MapPost("/a", (Todo? todo) => "ok");"""));
        Assert.Empty(run.DefectIds);
        // The annotation is dropped from the type argument and becomes the "optional" flag instead.
        Assert.Contains("TryResolveBodyAsync<global::Todo>(httpContext, todo_JsonTypeInfo, true);", run.Generated);
    }
}

/// <summary>
/// The five special types. Each is passed straight through, so none of them costs a
/// binding block or a failed-check branch.
/// </summary>
public class MinimalApiSpecialTypeBindingTests
{
    [Fact]
    public void SpecialTypesCostNoBindingBlock ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a", (HttpContext c, HttpRequest q, HttpResponse r, CancellationToken t, System.Security.Claims.ClaimsPrincipal u) => "ok");"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        // No binding block is emitted for any of them, and each is handed the context's own member.
        Assert.DoesNotContain("// Endpoint Parameter:", run.Generated);
        Assert.Contains("handler(httpContext, httpContext.Request, httpContext.Response, " +
                        "httpContext.RequestAborted, EmptyPrincipal)", run.Generated);
    }

    /// <summary>
    /// The principal is one shared empty instance rather than one per request: there is no
    /// authentication layer to fill it, and a per-request allocation would suggest otherwise.
    /// </summary>
    [Fact]
    public void ClaimsPrincipalIsASharedEmptyOne ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a", (System.Security.Claims.ClaimsPrincipal user) => user.Identity?.Name ?? "anonymous");"""));
        Assert.Contains("private static readonly global::System.Security.Claims.ClaimsPrincipal EmptyPrincipal", run.Generated);
        Assert.Equal("200|anonymous", MinimalApiHarness.Send(BindingSources.App(
            """app.MapGet("/a", (System.Security.Claims.ClaimsPrincipal user) => user.Identity?.Name ?? "anonymous");"""),
            "GET", "https://w.dev/a"));
    }

    [Fact]
    public void RequestAndResponseArriveFromTheContextBeingHandled () =>
        Assert.Equal("200|GET /a", MinimalApiHarness.Send(BindingSources.App(
            """app.MapGet("/a", (HttpRequest request, HttpResponse response) => $"{request.Method} {request.Path}");"""),
            "GET", "https://w.dev/a"));
}

/// <summary>
/// Handler shapes: what the emitted <c>Cast</c> pins the delegate to, and which of them make the
/// request handler asynchronous.
/// </summary>
public class MinimalApiHandlerShapeTests
{
    /// <summary>A local function is a known signature too, so it binds exactly as a lambda does.</summary>
    [Fact]
    public void MethodGroupIsBoundLikeALambda ()
    {
        var source = BindingSources.App(
            """app.MapGet("/a", Greet);""",
            """static string Greet(string name) => "hi " + name;""");
        var run = MinimalApiHarness.Run(source);
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains("""httpContext.Request.Query["name"]""", run.Generated);
        Assert.Equal("200|hi world", MinimalApiHarness.Send(source, "GET", "https://w.dev/a?name=world"));
    }

    /// <summary>
    /// A lambda with an optional parameter has no <c>Func&lt;&gt;</c> natural type, so the cast has
    /// to reproduce the default or it will not unify with the compiler's synthesised delegate.
    /// </summary>
    [Fact]
    public void DefaultValuesAreReproducedInTheCast ()
    {
        var source = BindingSources.App("""app.MapGet("/a", (int page = 3) => $"page {page}");""");
        var run = MinimalApiHarness.Run(source);
        Assert.Empty(run.DefectIds);
        Assert.Contains("global::System.Int32 arg0 = 3", run.Generated);
        // The cast is what would fail first, and it fails at startup rather than at the request.
        Assert.Equal("200|page 3", MinimalApiHarness.Send(source, "GET", "https://w.dev/a"));
        Assert.Equal("200|page 9", MinimalApiHarness.Send(source, "GET", "https://w.dev/a?page=9"));
    }

    [Fact]
    public void AwaitingHandlersGetAnAsynchronousRequestHandler ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a", async () => { await Task.Yield(); return "ok"; });"""));
        Assert.Empty(run.DefectIds);
        Assert.Contains("async Task RequestHandler(HttpContext httpContext)", run.Generated);
    }

    /// <summary>
    /// A handler that composes the response itself writes nothing of the generator's: no JSON, no
    /// content type, just the invocation.
    /// </summary>
    [Fact]
    public void TaskReturningHandlerWritesNothingOfItsOwn ()
    {
        var source = BindingSources.App(
            """app.MapGet("/a", async (HttpResponse response) => { await Task.Yield(); response.StatusCode = 204; });""");
        var run = MinimalApiHarness.Run(source);
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.DoesNotContain("WriteJsonAsync", run.Generated);
        // The whole invocation: no result to classify, so nothing is written on the handler's behalf.
        Assert.Contains("await handler(httpContext.Response);", run.Generated);
        Assert.Equal("204|", MinimalApiHarness.Send(source, "GET", "https://w.dev/a"));
    }

    /// <summary>
    /// The synchronous <c>void</c> handler — <c>app.MapGet("/x", (HttpResponse r) =&gt; { … })</c>,
    /// a shape ASP.NET Core supports and this generator accepts without a diagnostic.
    /// </summary>
    /// <remarks>
    /// A regression test with a history: <c>EndpointResolver.Display</c> renders through
    /// <c>SymbolDisplayFormat.FullyQualifiedFormat</c>, which spells <c>void</c> as
    /// <c>global::System.Void</c> — a type C# forbids naming — so the emitted
    /// <c>Cast(del, global::System.Void (…) =&gt; throw null!)</c> failed the app's own build with
    /// CS0673, a raw compiler error inside generated code rather than a CFW diagnostic. The
    /// <c>Cast(del, void (</c> assertion below is what pins the keyword.
    /// </remarks>
    [Fact]
    public void VoidReturningHandlerWritesNothingOfItsOwn ()
    {
        var source = BindingSources.App(
            """app.MapGet("/a", (HttpResponse response) => { response.StatusCode = 204; });""");
        var run = MinimalApiHarness.Run(source);
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains("Cast(del, void (", run.Generated);
        Assert.Equal("204|", MinimalApiHarness.Send(source, "GET", "https://w.dev/a"));
    }

    /// <summary>A string return is text, not a JSON document — the RDG's rule too.</summary>
    [Fact]
    public void StringReturnIsWrittenAsPlainText ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App("""app.MapGet("/a", () => "ok");"""));
        Assert.Contains("""httpContext.Response.ContentType ??= "text/plain; charset=utf-8";""", run.Generated);
        Assert.DoesNotContain("WriteJsonAsync", run.Generated);
    }

    [Fact]
    public void TaskOfValueIsUnwrappedBeforeItIsClassified ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a", () => Task.FromResult(new Todo(1, "x")));"""));
        Assert.Empty(run.DefectIds);
        Assert.Contains("var response_JsonTypeInfo = jsonOptions.GetTypeInfo<global::Todo>();", run.Generated);
    }

    [Fact]
    public void ValueTaskIsUnwrappedTheSameWay ()
    {
        var run = MinimalApiHarness.Run(BindingSources.App(
            """app.MapGet("/a", () => ValueTask.FromResult("ok"));"""));
        Assert.Empty(run.DefectIds);
        Assert.Equal("no errors", run.ErrorReport);
        Assert.Contains("async Task RequestHandler(HttpContext httpContext)", run.Generated);
    }
}
