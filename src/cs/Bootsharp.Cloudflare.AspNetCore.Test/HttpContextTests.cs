using System.Buffers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

namespace Bootsharp.Cloudflare.AspNetCore.Tests;

/// <summary>
/// the <c>Default*</c> trio is reimplemented over the workerd snapshot. These pin what
/// a handler can rely on having — the feature slots calls the practical minimum, and
/// the request surface derived from the four values the JS handle exposes.
/// </summary>
public class HttpContextFeatureTests
{
    /// <summary>
    /// Every feature the layer promises is present. <see cref="IHttpRequestBodyDetectionFeature"/>
    /// is in the list for a reason: without it the generated body binding treats every request as
    /// bodyless, which is a silent wrong answer rather than a failure.
    /// </summary>
    [Fact]
    public void EveryMandatoryFeatureIsSeeded ()
    {
        using var context = Worker.Context();
        Assert.NotNull(context.Features.Get<IHttpRequestFeature>());
        Assert.NotNull(context.Features.Get<IHttpRequestBodyDetectionFeature>());
        Assert.NotNull(context.Features.Get<IHttpResponseFeature>());
        Assert.NotNull(context.Features.Get<IHttpResponseBodyFeature>());
        Assert.NotNull(context.Features.Get<IItemsFeature>());
        Assert.NotNull(context.Features.Get<IQueryFeature>());
        Assert.NotNull(context.Features.Get<IRouteValuesFeature>());
        Assert.NotNull(context.Features.Get<IEndpointFeature>());
        Assert.NotNull(context.Features.Get<IServiceProvidersFeature>());
        Assert.NotNull(context.Features.Get<IHttpRequestLifetimeFeature>());
        // Declared by the same state feature but once left unregistered, so middleware reaching for
        // it — the usual way a trace id is set — got null while the HttpContext property worked.
        Assert.NotNull(context.Features.Get<IHttpRequestIdentifierFeature>());
    }

    /// <summary>The trace identifier is Cloudflare's ray id when the edge assigned one.</summary>
    /// <remarks>It is the id the dashboard and the logs already join on, so it is worth more than a
    /// counter of our own — which is what the fallback is, for `wrangler dev --local` and tests.</remarks>
    [Fact]
    public void TraceIdentifierComesFromTheRayHeaderWhenThereIsOne ()
    {
        using var rayed = Worker.Context(headersJson: """{"CF-Ray":"8f2c1a4b5d6e7f80-AMS"}""");
        Assert.Equal("8f2c1a4b5d6e7f80-AMS", rayed.TraceIdentifier);
        Assert.Equal(rayed.TraceIdentifier, rayed.Features.Get<IHttpRequestIdentifierFeature>()!.TraceIdentifier);
        using var bare = Worker.Context();
        Assert.NotEmpty(bare.TraceIdentifier);
        using var second = Worker.Context();
        Assert.NotEqual(bare.TraceIdentifier, second.TraceIdentifier);
    }

    /// <summary>
    /// <see cref="HttpContext.RequestAborted"/> is a real token, as its own remark promises.
    /// </summary>
    /// <remarks>It was a plain auto-property, so its value was <c>default(CancellationToken)</c>
    /// which is <see cref="CancellationToken.None"/>, the exact thing the remark said it was not.
    /// Nothing signals it (a worker cannot observe client disconnect), but code that links against
    /// it or registers on it needs a source behind it.</remarks>
    [Fact]
    public void RequestAbortedIsARealTokenThatIsNeverSignalled ()
    {
        using var context = Worker.Context();
        var token = context.RequestAborted;
        Assert.True(token.CanBeCanceled);
        Assert.NotEqual(CancellationToken.None, token);
        Assert.False(token.IsCancellationRequested);
        Assert.Equal(token, context.RequestAborted);
    }

    /// <summary>
    /// The features are the storage, not a copy of it: a value written through the feature is read
    /// back through the property, which is the contract middleware relies on.
    /// </summary>
    [Fact]
    public void FeaturesAndPropertiesShareOneBackingStore ()
    {
        using var context = Worker.Context();
        context.Features.Get<IHttpRequestFeature>()!.Path = "/rewritten";
        Assert.Equal("/rewritten", context.Request.Path);
        context.Features.Get<IHttpResponseFeature>()!.StatusCode = 418;
        Assert.Equal(418, context.Response.StatusCode);
        var endpoint = Worker.Endpoint("/a");
        context.Features.Get<IEndpointFeature>()!.Endpoint = endpoint;
        Assert.Same(endpoint, context.Features.Get<IEndpointFeature>()!.Endpoint);
    }

