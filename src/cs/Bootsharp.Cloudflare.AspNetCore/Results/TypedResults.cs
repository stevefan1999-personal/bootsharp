using System.Text;
using System.Text.Json.Serialization.Metadata;
using Bootsharp.Cloudflare.AspNetCore.Html;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Microsoft.AspNetCore.Http;

/// <summary>
/// Creates <see cref="IResult"/> values with a concrete return type.
/// </summary>
/// <remarks>
/// <para>
/// A façade over this package's own result types rather than a vendoring of
/// <c>Microsoft.AspNetCore.Http.Results</c>: that assembly's 51 files split into a clean core and
/// three groups that cannot exist here — <c>*AtRoute</c> and <c>RedirectToRoute</c> need
/// <c>LinkGenerator</c> (the whole outbound-routing half of Routing, four
/// <c>[RequiresUnreferencedCode]</c> sites), <c>Challenge</c>/<c>Forbid</c>/<c>SignIn</c>/<c>SignOut</c>
/// need an authentication layer, and <c>PhysicalFile</c>/<c>VirtualFile</c> need a filesystem.
/// </para>
/// <para>
/// Deliberately absent, each with a reason rather than a silent omission:
/// <list type="bullet">
/// <item><c>CreatedAtRoute</c>, <c>AcceptedAtRoute</c>, <c>RedirectToRoute</c> — no
/// <c>LinkGenerator</c>. Build the URL and use <see cref="Created(string?)"/> or
/// <see cref="Redirect"/>.</item>
/// <item><c>PhysicalFile</c>, <c>VirtualFile</c>, and the path-taking <c>File</c> overloads — a
/// worker has no filesystem. <see cref="File(ReadOnlyMemory{byte}, string?, string?)"/> takes the
/// bytes; static content is served from the assets binding.</item>
/// <item><c>Challenge</c>, <c>Forbid</c>, <c>SignIn</c>, <c>SignOut</c> — no authentication layer yet.</item>
/// <item>The untyped, reflective <c>Results.Json(object)</c> — <c>[RequiresDynamicCode]</c>.
/// Use <see cref="Json{T}(T, JsonTypeInfo{T}, string?, int?)"/>.</item>
/// </list>
/// <see cref="Stream"/> and <see cref="ServerSentEvents"/> exist but throw until
/// milestone 0b delivers Tier-1 handles, so that code written against them fails loudly at the
/// call rather than compiling against an API that was never there.
/// </para>
/// </remarks>
public static class TypedResults
{
    /// <summary>200 OK with no body.</summary>
    public static Ok Ok () => HttpResults.Ok.Instance;

    /// <summary>200 OK with a JSON body.</summary>
    public static Ok<TValue> Ok<TValue> (TValue? value) => new(value);

    /// <summary>201 Created with a <c>Location</c> header.</summary>
    public static Created Created (string? uri) => new(uri);

    /// <summary>201 Created with a <c>Location</c> header and a JSON body.</summary>
    public static Created<TValue> Created<TValue> (string? uri, TValue? value) => new(uri, value);

    /// <summary>202 Accepted.</summary>
    public static Accepted Accepted (string? uri) => new(uri);

    /// <summary>204 No Content.</summary>
    public static NoContent NoContent () => HttpResults.NoContent.Instance;

    /// <summary>400 Bad Request.</summary>
    public static BadRequest BadRequest () => HttpResults.BadRequest.Instance;

    /// <summary>400 Bad Request with a JSON body.</summary>
    public static BadRequest<TValue> BadRequest<TValue> (TValue? value) => new(value);

    /// <summary>404 Not Found.</summary>
    public static NotFound NotFound () => HttpResults.NotFound.Instance;

    /// <summary>404 Not Found with a JSON body.</summary>
    public static NotFound<TValue> NotFound<TValue> (TValue? value) => new(value);

    /// <summary>409 Conflict.</summary>
    public static Conflict Conflict () => HttpResults.Conflict.Instance;

