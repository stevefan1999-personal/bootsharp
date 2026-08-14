using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Bootsharp.Cloudflare.AspNetCore;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Microsoft.AspNetCore.Http.HttpResults;

/// <summary>Writes a JSON body through source-generated metadata.</summary>
/// <remarks>
/// Every value-carrying result funnels through here, so the "which metadata?" question is
/// answered in exactly one place. A result created with an explicit
/// <see cref="JsonTypeInfo{T}"/> uses it; one created from a bare value resolves it from the
/// request's <see cref="JsonOptions"/> at write time, and says which type is missing when the
/// resolver chain has no entry — the failure ASP.NET Core reports as an opaque first-request
/// 500.
/// </remarks>
internal static class JsonBody
{
    internal static async Task WriteAsync<TValue> (HttpContext httpContext, TValue? value, JsonTypeInfo<TValue>? jsonTypeInfo, string? contentType, int? statusCode)
    {
        if (statusCode.HasValue) httpContext.Response.StatusCode = statusCode.Value;
        httpContext.Response.ContentType = contentType ?? "application/json; charset=utf-8";
        if (value is null) return;
        var typeInfo = jsonTypeInfo ?? Resolve<TValue>(httpContext);
        await JsonSerializer.SerializeAsync(httpContext.Response.Body, value, typeInfo, httpContext.RequestAborted);
    }

    private static JsonTypeInfo<TValue> Resolve<TValue> (HttpContext httpContext)
    {
        var options = httpContext.RequestServices.GetService<IOptions<JsonOptions>>()?.Value
            ?? throw new InvalidOperationException(
                "JsonOptions is not registered. WebApplicationBuilder registers it; a hand-built " +
                "service provider has to register it too.");
        return options.GetTypeInfo<TValue>();
    }
}

/// <summary>200 OK with no body.</summary>
public sealed class Ok : IResult, IStatusCodeHttpResult
{
    internal static readonly Ok Instance = new();
    public int StatusCode => StatusCodes.Status200OK;
    public Task ExecuteAsync (HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCode;
        return Task.CompletedTask;
    }
}

/// <summary>200 OK with a JSON body.</summary>
public sealed class Ok<TValue> (TValue? value) : IResult, IStatusCodeHttpResult, IValueHttpResult<TValue>
{
    public TValue? Value { get; } = value;
    public int StatusCode => StatusCodes.Status200OK;
    public Task ExecuteAsync (HttpContext httpContext) => JsonBody.WriteAsync(httpContext, Value, null, null, StatusCode);
}

/// <summary>204 No Content.</summary>
public sealed class NoContent : IResult, IStatusCodeHttpResult
{
    internal static readonly NoContent Instance = new();
    public int StatusCode => StatusCodes.Status204NoContent;
    public Task ExecuteAsync (HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCode;
        return Task.CompletedTask;
    }
}

/// <summary>404 Not Found.</summary>
public sealed class NotFound : IResult, IStatusCodeHttpResult
{
    internal static readonly NotFound Instance = new();
    public int StatusCode => StatusCodes.Status404NotFound;
    public Task ExecuteAsync (HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCode;
        return Task.CompletedTask;
    }
}

/// <summary>404 Not Found with a JSON body.</summary>
public sealed class NotFound<TValue> (TValue? value) : IResult, IStatusCodeHttpResult, IValueHttpResult<TValue>
{
    public TValue? Value { get; } = value;
    public int StatusCode => StatusCodes.Status404NotFound;
    public Task ExecuteAsync (HttpContext httpContext) => JsonBody.WriteAsync(httpContext, Value, null, null, StatusCode);
}

/// <summary>400 Bad Request.</summary>
public sealed class BadRequest : IResult, IStatusCodeHttpResult
{
    internal static readonly BadRequest Instance = new();
    public int StatusCode => StatusCodes.Status400BadRequest;
    public Task ExecuteAsync (HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCode;
        return Task.CompletedTask;
    }
}