    /// <summary>The request and response feature objects are one object each, not one per read.</summary>
    [Fact]
    public void FeatureInstancesAreStable ()
    {
        using var context = Worker.Context();
        Assert.Same(context.Features.Get<IHttpRequestFeature>(), context.Features.Get<IHttpRequestFeature>());
        Assert.Same(context.Features.Get<IHttpResponseFeature>(), context.Features.Get<IHttpResponseBodyFeature>());
        Assert.Same(context.Request, context.Request);
        Assert.Same(context.Response, context.Response);
        Assert.Same(context, context.Request.HttpContext);
        Assert.Same(context, context.Response.HttpContext);
    }
}

/// <summary>The request half, derived from the URL, headers and body the JS handle reports.</summary>
public class HttpRequestTests
{
    [Fact]
    public void UrlIsSplitAcrossTheRequestSurface ()
    {
        using var context = Worker.Context("GET", "https://w.dev/api/todos?q=cats&page=2");
        Assert.Equal("GET", context.Request.Method);
        Assert.Equal("https", context.Request.Scheme);
        Assert.True(context.Request.IsHttps);
        Assert.Equal("/api/todos", context.Request.Path.Value);
        Assert.Equal("?q=cats&page=2", context.Request.QueryString.Value);
        Assert.Equal("/api/todos?q=cats&page=2", context.Features.Get<IHttpRequestFeature>()!.RawTarget);
        Assert.Equal("HTTP/1.1", context.Request.Protocol);
        Assert.False(context.Request.PathBase.HasValue);
    }

    [Fact]
    public void PlainHttpIsNotReportedAsHttps ()
    {
        using var context = Worker.Context(url: "http://w.dev/a");
        Assert.False(context.Request.IsHttps);
        Assert.Equal("http", context.Request.Scheme);
    }

    [Fact]
    public void QueryIsParsedFromTheUrl ()
    {
        using var context = Worker.Context(url: "https://w.dev/a?q=cats&page=2&q=dogs");
        Assert.Equal("2", context.Request.Query["page"]);
        Assert.Equal("cats,dogs", context.Request.Query["q"].ToString());
        Assert.Equal(2, context.Request.Query["q"].Count);
    }

    [Fact]
    public void HeadersArriveThroughTheirWellKnownProperties ()
    {
        using var context = Worker.Context(headersJson:
            """{"host":"w.dev","content-type":"application/json","content-length":"12","x-trace":"abc"}""");
        Assert.Equal("w.dev", context.Request.Host.Value);
        Assert.Equal("application/json", context.Request.ContentType);
        Assert.Equal(12, context.Request.ContentLength);
        Assert.Equal("abc", context.Request.Headers["x-trace"]);
        // Header lookup is case-insensitive, as the dictionary contract requires.
        Assert.Equal("abc", context.Request.Headers["X-Trace"]);
    }

    /// <summary>
    /// "Has a body" is the transfer-level question. A POST with an empty payload still has one, so
    /// that an empty JSON document binds rather than the request being treated as bodyless.
    /// </summary>
    [Theory]
    [InlineData("GET", false)]
    [InlineData("HEAD", false)]
    [InlineData("DELETE", false)]
    [InlineData("OPTIONS", false)]
    [InlineData("TRACE", false)]
    [InlineData("POST", true)]
    [InlineData("PUT", true)]
    [InlineData("PATCH", true)]
    public void BodyDetectionFollowsTheMethodRatherThanThePayload (string method, bool expected)
    {
        using var context = Worker.Context(method);
        Assert.Equal(expected, context.Features.Get<IHttpRequestBodyDetectionFeature>()!.CanHaveBody);
    }

    [Fact]
    public void BodyIsReadableAsTheBytesTheEventCarried ()
    {
        using var context = Worker.Context("POST", body: """{"id":1}""");
        Assert.Equal("""{"id":1}""", new StreamReader(context.Request.Body).ReadToEnd());
    }

    [Fact]
    public void EmptyBodyIsAnEmptyStreamRatherThanNull ()
    {
        using var context = Worker.Context();
        Assert.Equal(0, context.Request.Body.Length);
        Assert.Equal("", new StreamReader(context.Request.Body).ReadToEnd());
    }

    [Fact]
    public async Task BodyReaderReadsTheSameBytes ()
    {
        using var context = Worker.Context("POST", body: "hello");
        var read = await context.Request.BodyReader.ReadAsync();
        Assert.Equal("hello", Encoding.UTF8.GetString(read.Buffer.ToArray()));
    }

    [Fact]
    public void RouteValuesStartEmptyAndAreReplaceable ()
    {
        using var context = Worker.Context();
        Assert.Empty(context.Request.RouteValues);
        context.Request.RouteValues = new RouteValueDictionary { ["id"] = "7" };
        Assert.Equal("7", context.Request.RouteValues["id"]);
        Assert.Equal("7", context.Features.Get<IRouteValuesFeature>()!.RouteValues["id"]);
    }