    /// <summary>422 Unprocessable Entity.</summary>
    public static UnprocessableEntity UnprocessableEntity () => HttpResults.UnprocessableEntity.Instance;

    /// <summary>An arbitrary status code with no body.</summary>
    public static StatusCodeHttpResult StatusCode (int statusCode) => new(statusCode);

    /// <summary>A response that writes nothing at all, leaving whatever the handler already set.</summary>
    public static EmptyHttpResult Empty => EmptyHttpResult.Instance;

    /// <summary>A response with an explicit content type.</summary>
    public static ContentHttpResult Content (string? content, string? contentType = null, Encoding? contentEncoding = null, int? statusCode = null) =>
        new(content, contentType, contentEncoding, statusCode);

    /// <summary>A <c>text/plain</c> response.</summary>
    public static ContentHttpResult Text (string? content, string? contentType = null, Encoding? contentEncoding = null, int? statusCode = null) =>
        new(content, contentType ?? "text/plain", contentEncoding, statusCode);

    /// <summary>A JSON response serialized through source-generated metadata.</summary>
    public static JsonHttpResult<TValue> Json<TValue> (TValue? value, JsonTypeInfo<TValue> jsonTypeInfo, string? contentType = null, int? statusCode = null) =>
        new(value, jsonTypeInfo, contentType, statusCode);

    /// <summary>A <c>text/html</c> response rendered by a template.</summary>
    /// <remarks>Beyond upstream's surface — <c>Results.Content(html, "text/html")</c> is what ASP.NET
    /// Core offers, and it takes a string that something already built. Taking the template instead
    /// is what lets the response stream when the transport can, and what makes the encoding of every
    /// value in the page the writer's decision rather than the author's.</remarks>
    public static HtmlHttpResult Html (HtmlBody body, int? statusCode = null) => new(body, statusCode);

    /// <summary>A <c>text/html</c> response carrying an already-rendered fragment.</summary>
    public static HtmlHttpResult Html (HtmlString html, int? statusCode = null) =>
        new(writer => writer.WriteHtml(html), statusCode);

    /// <summary>A redirect to <paramref name="url"/>.</summary>
    public static RedirectHttpResult Redirect (string url, bool permanent = false, bool preserveMethod = false) =>
        new(url, permanent, preserveMethod);

    /// <summary>303 See Other: the answer to a form POST.</summary>
    /// <remarks>Beyond upstream's surface, and the one deliberate addition to it. 303 is the
    /// redirect that tells the browser to follow with <c>GET</c> whatever the original method was
    /// the Post/Redirect/Get answer every HTML form wants, and the one status
    /// <see cref="Redirect"/>'s permanent/preserve-method matrix cannot produce.</remarks>
    public static RedirectHttpResult RedirectSeeOther (string url) =>
        new(url, StatusCodes.Status303SeeOther);

    /// <summary>An RFC 7807 problem response.</summary>
    public static ProblemHttpResult Problem (string? detail = null, string? instance = null, int? statusCode = null, string? title = null, string? type = null) =>
        new(new ProblemDetails
        {
            Detail = detail,
            Instance = instance,
            Status = statusCode ?? StatusCodes.Status500InternalServerError,
            Title = title,
            Type = type,
        });

    /// <summary>An RFC 7807 problem response built from a <see cref="ProblemDetails"/>.</summary>
    public static ProblemHttpResult Problem (ProblemDetails problemDetails) => new(problemDetails);

    /// <summary>A 400 problem response listing per-member validation errors.</summary>
    public static ValidationProblem ValidationProblem (IDictionary<string, string[]> errors, string? detail = null, string? instance = null, string? title = null, string? type = null) =>
        new(errors, detail, instance, title, type);