/// <summary>400 Bad Request with a JSON body.</summary>
public sealed class BadRequest<TValue> (TValue? value) : IResult, IStatusCodeHttpResult, IValueHttpResult<TValue>
{
    public TValue? Value { get; } = value;
    public int StatusCode => StatusCodes.Status400BadRequest;
    public Task ExecuteAsync (HttpContext httpContext) => JsonBody.WriteAsync(httpContext, Value, null, null, StatusCode);
}

/// <summary>409 Conflict.</summary>
public sealed class Conflict : IResult, IStatusCodeHttpResult
{
    internal static readonly Conflict Instance = new();
    public int StatusCode => StatusCodes.Status409Conflict;
    public Task ExecuteAsync (HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCode;
        return Task.CompletedTask;
    }
}

/// <summary>422 Unprocessable Entity.</summary>
public sealed class UnprocessableEntity : IResult, IStatusCodeHttpResult
{
    internal static readonly UnprocessableEntity Instance = new();
    public int StatusCode => StatusCodes.Status422UnprocessableEntity;
    public Task ExecuteAsync (HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCode;
        return Task.CompletedTask;
    }
}

/// <summary>201 Created with a <c>Location</c> header.</summary>
public sealed class Created (string? location) : IResult, IStatusCodeHttpResult
{
    public string? Location { get; } = location;
    public int StatusCode => StatusCodes.Status201Created;
    public Task ExecuteAsync (HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCode;
        if (Location is not null) httpContext.Response.Headers.Location = Location;
        return Task.CompletedTask;
    }
}

/// <summary>201 Created with a <c>Location</c> header and a JSON body.</summary>
public sealed class Created<TValue> (string? location, TValue? value) : IResult, IStatusCodeHttpResult, IValueHttpResult<TValue>
{
    public string? Location { get; } = location;
    public TValue? Value { get; } = value;
    public int StatusCode => StatusCodes.Status201Created;
    public Task ExecuteAsync (HttpContext httpContext)
    {
        if (Location is not null) httpContext.Response.Headers.Location = Location;
        return JsonBody.WriteAsync(httpContext, Value, null, null, StatusCode);
    }
}

/// <summary>202 Accepted.</summary>
public sealed class Accepted (string? location) : IResult, IStatusCodeHttpResult
{
    public string? Location { get; } = location;
    public int StatusCode => StatusCodes.Status202Accepted;
    public Task ExecuteAsync (HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCode;
        if (Location is not null) httpContext.Response.Headers.Location = Location;
        return Task.CompletedTask;
    }
}

/// <summary>An arbitrary status code with no body.</summary>
public sealed class StatusCodeHttpResult (int statusCode) : IResult, IStatusCodeHttpResult
{
    public int StatusCode { get; } = statusCode;
    public Task ExecuteAsync (HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCode;
        return Task.CompletedTask;
    }
}

/// <summary>A response that writes nothing, leaving whatever the handler already set.</summary>
public sealed class EmptyHttpResult : IResult
{
    internal static readonly EmptyHttpResult Instance = new();
    private EmptyHttpResult () { }
    public Task ExecuteAsync (HttpContext httpContext) => Task.CompletedTask;
}

/// <summary>A response with an explicit content type.</summary>
public sealed class ContentHttpResult (string? content, string? contentType, Encoding? contentEncoding, int? statusCode) : IResult, IStatusCodeHttpResult
{
    public string? ResponseContent { get; } = content;
    public string? ContentType { get; } = contentType;
    public int? StatusCode { get; } = statusCode;
    int IStatusCodeHttpResult.StatusCode => StatusCode ?? StatusCodes.Status200OK;

    public Task ExecuteAsync (HttpContext httpContext)
    {
        if (StatusCode.HasValue) httpContext.Response.StatusCode = StatusCode.Value;
        var encoding = contentEncoding ?? Encoding.UTF8;
        httpContext.Response.ContentType = ContentType is null
            ? "text/plain; charset=" + encoding.WebName
            : ContentType.Contains("charset", StringComparison.OrdinalIgnoreCase)
                ? ContentType
                : ContentType + "; charset=" + encoding.WebName;
        return ResponseContent is null ? Task.CompletedTask : httpContext.Response.WriteAsync(ResponseContent, encoding);
    }
}