    [Fact]
    public void ItemsAndServicesAreThePerRequestSlotsTheyLookLike ()
    {
        var services = Worker.Services();
        using var context = Worker.Context(services: services);
        Assert.Same(services, context.RequestServices);
        context.Items["key"] = "value";
        Assert.Equal("value", context.Items["key"]);
        Assert.Same(context.Items, context.Features.Get<IItemsFeature>()!.Items);
    }
}

/// <summary>The response half: a buffer plus headers, with no socket underneath.</summary>
public class HttpResponseTests
{
    [Fact]
    public void ResponseStartsAsAnUncommitted200 ()
    {
        using var context = Worker.Context();
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.False(context.Response.HasStarted);
        Assert.Empty(context.Response.Headers);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task WritingGoesIntoTheBufferTheSnapshotIsRenderedFrom ()
    {
        using var context = Worker.Context();
        await context.Response.WriteAsync("hello");
        Assert.Equal("hello", Worker.Body(context));
    }

    [Fact]
    public void ContentTypeAndLengthAreHeaderBacked ()
    {
        using var context = Worker.Context();
        context.Response.ContentType = "text/plain";
        context.Response.ContentLength = 5;
        Assert.Equal("text/plain", context.Response.Headers.ContentType);
        Assert.Equal(5, context.Response.Headers.ContentLength);
    }

    [Fact]
    public void RedirectSetsTheStatusAndLocation ()
    {
        using var context = Worker.Context();
        context.Response.Redirect("/there", permanent: false);
        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal("/there", context.Response.Headers.Location);
        context.Response.Redirect("/elsewhere", permanent: true);
        Assert.Equal(StatusCodes.Status301MovedPermanently, context.Response.StatusCode);
    }

    /// <summary>
    /// Starting and completing are deliberate no-ops: nothing is transmitted until the event ends,
    /// and every <c>WriteAsync</c> goes through <c>StartAsync</c> — throwing there would make the
    /// most ordinary thing a handler can do fail.
    /// </summary>
    [Fact]
    public async Task StartingAndCompletingAreNoOpsRatherThanFailures ()
    {
        using var context = Worker.Context();
        await context.Response.StartAsync();
        await context.Response.CompleteAsync();
        // Still uncommitted: only the snapshot can commit a response here.
        Assert.False(context.Response.HasStarted);
        context.Response.StatusCode = 201;
        Assert.Equal(201, context.Response.StatusCode);
    }
}

/// <summary>
/// "never silently degrade" rule: a member this platform cannot honour throws with a
/// sentence naming the reason and the alternative, so the failure is legible where it happens.
/// </summary>
public class UnsupportedSurfaceTests
{
    private static string Refusal (Action read)
    {
        var error = Assert.Throws<PlatformNotSupportedException>(read);
        Assert.NotEmpty(error.Message);
        return error.Message;
    }

    [Fact]
    public void ConnectionNamesTheEdgeTerminationAndTheAlternative () =>
        Assert.Contains("CF-Connecting-IP", Refusal(static () => { _ = Worker.Context().Connection; }));

    [Fact]
    public void WebSocketsNameTheLayerThatWouldCarryThem () =>
        Assert.Contains("WebSocket layer", Refusal(static () => { _ = Worker.Context().WebSockets; }));

    [Fact]
    public void UserNamesTheMissingAuthenticationLayer ()
    {
        Assert.Contains("authentication layer", Refusal(static () => { _ = Worker.Context().User; }));
        var context = Worker.Context();
        Assert.Throws<PlatformNotSupportedException>(() => { context.User = new ClaimsPrincipal(); });
    }

    [Fact]
    public void SessionNamesTheStoresAWorkerActuallyHas ()
    {
        Assert.Contains("KV", Refusal(static () => { _ = Worker.Context().Session; }));
        var context = Worker.Context();
        Assert.Throws<PlatformNotSupportedException>(() => { context.Session = null!; });
    }

    [Fact]
    public void RequestCookiesNameTheJavaScriptSideChangeTheyWaitOn ()
    {
        Assert.Contains("Set-Cookie", Refusal(static () => { _ = Worker.Context().Request.Cookies; }));
        var context = Worker.Context();
        Assert.Throws<PlatformNotSupportedException>(() => { context.Request.Cookies = null!; });
    }

    [Fact]
    public void ResponseCookiesRefuseForTheSameReason () =>
        Assert.Contains("Set-Cookie", Refusal(static () => { _ = Worker.Context().Response.Cookies; }));

