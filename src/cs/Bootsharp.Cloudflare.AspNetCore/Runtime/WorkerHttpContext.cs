using System.Globalization;
using System.IO.Pipelines;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Primitives;

namespace Bootsharp.Cloudflare.AspNetCore;

/// <summary>
/// <see cref="HttpContext"/> over one workerd fetch event.
/// </summary>
/// <remarks>
/// <para>
/// The lifetime is the event, not the object: <see cref="HttpRequest.Body"/> and the response
/// buffer are only meaningful while workerd is still willing to do I/O on behalf of this request,
/// so the context is disposed in the same <c>finally</c> that ends the invocation and every member
/// throws <see cref="ObjectDisposedException"/> afterwards — ASP.NET
/// Core has no equivalent concern, because Kestrel owns the socket.
/// </para>
/// <para>
/// Feature storage is the real <see cref="FeatureCollection"/> from
/// <c>Microsoft.Extensions.Features</c>: that assembly ships as a RID-agnostic package, so
/// identity rule says reference it rather than vendor a look-alike.
/// </para>
/// </remarks>
public sealed class WorkerHttpContext : HttpContext, IDisposable
{
    private readonly FeatureCollection features = new(featuresCapacity);
    private readonly WorkerRequestFeature requestFeature = new();
    private readonly WorkerResponseFeature responseFeature = new();
    private readonly WorkerRequestStateFeature stateFeature = new();
    private readonly WorkerHttpRequest request;
    private readonly WorkerHttpResponse response;
    private bool disposed;

    // Request, response, response body, body detection, and the six state slots — the practical
    // minimum identifies, with no room reserved for features nothing sets.
    private const int featuresCapacity = 11;

    /// <summary>Builds a context from the snapshot taken off the live workerd request.</summary>
    /// <param name="method">HTTP method, e.g. <c>GET</c>.</param>
    /// <param name="url">Absolute request URL as workerd reports it.</param>
    /// <param name="headersJson">Request headers as a flat JSON object.</param>
    /// <param name="body">Request body, already buffered. Empty for bodyless methods.</param>
    /// <param name="services">The request's service scope.</param>
    public WorkerHttpContext (string method, string url, string? headersJson, string body, IServiceProvider services)
    {
        var uri = new Uri(url, UriKind.Absolute);
        requestFeature.Method = method;
        requestFeature.Scheme = uri.Scheme;
        requestFeature.Path = uri.AbsolutePath;
        requestFeature.QueryString = uri.Query;
        requestFeature.RawTarget = uri.PathAndQuery;
        requestFeature.Headers = HeaderJson.Parse(headersJson);
        requestFeature.Body = body.Length == 0 ? Stream.Null : new MemoryStream(Encoding.UTF8.GetBytes(body), writable: false);
        requestFeature.CanHaveBody = CanHaveBody(method);
        stateFeature.RequestServices = services;
        stateFeature.TraceIdentifier = TraceIdentifierOf(requestFeature.Headers);
        stateFeature.Query = QueryStringParser.Parse(uri.Query);
        features.Set<IHttpRequestFeature>(requestFeature);
        features.Set<IHttpRequestBodyDetectionFeature>(requestFeature);
        features.Set<IHttpResponseFeature>(responseFeature);
        features.Set<IHttpResponseBodyFeature>(responseFeature);
        features.Set<IItemsFeature>(stateFeature);
        features.Set<IQueryFeature>(stateFeature);
        features.Set<IRouteValuesFeature>(stateFeature);
        features.Set<IEndpointFeature>(stateFeature);
        features.Set<IServiceProvidersFeature>(stateFeature);
        features.Set<IHttpRequestLifetimeFeature>(stateFeature);
        features.Set<IHttpRequestIdentifierFeature>(stateFeature);
        request = new WorkerHttpRequest(this, requestFeature, stateFeature);
        response = new WorkerHttpResponse(this, responseFeature);
    }

    /// <summary>
    /// Whether a request with this method carries a body worth reading off the JS side.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The single answer to that question in this package. It seeds
    /// <see cref="IHttpRequestBodyDetectionFeature.CanHaveBody"/>, and
    /// <see cref="WebApplication.InvokeAsync"/> uses the same predicate to decide whether to spend an
    /// interop round-trip on <c>request.Text()</c> — so a method the context declares bodyless is
    /// never buffered, and one it declares body-carrying is never left empty. They were two lists
    /// once, and DELETE fell between them: buffered on the way in, unreadable on arrival.
    /// </para>
    /// <para>
    /// "Has a body" is the transfer-level question, not "did the client send bytes": a POST with an
    /// empty body must still bind an empty JSON document rather than be treated as bodyless.
    /// </para>
    /// </remarks>
    public static bool CanHaveBody (string method) =>
        !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsDelete(method) &&
        !HttpMethods.IsOptions(method) && !HttpMethods.IsTrace(method);

