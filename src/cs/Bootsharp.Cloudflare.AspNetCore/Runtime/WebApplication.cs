using System.Diagnostics.CodeAnalysis;
using Bootsharp.Cloudflare;
using Bootsharp.Cloudflare.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Configures the services and middleware of a worker's HTTP application.
/// </summary>
/// <remarks>
/// API-shape only. Upstream's <c>WebApplicationBuilder</c> lives in an assembly that references
/// Kestrel, IIS, HostFiltering, Diagnostics, Cors, Authentication, generic Hosting and five
/// configuration providers — none of which can exist in workerd. What survives is
/// what users actually type: <see cref="Services"/>, <see cref="Logging"/>,
/// <see cref="Build"/>. <c>Configuration</c> and <c>Environment</c> are absent rather than faked:
/// a worker's configuration is its <c>env</c> bindings, reachable through
/// <see cref="WorkerContext{TEnv}"/>, and modelling that as an <c>IConfiguration</c> chain would
/// be a different shape wearing the name.
/// </remarks>
public sealed class WebApplicationBuilder
{
    internal WebApplicationBuilder ()
    {
        // Registered up front so that a JSON result can resolve metadata without the app having to
        // know it needed to; AddContext is how a generated context joins the resolver chain.
        Services.AddSingleton<IOptions<JsonOptions>>(new StaticOptions<JsonOptions>(new JsonOptions()));
    }

    /// <summary>Services available to endpoints and middleware.</summary>
    public IServiceCollection Services { get; } = new ServiceCollection();

    /// <summary>Logging configuration.</summary>
    /// <remarks>
    /// A builder over <see cref="Services"/>, deliberately without <c>AddLogging</c>: that call
    /// resolves filters through the options and configuration binders, whose reflection NativeAOT
    /// cannot honour. Register an <see cref="ILoggerProvider"/>, an
    /// <see cref="ILoggerFactory"/> and the open generic <c>ILogger&lt;&gt;</c> directly — which is
    /// what <c>Bootsharp.Cloudflare</c>'s JSON logger does.
    /// </remarks>
    public ILoggingBuilder Logging => new WorkerLoggingBuilder(Services);

    /// <summary>Builds the application.</summary>
    public WebApplication Build () => new(Services.BuildServiceProvider());

    private sealed class WorkerLoggingBuilder (IServiceCollection services) : ILoggingBuilder
    {
        public IServiceCollection Services => services;
    }

    // IOptions<T> annotates T with PublicParameterlessConstructor for the binder paths this package
    // never uses; the annotation is repeated rather than suppressed so the trim analyzer stays
    // meaningful for everything else in the file.
    private sealed class StaticOptions<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T
    > (T value) : IOptions<T> where T : class
    {
        public T Value { get; } = value;
    }
}

/// <summary>
/// The worker's HTTP application: a middleware pipeline terminating in endpoint dispatch.
/// </summary>
/// <remarks>
/// <para>
/// Implements <see cref="IApplicationBuilder"/> and <see cref="IEndpointRouteBuilder"/>, as
/// upstream's does; <c>IHost</c> is not implemented, because there is no process whose lifetime
/// could be started or stopped — workerd owns that. <c>Urls</c> is likewise absent: it is backed by
/// <c>IServerAddressesFeature</c>, and a worker does not choose its address.
/// </para>
/// <para>
/// <see cref="Run()"/> does not block. In ASP.NET Core it starts a server and waits; here it seals
/// the endpoint table and builds the pipeline, so that the same line that ends a Program.cs means
/// the same thing — "the app is now ready to serve" — without pretending to own a thread.
/// </para>
/// </remarks>
public sealed class WebApplication : IApplicationBuilder, IEndpointRouteBuilder
{
    private readonly List<Func<RequestDelegate, RequestDelegate>> middleware = [];
    private readonly WorkerEndpointDataSource endpoints = new();
    private readonly ILogger<WebApplication>? logger;
    private RequestDelegate? pipeline;
    private WorkerRouteMatcher? matcher;

    internal WebApplication (ServiceProvider services)
    {
        Services = services;
        DataSources = [endpoints];
        logger = services.GetService<ILogger<WebApplication>>();
    }

    /// <summary>Starts configuring a worker application.</summary>
    /// <remarks>Named for the ASP.NET Core call it stands in for. There is no non-slim builder to
    /// contrast with here — every worker application is slim by construction.</remarks>
    public static WebApplicationBuilder CreateSlimBuilder () => new();

    /// <summary>The application's services.</summary>
    public IServiceProvider Services { get; }

    /// <summary>The endpoint sources this application dispatches from.</summary>
    public ICollection<EndpointDataSource> DataSources { get; }

