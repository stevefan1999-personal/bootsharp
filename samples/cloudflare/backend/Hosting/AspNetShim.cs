using Microsoft.Extensions.DependencyInjection;

namespace Cloudflare.Backend.Hosting;

/// <summary>
/// Minimal ASP.NET Core-shaped host for NativeAOT-LLVM WASM.
/// Kestrel cannot run in a V8 isolate (no sockets, no thread pool), so this
/// shim owns routing and DI while keeping Minimal API muscle memory.
/// </summary>
public sealed class WebApplicationBuilder
{
    public IServiceCollection Services { get; } = new ServiceCollection();

    public WebApplication Build() => new(Services.BuildServiceProvider());
}

public interface IResult
{
    HttpResponseData ToResponse();
}

public sealed class HttpContext
{
    public required HttpRequestData Request { get; init; }
    public required IServiceProvider RequestServices { get; init; }
    public Dictionary<string, string> RouteValues { get; } = new(StringComparer.OrdinalIgnoreCase);

    public T GetRequiredService<T>() where T : notnull => RequestServices.GetRequiredService<T>();
}

public sealed class WebApplication
{
    private readonly List<Route> _routes = [];

    public IServiceProvider Services { get; }

    public WebApplication(IServiceProvider services) => Services = services;

    public static WebApplicationBuilder CreateSlimBuilder() => new();

    public WebApplication MapGet(string pattern, Func<HttpContext, Task<IResult>> handler)
        => Map("GET", pattern, handler);

    public WebApplication MapGet<TService>(string pattern, Func<TService, HttpContext, Task<IResult>> handler)
        where TService : class
        => Map("GET", pattern, ctx => handler(ctx.GetRequiredService<TService>(), ctx));

    public WebApplication MapPost(string pattern, Func<HttpContext, Task<IResult>> handler)
        => Map("POST", pattern, handler);

    public WebApplication MapPost<TService>(string pattern, Func<TService, HttpContext, Task<IResult>> handler)
        where TService : class
        => Map("POST", pattern, ctx => handler(ctx.GetRequiredService<TService>(), ctx));

    public WebApplication MapPut(string pattern, Func<HttpContext, Task<IResult>> handler)
        => Map("PUT", pattern, handler);

    public WebApplication MapDelete(string pattern, Func<HttpContext, Task<IResult>> handler)
        => Map("DELETE", pattern, handler);

    public async Task<HttpResponseData> InvokeAsync(HttpRequestData request)
    {
        using var scope = Services.CreateScope();
        var ctx = new HttpContext { Request = request, RequestServices = scope.ServiceProvider };
        foreach (var route in _routes)
        {
            if (!string.Equals(route.Method, request.Method, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!route.TryMatch(request.Path, ctx.RouteValues))
                continue;
            return (await route.Handler(ctx)).ToResponse();
        }
        return Results.NotFound($"No route for {request.Method} {request.Path}").ToResponse();
    }

    private WebApplication Map(string method, string pattern, Func<HttpContext, Task<IResult>> handler)
    {
        _routes.Add(new Route(method, pattern, handler));
        return this;
    }

    private sealed record Route(string Method, string Pattern, Func<HttpContext, Task<IResult>> Handler)
    {
        public bool TryMatch(string path, Dictionary<string, string> values)
        {
            values.Clear();
            var pathParts = Split(path);
            var patternParts = Split(Pattern);
            if (pathParts.Length != patternParts.Length)
                return false;
            for (var i = 0; i < patternParts.Length; i++)
            {
                var part = patternParts[i];
                if (part.StartsWith('{') && part.EndsWith('}'))
                    values[part[1..^1]] = Uri.UnescapeDataString(pathParts[i]);
                else if (!string.Equals(part, pathParts[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }

        private static string[] Split(string value)
        {
            var trimmed = value.Trim('/');
            return trimmed.Length == 0 ? [] : trimmed.Split('/');
        }
    }
}

public static class Results
{
    public static IResult Html(string html) => new ContentResult(200, html, "text/html; charset=utf-8");
    public static IResult Text(string text, int status = 200) => new ContentResult(status, text, "text/plain; charset=utf-8");
    public static IResult Json(string json, int status = 200) => new ContentResult(status, json, "application/json; charset=utf-8");
    public static IResult Redirect(string location) => new HeaderResult(303, "text/plain; charset=utf-8", "Redirected", ("Location", location));
    public static IResult NotFound(string message = "Not found") => new ContentResult(404, message, "text/plain; charset=utf-8");
    public static IResult BadRequest(string message) => new ContentResult(400, message, "text/plain; charset=utf-8");
    public static IResult Assets() => new ContentResult(0, string.Empty, "application/octet-stream");

    private sealed record ContentResult(int Status, string Body, string ContentType) : IResult
    {
        public HttpResponseData ToResponse() => new(Status, "{\"content-type\":\"" + ContentType + "\"}", Body);
    }

    private sealed record HeaderResult(int Status, string ContentType, string Body, (string Key, string Value) Extra) : IResult
    {
        public HttpResponseData ToResponse() =>
            new(Status, "{\"content-type\":\"" + ContentType + "\",\"" + Extra.Key.ToLowerInvariant() + "\":\"" + Extra.Value + "\"}", Body);
    }
}