    /// <summary>
    /// The request's trace identifier: Cloudflare's own ray id when it reached us, else a fresh one.
    /// </summary>
    /// <remarks>ASP.NET Core mints an identifier per request from the connection id and a counter.
    /// A worker has no connection to count within, but every request through Cloudflare's edge
    /// carries <c>CF-Ray</c>, which is the id the dashboard and the logs already join on — so it is
    /// worth more here than a number of our own. The fallback covers direct invocation in tests and
    /// `wrangler dev --local`, where no ray is assigned.</remarks>
    private static string TraceIdentifierOf (IHeaderDictionary headers) =>
        headers.TryGetValue("cf-ray", out var ray) && !StringValues.IsNullOrEmpty(ray)
            ? ray.ToString()
            : Interlocked.Increment(ref traceCounter).ToString(CultureInfo.InvariantCulture);

    private static long traceCounter;

    public override IFeatureCollection Features => Live().features;
    public override HttpRequest Request => Live().request;
    public override HttpResponse Response => Live().response;
    public override IDictionary<object, object?> Items { get => Live().stateFeature.Items; set => Live().stateFeature.Items = value; }
    public override IServiceProvider RequestServices { get => Live().stateFeature.RequestServices; set => Live().stateFeature.RequestServices = value; }
    public override CancellationToken RequestAborted { get => Live().stateFeature.RequestAborted; set => Live().stateFeature.RequestAborted = value; }
    public override string TraceIdentifier { get => Live().stateFeature.TraceIdentifier; set => Live().stateFeature.TraceIdentifier = value; }

    /// <summary>Not available: workerd terminates the connection at the edge.</summary>
    /// <remarks>Client IP, TLS version and colo arrive as <c>request.cf</c> properties and as
    /// <c>CF-Connecting-IP</c>, both reachable through <see cref="HttpRequest.Headers"/>.</remarks>
    public override ConnectionInfo Connection => throw new PlatformNotSupportedException(
        "HttpContext.Connection is not available on Cloudflare Workers: the connection terminates at " +
        "the edge. Read CF-Connecting-IP and the request.cf properties from the request headers instead.");

    /// <summary>Not available in this package.</summary>
    /// <remarks>WebSockets are their own layer, because the Workers-native shape is a
    /// Durable Object hibernation socket rather than an upgrade on the fetch event.</remarks>
    public override WebSocketManager WebSockets => throw new PlatformNotSupportedException(
        "HttpContext.WebSockets requires the WebSocket layer, which is not part of " +
        "Bootsharp.Cloudflare.AspNetCore's core surface.");

    /// <summary>Not available until an authentication layer exists.</summary>
    public override ClaimsPrincipal User
    {
        get => throw new PlatformNotSupportedException(
            "HttpContext.User requires an authentication layer, which Bootsharp.Cloudflare.AspNetCore " +
            "does not ship yet. Read the credential off the request headers and authorise explicitly.");
        set => throw new PlatformNotSupportedException("HttpContext.User requires an authentication layer.");
    }

    /// <summary>Not available: a worker has no server-side session store.</summary>
    public override ISession Session
    {
        get => throw new PlatformNotSupportedException(
            "HttpContext.Session is not available on Cloudflare Workers. Keep session state in KV, " +
            "D1 or a Durable Object.");
        set => throw new PlatformNotSupportedException("HttpContext.Session is not available on Cloudflare Workers.");
    }

    public override void Abort () => Live().stateFeature.Abort();

    /// <summary>Runs the response callbacks and renders the snapshot handed back to JavaScript.</summary>
    internal Task<HttpResponseData> SnapshotAsync () => Live().responseFeature.SnapshotAsync();

    /// <summary>
    /// Ends the context. Called from the invocation's <c>finally</c>, so it runs on the failure path
    /// too — the point being that no reference to this context can outlive the event that owns it.
    /// </summary>
    public void Dispose ()
    {
        if (disposed) return;
        disposed = true;
        requestFeature.Body.Dispose();
    }

    private WorkerHttpContext Live ()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return this;
    }
}