/// <summary>A response carrying bytes.</summary>
/// <remarks>
/// The buffered body is bytes all the way to the <c>Response</c> now (' byte
/// channel), so this writes them straight through instead of degrading to a string. Upstream's
/// range processing, <c>ETag</c> and <c>Last-Modified</c> handling are deliberately absent: they
/// belong to a static-file layer, and a worker serves static files from the assets binding.
/// </remarks>
public sealed class FileContentHttpResult (
    ReadOnlyMemory<byte> contents, string? contentType, string? fileDownloadName) : IResult, IStatusCodeHttpResult
{
    public ReadOnlyMemory<byte> FileContents { get; } = contents;
    public string ContentType { get; } = contentType ?? "application/octet-stream";
    public string? FileDownloadName { get; } = fileDownloadName;
    public int StatusCode => StatusCodes.Status200OK;

    public async Task ExecuteAsync (HttpContext httpContext)
    {
        httpContext.Response.ContentType = ContentType;
        httpContext.Response.ContentLength = FileContents.Length;
        if (FileDownloadName is { } name)
            httpContext.Response.Headers.ContentDisposition = Disposition(name);
        await httpContext.Response.Body.WriteAsync(FileContents, httpContext.RequestAborted);
    }

    // SetHttpFileName writes both the plain and the RFC 5987 filename* form, which is what makes a
    // non-ASCII name survive; hand-formatting the header is where that goes wrong.
    private static string Disposition (string name)
    {
        var disposition = new ContentDispositionHeaderValue("attachment");
        disposition.SetHttpFileName(name);
        return disposition.ToString();
    }
}

/// <summary>
/// The worker declining the request in favour of the assets binding.
/// </summary>
/// <remarks>
/// Not a status code: the emitted module recognises it through a field of the response snapshot, so
/// "this one is not mine" cannot be confused with an answer. It replaces the status-zero sentinel,
/// which the runtime had to special-case before building a <c>Response</c> and which any handler
/// could have produced by accident.
/// </remarks>
public sealed class PassThroughToAssetsHttpResult : IResult
{
    internal static readonly PassThroughToAssetsHttpResult Instance = new();
    private PassThroughToAssetsHttpResult () { }

    public Task ExecuteAsync (HttpContext httpContext)
    {
        if (httpContext.Features.Get<IHttpResponseFeature>() is not WorkerResponseFeature feature)
            throw new InvalidOperationException(
                "Results.PassThroughToAssets() is answered by the worker entrypoint and only works " +
                "on a context this package created.");
        feature.PassThroughToAssets = true;
        return Task.CompletedTask;
    }
}

/// <summary>A JSON response serialized through the supplied metadata.</summary>
public sealed class JsonHttpResult<TValue> (TValue? value, JsonTypeInfo<TValue>? jsonTypeInfo, string? contentType, int? statusCode)
    : IResult, IStatusCodeHttpResult, IValueHttpResult<TValue>
{
    public TValue? Value { get; } = value;
    public string? ContentType { get; } = contentType;
    public int? StatusCode { get; } = statusCode;
    int IStatusCodeHttpResult.StatusCode => StatusCode ?? StatusCodes.Status200OK;
    public Task ExecuteAsync (HttpContext httpContext) => JsonBody.WriteAsync(httpContext, Value, jsonTypeInfo, ContentType, StatusCode);
}

/// <summary>A redirect response.</summary>
public sealed class RedirectHttpResult (string url, bool permanent, bool preserveMethod) : IResult, IStatusCodeHttpResult
{
    private readonly int? explicitStatusCode;

    /// <summary>A redirect whose status is stated rather than derived from the two flags.</summary>
    /// <remarks>Exists for 303 See Other, which upstream's four-way matrix cannot express: it is the
    /// answer to a form POST — "your submission was accepted, now GET this instead" — and the only
    /// redirect that tells the browser to change the method. Every framework that redirects after a
    /// POST wants it, so it is part of the curated set rather than something each app re-implements
    /// as a six-line <see cref="IResult"/>.</remarks>
    public RedirectHttpResult (string url, int statusCode) : this(url, false, false) => explicitStatusCode = statusCode;

