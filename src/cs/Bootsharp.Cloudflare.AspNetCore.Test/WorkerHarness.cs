using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bootsharp.Cloudflare.AspNetCore.Tests;

/// <summary>A payload with source-generated metadata, for the results that serialize one.</summary>
public sealed record Todo (int Id, string Title);

/// <summary>The only way a type becomes serializable here: there is no reflective resolver.</summary>
/// <remarks>The naming policy is stated rather than left to the generator's default so that the two
/// paths through the writer agree: metadata resolved from the request's <see cref="JsonOptions"/>
/// takes those options' web defaults, while metadata handed to
/// <c>TypedResults.Json</c> directly carries this context's.</remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(Todo))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
internal sealed partial class TestJson : JsonSerializerContext;

/// <summary>
/// The snapshot the JavaScript side would receive, decomposed for assertions.
/// </summary>
/// <remarks>Headers are compared through the rendered JSON rather than the dictionary, because the
/// rendering is the part that crosses the boundary — a header the dictionary holds but the renderer
/// drops would otherwise pass unnoticed.</remarks>
internal readonly record struct Answer (int Status, string HeadersJson, string Body, bool PassedThrough = false)
{
    /// <summary>The single value rendered under this name, or null when the name is absent.</summary>
    /// <remarks>Returns null for a name that rendered as an array too: <see cref="Headers"/> is the
    /// accessor for those, and silently taking the first of several would hide the case this wire
    /// shape exists for.</remarks>
    public string? Header (string name) =>
        Find(name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    /// <summary>Every value rendered under this name, whether it rendered as a string or an array.</summary>
    public string?[] Headers (string name) => Find(name) switch {
        { ValueKind: JsonValueKind.String } value => [value.GetString()],
        { ValueKind: JsonValueKind.Array } array => [..array.EnumerateArray().Select(static v => v.GetString())],
        _ => []
    };

    private JsonElement? Find (string name)
    {
        using var document = JsonDocument.Parse(HeadersJson);
        foreach (var property in document.RootElement.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value.Clone();
        return null;
    }
}

/// <summary>A workerd <c>Request</c> stand-in: the values the real handle exposes.</summary>
internal sealed class FakeRequest (string method, string url, string body = "", string headersJson = "{}") : IJsRequest
{
    public string Method => method;
    public string Url => url;
    public string HeadersJson => headersJson;
    public string? CfJson => null;
    public Task<string> Text () => Task.FromResult(body);
    public Task<byte[]> Bytes () => Task.FromResult(Encoding.UTF8.GetBytes(body));
}

/// <summary>
/// Builds the pieces of the runtime under test: a service provider carrying the JSON metadata a
/// result needs, contexts over a fake event, endpoint tables for the matcher, and applications the
/// whole invocation can be driven through.
/// </summary>
internal static class Worker
{
    /// <summary>A provider with the one service every value-carrying result resolves.</summary>
    public static IServiceProvider Services (Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOptions<JsonOptions>>(
            Options.Create(new JsonOptions().AddContext(TestJson.Default)));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    /// <summary>A context over one fake event.</summary>
    public static WorkerHttpContext Context (
        string method = "GET", string url = "https://w.dev/", string headersJson = "{}",
        string body = "", IServiceProvider? services = null) =>
        new(method, url, headersJson, Encoding.UTF8.GetBytes(body), services ?? Services());

    /// <summary>Executes a result against a fresh context and reads what it wrote.</summary>
    /// <remarks>The status and headers come off the response, the body off the buffer the response
    /// writes into — which is the same buffer the snapshot is rendered from.</remarks>
    public static async Task<Answer> Execute (IResult result, IServiceProvider? services = null)
    {
        using var context = Context(services: services);
        await result.ExecuteAsync(context);
        return new Answer(context.Response.StatusCode, HeaderJson.Render(context.Response.Headers), Body(context));
    }

    /// <summary>The bytes a handler wrote to the response, decoded.</summary>
    public static string Body (HttpContext context) =>
        Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    /// <summary>An application whose endpoints are registered by hand.</summary>
    /// <remarks>
    /// <see cref="RouteHandlerServices.Map(IEndpointRouteBuilder, string, RequestDelegate, IEnumerable{string}?, string?)"/>
    /// is the overload that takes an already-bound delegate — the seam that exists for registration
    /// the generator did not perform, which is exactly what a runtime unit test needs.
    /// </remarks>
    public static WebApplication App (Action<WebApplication> map, Action<IServiceCollection>? configure = null)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddSingleton<IOptions<JsonOptions>>(
            Options.Create(new JsonOptions().AddContext(TestJson.Default)));
        configure?.Invoke(builder.Services);
        var app = builder.Build();
        map(app);
        return app;
    }

    /// <summary>Registers a handler that answers with the given text.</summary>
    public static RouteHandlerBuilder Says (this WebApplication app, string pattern, string text, params string[] methods) =>
        RouteHandlerServices.Map(app, pattern, context => context.Response.WriteAsync(text),
            methods.Length == 0 ? null : methods);

    /// <summary>Drives one event through an application.</summary>
    /// <remarks>The snapshot's body is bytes — that is the whole point of the byte channel — so the
    /// assertions read it decoded, and <see cref="Bytes"/> is what a binary case asserts on.</remarks>
    public static async Task<Answer> Send (this WebApplication app,
        string method, string url, string body = "", string headersJson = "{}")
    {
        var response = await app.InvokeAsync(new FakeRequest(method, url, body, headersJson));
        return new Answer(response.Status, response.HeadersJson, Text(response), response.PassThroughToAssets);
    }

    /// <summary>The bytes a snapshot carries, whichever half of the body it used.</summary>
    public static byte[] Bytes (HttpResponseData response) =>
        response.BodyBytes ?? Encoding.UTF8.GetBytes(response.Body);

    /// <summary>The snapshot's body, decoded.</summary>
    public static string Text (HttpResponseData response) => Encoding.UTF8.GetString(Bytes(response));

    /// <summary>
    /// An endpoint table built without the application, for matcher tests that need to state the
    /// registration order and the precedence metadata explicitly.
    /// </summary>
    public static RouteEndpoint Endpoint (string pattern, string[]? methods = null,
        int order = 0, decimal? precedence = null)
    {
        var builder = new RouteEndpointBuilder(
            static _ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), order) {
            DisplayName = pattern
        };
        if (methods is not null) builder.Metadata.Add(new HttpMethodMetadata(methods));
        if (precedence is { } value) builder.Metadata.Add(new RoutePrecedenceMetadata(value));
        return (RouteEndpoint)builder.Build();
    }

    /// <summary>The matcher over a table stated in registration order.</summary>
    public static WorkerRouteMatcher Matcher (params RouteEndpoint[] endpoints) => new(endpoints);
}