    /// <summary>A response carrying bytes.</summary>
    /// <remarks>The buffered body is a byte channel end to end, so binary content is byte-exact
    /// rather than mangled by a UTF-8 round trip — which is what returning it through
    /// <see cref="Content(string?, string?, Encoding?, int?)"/> used to do.</remarks>
    public static FileContentHttpResult Bytes (ReadOnlyMemory<byte> contents, string? contentType = null, string? fileDownloadName = null) =>
        new(contents, contentType, fileDownloadName);

    /// <summary>A file response built from bytes the handler already has.</summary>
    /// <remarks>The one <c>File</c> overload that exists here: the path-based ones need a
    /// filesystem, which a worker has no equivalent of. Read the bytes from R2, KV or the assets
    /// binding and hand them over.</remarks>
    public static FileContentHttpResult File (ReadOnlyMemory<byte> fileContents, string? contentType = null, string? fileDownloadName = null) =>
        new(fileContents, contentType, fileDownloadName);

    /// <summary>Declines the request, handing it to the worker's assets binding.</summary>
    /// <remarks>The answer for a route the worker matches but does not serve — a client-side
    /// application's catch-all, typically. The emitted module recognises it from the response
    /// snapshot and calls the assets binding; a worker that has none answers 404.</remarks>
    public static PassThroughToAssetsHttpResult PassThroughToAssets () => PassThroughToAssetsHttpResult.Instance;

    /// <summary>Not available until milestone 0b.</summary>
    /// <exception cref="PlatformNotSupportedException">Always.</exception>
    public static IResult Stream (System.IO.Stream stream, string? contentType = null) =>
        throw new PlatformNotSupportedException(
            "TypedResults.Stream needs a live JS ReadableStream handle; until " +
            "then bodies are buffered. Read the stream and return TypedResults.Bytes.");

    /// <summary>Not available until milestone 0b.</summary>
    /// <exception cref="PlatformNotSupportedException">Always.</exception>
    public static IResult ServerSentEvents<T> (IAsyncEnumerable<T> values, string? eventType = null) =>
        throw new PlatformNotSupportedException(
            "TypedResults.ServerSentEvents needs a live JS ReadableStream handle. " +
            "The serialization gate also has to release after the headers flush before a streaming " +
            "response can be held open.");
}

/// <summary>
/// Creates <see cref="IResult"/> values without naming their concrete type.
/// </summary>
/// <remarks>The same curated set as <see cref="TypedResults"/>, for handlers whose branches return
/// different results and so need a common <see cref="IResult"/> type.</remarks>
public static class Results
{
    /// <inheritdoc cref="TypedResults.Ok()"/>
    public static IResult Ok () => TypedResults.Ok();

    /// <inheritdoc cref="TypedResults.Ok{TValue}(TValue)"/>
    public static IResult Ok<TValue> (TValue? value) => TypedResults.Ok(value);

    /// <inheritdoc cref="TypedResults.Created(string?)"/>
    public static IResult Created (string? uri) => TypedResults.Created(uri);

    /// <inheritdoc cref="TypedResults.Created{TValue}(string?, TValue)"/>
    public static IResult Created<TValue> (string? uri, TValue? value) => TypedResults.Created(uri, value);

    /// <inheritdoc cref="TypedResults.Accepted(string?)"/>
    public static IResult Accepted (string? uri) => TypedResults.Accepted(uri);

    /// <inheritdoc cref="TypedResults.NoContent()"/>
    public static IResult NoContent () => TypedResults.NoContent();

    /// <inheritdoc cref="TypedResults.BadRequest()"/>
    public static IResult BadRequest () => TypedResults.BadRequest();

    /// <inheritdoc cref="TypedResults.BadRequest{TValue}(TValue)"/>
    public static IResult BadRequest<TValue> (TValue? value) => TypedResults.BadRequest(value);

    /// <inheritdoc cref="TypedResults.NotFound()"/>
    public static IResult NotFound () => TypedResults.NotFound();

    /// <inheritdoc cref="TypedResults.NotFound{TValue}(TValue)"/>
    public static IResult NotFound<TValue> (TValue? value) => TypedResults.NotFound(value);