    public string Url { get; } = url;
    public bool Permanent { get; } = permanent;
    public bool PreserveMethod { get; } = preserveMethod;

    public int StatusCode => explicitStatusCode ?? (Permanent, PreserveMethod) switch {
        (true, true) => StatusCodes.Status308PermanentRedirect,
        (true, false) => StatusCodes.Status301MovedPermanently,
        (false, true) => StatusCodes.Status307TemporaryRedirect,
        (false, false) => StatusCodes.Status302Found,
    };

    public Task ExecuteAsync (HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCode;
        httpContext.Response.Headers.Location = Url;
        return Task.CompletedTask;
    }
}

/// <summary>An RFC 7807 problem response.</summary>
/// <remarks>
/// Serialized through <see cref="ProblemJsonContext"/>. <see cref="ProblemDetails.Extensions"/>
/// is a bag of arbitrary values that source-generated metadata cannot describe, so this host
/// writes the five known members (and, for validation, the errors map) and nothing else.
/// </remarks>
public class ProblemHttpResult (ProblemDetails problemDetails) : IResult, IStatusCodeHttpResult
{
    public ProblemDetails ProblemDetails { get; } = problemDetails;
    public int StatusCode => ProblemDetails.Status ?? StatusCodes.Status500InternalServerError;

    public virtual Task ExecuteAsync (HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCode;
        httpContext.Response.ContentType = "application/problem+json; charset=utf-8";
        return httpContext.Response.WriteAsync(Render());
    }

    private protected string Render () =>
        JsonSerializer.Serialize(Document(), ProblemJsonContext.Default.ProblemDocument);

    private protected virtual ProblemDocument Document () => new(
        Type: ProblemDetails.Type,
        Title: ProblemDetails.Title ?? ReasonPhrases.Get(StatusCode),
        Status: StatusCode,
        Detail: ProblemDetails.Detail,
        Instance: ProblemDetails.Instance,
        Errors: null);
}

/// <summary>A 400 problem response listing per-member validation errors.</summary>
public sealed class ValidationProblem : ProblemHttpResult
{
    private readonly IDictionary<string, string[]> errors;

    internal ValidationProblem (IDictionary<string, string[]> errors, string? detail, string? instance, string? title, string? type)
        : base(new ProblemDetails
        {
            Detail = detail,
            Instance = instance,
            Status = StatusCodes.Status400BadRequest,
            Title = title ?? "One or more validation errors occurred.",
            Type = type,
        })
    {
        ArgumentNullException.ThrowIfNull(errors);
        this.errors = errors;
    }

    public IDictionary<string, string[]> Errors => errors;

    private protected override ProblemDocument Document () =>
        base.Document() with {
            Errors = errors as Dictionary<string, string[]> ?? new Dictionary<string, string[]>(errors)
        };
}

/// <summary>A result that carries a status code.</summary>
public interface IStatusCodeHttpResult
{
    /// <summary>The status code the result writes.</summary>
    int StatusCode { get; }
}

/// <summary>A result that carries a value.</summary>
public interface IValueHttpResult<out TValue>
{
    /// <summary>The value the result writes.</summary>
    TValue? Value { get; }
}

/// <summary>The default titles RFC 7807 problem responses use.</summary>
/// <remarks>Only the codes the curated results can produce: a full status-to-phrase table
/// belongs to <c>Microsoft.AspNetCore.WebUtilities</c>, which is not referenced here.</remarks>
internal static class ReasonPhrases
{
    internal static string Get (int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "Bad Request",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status405MethodNotAllowed => "Method Not Allowed",
        StatusCodes.Status409Conflict => "Conflict",
        StatusCodes.Status422UnprocessableEntity => "Unprocessable Entity",
        StatusCodes.Status500InternalServerError => "An error occurred while processing your request.",
        _ => "An error occurred while processing your request.",
    };
}