    IServiceProvider IEndpointRouteBuilder.ServiceProvider => Services;
    IServiceProvider IApplicationBuilder.ApplicationServices { get => Services; set => throw new NotSupportedException(); }
    IFeatureCollection IApplicationBuilder.ServerFeatures { get; } = new FeatureCollection();
    IDictionary<string, object?> IApplicationBuilder.Properties { get; } = new Dictionary<string, object?>();

    /// <summary>Adds a middleware to the pipeline.</summary>
    public IApplicationBuilder Use (Func<RequestDelegate, RequestDelegate> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        if (pipeline is not null)
            throw new InvalidOperationException("Middleware cannot be added after the application has started serving.");
        this.middleware.Add(middleware);
        return this;
    }

    IApplicationBuilder IApplicationBuilder.New () =>
        throw new NotSupportedException("Branching the middleware pipeline is not supported on Cloudflare Workers.");

    RequestDelegate IApplicationBuilder.Build () => BuildPipeline();

    /// <summary>Seals the endpoint table and builds the pipeline.</summary>
    /// <remarks>The non-blocking counterpart of <c>app.Run()</c>: see the type's remarks.</remarks>
    public void Run () => BuildPipeline();

    /// <summary>Handles one workerd fetch event.</summary>
    /// <remarks>
    /// <para>
    /// The whole event is one DI scope, opened here and disposed in the <c>finally</c> that also
    /// disposes the <see cref="HttpContext"/>. That <c>finally</c> runs inside
    /// <c>js/runtime.mjs</c>'s own <c>scoped()</c> wrapper, which releases the JS handles imported
    /// during the same invocation — so the.NET scope can never outlive the handles it was built
    /// over, and no new seam in <c>Bootsharp.Cloudflare</c> is needed to arrange that.
    /// </para>
    /// <para>
    /// An exception escaping the pipeline becomes an opaque 500 and a logged error. Nothing is read
    /// off the request to build that response: the request handle may be exactly what failed.
    /// </para>
    /// </remarks>
    public async Task<HttpResponseData> InvokeAsync (IJsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var handle = BuildPipeline();
        await using var scope = Services.CreateAsyncScope();
        WorkerHttpContext? context = null;
        try
        {
            var method = request.Method;
            var body = WorkerHttpContext.CanHaveBody(method) ? await request.Text() : string.Empty;
            context = new WorkerHttpContext(method, request.Url, request.HeadersJson, body, scope.ServiceProvider);
            await handle(context);
            return await context.SnapshotAsync();
        }
        catch (Exception exception)
        {
            logger?.LogError(exception, "Unhandled exception handling a worker request.");
            return new HttpResponseData(
                StatusCodes.Status500InternalServerError,
                "{\"content-type\":\"text/plain; charset=utf-8\"}",
                "Internal Server Error");
        }
        finally
        {
            context?.Dispose();
        }
    }

    private RequestDelegate BuildPipeline ()
    {
        if (pipeline is not null) return pipeline;
        matcher = new WorkerRouteMatcher(endpoints.Endpoints);
        RequestDelegate handle = DispatchAsync;
        for (var index = middleware.Count - 1; index >= 0; index--)
            handle = middleware[index](handle);
        return pipeline = handle;
    }

    /// <summary>
    /// The terminal middleware: match, then invoke.
    /// </summary>
    /// <remarks>
    /// ASP.NET Core splits this across <c>EndpointRoutingMiddleware</c> and <c>EndpointMiddleware</c>
    /// so that middleware registered between them can see the matched endpoint. There is nothing to
    /// register between them here, and the pair carries ~350 lines of metrics,
    /// <c>DiagnosticListener</c>, and a reflective assembly-public-key check on top of ~50 lines of
    /// real logic — so the real logic is what is reproduced.
    /// </remarks>
    private async Task DispatchAsync (HttpContext httpContext)
    {
        var match = matcher!.Find(httpContext.Request.Path, httpContext.Request.Method);
        if (match.Endpoint is null)
        {
            httpContext.Response.StatusCode = match.AllowedMethods is { Length: > 0 } allowed
                ? Allow(httpContext, allowed)
                : StatusCodes.Status404NotFound;
            return;
        }
        httpContext.Request.RouteValues = match.RouteValues;
        httpContext.Features.Get<IEndpointFeature>()!.Endpoint = match.Endpoint;
        await match.Endpoint.RequestDelegate!(httpContext);
    }

    private static int Allow (HttpContext httpContext, string[] allowed)
    {
        httpContext.Response.Headers.Allow = string.Join(", ", allowed);
        return StatusCodes.Status405MethodNotAllowed;
    }
}