    /// <inheritdoc cref="TypedResults.Conflict()"/>
    public static IResult Conflict () => TypedResults.Conflict();

    /// <inheritdoc cref="TypedResults.UnprocessableEntity()"/>
    public static IResult UnprocessableEntity () => TypedResults.UnprocessableEntity();

    /// <inheritdoc cref="TypedResults.StatusCode(int)"/>
    public static IResult StatusCode (int statusCode) => TypedResults.StatusCode(statusCode);

    /// <inheritdoc cref="TypedResults.Empty"/>
    public static IResult Empty => TypedResults.Empty;

    /// <inheritdoc cref="TypedResults.Content(string?, string?, Encoding?, int?)"/>
    public static IResult Content (string? content, string? contentType = null, Encoding? contentEncoding = null, int? statusCode = null) =>
        TypedResults.Content(content, contentType, contentEncoding, statusCode);

    /// <inheritdoc cref="TypedResults.Text(string?, string?, Encoding?, int?)"/>
    public static IResult Text (string? content, string? contentType = null, Encoding? contentEncoding = null, int? statusCode = null) =>
        TypedResults.Text(content, contentType, contentEncoding, statusCode);

    /// <inheritdoc cref="TypedResults.Json{TValue}(TValue, JsonTypeInfo{TValue}, string?, int?)"/>
    public static IResult Json<TValue> (TValue? value, JsonTypeInfo<TValue> jsonTypeInfo, string? contentType = null, int? statusCode = null) =>
        TypedResults.Json(value, jsonTypeInfo, contentType, statusCode);

    /// <inheritdoc cref="TypedResults.Html(HtmlBody, int?)"/>
    public static IResult Html (HtmlBody body, int? statusCode = null) => TypedResults.Html(body, statusCode);

    /// <inheritdoc cref="TypedResults.Html(HtmlString, int?)"/>
    public static IResult Html (HtmlString html, int? statusCode = null) => TypedResults.Html(html, statusCode);

    /// <inheritdoc cref="TypedResults.Bytes(ReadOnlyMemory{byte}, string?, string?)"/>
    public static IResult Bytes (ReadOnlyMemory<byte> contents, string? contentType = null, string? fileDownloadName = null) =>
        TypedResults.Bytes(contents, contentType, fileDownloadName);

    /// <inheritdoc cref="TypedResults.File(ReadOnlyMemory{byte}, string?, string?)"/>
    public static IResult File (ReadOnlyMemory<byte> fileContents, string? contentType = null, string? fileDownloadName = null) =>
        TypedResults.File(fileContents, contentType, fileDownloadName);

    /// <inheritdoc cref="TypedResults.PassThroughToAssets()"/>
    public static IResult PassThroughToAssets () => TypedResults.PassThroughToAssets();

    /// <inheritdoc cref="TypedResults.Redirect(string, bool, bool)"/>
    public static IResult Redirect (string url, bool permanent = false, bool preserveMethod = false) =>
        TypedResults.Redirect(url, permanent, preserveMethod);

    /// <inheritdoc cref="TypedResults.RedirectSeeOther(string)"/>
    public static IResult RedirectSeeOther (string url) => TypedResults.RedirectSeeOther(url);

    /// <inheritdoc cref="TypedResults.Problem(string?, string?, int?, string?, string?)"/>
    public static IResult Problem (string? detail = null, string? instance = null, int? statusCode = null, string? title = null, string? type = null) =>
        TypedResults.Problem(detail, instance, statusCode, title, type);

    /// <inheritdoc cref="TypedResults.ValidationProblem(IDictionary{string, string[]}, string?, string?, string?, string?)"/>
    public static IResult ValidationProblem (IDictionary<string, string[]> errors, string? detail = null, string? instance = null, string? title = null, string? type = null) =>
        TypedResults.ValidationProblem(errors, detail, instance, title, type);
}