/// <summary>The incoming half of <see cref="WorkerHttpContext"/>.</summary>
internal sealed class WorkerHttpRequest (
    WorkerHttpContext context,
    WorkerRequestFeature feature,
    WorkerRequestStateFeature state) : HttpRequest
{
    public override HttpContext HttpContext => context;
    public override string Method { get => feature.Method; set => feature.Method = value; }
    public override string Scheme { get => feature.Scheme; set => feature.Scheme = value; }
    public override bool IsHttps { get => string.Equals(feature.Scheme, "https", StringComparison.OrdinalIgnoreCase); set => feature.Scheme = value ? "https" : "http"; }
    public override string Protocol { get => feature.Protocol; set => feature.Protocol = value; }
    public override IHeaderDictionary Headers => feature.Headers;
    public override PathString PathBase { get => new(feature.PathBase); set => feature.PathBase = value.Value ?? string.Empty; }
    public override PathString Path { get => new(feature.Path); set => feature.Path = value.Value ?? string.Empty; }
    public override QueryString QueryString { get => new(feature.QueryString); set => feature.QueryString = value.Value ?? string.Empty; }
    public override IQueryCollection Query { get => state.Query; set => state.Query = value; }
    public override RouteValueDictionary RouteValues { get => state.RouteValues; set => state.RouteValues = value; }
    public override Stream Body { get => feature.Body; set => feature.Body = value; }
    public override PipeReader BodyReader => PipeReader.Create(feature.Body, new StreamPipeReaderOptions(leaveOpen: true));

    public override HostString Host
    {
        get => new(feature.Headers.Host.ToString());
        set => feature.Headers.Host = value.Value;
    }

    public override long? ContentLength
    {
        get => feature.Headers.ContentLength;
        set => feature.Headers.ContentLength = value;
    }

    public override string? ContentType
    {
        get => feature.Headers.ContentType;
        set => feature.Headers.ContentType = value;
    }

    /// <summary>Not available in this package.</summary>
    /// <remarks>Cookie parsing is cheap but only useful with the response half, which needs the
    /// JS side to accept repeated <c>Set-Cookie</c> headers; both land together or not at all.</remarks>
    public override IRequestCookieCollection Cookies
    {
        get => throw new PlatformNotSupportedException(
            "HttpRequest.Cookies is not implemented yet: representing repeated Set-Cookie headers " +
            "needs a change on the JavaScript side of the response snapshot. Read the Cookie header directly.");
        set => throw new PlatformNotSupportedException("HttpRequest.Cookies is not implemented yet.");
    }

    /// <summary>Always false: form parsing is the <c>.Forms</c> layer's job.</summary>
    public override bool HasFormContentType => false;

    /// <summary>Not available in this package.</summary>
    /// <remarks>The multipart reader lives in <c>Microsoft.AspNetCore.WebUtilities</c>, which the
    /// verdicts put in a separate opt-in package so that an app not accepting uploads does not
    /// pay for one.</remarks>
    public override IFormCollection Form
    {
        get => throw new PlatformNotSupportedException(FormReason);
        set => throw new PlatformNotSupportedException(FormReason);
    }

    public override Task<IFormCollection> ReadFormAsync (CancellationToken cancellationToken = default) =>
        throw new PlatformNotSupportedException(FormReason);

    private const string FormReason =
        "Form binding requires the Bootsharp.Cloudflare.AspNetCore.Forms layer, which is not part " +
        "of the core package.";
}

/// <summary>The outgoing half of <see cref="WorkerHttpContext"/>.</summary>
internal sealed class WorkerHttpResponse (WorkerHttpContext context, WorkerResponseFeature feature) : HttpResponse
{
    public override HttpContext HttpContext => context;
    public override int StatusCode { get => feature.StatusCode; set => feature.StatusCode = value; }
    public override IHeaderDictionary Headers => feature.Headers;
    public override Stream Body { get => ((IHttpResponseBodyFeature)feature).Stream; set => feature.Body = value; }
    public override PipeWriter BodyWriter => feature.Writer;
    public override bool HasStarted => feature.HasStarted;

    public override long? ContentLength
    {
        get => feature.Headers.ContentLength;
        set => feature.Headers.ContentLength = value;
    }

    public override string? ContentType
    {
        get => feature.Headers.ContentType;
        set => feature.Headers.ContentType = value;
    }

    /// <summary>Not available in this package — see <see cref="WorkerHttpRequest.Cookies"/>.</summary>
    public override IResponseCookies Cookies => throw new PlatformNotSupportedException(
        "HttpResponse.Cookies is not implemented yet: representing repeated Set-Cookie headers needs " +
        "a change on the JavaScript side of the response snapshot. Set the Set-Cookie header directly.");

    /// <inheritdoc cref="WorkerResponseFeature.StartAsync"/>
    public override Task StartAsync (CancellationToken cancellationToken = default) => feature.StartAsync(cancellationToken);

    /// <inheritdoc cref="WorkerResponseFeature.CompleteAsync"/>
    public override Task CompleteAsync () => feature.CompleteAsync();

    public override void OnStarting (Func<object, Task> callback, object state) => feature.OnStarting(callback, state);
    public override void OnCompleted (Func<object, Task> callback, object state) => feature.OnCompleted(callback, state);

    public override void Redirect (string location, bool permanent)
    {
        StatusCode = permanent ? StatusCodes.Status301MovedPermanently : StatusCodes.Status302Found;
        Headers.Location = location;
    }
}