    [Fact]
    public async Task FormBindingNamesTheLayerItBelongsTo ()
    {
        Assert.Contains("Forms", Refusal(static () => { _ = Worker.Context().Request.Form; }));
        var context = Worker.Context("POST", body: "a=b");
        Assert.False(context.Request.HasFormContentType);
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => context.Request.ReadFormAsync());
        Assert.Throws<PlatformNotSupportedException>(() => { context.Request.Form = null!; });
    }

    /// <summary>
    /// The call server-sent events make before their first frame. Failing it is the difference
    /// between "SSE is not supported yet" and an SSE response that silently arrives all at once.
    /// </summary>
    [Fact]
    public void DisablingBufferingNamesTheMilestoneThatWouldAllowIt ()
    {
        using var context = Worker.Context();
        var error = Assert.Throws<PlatformNotSupportedException>(
            () => context.Features.Get<IHttpResponseBodyFeature>()!.DisableBuffering());
        Assert.Contains("milestone 0b", error.Message);
    }

    [Fact]
    public void ReplacingTheResponseBodyNamesTheSameMilestone ()
    {
        using var context = Worker.Context();
        var error = Assert.Throws<PlatformNotSupportedException>(() => context.Response.Body = new MemoryStream());
        Assert.Contains("milestone 0b", error.Message);
    }

    [Fact]
    public async Task SendingAFileNamesTheAbsentFilesystem ()
    {
        using var context = Worker.Context();
        var error = await Assert.ThrowsAsync<PlatformNotSupportedException>(
            () => context.Features.Get<IHttpResponseBodyFeature>()!.SendFileAsync("/x", 0, null));
        Assert.Contains("no filesystem", error.Message);
    }

    [Fact]
    public void AbortingNamesWhoOwnsTheSocket ()
    {
        using var context = Worker.Context();
        var error = Assert.Throws<PlatformNotSupportedException>(context.Abort);
        Assert.Contains("owns the socket", error.Message);
    }

    /// <summary>
    /// The one case a build error cannot cover: a project that references the package without the
    /// generator. The message has to name the generator, because there is no binder to fall back to.
    /// </summary>
    [Fact]
    public void UninterceptedMapCallsSayWhichPieceIsMissing ()
    {
        var app = Worker.App(static _ => { });
        var handler = () => "ok";
        Assert.Contains("was not intercepted by Bootsharp.Cloudflare.Generate",
            Assert.Throws<InvalidOperationException>(() => { app.MapGet("/a", handler); }).Message);
        Assert.Contains("MapPost", Assert.Throws<InvalidOperationException>(() => { app.MapPost("/a", handler); }).Message);
        Assert.Contains("MapPut", Assert.Throws<InvalidOperationException>(() => { app.MapPut("/a", handler); }).Message);
        Assert.Contains("MapDelete", Assert.Throws<InvalidOperationException>(() => { app.MapDelete("/a", handler); }).Message);
        Assert.Contains("MapPatch", Assert.Throws<InvalidOperationException>(() => { app.MapPatch("/a", handler); }).Message);
        Assert.Contains("MapMethods", Assert.Throws<InvalidOperationException>(() => { app.MapMethods("/a", ["GET"], handler); }).Message);
        Assert.Contains("Map(\"/a\"", Assert.Throws<InvalidOperationException>(() => { app.Map("/a", handler); }).Message);
    }
}

/// <summary>
/// The lifetime is the event, not the object: once the invocation's
/// <c>finally</c> has run, no reference to the context can still do anything with it.
/// </summary>
public class ContextLifetimeTests
{
    [Fact]
    public void EveryMemberRefusesAfterTheEventEnds ()
    {
        var context = Worker.Context();
        context.Dispose();
        Assert.Throws<ObjectDisposedException>(() => { _ = context.Features; });
        Assert.Throws<ObjectDisposedException>(() => { _ = context.Request; });
        Assert.Throws<ObjectDisposedException>(() => { _ = context.Response; });
        Assert.Throws<ObjectDisposedException>(() => { _ = context.Items; });
        Assert.Throws<ObjectDisposedException>(() => { _ = context.RequestServices; });
        Assert.Throws<ObjectDisposedException>(() => { _ = context.RequestAborted; });
        Assert.Throws<ObjectDisposedException>(() => { _ = context.TraceIdentifier; });
        Assert.Throws<ObjectDisposedException>(context.Abort);
    }

    /// <summary>Disposal runs from a <c>finally</c>, so it has to tolerate running twice.</summary>
    [Fact]
    public void DisposingTwiceIsHarmless ()
    {
        var context = Worker.Context("POST", body: "x");
        context.Dispose();
        context.Dispose();
    }
}
